using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class StartMenuPlacementChecks
{
    internal static void RunTaskbarLive(Action<bool, string> check)
    {
        var bars = new List<IntPtr>();
        Native.EnumWindows((window, _) => { if (Native.ClassNameOf(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") bars.Add(window); return true; }, IntPtr.Zero);
        var displays = DesktopDisplays.Enumerate();
        if (displays.Count < 2 || displays.Any(display => !bars.Any(window => DesktopDisplays.ForWindow(window).Handle == display.Handle)))
            throw new InvalidOperationException("Live taskbar check needs a Windows taskbar on every display.");
        if (!Native.GetCursorPos(out var cursor)) throw new InvalidOperationException("Pointer position unavailable.");
        var type = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"))
            .GetType("WinForge.Modules.Extras.CursorTaskbarModule")!;
        var module = (ITweakModule)Activator.CreateInstance(type)!;
        module.Initialize(new Context());
        ITweakModule? restarted = null;
        var autoHide = (bool)type.GetMethod("IsAutoHide", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        bool WaitFor(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            long stableSince = -1;
            while (clock.ElapsedMilliseconds < 5000)
            {
                Pump();
                if (!condition()) stableSince = -1;
                else if (stableSince < 0) stableSince = clock.ElapsedMilliseconds;
                else if (clock.ElapsedMilliseconds - stableSince >= 800) return true;
            }
            return false;
        }
        try
        {
            module.Activate();
            foreach (var target in displays)
            {
                Native.SetCursorPos(target.Bounds.Left + 100, target.Bounds.Top + 100);
                if (!autoHide)
                {
                    var success = WaitFor(() => bars.All(window => Native.IsWindowVisible(window) == (DesktopDisplays.ForWindow(window).Handle == target.Handle)));
                    Console.WriteLine("Taskbars: " + string.Join(", ", bars.Select(window => $"{Native.ClassNameOf(window)} display={DesktopDisplays.ForWindow(window).Handle} visible={Native.IsWindowVisible(window)}")) + $" target={target.Handle} warning={module.Warning}");
                    check(success, "Only the pointer display's taskbar is visible");
                }
                Native.SetCursorPos(target.Bounds.Left + (target.Bounds.Right - target.Bounds.Left) / 2, target.Bounds.Bottom - 1);
                var targetBar = bars.Single(window => DesktopDisplays.ForWindow(window).Handle == target.Handle);
                var revealed = WaitFor(() =>
                {
                    if (!Native.IsWindowVisible(targetBar) || !Native.GetWindowRect(targetBar, out var bounds)) return false;
                    var visibleHeight = Math.Min(bounds.Bottom, target.Bounds.Bottom) - Math.Max(bounds.Top, target.Bounds.Top);
                    return visibleHeight >= (bounds.Bottom - bounds.Top) / 2.0;
                });
                if (Native.GetWindowRect(targetBar, out var rect))
                    Console.WriteLine($"Reveal: target={target.Handle} taskbar=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) screen=({target.Bounds.Left},{target.Bounds.Top},{target.Bounds.Right},{target.Bounds.Bottom})");
                check(revealed, "The taskbar actually reveals at the screen edge");
            }
            // Simulate an abrupt exit: the timer stops but Deactivate never runs.
            // A new process must recover the taskbar hidden by its predecessor.
            ((DispatcherTimer)type.GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!).Stop();
            var hiddenBar = autoHide ? bars[0] : bars.Single(window => !Native.IsWindowVisible(window));
            if (autoHide)
            {
                type.GetMethod("ShowWindowAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [hiddenBar, 0]);
                check(WaitFor(() => !Native.IsWindowVisible(hiddenBar)), "Interrupted taskbar hide was reproduced");
            }
            var hiddenDisplay = displays.Single(display => display.Handle == DesktopDisplays.ForWindow(hiddenBar).Handle);
            Native.SetCursorPos(hiddenDisplay.Bounds.Left + 100, hiddenDisplay.Bounds.Top + 100);
            restarted = (ITweakModule)Activator.CreateInstance(type)!;
            restarted.Initialize(new Context());
            restarted.Activate();
            check(WaitFor(() => Native.IsWindowVisible(hiddenBar)), "A new instance recovers a taskbar hidden by an interrupted instance");
        }
        finally { restarted?.Deactivate(); module.Deactivate(); Native.SetCursorPos(cursor.X, cursor.Y); }
        check(WaitFor(() => bars.All(Native.IsWindowVisible)), "Taskbars hidden by the feature are restored on deactivation");
    }
    internal static void Run(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = assembly.GetType("WinForge.Modules.Extras.StartMenuPlacementModule")!;
        var calculate = type.GetMethod("CalculateBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        RECT Bounds(RECT source, RECT from, RECT to) => (RECT)calculate.Invoke(null, [source, from, to])!;
        var from = new RECT { Right = 1920, Bottom = 1040 };
        var to = new RECT { Left = -1280, Top = -200, Right = 0, Bottom = 784 };
        var centered = Bounds(new() { Left = 560, Top = 332, Right = 1360, Bottom = 1032 }, from, to);
        check(centered.Left == -1040 && centered.Top == 76 && centered.Right == -240 && centered.Bottom == 776,
            "Start placement preserves centered alignment and taskbar gap on a negative-coordinate display");
        var left = Bounds(new() { Left = 0, Top = 332, Right = 800, Bottom = 1032 }, from, to);
        check(left.Left == to.Left && left.Bottom == to.Bottom - 8, "Left-aligned Start remains left-aligned on the cursor display");
        var small = Bounds(new() { Left = 560, Top = 332, Right = 1360, Bottom = 1032 }, from,
            new() { Left = 2000, Top = 100, Right = 2640, Bottom = 580 });
        check(small.Left == 2000 && small.Top == 100 && small.Right == 2640 && small.Bottom == 580,
            "Oversized Start fits inside a smaller display work area");

        var context = new Context();
        var real = (ITweakModule)Activator.CreateInstance(type)!;
        real.Initialize(context);
        using var fixture = new NativeWindowFixture("Windows.UI.Core.CoreWindow", "WinForge Start placement fixture",
            new() { Left = 80, Top = 80, Right = 480, Bottom = 380 }, 0x80000000, 0x08000080);
        try
        {
            real.Activate();
            fixture.Show();
            Pump();
            Native.GetWindowRect(fixture.Handle, out var unchanged);
            check(unchanged.Left == 80 && unchanged.Top == 80, "Start placement ignores CoreWindow windows belonging to other processes");
        }
        finally { real.Deactivate(); }
        check(!real.IsActive, "Start placement releases its native event subscriptions");

        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Func<IntPtr, bool>)], null)!;
        var onEvent = type.GetMethod("OnEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var openingsField = type.GetField("_openings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var timerField = type.GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lifecycle = (ITweakModule)constructor.Invoke([new Func<IntPtr, bool>(window => window == fixture.Handle)]);
        lifecycle.Initialize(context);
        try
        {
            lifecycle.Activate();
            var openings = (System.Collections.IDictionary)openingsField.GetValue(lifecycle)!;
            onEvent.Invoke(lifecycle, [new WindowEventObserver.WindowEvent(0x8018, fixture.Handle)]);
            check(openings.Contains(fixture.Handle), "Uncloaking Start begins a placement session without a SHOW event");
            var firstOpening = openings[fixture.Handle];
            onEvent.Invoke(lifecycle, [new WindowEventObserver.WindowEvent(WindowEventObserver.Foreground, fixture.Handle)]);
            check(ReferenceEquals(firstOpening, openings[fixture.Handle]), "Repeated foreground events retain the same opening");
            onEvent.Invoke(lifecycle, [new WindowEventObserver.WindowEvent(0x8017, fixture.Handle)]);
            check(openings.Count == 0, "Cloaking closes the placement session even while WS_VISIBLE remains set");
            onEvent.Invoke(lifecycle, [new WindowEventObserver.WindowEvent(0x8018, fixture.Handle)]);
            check(openings.Contains(fixture.Handle) && !ReferenceEquals(firstOpening, openings[fixture.Handle]),
                "Reopening the same Start window captures a new monitor selection");
            onEvent.Invoke(lifecycle, [new WindowEventObserver.WindowEvent(WindowEventObserver.Foreground, new IntPtr(123))]);
            check(openings.Contains(fixture.Handle), "A foreground change does not stop tracking an open Start window");
            check(((DispatcherTimer)timerField.GetValue(lifecycle)!).IsEnabled, "Foreground fallback remains enabled between shell events");
        }
        finally { lifecycle.Deactivate(); }

        if (!Native.GetCursorPos(out var cursor)) return;
        var target = DesktopDisplays.ForPoint(cursor);
        var other = DesktopDisplays.Enumerate().FirstOrDefault(display => display.Handle != target.Handle);
        if (other is null) { Console.WriteLine("SKIP: Start cross-monitor fixture needs two displays."); return; }
        using var moving = new NativeWindowFixture("WinForge.StartPlacementFixture", "",
            new() { Left = other.WorkArea.Left + 40, Top = other.WorkArea.Top + 40,
                Right = other.WorkArea.Left + 440, Bottom = other.WorkArea.Top + 340 }, 0x80000000, 0x08000080);
        var module = (ITweakModule)constructor.Invoke([new Func<IntPtr, bool>(window => window == moving.Handle)]);
        module.Initialize(context);
        try
        {
            module.Activate();
            moving.Show();
            var clock = Stopwatch.StartNew();
            while (DesktopDisplays.ForWindow(moving.Handle).Handle != target.Handle && clock.ElapsedMilliseconds < 1500) Pump();
            check(DesktopDisplays.ForWindow(moving.Handle).Handle == target.Handle, "Native Start fixture moves to the cursor display on its show event");
            clock.Restart();
            while (clock.ElapsedMilliseconds < 900) Pump();
            Native.SetWindowPos(moving.Handle, IntPtr.Zero, other.WorkArea.Left + 40, other.WorkArea.Top + 40,
                400, 300, 0x0004 | Native.SWP_NOACTIVATE);
            clock.Restart();
            while (DesktopDisplays.ForWindow(moving.Handle).Handle != target.Handle && clock.ElapsedMilliseconds < 1500) Pump();
            check(DesktopDisplays.ForWindow(moving.Handle).Handle == target.Handle,
                "Start remains on the captured display after a shell layout reset later than 750 ms");
        }
        finally { module.Deactivate(); module.Deactivate(); }
        check(!module.IsActive, "Start placement deactivation is idempotent after a move");

        using var search = new NativeWindowFixture("WinForge.SearchPlacementFixture", "",
            new() { Left = other.WorkArea.Left + 40, Top = other.WorkArea.Top + 40,
                Right = other.WorkArea.Left + 440, Bottom = other.WorkArea.Top + 340 }, 0x80000000, 0x08000080);
        var sessionConstructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(Func<IntPtr, bool>), typeof(Func<IntPtr, bool>)], null)!;
        var session = (ITweakModule)sessionConstructor.Invoke([
            new Func<IntPtr, bool>(window => window == moving.Handle),
            new Func<IntPtr, bool>(window => window == search.Handle)]);
        session.Initialize(context);
        void Event(uint kind, IntPtr window) => onEvent.Invoke(session, [new WindowEventObserver.WindowEvent(kind, window)]);
        try
        {
            session.Activate();
            Event(WindowEventObserver.ObjectShow, moving.Handle);
            Native.SetCursorPos(other.WorkArea.Left + 100, other.WorkArea.Top + 100);
            Event(0x8017, moving.Handle);
            search.Show();
            Event(WindowEventObserver.ObjectShow, search.Handle);
            check(DesktopDisplays.ForWindow(search.Handle).Handle == other.Handle,
                "Search opens on the current pointer display after Start hides");
            Event(WindowEventObserver.ObjectShow, moving.Handle);
            check(DesktopDisplays.ForWindow(moving.Handle).Handle == other.Handle,
                "Returning from Search to Start follows the current pointer display");
            Native.SetCursorPos(target.WorkArea.Left + 100, target.WorkArea.Top + 100);
            var clock = Stopwatch.StartNew();
            while ((DesktopDisplays.ForWindow(moving.Handle).Handle != target.Handle ||
                    DesktopDisplays.ForWindow(search.Handle).Handle != target.Handle) && clock.ElapsedMilliseconds < 1500) Pump();
            check(DesktopDisplays.ForWindow(moving.Handle).Handle == target.Handle && DesktopDisplays.ForWindow(search.Handle).Handle == target.Handle,
                "Open Start and Search windows follow pointer changes between displays");
            Event(0x8017, search.Handle);
            Native.SetWindowPos(search.Handle, IntPtr.Zero, other.WorkArea.Left + 40, other.WorkArea.Top + 40,
                400, 300, 0x0004 | Native.SWP_NOACTIVATE);
            Event(0x8018, search.Handle);
            check(DesktopDisplays.ForWindow(search.Handle).Handle == target.Handle,
                "Search reopening follows the current pointer display");
            Native.SetWindowPos(search.Handle, IntPtr.Zero, other.WorkArea.Left + 40, other.WorkArea.Top + 40,
                400, 300, 0x0004 | Native.SWP_NOACTIVATE);
            clock.Restart();
            while (DesktopDisplays.ForWindow(search.Handle).Handle != target.Handle && clock.ElapsedMilliseconds < 1500) Pump();
            check(DesktopDisplays.ForWindow(search.Handle).Handle == target.Handle,
                "Search layout resets return to the pointer display");
            Event(0x8017, moving.Handle);
            Event(0x8017, search.Handle);
            Native.SetCursorPos(other.WorkArea.Left + 100, other.WorkArea.Top + 100);
            Event(0x8018, search.Handle);
            check(DesktopDisplays.ForWindow(search.Handle).Handle == other.Handle,
                "An independent Search opening uses the current cursor display");
        }
        finally { session.Deactivate(); Native.SetCursorPos(cursor.X, cursor.Y); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private sealed class Context : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(Path.GetTempPath(), "WinForge-start-placement-tests.log"));
        public IHotkeyService Hotkeys => throw new InvalidOperationException("Start placement must preserve Windows shortcuts.");
        public void Notify(string message) { }
    }
}
