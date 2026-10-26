using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class PopupEngineChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var type = assembly.GetType("WinForge.Modules.Extras.BrowserFullscreenModule")!;
        var module = (ITweakModule)Activator.CreateInstance(type)!;
        var context = new Context();
        using var process = Process.GetCurrentProcess();
        context.Settings.Set("processes", process.ProcessName);
        module.Initialize(context);
        var bounds = DesktopDisplays.Enumerate().First(display => display.IsPrimary).Bounds;
        var center = (bounds.Left + bounds.Right) / 2;
        var tip = new RECT { Left = center - 200, Right = center + 200, Top = bounds.Top + 24, Bottom = bounds.Top + 88 };
        const string browserClass = "Chrome_WidgetWin_1";
        const uint popup = 0x80000000;
        const uint menu = 0x00080000;
        const uint requiredExtended = 0x080000A8;
        using var browser = new NativeWindowFixture(browserClass, "WinForge browser fixture", bounds, popup | menu, 0x08000080);
        using var candidate = new NativeWindowFixture(browserClass, "", tip, popup, requiredExtended, browser.Handle);
        using var permission = new NativeWindowFixture(browserClass, "WinForge permission fixture", tip, popup | menu, requiredExtended, browser.Handle);
        using var tooltip = new NativeWindowFixture(browserClass, "WinForge tooltip fixture", new RECT
            { Left = bounds.Left + 20, Right = bounds.Left + 420, Top = tip.Top, Bottom = tip.Bottom }, popup, requiredExtended, browser.Handle);
        browser.Show(behindOtherWindows: true);
        candidate.Show();
        permission.Show();
        tooltip.Show();
        try
        {
            module.Activate();
            WaitUntil(() => !Native.IsWindowVisible(candidate.Handle), "Own browser tip event handling");
            check(module.IsActive && Native.IsWindowVisible(browser.Handle), "Own popup engine hides the actual fixture tip while preserving its fullscreen owner");
            check(Native.IsWindowVisible(permission.Handle), "Popup policy preserves system-menu/permission windows with similar styles");
            check(Native.IsWindowVisible(tooltip.Handle), "Popup policy preserves off-center browser tooltips");
            module.Deactivate();
            WaitUntil(() => Native.IsWindowVisible(candidate.Handle), "Own browser tip restoration");
            check(Native.IsWindowVisible(candidate.Handle), "Disabling the popup engine restores the still-valid tip it hid");
            module.Deactivate();
            check(!module.IsActive, "Popup event observation releases idempotently");
            context.Settings.Set("processes", "different-browser-process");
            module.Activate();
            Pump();
            check(Native.IsWindowVisible(candidate.Handle), "Popup engine does not hide a window whose process was not explicitly selected");
        }
        finally { module.Deactivate(); }
    }

    private static void WaitUntil(Func<bool> condition, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3)) { if (condition()) return; Pump(); Thread.Sleep(20); }
        throw new TimeoutException(operation);
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private sealed class Context : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(Path.GetTempPath(), "WinForge-popup-tests.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }
}
