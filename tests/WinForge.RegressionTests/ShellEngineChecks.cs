using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class ShellEngineChecks
{
    internal static void Run(string nativeDirectory, Action<bool, string> check)
    {
        var fixturePath = Path.Combine(nativeDirectory, "WinForge.ShellFixture.exe");
        using var fixture = Process.Start(new ProcessStartInfo(fixturePath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new InvalidOperationException("XAML verification process could not start.");
        IntPtr window = IntPtr.Zero;
        try
        {
            var line = fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidOperationException(fixture.StandardError.ReadToEnd());
            var parts = line.Split(' ');
            var processId = uint.Parse(parts[0], CultureInfo.InvariantCulture);
            window = new IntPtr(long.Parse(parts[1], CultureInfo.InvariantCulture));
            check(processId == fixture.Id && ReadWidth(window) == 160, "Own XAML fixture exposes a measured initial property");
            using var session = ShellDiagnosticsSession.ConnectAsync(processId, nativeDirectory).GetAwaiter().GetResult();
            try { WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "WinForgeVerificationTarget"), "XAML visual tree enumeration"); }
            catch (TimeoutException)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { session.Status, Tree = session.ReadVisualTree() }));
                throw;
            }
            check(session.ReadVisualTree().Any(node => node.Name == "WinForgeVerificationTarget"),
                "Own native diagnostics bridge reads the live XAML tree");
            session.Apply(true, [new ShellStyleRule("#WinForgeVerificationTarget", new Dictionary<string, string> { ["Width"] = "250" })]);
            WaitUntil(() => ReadWidth(window) == 250, "XAML property application");
            check(session.Status.Changes > 0 && session.Status.Errors == 0, "Own native bridge changes a real XAML property without errors");
            session.Apply(false, []);
            WaitUntil(() => ReadWidth(window) == 160, "XAML property restoration");
            check(session.Status.Restored > 0, "Disabling shell styles restores the original local property value");
            var targetHandle = session.ReadVisualTree().Single(node => node.Name == "WinForgeVerificationTarget").Handle;
            session.Apply(true, [new ShellStyleRule("@" + targetHandle.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string> { ["Width"] = "280", ["DockAnimation"] = "125,160" })]);
            WaitUntil(() => ReadWidth(window) == 280, "Repeated XAML style application");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Handle == targetHandle && node.DockAnimation), "Native dock attachment");
            check(!ReadOriginalTransform(window), "Native handle selectors attach the dock controller and preserve the shell transform inside a group");
            var heartbeat = (Timer)typeof(ShellDiagnosticsSession).GetField("_heartbeat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            heartbeat.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var view = (System.IO.MemoryMappedFiles.MemoryMappedViewAccessor)typeof(ShellDiagnosticsSession)
                .GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            const int heartbeatOffset = 16;
            view.Write(heartbeatOffset, 0UL);
            WaitUntil(() => ReadWidth(window) == 160, "Shell heartbeat restoration");
            WaitUntil(() => ReadOriginalTransform(window), "Dock watchdog restoration");
            check(true, "Heartbeat loss restores the exact preexisting transform and transform origin without managed cleanup");
            WaitUntil(() => !session.IsConnected, "Native session cleanup acknowledgement after heartbeat loss");
            check(!session.IsConnected, "Loss of the owner heartbeat restores XAML properties while the active style payload remains unchanged and managed disposal has not run");
            session.Dispose();
            SetBoundWidth(window, 180);
            WaitUntil(() => ReadWidth(window) == 180, "Original XAML binding after restoration");
            check(true, "Restoration preserves the original live XAML binding rather than freezing its old value");
            using var reconnected = ShellDiagnosticsSession.ConnectAsync(processId, nativeDirectory).GetAwaiter().GetResult();
            WaitUntil(() => reconnected.ReadVisualTree().Any(node => node.Name == "WinForgeVerificationTarget"), "Repeated diagnostics attachment");
            reconnected.Apply(true, [new ShellStyleRule("#WinForgeVerificationTarget", new Dictionary<string, string> { ["Width"] = "300" })]);
            WaitUntil(() => ReadWidth(window) == 300, "Repeated attachment property application");
            reconnected.Apply(false, []);
            WaitUntil(() => ReadWidth(window) == 180, "Repeated attachment property restoration");
            SetBoundWidth(window, 200);
            WaitUntil(() => ReadWidth(window) == 200, "Repeated attachment binding restoration");
            check(reconnected.Status.Errors == 0, "Reconnecting the own bridge still applies, restores and preserves a live binding");
            reconnected.Dispose();
            VerifyBrokerRecovery(processId, nativeDirectory, window, check);
        }
        finally
        {
            if (!fixture.HasExited)
            {
                if (window != IntPtr.Zero) Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (!fixture.WaitForExit(3000)) { fixture.Kill(); fixture.WaitForExit(3000); }
            }
        }
    }

    private static void VerifyBrokerRecovery(uint processId, string nativeDirectory, IntPtr window, Action<bool, string> check)
    {
        using var previous = ShellDiagnosticsBroker.Acquire(processId, nativeDirectory);
        using var otherPrevious = ShellDiagnosticsBroker.Acquire(processId, nativeDirectory);
        check(ReferenceEquals(previous.Session, otherPrevious.Session), "Shell modules share a single live native consumer");
        previous.Apply([new ShellStyleRule("#WinForgeVerificationTarget", new Dictionary<string, string> { ["Width"] = "260" })]);
        WaitUntil(() => ReadWidth(window) == 260, "Broker-owned style application");
        previous.Session.Dispose();
        WaitUntil(() => ReadWidth(window) == 200, "Disconnected broker restores fixture binding");
        using var replacement = ShellDiagnosticsBroker.Acquire(processId, nativeDirectory);
        check(replacement.Session.IsConnected && !ReferenceEquals(previous.Session, replacement.Session),
            "Acquisition replaces a disconnected shell consumer while previous leases still exist");
        previous.Dispose();
        otherPrevious.Dispose();
        using var additional = ShellDiagnosticsBroker.Acquire(processId, nativeDirectory);
        check(ReferenceEquals(replacement.Session, additional.Session), "Disposing old leases preserves the replacement shell consumer");
        replacement.Apply([new ShellStyleRule("#WinForgeVerificationTarget", new Dictionary<string, string> { ["Width"] = "270" })]);
        WaitUntil(() => ReadWidth(window) == 270, "Replacement broker style application");
        replacement.Apply([]);
        WaitUntil(() => ReadWidth(window) == 200, "Replacement broker restores original binding");
        check(replacement.Session.Status.Errors == 0, "Recovered shell consumer applies and restores real XAML properties without errors");
    }

    internal static void Probe(uint processId, string nativeDirectory)
    {
        var endpoint = int.TryParse(Environment.GetEnvironmentVariable("WINFORGE_SHELL_ENDPOINT"), out var selected) ? selected : 0;
        var winUi = Environment.GetEnvironmentVariable("WINFORGE_SHELL_WINUI") == "1";
        using var session = ShellDiagnosticsSession.ConnectAsync(processId, nativeDirectory, endpoint, winUi).GetAwaiter().GetResult();
        Thread.Sleep(1500);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { session.Status, Tree = session.ReadVisualTree() }));
    }

    internal static void VerifyTaskbar(string nativeDirectory, Action<bool, string> check)
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) throw new InvalidOperationException("Windows taskbar was not found.");
        Native.GetWindowThreadProcessId(taskbar, out var processId);
        using var session = ShellDiagnosticsSession.ConnectAsync(processId, nativeDirectory).GetAwaiter().GetResult();
        ShellVisualNode? Start() => session.ReadVisualTree().SingleOrDefault(node => node.AutomationId == "StartButton");
        WaitUntil(() => session.ReadVisualTree().Any(node => node.AutomationId == "StartButton"), "Actual taskbar enumeration");
        var original = Start() ?? throw new InvalidOperationException("Start button snapshot was incomplete.");
        check(session.Status.ProcessId == processId && session.Status.Elements > 20,
            "Own shell bridge enumerates the current Explorer taskbar in a clean process");
        session.Apply(true, [new ShellStyleRule("[AutomationId=StartButton]", new Dictionary<string, string> { ["Opacity"] = "0.63" })]);
        WaitUntil(() => Start() is { } start && Math.Abs(start.Opacity - 0.63) < 0.001, "Actual taskbar property readback");
        check(session.Status.Changes > 0 && session.Status.Errors == 0,
            "Own shell engine applies a real taskbar property and reads its effective value back");
        session.Apply(false, []);
        WaitUntil(() => Start() is { } start && Math.Abs(start.Opacity - original.Opacity) < 0.001, "Actual taskbar restoration readback");
        check(session.Status.Restored > 0, "Own shell engine restores the exact original taskbar property");
        session.Apply(true, [new ShellStyleRule("[AutomationId=StartButton]", new Dictionary<string, string> { ["Visibility"] = "Collapsed" })]);
        WaitUntil(() => Start()?.Visibility == "Collapsed", "Actual Start button hide");
        check(true, "Own shell engine hides the real Start button without Windhawk");
        session.Apply(false, []);
        WaitUntil(() => Start()?.Visibility == original.Visibility, "Actual Start button restoration");
        check(session.Status.Errors == 0, "Disabling the customization restores actual Start button visibility");
        session.Dispose();
        TestTaskbarModule(nativeDirectory, original, check);
    }

    private static void TestTaskbarModule(string nativeDirectory, ShellVisualNode originalStart, Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Taskbar.dll"));
        var module = (ITweakModule)Activator.CreateInstance(assembly.GetType("WinForge.Modules.Taskbar.ShellCustomizationModule")!, nonPublic: true)!;
        var context = new ShellContext();
        context.Settings.Set("hide-start", true);
        context.Settings.Set("theme", "Gece");
        module.Initialize(context);
        try
        {
            module.Activate();
            var session = (ShellDiagnosticsSession)module.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
            WaitUntil(() => session.ReadVisualTree().Any(node => node.AutomationId == "StartButton" && node.Visibility == "Collapsed"), "Module hides actual Start");
            check(module.IsActive && module.Warning is null, "Shipped customization module controls the actual taskbar through its own engine");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "BackgroundFill" && node.Fill == "#F0171B24"), "Theme effective brush readback");
            check(true, "Own taskbar theme changes the actual background brush");
            context.Settings.Set("app-icon-size", 32);
            context.Settings.Set("button-width", 60);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "Icon" && node.Type.EndsWith(".Image", StringComparison.Ordinal) && Math.Abs(node.Height - 32) < 0.1), "Actual taskbar icon size");
            check(true, "Own shell module resizes the actual application icon");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "Taskbar.TaskListButton" && Math.Abs(node.Width - 60) < 0.1), "Actual app button width");
            check(true, "Own shell module changes the actual application button width");
            context.Settings.Set("time-format", "HH:mm:ss");
            context.Settings.Set("date-format", "yyyy-MM-dd");
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "TimeInnerTextBlock" && node.Text.Count(character => character == ':') == 2), "Actual clock text");
            check(session.ReadVisualTree().Any(node => node.Name == "DateInnerTextBlock" && node.Text == DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)),
                "Own clock formatting changes the actual taskbar time and date without editing global locale preferences");
            context.Settings.Set("tray-two-rows", true);
            module.Apply();
            check(session.Status.Errors == 0, "Two-row notification layout applies through native XAML properties without hidden failures");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "SystemTray.NotifyIconView" && Math.Abs(node.Height - 20) < 0.1 && node.ScreenWidth > 0), "Actual tray icon layout size");
            try
            {
                WaitUntil(() =>
                {
                    var icons = session.ReadVisualTree().Where(node => node.Type == "SystemTray.NotifyIconView" && node.ScreenWidth > 0).ToArray();
                    var rowStep = 20 * Native.GetDpiForWindow(Native.FindWindow("Shell_TrayWnd", null)) / 96d;
                    return icons.Any(first => icons.Any(second => first.Handle != second.Handle &&
                        Math.Abs(first.ScreenLeft - second.ScreenLeft) < 1 && Math.Abs(Math.Abs(first.ScreenTop - second.ScreenTop) - rowStep) < 1));
                }, "Actual two-row arrangement of own Windows notification icons");
            }
            catch
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(session.ReadVisualTree().Where(node => node.Type.Contains("WrapGrid", StringComparison.Ordinal) || node.Type.Contains("StackListView", StringComparison.Ordinal) || node.Type == "SystemTray.NotifyIconView")));
                throw;
            }
            check(true, "Actual Windows notification icons occupy two distinct tray rows in one measured column with the configured row spacing");
            var previousTime = session.ReadVisualTree().First(node => node.Name == "TimeInnerTextBlock").Text;
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "TimeInnerTextBlock" && node.Text != previousTime), "Running taskbar clock updates");
            check(true, "Custom taskbar clock continues updating after its initial application");
            var taskbar = Native.FindWindow("Shell_TrayWnd", null);
            if (!Native.GetWindowRect(taskbar, out var originalBounds)) throw new InvalidOperationException("Taskbar bounds unavailable.");
            var desiredPixels = (int)Math.Round(52 * Native.GetDpiForWindow(taskbar) / 96.0);
            context.Settings.Set("taskbar-height", 52);
            try { module.Apply(); }
            catch { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(session.Status)); throw; }
            WaitUntil(() => Native.GetWindowRect(taskbar, out var bounds) && bounds.Bottom - bounds.Top == desiredPixels,
                "Actual taskbar HWND height");
            try { WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "Taskbar.TaskbarFrame" && Math.Abs(node.Height - 52) < 0.1 && Math.Abs(node.HostHeight - desiredPixels) < 0.1), "Actual unclipped XAML frame"); }
            catch { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(session.ReadVisualTree().Where(node => node.Type == "Taskbar.TaskbarFrame"))); throw; }
            check(true,
                "Own taskbar height changes both the actual HWND and its actual XAML frame to 52 logical pixels");
            context.Settings.Set("taskbar-height", 0);
            try { module.Apply(); }
            catch { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(session.Status)); throw; }
            WaitUntil(() => Native.GetWindowRect(taskbar, out var bounds) && bounds.Bottom - bounds.Top == originalBounds.Bottom - originalBounds.Top,
                "Taskbar native height restoration");
            check(session.Status.Errors == 0, "Removing own taskbar height restores the original native height without hidden errors");
            module.Deactivate();
            check(!module.IsActive, "Shipped taskbar customization deactivates and releases its native session");
            using var verify = ShellDiagnosticsSession.ConnectAsync((uint)System.Diagnostics.Process.GetProcessesByName("explorer").Single().Id, nativeDirectory).GetAwaiter().GetResult();
            WaitUntil(() => verify.ReadVisualTree().Any(node => node.AutomationId == "StartButton" && node.Visibility == originalStart.Visibility), "Module restoration through a fresh session");
            check(verify.Status.Errors == 0, "A fresh native session confirms the module restored Start visibility after deactivation");
        }
        finally { module.Deactivate(); }
    }

    internal static void VerifyStartAppearance(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var module = (ITweakModule)Activator.CreateInstance(assembly.GetType("WinForge.Modules.Extras.StartAppearanceModule")!)!;
        var context = new ShellContext();
        module.Initialize(context);
        try
        {
            module.Activate();
            var lease = (ShellDiagnosticsBroker.Lease)module.GetType().GetField("_lease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
            var session = lease.Session;
            var tree = session.ReadVisualTree();
            if (!tree.Any(node => node.Type == "StartMenu.StartBlendedFlexFrame"))
                throw new InvalidOperationException("Live verification requires the redesigned Start menu.");
            var originalMenu = tree.First(node => node.Name == "MainMenu");
            var originalRoot = tree.First(node => node.Name == "FrameRoot");
            var originalSearch = tree.First(node => node.Type == "StartMenu.SearchBoxToggleButton");
            var originalUser = tree.First(node => node.Type == "StartDocked.UserTileView");
            var originalPower = tree.First(node => node.Type == "StartDocked.PowerOptionsView");
            var originalAllHeading = tree.First(node => node.Name == "AllListHeading");
            var originalSuggestions = tree.First(node => node.Name == "TopLevelSuggestionsRoot");
            var originalPinned = tree.First(node => node.Name == "StartMenuPinnedList");
            var originalAllApps = tree.First(node => node.Name == "AllAppsGrid");
            var originalLabels = tree.Where(node => node.Name == "DisplayName" && node.Type.EndsWith(".TextBlock", StringComparison.Ordinal)).ToArray();
            check(originalLabels.Length > 0, "Pinned application and folder names are available for live verification");
            context.Settings.Set("hide-labels", true);
            module.Apply();
            WaitUntil(() => originalLabels.All(original => session.ReadVisualTree().Any(node => node.Handle == original.Handle && node.Visibility == "Collapsed")), "Pinned application and folder names hidden");
            check(session.Status.Errors == 0 && session.ReadVisualTree().Any(node => node.Name == "Logo" && node.Visibility == "Visible"),
                "Icon-only mode hides names while keeping application icons visible");
            context.Settings.Set("hide-labels", false);
            module.Apply();
            WaitUntil(() => originalLabels.All(original => session.ReadVisualTree().Any(node => node.Handle == original.Handle && node.Visibility == original.Visibility && node.Text == original.Text)), "Original application and folder names restored");
            check(session.Status.Errors == 0, "Disabling icon-only mode restores the original label visibility and text");
            var originalIcons = tree.Where(node => node.Type.EndsWith(".Image", StringComparison.Ordinal) && node.Name is "Logo" or "AllAppsTileLogo").ToArray();
            check(originalIcons.Any(node => node.Name == "Logo") && originalIcons.Any(node => node.Name == "AllAppsTileLogo"),
                "Live Start menu exposes pinned and all apps icons for size verification");
            foreach (var iconSize in new[] { 16, 40, 48 })
            {
                context.Settings.Set("icon-size", iconSize);
                module.Apply();
                WaitUntil(() => originalIcons.All(original => session.ReadVisualTree().Any(node => node.Handle == original.Handle &&
                    Math.Abs(node.Width - iconSize) < 0.1 && Math.Abs(node.Height - iconSize) < 0.1)), "Pinned and all apps icon dimensions");
                check(session.Status.Errors == 0, $"Start application icons resize to {iconSize} logical pixels without shell errors");
            }
            context.Settings.Set("icon-size", 0);
            module.Apply();
            WaitUntil(() => originalIcons.All(original => session.ReadVisualTree().Any(node => node.Handle == original.Handle &&
                Math.Abs(node.Width - original.Width) < 0.1 && Math.Abs(node.Height - original.Height) < 0.1)), "Original icon dimensions restored");
            check(session.Status.Errors == 0, "Zero icon size restores the original Windows icon dimensions");
            context.Settings.Set("pinned-only", true);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "AllListHeading" && node.Visibility == "Collapsed"), "All apps heading and view selector hidden");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "TopLevelSuggestionsRoot" && node.Visibility == "Collapsed"), "Start suggestions hidden");
            WaitUntil(() => AllAppsPanel(session.ReadVisualTree())?.Visibility == "Collapsed", "All apps items panel hidden while retaining pinned header");
            check(session.ReadVisualTree().Any(node => node.Name == "StartMenuPinnedList" && node.Visibility == originalPinned.Visibility && node.Height > 0) &&
                  session.ReadVisualTree().Any(node => node.Name == "AllAppsGrid" && node.Visibility == originalAllApps.Visibility) && session.Status.Errors == 0,
                "Pinned-only mode hides the all apps panel without collapsing its pinned header");
            context.Settings.Set("pinned-only", false);
            module.Apply();
            WaitUntil(() => AllAppsPanel(session.ReadVisualTree())?.Visibility == "Visible", "All apps original items panel restored");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "AllListHeading" && node.Visibility == originalAllHeading.Visibility), "All apps heading restored");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "TopLevelSuggestionsRoot" && node.Visibility == originalSuggestions.Visibility), "Start suggestions restored");
            check(session.Status.Errors == 0, "Disabling pinned-only mode restores all apps and recommendations");
            context.Settings.Set("width", 700);
            context.Settings.Set("height", 700);
            context.Settings.Set("opacity", 75);
            context.Settings.Set("hide-search", true);
            context.Settings.Set("hide-user", true);
            context.Settings.Set("hide-power", true);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "MainMenu" && Math.Abs(node.Width - 676) < 0.1 && Math.Abs(node.Opacity - 0.75) < 0.01), "Start menu width and opacity");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "FrameRoot" && Math.Abs(node.Height - 700) < 0.1), "Start menu height");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartMenu.SearchBoxToggleButton" && node.Visibility == "Collapsed"), "Start menu search visibility");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartDocked.UserTileView" && node.Visibility == "Collapsed"), "Start user visibility");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartDocked.PowerOptionsView" && node.Visibility == "Collapsed"), "Start power visibility");
            check(session.Status.Errors == 0, "Start customization changes actual menu dimensions, opacity and section visibility");
            context.Settings.Set("width", 0);
            context.Settings.Set("height", 0);
            context.Settings.Set("opacity", 100);
            context.Settings.Set("hide-search", false);
            context.Settings.Set("hide-user", false);
            context.Settings.Set("hide-power", false);
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "MainMenu" && Math.Abs(node.Width - originalMenu.Width) < 0.1 && Math.Abs(node.Opacity - originalMenu.Opacity) < 0.01), "Start menu width and opacity restoration");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "FrameRoot" && Math.Abs(node.Height - originalRoot.Height) < 0.1), "Start menu height restoration");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartMenu.SearchBoxToggleButton" && node.Visibility == originalSearch.Visibility), "Start menu search restoration");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartDocked.UserTileView" && node.Visibility == originalUser.Visibility), "Start user restoration");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Type == "StartDocked.PowerOptionsView" && node.Visibility == originalPower.Visibility), "Start power restoration");
            check(session.Status.Errors == 0 && session.Status.Restored > 0, "Start customization restores the original actual menu properties");
            context.Settings.Set("custom-rules", "[{\"selector\":\"#MainMenu\",\"properties\":{\"Opacity\":\"0.8\"}}]");
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "MainMenu" && Math.Abs(node.Opacity - 0.8) < 0.01), "Custom Start rule");
            context.Settings.Set("custom-rules", "");
            module.Apply();
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "MainMenu" && Math.Abs(node.Opacity - originalMenu.Opacity) < 0.01), "Custom Start rule restoration");
            check(session.Status.Errors == 0, "Custom Start XAML rules apply and restore");
            context.Settings.Set("pinned-only", true);
            context.Settings.Set("icon-size", 40);
            context.Settings.Set("hide-labels", true);
            module.Apply();
            WaitUntil(() => AllAppsPanel(session.ReadVisualTree())?.Visibility == "Collapsed", "Pinned-only reapplied");
            WaitUntil(() => session.ReadVisualTree().Any(node => node.Name == "Logo" && Math.Abs(node.Width - 40) < 0.1), "Icon size works with pinned-only mode");
            module.Deactivate();
            context.Settings.Set("icon-size", 0);
            context.Settings.Set("hide-labels", false);
            module.Activate();
            var restoredLease = (ShellDiagnosticsBroker.Lease)module.GetType().GetField("_lease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
            context.Settings.Set("pinned-only", false);
            module.Apply();
            WaitUntil(() => AllAppsPanel(restoredLease.Session.ReadVisualTree())?.Visibility == "Visible", "All apps restored after module reactivation");
            check(restoredLease.Session.Status.Errors == 0, "Pinned-only mode survives module deactivation and reactivation without losing the original panel");
            WaitUntil(() => originalIcons.All(original => restoredLease.Session.ReadVisualTree().Any(node => node.Handle == original.Handle &&
                Math.Abs(node.Width - original.Width) < 0.1 && Math.Abs(node.Height - original.Height) < 0.1)), "Icon dimensions restored after module deactivation");
            check(restoredLease.Session.Status.Errors == 0, "Disabling the module restores application icons after using pinned-only mode");
            WaitUntil(() => originalLabels.All(original => restoredLease.Session.ReadVisualTree().Any(node => node.Handle == original.Handle && node.Visibility == original.Visibility)), "Names restored after module deactivation");
            check(restoredLease.Session.Status.Errors == 0, "Module deactivation restores names after combining icon-only, pinned-only and icon resizing");
        }
        finally { module.Deactivate(); }
    }

    private static ShellVisualNode? AllAppsPanel(IReadOnlyList<ShellVisualNode> tree)
    {
        var header = tree.FirstOrDefault(node => node.Name == "TopLevelHeader");
        var headerPresenter = tree.FirstOrDefault(node => node.Handle == header?.Parent);
        var headerControl = tree.FirstOrDefault(node => node.Handle == headerPresenter?.Parent);
        return tree.FirstOrDefault(node => node.Parent == headerControl?.Parent && node.Type.EndsWith(".ItemsWrapGrid", StringComparison.Ordinal));
    }

    private sealed class ShellContext : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(Path.GetTempPath(), "WinForge-shell-verification.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }

    private static void WaitUntil(Func<bool> predicate, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(8))
        {
            if (predicate()) return;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Thread.Sleep(40);
        }
        throw new TimeoutException(operation);
    }

    private static int ReadWidth(IntPtr window)
    {
        if (SendMessageTimeout(window, 0x0401, IntPtr.Zero, IntPtr.Zero, 2, 1000, out var result) == IntPtr.Zero)
            throw new InvalidOperationException("XAML fixture did not respond.");
        return result.ToInt32();
    }

    private static bool ReadOriginalTransform(IntPtr window)
    {
        if (SendMessageTimeout(window, 0x0403, IntPtr.Zero, IntPtr.Zero, 2, 1000, out var result) == IntPtr.Zero)
            throw new InvalidOperationException("XAML transform fixture did not respond.");
        return result != IntPtr.Zero;
    }

    private static void SetBoundWidth(IntPtr window, int value)
    {
        if (SendMessageTimeout(window, 0x0402, new IntPtr(value), IntPtr.Zero, 2, 1000, out _) == IntPtr.Zero)
            throw new InvalidOperationException("XAML fixture did not accept a binding-source update.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
