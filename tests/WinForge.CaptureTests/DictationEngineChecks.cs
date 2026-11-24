using WinForge.Core;
using WinForge.Dictation;
using WinForge.Modules.Capture;

internal static class DictationEngineChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var originalTarget = new TextTarget(1, 5, "field");
        var changedTarget = new TextTarget(2, 6, "other");
        check(await DictationModule.CaptureTargetAsync(1, default, () => originalTarget) == originalTarget,
            "Startup accepts a target from the original foreground window");
        check(await DictationModule.CaptureTargetAsync(1, default, () => changedTarget) is null,
            "Delayed target capture rejects a window focused after the shortcut");
        check(await DictationModule.CaptureTargetAsync(1, default, () => originalTarget, 10, _ => 11) is null,
            "Delayed target capture rejects a changed native focus within the same window");
        check(await DictationModule.CaptureTargetAsync(1, default, () => originalTarget, 10, _ => 10) == originalTarget,
            "Unchanged native focus retains the original target");
        check(await DictationModule.CaptureTargetAsync(1, default, () => null, 10, _ => 10) is null &&
            await DictationModule.RecoverTargetAsync(1, 10, default, () => originalTarget, () => 1, _ => 10) == originalTarget,
            "A field missed at startup can be recovered at stop with the same native focus");
        TextTarget? UnexpectedCapture() => throw new InvalidOperationException("A changed focus must not be queried");
        check(await DictationModule.RecoverTargetAsync(1, 10, default, UnexpectedCapture, () => 2, _ => 10) is null &&
            await DictationModule.RecoverTargetAsync(1, 10, default, UnexpectedCapture, () => 1, _ => 11) is null &&
            await DictationModule.RecoverTargetAsync(1, 0, default, UnexpectedCapture, () => 1, _ => 0) is null,
            "Stop-time recovery requires the known, original window and native focus");
        var configuration = new DictationConfiguration("model.bin", "tr", false, false);
        var engines = new List<FakeEngine>();
        using var client = new DictationClient(factory: _ => { var engine = new FakeEngine(); engines.Add(engine); return engine; });
        await client.PrepareAsync(configuration, default);
        check(engines.Count == 1 && engines[0].Calls == 0, "Preparation loads the model without running inference");
        check(await client.TranscribeAsync([.2f], configuration, default) == "İstanbul", "In-process engine preserves Unicode");
        await client.TranscribeAsync([.2f], configuration, default);
        check(engines.Count == 1 && engines[0].Calls == 2, "Repeated dictation reuses the loaded model");
        await client.TranscribeAsync([.2f], configuration with { Language = "en" }, default);
        check(engines.Count == 1 && engines[0].Calls == 3, "Language changes reuse the loaded model");
        await client.TranscribeAsync([.2f], configuration with { Translate = true }, default);
        check(engines.Count == 1 && engines[0].Calls == 4, "Translation changes reuse the loaded model");
        await client.PrepareAsync(configuration with { Model = "other.bin" }, default);
        check(engines.Count == 2 && engines[0].Disposed, "Model changes release the previous model");
        client.Dispose(); client.Dispose();
        check(!client.IsRunning && engines[1].Disposed, "Repeated session shutdown releases the model");
        await client.TranscribeAsync([.2f], configuration, default);
        check(engines.Count == 3, "Module reactivation starts a fresh in-process session");

        var gpuAttempts = 0;
        using var fallback = new DictationClient(factory: config =>
        {
            if (config.Gpu) { gpuAttempts++; throw new InvalidOperationException("GPU unavailable"); }
            return new FakeEngine();
        });
        await fallback.PrepareAsync(configuration with { Gpu = true }, default);
        await fallback.TranscribeAsync([.2f], configuration with { Gpu = true }, default);
        await fallback.TranscribeAsync([.2f], configuration with { Gpu = true, Language = "en" }, default);
        check(gpuAttempts == 1 && fallback.Engine == "CPU (GPU yerine)", "GPU failure selects and reuses the CPU backend");

        var blocking = new FakeEngine { Block = true };
        using var active = new DictationClient(factory: _ => blocking);
        var running = active.TranscribeAsync([.2f], configuration, default);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = active.TranscribeAsync([.2f], configuration, default);
        active.Dispose();
        check(!blocking.Disposed, "Shutdown does not free a model during a native call");
        blocking.Continue.Set();
        foreach (var task in new[] { running, queued })
        {
            try { await task; throw new InvalidOperationException("Cancellation ignored"); }
            catch (OperationCanceledException) { }
        }
        check(blocking.Disposed && blocking.Calls == 1 && !active.IsRunning,
            "Shutdown cancels queued inference and frees the model after the native call returns");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await client.TranscribeAsync([.2f], configuration, cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(engines.Count == 3, "Pre-cancelled inference never loads a model"); }

        var slow = new FakeEngine { WaitUntilCancelled = true };
        using var timed = new DictationClient(factory: _ => slow, timeout: TimeSpan.FromMilliseconds(100));
        try { await timed.TranscribeAsync([.2f], configuration, default); throw new InvalidOperationException("Timeout ignored"); }
        catch (TimeoutException) { check(slow.Disposed && !timed.IsRunning, "Inference timeout is reported and releases the model"); }
    }

    private sealed class FakeEngine : IDictationEngine
    {
        internal bool Disposed, Block, WaitUntilCancelled;
        internal int Calls;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Continue = new(false);
        public string Transcribe(float[] samples, DictationConfiguration configuration, CancellationToken token)
        {
            Calls++; Entered.TrySetResult();
            if (Block) Continue.Wait(TimeSpan.FromSeconds(5));
            if (WaitUntilCancelled) token.WaitHandle.WaitOne();
            if (Disposed) throw new InvalidOperationException("Model freed during inference");
            token.ThrowIfCancellationRequested(); return "İstanbul";
        }
        public void Dispose() { Disposed = true; Continue.Dispose(); }
    }
}
