using System.Diagnostics;
using System.IO;
using WinForge.Core;
using WinForge.Modules.AltSnap;

namespace WinForge.RegressionTests;

internal static class AltSnapChecks
{
    internal static void Run(string testRoot, Action<bool, string> check, bool smoke)
    {
        var directory = Path.Combine(testRoot, "altsnap");
        using var runtime = new AltSnapRuntime(directory);
        var module = new AltSnapModule(runtime);
        var context = new Context(testRoot);
        module.Initialize(context);
        check(!Directory.Exists(directory), "AltSnap initialization performs no installation or process launch");
        check(context.Settings.GetString("Hotkeys") == "Sol Alt", "AltSnap defaults to left Alt to preserve AltGr");
        check(!context.Settings.GetBool("ResizeAll"), "AltSnap respects fixed-size windows by default");
        check(context.Settings.GetString("Processes").Contains("mstsc.exe"), "AltSnap preserves upstream process exclusions");

        runtime.Prepare();
        check(File.Exists(Path.Combine(directory, "hooks.dll")) && File.Exists(Path.Combine(directory, "License.txt"))
            && File.Exists(Path.Combine(directory, "AltSnap1.68src.zip")), "Verified offline engine includes hook library, license and matching source");
        check(runtime.ReadConfiguration().Get("Input", "Hotkeys") == "A4", "Portable INI uses WinForge defaults on first activation");
        var configuration = runtime.ReadConfiguration();
        configuration.Set("Zones", "Zone0", "0,0,960,1080,");
        configuration.Set("Input", "LMB", "Step_U_32");
        configuration.Set("Blacklist", "Windows", "Türkçe pencere|ÖzelSınıf");
        configuration.Save(runtime.ConfigurationPath);
        runtime.Prepare();
        configuration = runtime.ReadConfiguration();
        check(configuration.Get("Zones", "Zone0") == "0,0,960,1080,", "Preparing AltSnap preserves custom zone layouts");
        check(configuration.Get("Blacklist", "Windows") == "Türkçe pencere|ÖzelSınıf", "Portable INI preserves Unicode titles");

        var original = new AltSnapConfiguration("; comment\r\n[Input]\r\nLMB=Move\r\n[Other]\r\nLMB=Custom\r\n");
        original.Set("input", "lmb", "Resize");
        check(original.Get("Input", "LMB") == "Resize" && original.Get("Other", "LMB") == "Custom", "INI edits are case insensitive and isolated by section");
        var rejected = false;
        try { original.Set("Input", "LMB", "Move\r\nRMB=Close"); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "INI rejects injected configuration lines");

        var reloaded = new AltSnapModule(runtime);
        reloaded.Initialize(context);
        check(context.Settings.GetString("LMB") == "Step_U_32", "Native parameterized actions survive panel synchronization");
        context.Settings.Set("SnapThreshold", 37);
        var restarted = new AltSnapModule(runtime);
        restarted.Initialize(context);
        check(context.Settings.GetInt("SnapThreshold") == 37, "Unapplied inactive settings survive WinForge restart");

        restarted.WritePendingSettings();
        configuration = runtime.ReadConfiguration();
        check(configuration.Get("Advanced", "SnapThreshold") == "37" && configuration.Get("Input", "LMB") == "Step_U_32",
            "Applying pending settings preserves unrelated parameterized actions");
        configuration.Set("Input", "Scroll", "Transparency");
        configuration.Save(runtime.ConfigurationPath);
        context.Settings.Set("Aero", false);
        restarted.WritePendingSettings();
        configuration = runtime.ReadConfiguration();
        check(configuration.Get("General", "Aero") == "0" && configuration.Get("Input", "Scroll") == "Transparency",
            "Panel changes preserve unsynchronized native configuration edits");
        check(context.Settings.GetString("Scroll") == "Saydamlık", "Native edits synchronize back to panel values");
        check(configuration.Get("Advanced", "AlwaysElevate") == "0", "Managed engine inherits WinForge privileges");
        TestLifetimeJob(check);

        if (!smoke) return;
        AltSnapRuntime.EnsureNoOtherInstance();
        try
        {
            restarted.Activate();
            check(restarted.IsActive, "Real AltSnap engine starts and creates its control window");
            check(runtime.ReadConfiguration().Get("Advanced", "SnapThreshold") == "37", "Module activation applies pending settings to the real engine");
            check(runtime.ReadConfiguration().Get("Input", "LMB") == "Step_U_32", "Module activation preserves native advanced actions");
            var conflictDetected = false;
            try { AltSnapRuntime.EnsureNoOtherInstance(); }
            catch (InvalidOperationException) { conflictDetected = true; }
            check(conflictDetected, "AltSnap refuses concurrent hook engines");

            configuration = runtime.ReadConfiguration();
            configuration.Set("Input", "Scroll", "Transparency");
            configuration.Save(runtime.ConfigurationPath);
            context.Settings.Set("Aero", false);
            restarted.Apply();
            configuration = runtime.ReadConfiguration();
            check(configuration.Get("General", "Aero") == "0" && configuration.Get("Input", "Scroll") == "Transparency", "Live changes preserve unrelated edits from advanced configuration");
            check(context.Settings.GetString("Scroll") == "Saydamlık", "Panel synchronizes native edits after apply");
            restarted.Settings.Single(setting => setting.Key == "configure").Execute!();
            Thread.Sleep(150);
            check(restarted.IsActive, "Full native configuration opens for the owned engine");
        }
        finally { restarted.Deactivate(); }
        check(!runtime.IsRunning && !restarted.IsActive, "Deactivation stops the owned AltSnap process");
        restarted.Deactivate();
        check(!runtime.IsRunning, "AltSnap cleanup is idempotent");
        using var nativeProcess = Process.GetProcessesByName("AltSnap").FirstOrDefault();
        check(nativeProcess is null, "No orphan AltSnap process remains after smoke test");
    }

    private static void TestLifetimeJob(Action<bool, string> check)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        using var job = AltSnapNative.CreateLifetimeJob();
        try
        {
            AltSnapNative.Assign(job, process);
            job.Dispose();
            check(process.WaitForExit(2000), "Closing WinForge lifetime job terminates its child process");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); }
        }
    }

    private sealed class Context(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new MemorySettings();
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "altsnap-test.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }

    private sealed class MemorySettings : IModuleSettings
    {
        private readonly Dictionary<string, string> _values = new();
        public bool GetBool(string key, bool fallback = false) => bool.TryParse(GetString(key), out var value) ? value : fallback;
        public int GetInt(string key, int fallback = 0) => int.TryParse(GetString(key), out var value) ? value : fallback;
        public string GetString(string key, string fallback = "") => _values.GetValueOrDefault(key, fallback);
        public void Set(string key, object? value) => _values[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
}
