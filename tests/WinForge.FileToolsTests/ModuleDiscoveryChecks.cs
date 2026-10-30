using WinForge.Core;

internal static class ModuleDiscoveryChecks
{
    internal static void Run(string modulesDirectory, Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-ModuleDiscovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            var logger = new FileLogger(Path.Combine(root, "modules.log"));
            var packageRoot = Directory.GetParent(Path.GetFullPath(modulesDirectory))?.FullName ?? throw new InvalidOperationException("Package root missing.");
            foreach (var notice in new[] { "LICENSE-ONNX-Runtime.txt", "THIRD-PARTY-NOTICES-ONNX-Runtime.txt", "LICENSE-Tesseract-core-and-data.txt", "NOTICE-Tesseract-dotnet.txt" })
                check(File.Exists(Path.Combine(packageRoot, "licenses", "ClipboardLine", notice)), $"Published package includes {notice}.");
            var host = new ModuleHost(store, logger, new NoHotkeys(), Path.GetFullPath(modulesDirectory), _ => { });
            host.Load();
            foreach (var id in new[] { "nashi", "clipboardline" })
            {
                var matches = host.Modules.Where(module => module.Id == id).ToArray();
                check(matches.Length == 1, $"Published module host discovers exactly one {id} module.");
                var module = matches.Single();
                check(module.Category == "Verimlilik" && !module.IsActive, $"{id} loads inactive in the productivity category.");
                check(module.Settings.Any(setting => setting.Key == "open" && setting.Kind == SettingKind.Action && setting.Execute is not null), $"{id} contributes a usable open action.");
                check(module.Settings.Any(setting => setting.Key == "hotkey"), $"{id} exposes its configurable global shortcut.");
            }
        }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar); var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("WinForge-ModuleDiscovery-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private sealed class NoHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => throw new InvalidOperationException("Discovery must not register shortcuts.");
    }
}
