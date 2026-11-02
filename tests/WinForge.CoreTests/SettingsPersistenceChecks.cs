using System.IO;
using System.Text.Json;
using WinForge.Core;

internal static class SettingsPersistenceChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "recover.json");
        using (var store = new SettingsStore(path))
        {
            store.For("test")["value"] = "one"; store.Save();
            store.For("test")["value"] = "two"; store.Save();
            check(new SettingsStore(path + ".bak").For("test")["value"] == "one", "Atomic replacement preserves a readable prior snapshot");
        }
        // Dispose committed the current value, so the durable backup is now two.
        File.WriteAllText(path, "{broken");
        using (var recovered = new SettingsStore(path))
        {
            check(recovered.For("test")["value"] == "two" && recovered.Persistence.Recovery is not null,
                "Corrupt primary settings recover from the last valid backup with a visible notice");
            check(Directory.GetFiles(root, "recover.json.corrupt-*").Any(file => File.ReadAllText(file) == "{broken"),
                "Recovery preserves the corrupt file for diagnosis instead of overwriting it");
            recovered.Save();
            check(new SettingsStore(path + ".bak").For("test")["value"] == "two", "Replacing a corrupt primary does not overwrite the healthy backup with corrupt data");
        }

        var futurePath = Path.Combine(root, "future.json");
        const string future = "{\"SchemaVersion\":999,\"Values\":{}}";
        File.WriteAllText(futurePath, future);
        var futureStore = new SettingsStore(futurePath);
        ExpectFailure(futureStore.Save);
        check(File.ReadAllText(futurePath) == future && futureStore.Persistence.Error is not null,
            "Unknown future settings schemas remain intact and cannot be silently replaced");
        try { futureStore.Dispose(); } catch (IOException) { }

        var lockedPath = Path.Combine(root, "locked.json");
        using (var store = new SettingsStore(lockedPath))
        {
            store.For("test")["value"] = "original"; store.Save();
            using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                store.For("test")["value"] = "new";
                ExpectFailure(store.Save);
                check(store.Persistence.Pending && store.Persistence.Error is not null, "A locked settings file reports failure and retains a pending retry");
            }
            store.Flush();
            check(!store.Persistence.Pending && store.Persistence.Error is null && new SettingsStore(lockedPath).For("test")["value"] == "new",
                "A retry clears the error only after the pending snapshot becomes durable");
        }

        var fullPath = Path.Combine(root, "full.json");
        var allow = false;
        var writes = 0;
        using (var store = new SettingsStore(fullPath, bytes =>
        {
            if (!allow) throw new IOException("Disk full", unchecked((int)0x80070070));
            writes++; File.WriteAllBytes(fullPath, bytes);
        }))
        {
            store.DeferWrites = true;
            var settings = new ModuleSettings(store.For("test"), store.ScheduleSave, store.SyncRoot);
            var failures = 0;
            store.PersistenceChanged += state => { if (state.Error is not null) failures++; };
            settings.Set("value", "latest");
            ExpectFailure(store.Flush);
            check(failures == 1 && store.Persistence.SavedVersion == 0 && store.Persistence.Pending,
                "Simulated disk exhaustion publishes a failure rather than a saved acknowledgement");
            allow = true;
            store.Flush();
            check(writes == 1 && new SettingsStore(fullPath).For("test")["value"] == "latest", "Disk exhaustion keeps the newest snapshot available for retry");
        }

        var batchPath = Path.Combine(root, "batch.json");
        writes = 0;
        using (var store = new SettingsStore(batchPath, bytes => { writes++; File.WriteAllBytes(batchPath, bytes); }))
        {
            store.DeferWrites = true;
            var settings = new ModuleSettings(store.For("test"), store.ScheduleSave, store.SyncRoot);
            for (var i = 0; i < 50; i++) settings.Set("value", i);
            store.Flush();
            check(writes == 1 && store.Persistence.RequestedVersion == store.Persistence.SavedVersion,
                "A burst of edits coalesces into one durable snapshot with an acknowledged revision");
            check(new SettingsStore(batchPath).For("test")["value"] == "49", "Reopening settings returns the final edit from the batch");
            settings.Set("value", "close");
        }
        check(new SettingsStore(batchPath).For("test")["value"] == "close", "Normal disposal flushes the last pending edit");
        check(!Directory.GetFiles(root, "*.tmp").Any(), "Successful and failed writes clean their temporary files");
        SerializedRollback(root, check);
    }

    private static void SerializedRollback(string root, Action<bool, string> check)
    {
        var modules = Directory.CreateDirectory(Path.Combine(root, "modules")).FullName;
        File.Copy(typeof(SettingsFixtureModule).Assembly.Location, Path.Combine(modules, "WinForge.Modules.SettingsFixture.dll"));
        using var store = new SettingsStore(Path.Combine(root, "transaction.json")) { DeferWrites = true };
        var host = new ModuleHost(store, new FileLogger(Path.Combine(root, "fixture.log")), new NoHotkeys(), modules, _ => { });
        host.Load();
        var module = (SettingsFixtureModule)host.Modules.Single();
        host.SetEnabled(module, true);
        var descriptor = module.Settings.Single();
        var first = host.UpdateSettingAsync(module, descriptor, 1);
        module.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        var second = host.UpdateSettingAsync(module, descriptor, 2);
        check(module.Value == 1 && !second.IsCompleted, "A newer edit waits until the earlier apply and rollback transaction finishes");
        var externalFlush = store.FlushAsync();
        Task.Delay(200).GetAwaiter().GetResult();
        check(!externalFlush.IsCompleted && !new SettingsStore(store.FilePath).For(module.Id).ContainsKey(descriptor.Key),
            "Automatic and external flushes cannot persist a tentative setting before apply finishes");
        module.ReleaseFirst.SetResult();
        try { first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); throw new Exception("The fixture apply should fail."); }
        catch (InvalidOperationException) { }
        second.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        externalFlush.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        check(module.Value == 2 && new SettingsStore(store.FilePath).For(module.Id)[descriptor.Key] == "2",
            "An earlier failed apply cannot roll back a later successful edit");
        check(host.ErrorOf(module) is null, "A later successful edit clears the earlier transaction error");
        host.StopAll();
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); throw new Exception("Expected persistence failure."); }
        catch (IOException) { }
    }
    private sealed class NoHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => throw new NotSupportedException();
    }
}

public sealed class SettingsFixtureModule : ITweakModule, IAsyncTweakModule
{
    private IModuleSettings settings = null!;
    public string Id => "settings-fixture";
    public string Name => Id;
    public string Category => "Tests";
    public string Description => "Settings transaction fixture";
    public string? Warning => null;
    public bool IsActive { get; private set; }
    public int Value => settings.GetInt("value");
    public IReadOnlyList<SettingDescriptor> Settings { get; } = [new() { Key = "value", Label = "Value", Kind = SettingKind.Number, Default = 0 }];
    public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Initialize(IModuleContext context) => settings = context.Settings;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void Apply() { }
    public Task ActivateAsync() { Activate(); return Task.CompletedTask; }
    public Task DeactivateAsync() { Deactivate(); return Task.CompletedTask; }
    public async Task ApplyAsync()
    {
        if (Value != 1) return;
        FirstStarted.TrySetResult();
        await ReleaseFirst.Task;
        throw new InvalidOperationException("Delayed apply failure");
    }
}
