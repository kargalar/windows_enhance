using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;
using WinForge.Modules.Extras;

internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        if (arguments.Contains("--folder-dialog")) { VerifyFolderDialog(); return; }
        using (var runtime = Registry.CurrentUser.OpenSubKey(ExplorerNavigationModule.RuntimeKey))
            Check(runtime?.GetValue("active") is not int active || active == 0, "Disable navigation styling before running these tests.");
        Check(ExplorerNavigationModule.ParseColor("#1234AB") == 0xAB3412, "RGB is encoded as Win32 COLORREF.");
        foreach (var color in new[] { "red", "#FFF", "#00000000", "123456", "#GG0000", "#112233\n" })
        {
            try { ExplorerNavigationModule.ParseColor(color); throw new Exception("Invalid color accepted: " + color); }
            catch (ArgumentException) { }
        }
        var module = new ExplorerNavigationModule();
        Check(module.Settings.Select(value => value.Key).Distinct().Count() == module.Settings.Count, "Unique setting keys.");
        foreach (var value in module.Settings.Where(value => value.Kind == SettingKind.Number))
            Check((int)value.Default! >= value.Min && (int)value.Default! <= value.Max, "Valid numeric default: " + value.Key);
        Check(module.Settings.Any(value => value.Key == "hide-home") && module.Settings.Any(value => value.Key == "hide-gallery"), "Home and Gallery visibility controls.");
        var settings = new MemorySettings();
        settings.Set("style-enabled", false);
        settings.Set("text", "invalid");
        module.Initialize(new Context(settings));
        const string home = @"Software\Classes\CLSID\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}";
        using var beforeKey = Registry.CurrentUser.OpenSubKey(home);
        var before = beforeKey?.GetValue("System.IsPinnedToNameSpaceTree");
        try { module.Activate(); throw new Exception("Invalid appearance was accepted."); }
        catch (ArgumentException) { }
        using var afterKey = Registry.CurrentUser.OpenSubKey(home);
        Check(Equals(before, afterKey?.GetValue("System.IsPinnedToNameSpaceTree")), "Failed activation preserves Home visibility.");
        Check(!module.IsActive && settings.GetString("namespace-backup") == "", "Failure cleans lifecycle and recovery state.");
        module.Deactivate();
        Check(!module.IsActive, "Repeated deactivation is safe.");
        VerifyFrequentFolderControl();
        if (arguments.Contains("--live")) VerifyExplorer();
        Console.WriteLine("PASS: navigation color validation, descriptor bounds, visibility controls, activation rollback and idempotent shutdown.");
    }
    private static void VerifyFolderDialog()
    {
        var module = new FolderDialogModule();
        module.Initialize(new Context(new MemorySettings()));
        using var runtime = Registry.CurrentUser.OpenSubKey(FolderDialogModule.RuntimeKey);
        Check(runtime?.GetValue("active") is not int active || active == 0, "Disable Modern Folder Dialog before running its tests.");
        var native = System.IO.Path.Combine(AppContext.BaseDirectory, "native");
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(native, "WinForge.FolderDialogFixture.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add(System.IO.Path.Combine(native, "WinForge.FolderDialogTap.dll"));
        start.ArgumentList.Add("--external");
        try
        {
            module.Activate();
            module.Activate();
            Check(module.IsActive, "Repeated activation remains active.");
            using var process = System.Diagnostics.Process.Start(start)!;
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!process.HasExited && deadline.Elapsed < TimeSpan.FromSeconds(20)) Pump(TimeSpan.FromMilliseconds(100));
            if (!process.HasExited) { process.Kill(); throw new Exception("Cross-process folder dialog test timed out."); }
            var output = process.StandardOutput.ReadToEnd();
            Check(process.ExitCode == 0, "Managed module injects and replaces a separate process's folder dialog: " + output);
            Console.Write(output);
        }
        finally { module.Deactivate(); module.Deactivate(); }
        using var stopped = Registry.CurrentUser.OpenSubKey(FolderDialogModule.RuntimeKey);
        Check(!module.IsActive && stopped?.GetValue("active") is 0, "Repeated deactivation removes the runtime lease.");
        Console.WriteLine("PASS: managed folder dialog lifecycle and cross-process replacement.");
    }

    private static void VerifyFrequentFolderControl()
    {
        var descriptor = new ExplorerNavigationModule().Settings.Single(setting => setting.Key == "block-frequent-folders");
        Check(descriptor.Kind == SettingKind.Toggle && descriptor.Default is false, "Automatic-folder control is opt-in.");
        var original = Reg.ReadDword(ExplorerNavigationModule.ExplorerKey, ExplorerNavigationModule.FrequentFoldersValue);
        var settings = new MemorySettings();
        settings.Set("style-enabled", false);
        var module = new ExplorerNavigationModule();
        module.Initialize(new Context(settings));
        try
        {
            module.Activate();
            Check(Reg.ReadDword(ExplorerNavigationModule.ExplorerKey, ExplorerNavigationModule.FrequentFoldersValue) == original, "Disabled control preserves the original frequent-folder preference.");
            var backup = JsonNode.Parse(settings.GetString("namespace-backup"))!.AsArray();
            var legacy = new JsonArray(backup.Where(entry => entry!["Name"]!.GetValue<string>() != ExplorerNavigationModule.FrequentFoldersValue).Select(entry => entry!.DeepClone()).ToArray());
            module.Deactivate();
            settings.Set("namespace-backup", legacy.ToJsonString());
            settings.Set("block-frequent-folders", true);
            module.Activate();
            Check(Reg.ReadDword(ExplorerNavigationModule.ExplorerKey, ExplorerNavigationModule.FrequentFoldersValue) == 0, "Enabled control disables automatic frequent folders at the Explorer registry root.");
            var migrated = JsonNode.Parse(settings.GetString("namespace-backup"))!.AsArray();
            Check(migrated.Count == backup.Count && legacy.All(entry => migrated.Any(candidate => JsonNode.DeepEquals(entry, candidate))), "Existing recovery records survive addition of the frequent-folder backup.");
            settings.Set("block-frequent-folders", false);
            module.Apply();
            Check(Reg.ReadDword(ExplorerNavigationModule.ExplorerKey, ExplorerNavigationModule.FrequentFoldersValue) == original, "Turning the control off restores the original preference, including a missing value.");
            settings.Set("block-frequent-folders", true);
            module.Apply();
        }
        finally { module.Deactivate(); }
        Check(Reg.ReadDword(ExplorerNavigationModule.ExplorerKey, ExplorerNavigationModule.FrequentFoldersValue) == original, "Module shutdown restores the frequent-folder preference.");
        Console.WriteLine("PASS: automatic frequent-folder control, recovery migration and preference restoration.");
    }
    private static void VerifyExplorer()
    {
        var settings = new MemorySettings();
        var module = new ExplorerNavigationModule();
        module.Initialize(new Context(settings));
        var trees = new List<nint>();
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "CabinetWClass") return true;
            EnumChildWindows(window, (child, _) =>
            {
                var childName = new StringBuilder(128);
                GetClassName(child, childName, childName.Capacity);
                if (childName.ToString() == "SysTreeView32") trees.Add(child);
                return true;
            }, 0);
            return true;
        }, 0);
        Check(trees.Count > 0, "Open an Explorer window for the live navigation check.");
        var originals = trees.ToDictionary(tree => tree, tree => SendMessage(tree, 0x111F, 0, 0));
        settings.Set("background", "#123456");
        settings.Set("font-size", 17);
        settings.Set("padding-vertical", 9);
        settings.Set("indent", 31);
        try
        {
            module.Activate();
            Pump(TimeSpan.FromSeconds(4));
            Check(trees.Any(tree => SendMessage(tree, 0x111F, 0, 0) == 0x563412), "Native style reaches real Explorer navigation.");
            Check(trees.Any(tree => SendMessage(tree, 0x111C, 0, 0) >= 40), "Row padding reaches real Explorer navigation.");
        }
        finally { module.Deactivate(); Pump(TimeSpan.FromSeconds(2)); }
        Check(trees.All(tree => SendMessage(tree, 0x111F, 0, 0) == originals[tree]), "Real Explorer background restores after deactivation.");
        Console.WriteLine("PASS: real Explorer navigation applies and restores custom styling.");
    }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, EnumWindow callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private sealed class MemorySettings : IModuleSettings
    {
        private readonly Dictionary<string, string> _values = [];
        public bool GetBool(string key, bool fallback = false) => bool.TryParse(GetString(key), out var value) ? value : fallback;
        public int GetInt(string key, int fallback = 0) => int.TryParse(GetString(key), out var value) ? value : fallback;
        public string GetString(string key, string fallback = "") => _values.GetValueOrDefault(key, fallback);
        public void Set(string key, object? value) => _values[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }
    private sealed class Context(IModuleSettings settings) : IModuleContext
    {
        public IModuleSettings Settings => settings;
        public ILogger Log => throw new NotSupportedException();
        public IHotkeyService Hotkeys => throw new NotSupportedException();
        public void Notify(string message) { }
    }
}
