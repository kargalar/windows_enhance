using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class TaskbarCustomizationChecks
{
    private static Assembly ModuleAssembly => Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Taskbar.dll"));

    internal static void Run(string directory, Action<bool, string> check)
    {
        TestMigration(directory, check);
        var module = (ITweakModule)Activator.CreateInstance(ModuleAssembly.GetType("WinForge.Modules.Taskbar.TaskbarModule")!)!;
        module.Initialize(new Context());
        check(module.Settings.Select(setting => setting.Key).Distinct().Count() == module.Settings.Count,
            "Unified taskbar settings have no collisions between registry icon size, visual icon size and tray entries");
        var discovered = ModuleAssembly.GetTypes().Where(type => typeof(ITweakModule).IsAssignableFrom(type) &&
            !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null).ToArray();
        check(discovered.Length == 1 && discovered[0] == module.GetType(), "Only the unified taskbar module can be discovered by the host");
        check(new[] { "hide-start", "hide-volume", "hide-network", "hide-battery", "hide-microphone", "hide-location", "hide-studio-effects", "hide-recall", "hide-language", "hide-bell", "hide-show-desktop", "show-desktop-width", "dock-animation" }
            .All(key => module.Settings.Any(setting => setting.Key == key)), "The unified panel exposes every requested visibility and dock control");
        TestRules(check);
    }

    private static void TestMigration(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "taskbar-migration.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Enabled = new[] { "shell-customization", "tray-icons", "unrelated" },
            Values = new Dictionary<string, Dictionary<string, string>>
            {
                ["taskbar"] = new() { ["icon-size"] = "Orta", ["theme"] = "Cam" },
                ["shell-customization"] = new() { ["icon-size"] = "32", ["theme"] = "Gece", ["hide-start"] = "true" },
                ["tray-icons"] = new() { ["tray-example"] = "false" }
            }
        }));
        var store = new SettingsStore(path);
        check(store.Enabled.SetEquals(["taskbar", "unrelated"]) && !store.Values.ContainsKey("shell-customization") && !store.Values.ContainsKey("tray-icons"),
            "Legacy enabled modules migrate to one taskbar module while preserving unrelated modules");
        var values = store.For("taskbar");
        check(values["icon-size"] == "Orta" && values["app-icon-size"] == "32" && values["theme"] == "Cam" && values["hide-start"] == "true" && values["tray-example"] == "false",
            "Taskbar migration preserves both icon sizes, current-setting precedence and individual tray choices");
        check(values["behavior-enabled"] == "false" && values["design-enabled"] == "true" && values["tray-enabled"] == "true",
            "Migration preserves the independent activation state of each legacy taskbar component");
        store.Save();
        check(new SettingsStore(path).For("taskbar").OrderBy(pair => pair.Key).SequenceEqual(values.OrderBy(pair => pair.Key)), "Taskbar migration is idempotent after save and reload");
    }

    private static ShellVisualNode Node(ulong handle, ulong parent, string type, string name = "", string text = "") =>
        new(handle, parent, type, name, "", "", 20, 20, 1, "Visible", text, "", 0, 0, 20, 20);

    private static IReadOnlyList<ShellStyleRule> Rules(IModuleSettings settings, IReadOnlyList<ShellVisualNode> tree) =>
        (IReadOnlyList<ShellStyleRule>)ModuleAssembly.GetType("WinForge.Modules.Taskbar.TaskbarSystemIcons")!
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic, [typeof(IModuleSettings), typeof(IReadOnlyList<ShellVisualNode>)])!.Invoke(null, [settings, tree])!;

    private static void TestRules(Action<bool, string> check)
    {
        ShellVisualNode[] tree =
        [
            Node(1, 0, "SystemTray.OmniButton", "ControlCenterButton"),
            Node(2, 1, "SystemTray.IconView"), Node(3, 2, "Windows.UI.Xaml.Controls.TextBlock", text: "\uE993"),
            Node(4, 1, "SystemTray.IconView"), Node(5, 4, "Windows.UI.Xaml.Controls.TextBlock", text: "\uE839"),
            Node(6, 1, "SystemTray.IconView"), Node(7, 6, "SystemTray.BatteryIconContent"),
            Node(10, 0, "SystemTray.OmniButton", "NotificationCenterButton"),
            Node(11, 10, "SystemTray.IconView"), Node(12, 11, "Windows.UI.Xaml.Controls.TextBlock", text: "\uF2A3"),
            Node(13, 10, "SystemTray.IconView"), Node(14, 13, "SystemTray.DateTimeIconContent"),
            Node(20, 0, "SystemTray.Stack", "ShowDesktopStack"), Node(21, 20, "SystemTray.IconView"),
            Node(30, 0, "SystemTray.IconView"), Node(31, 30, "Windows.UI.Xaml.Controls.TextBlock", text: "\uF47F"),
            Node(40, 0, "SystemTray.IconView"), Node(41, 40, "SystemTray.LanguageTextIconContent"),
            Node(50, 0, "SystemTray.NotifyIconView"), Node(51, 50, "Windows.UI.Xaml.Controls.TextBlock", text: "\uE993")
        ];
        var settings = new Context().Settings;
        check(Rules(settings, tree).Count == 0, "Default system icon settings leave every shell property untouched");
        foreach (var key in new[] { "volume", "network", "battery", "language" }) settings.Set("hide-" + key, true);
        settings.Set("hide-bell", "when-empty");
        settings.Set("show-desktop-width", 999);
        var rules = Rules(settings, tree);
        check(new[] { "@2", "@4", "@6", "@11", "@40" }.All(selector => rules.Any(rule => rule.Selector == selector && rule.Properties["Visibility"] == "Collapsed")) &&
              rules.All(rule => rule.Selector is not "@13" and not "@50"), "System icons hide without hiding the clock or an application's similarly shaped tray icon");
        check(rules.Single(rule => rule.Selector == "@21").Properties["Width"] == "100", "Desktop width is bounded independently of untrusted stored settings");
        settings.Set("hide-show-desktop", true);
        check(Rules(settings, tree).Single(rule => rule.Selector == "@21").Properties.Count == 1, "Desktop hiding takes precedence over its width override");
        tree[9] = tree[9] with { Text = "\uF2A5" };
        check(Rules(settings, tree).All(rule => rule.Selector != "@11"), "New notifications reveal a bell hidden by the empty-notifications mode");
        settings.Set("hide-bell", "always");
        check(Rules(settings, tree).Any(rule => rule.Selector == "@11"), "Always-hide bell mode also hides a full bell");
        settings.Set("hide-microphone", true);
        check(Rules(settings, tree).All(rule => rule.Selector != "@30"), "A shared microphone/location indicator remains when location is still requested");
        settings.Set("hide-location", true);
        check(Rules(settings, tree).Any(rule => rule.Selector == "@30"), "A combined privacy indicator hides only when both settings request hiding");
        settings.Set("dock-animation", true);
        settings.Set("dock-scale", 999);
        settings.Set("dock-duration", 1);
        var catalog = ModuleAssembly.GetType("WinForge.Modules.Taskbar.TaskbarStyleCatalog")!.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;
        var styles = (IReadOnlyList<ShellStyleRule>)catalog.Invoke(null, [settings, DateTime.Now, tree])!;
        check(styles.Single(rule => rule.Properties.ContainsKey("DockAnimation")).Properties["DockAnimation"] == "160,50", "Dock animation scale and duration are clamped before crossing the native bridge");
        settings.Set("taskbar-height", 32);
        styles = (IReadOnlyList<ShellStyleRule>)catalog.Invoke(null, [settings, DateTime.Now, tree])!;
        check(styles.Single(rule => rule.Selector == "Taskbar.TaskbarFrame").Properties["Height"] == "32" &&
            styles.Single(rule => rule.Selector == "Taskbar.TaskListButton" && rule.Properties.ContainsKey("MinHeight")).Properties["MinHeight"] == "0" &&
            styles.Single(rule => rule.Selector == "Image#Icon").Properties["Height"] == "24",
            "Compact taskbar height shrinks the frame and button minimums with fitting default icons");
    }

    internal static void RunLive(Action<bool, string> check)
    {
        var type = ModuleAssembly.GetType("WinForge.Modules.Taskbar.TaskbarModule")!;
        var module = (ITweakModule)Activator.CreateInstance(type)!;
        var context = new Context();
        context.Settings.Set("behavior-enabled", false);
        context.Settings.Set("tray-enabled", false);
        context.Settings.Set("hide-start", true);
        module.Initialize(context);
        try
        {
            module.Activate();
            var design = type.GetField("_design", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
            var session = (ShellDiagnosticsSession)design.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(design)!;
            WaitUntil(() => session.ReadVisualTree().Any(node => node.AutomationId == "StartButton" && node.Visibility == "Collapsed"), "Start visibility");
            check(true, "The taskbar engine hides the live Start button");
            foreach (var setting in new[] { "hide-volume", "hide-network", "hide-battery", "hide-language" })
            {
                var original = ReadTree(session);
                context.Settings.Set(setting, true);
                var targets = Rules(context.Settings, original).Where(rule => rule.Properties.ContainsKey("Visibility")).Select(rule => ulong.Parse(rule.Selector.AsSpan(1), CultureInfo.InvariantCulture)).ToHashSet();
                module.Apply();
                if (targets.Count > 0)
                {
                    WaitUntil(() => targets.All(handle => session.ReadVisualTree().Any(node => node.Handle == handle && node.Visibility == "Collapsed")), setting);
                    check(true, $"Live taskbar applies {setting} to the identified system icon");
                }
                else Console.WriteLine($"SKIP: {setting} is not present on this machine.");
                context.Settings.Set(setting, false);
                module.Apply();
                WaitUntil(() => targets.All(handle => session.ReadVisualTree().Any(node => node.Handle == handle && node.Visibility == original.Single(before => before.Handle == handle).Visibility)), setting + " restoration");
            }
            context.Settings.Set("show-desktop-width", 24);
            var desktopRules = Rules(context.Settings, ReadTree(session));
            var desktopHandles = desktopRules.Where(rule => rule.Properties.ContainsKey("Width")).Select(rule => ulong.Parse(rule.Selector.AsSpan(1), CultureInfo.InvariantCulture)).ToArray();
            module.Apply();
            if (desktopHandles.Length > 0)
            {
                WaitUntil(() => desktopHandles.All(handle => session.ReadVisualTree().Any(node => node.Handle == handle && Math.Abs(node.Width - 24) < 0.1)), "Desktop width");
                check(true, "The live Show desktop button accepts a width of 24 logical pixels");
            }
            context.Settings.Set("hide-show-desktop", true);
            module.Apply();
            WaitUntil(() => desktopHandles.All(handle => session.ReadVisualTree().Any(node => node.Handle == handle && node.Visibility == "Collapsed")), "Desktop hide");
            check(desktopHandles.Length > 0, "The live Show desktop button can be hidden");
            context.Settings.Set("dock-animation", true);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "Taskbar.TaskListButton" && node.DockAnimation), "Dock attachment");
            check(true, "Live taskbar buttons attach the native dock animation controller");
            if (!Native.GetCursorPos(out var cursor)) throw new InvalidOperationException("Cursor position unavailable.");
            try
            {
                var taskbar = Native.FindWindow("Shell_TrayWnd", null);
                var screen = DesktopDisplays.ForWindow(taskbar).Bounds;
                Native.SetCursorPos((screen.Left + screen.Right) / 2, screen.Bottom - 1);
                WaitUntil(() => Native.GetWindowRect(taskbar, out var bounds) && ShellTaskbar.IsTaskbarRevealed(bounds, screen), "Reveal auto-hidden taskbar");
                ShellVisualNode? button = null;
                WaitUntil(() =>
                {
                    button = session.ReadVisualTree().FirstOrDefault(node => node.Type == "Taskbar.TaskListButton" && node.DockAnimation &&
                        node.ScreenWidth > 0 && node.ScreenTop >= screen.Top && node.ScreenTop + node.ScreenHeight <= screen.Bottom + 1);
                    return button is not null;
                }, "Revealed taskbar button geometry");
                if (button is null) throw new InvalidOperationException("No revealed application button was found.");
                Native.SetCursorPos((int)(button.ScreenLeft + button.ScreenWidth / 2), (int)(button.ScreenTop + button.ScreenHeight / 2));
                WaitUntil(() => session.ReadVisualTree().Any(node => node.Handle == button.Handle && node.DockScale > 1.1), "Dock hover magnification");
                check(true, "Moving over a real taskbar button magnifies its icon");
                Native.SetCursorPos((int)button.ScreenLeft, (int)button.ScreenTop - 50);
                WaitUntil(() => session.ReadVisualTree().Any(node => node.Handle == button.Handle && Math.Abs(node.DockScale - 1) < 0.01), "Dock pointer exit");
                check(true, "Leaving the taskbar button restores the icon scale");
            }
            finally { Native.SetCursorPos(cursor.X, cursor.Y); }
            context.Settings.Set("dock-animation", false);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().All(node => !node.DockAnimation), "Dock cleanup");
            check(session.Status.Errors == 0, "Disabling dock animation releases all live controllers without native errors");
        }
        finally { module.Deactivate(); }
    }

    private static IReadOnlyList<ShellVisualNode> ReadTree(ShellDiagnosticsSession session)
    {
        IReadOnlyList<ShellVisualNode> tree = [];
        WaitUntil(() => { tree = session.ReadVisualTree(); return tree.Count > 0; }, "Consistent native visual snapshot");
        return tree;
    }

    private static void WaitUntil(Func<bool> predicate, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (predicate()) return;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Thread.Sleep(40);
        }
        throw new TimeoutException(operation);
    }

    private sealed class Context : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(Path.GetTempPath(), "WinForge-taskbar-tests.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }
}
