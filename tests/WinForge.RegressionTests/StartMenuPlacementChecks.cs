using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class StartMenuPlacementChecks
{
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

        if (!Native.GetCursorPos(out var cursor)) return;
        var target = DesktopDisplays.ForPoint(cursor);
        var other = DesktopDisplays.Enumerate().FirstOrDefault(display => display.Handle != target.Handle);
        if (other is null) { Console.WriteLine("SKIP: Start cross-monitor fixture needs two displays."); return; }
        using var moving = new NativeWindowFixture("WinForge.StartPlacementFixture", "",
            new() { Left = other.WorkArea.Left + 40, Top = other.WorkArea.Top + 40,
                Right = other.WorkArea.Left + 440, Bottom = other.WorkArea.Top + 340 }, 0x80000000, 0x08000080);
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Func<IntPtr, bool>)], null)!;
        var module = (ITweakModule)constructor.Invoke([new Func<IntPtr, bool>(window => window == moving.Handle)]);
        module.Initialize(context);
        try
        {
            module.Activate();
            moving.Show();
            var clock = Stopwatch.StartNew();
            while (DesktopDisplays.ForWindow(moving.Handle).Handle != target.Handle && clock.ElapsedMilliseconds < 1500) Pump();
            check(DesktopDisplays.ForWindow(moving.Handle).Handle == target.Handle, "Native Start fixture moves to the cursor display on its show event");
        }
        finally { module.Deactivate(); module.Deactivate(); }
        check(!module.IsActive, "Start placement deactivation is idempotent after a move");
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
