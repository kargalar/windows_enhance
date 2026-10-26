using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class LiveBrowserChecks
{
    internal static void Run(string directory, Action<bool, string> check)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Microsoft Edge is required for this live test.", executable);
        var profile = Path.Combine(directory, "isolated-browser");
        var page = Path.Combine(directory, "browser-verification.html");
        File.WriteAllText(page, "<!doctype html><title>WinForge fullscreen verification</title><body style='background:#182028;color:white;font:24px sans-serif'>WinForge browser verification</body>");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in new[] { "--user-data-dir=" + profile, "--no-first-run", "--disable-sync", "--disable-background-mode", "--start-fullscreen", new Uri(page).AbsoluteUri }) start.ArgumentList.Add(argument);
        using var browser = Process.Start(start) ?? throw new InvalidOperationException("The isolated browser could not start.");
        var module = (ITweakModule)Activator.CreateInstance(Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"))
            .GetType("WinForge.Modules.Extras.BrowserFullscreenModule")!)!;
        module.Initialize(new Context());
        module.Apply();
        var windows = new HashSet<IntPtr>();
        try
        {
            WaitUntil(() =>
            {
                CollectWindows(browser.Id, windows);
                return windows.Any(window => Native.IsWindowVisible(window) && Native.TitleOf(window).Contains("WinForge fullscreen verification", StringComparison.Ordinal));
            }, "Isolated browser window", TimeSpan.FromSeconds(20));
            var root = windows.First(window => Native.IsWindowVisible(window) && Native.TitleOf(window).Contains("WinForge fullscreen verification", StringComparison.Ordinal));
            Native.GetWindowRect(root, out var bounds);
            var screen = DesktopDisplays.ForWindow(root).Bounds;
            check(bounds.Left == screen.Left && bounds.Top == screen.Top && bounds.Right == screen.Right && bounds.Bottom == screen.Bottom,
                "An isolated actual Edge window enters native fullscreen on the real monitor");
            var policy = module.GetType().GetMethod("IsExitBubble", BindingFlags.Instance | BindingFlags.NonPublic)!;
            IntPtr bubble = IntPtr.Zero;
            WaitUntil(() =>
            {
                CollectWindows(browser.Id, windows);
                bubble = windows.FirstOrDefault(window => Native.IsWindowVisible(window) && (bool)policy.Invoke(module, [window])!);
                return bubble != IntPtr.Zero;
            }, "Actual Edge fullscreen exit bubble", TimeSpan.FromSeconds(8));
            check(true, "The strict popup policy identifies an actual Edge fullscreen exit bubble");
            module.Activate();
            WaitUntil(() => !Native.IsWindowVisible(bubble), "Actual browser bubble hiding", TimeSpan.FromSeconds(3));
            check(Native.IsWindowVisible(root), "The own browser engine hides the real exit bubble and preserves the real fullscreen browser");
            module.Deactivate();
            WaitUntil(() => !Native.IsWindow(bubble) || Native.IsWindowVisible(bubble), "Actual browser bubble restoration", TimeSpan.FromSeconds(1));
            check(!Native.IsWindow(bubble) || Native.IsWindowVisible(bubble), "Deactivation restores the actual browser hint if the browser still owns it");
        }
        finally
        {
            module.Deactivate();
            CollectWindows(browser.Id, windows);
            foreach (var window in windows) Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!browser.HasExited && !browser.WaitForExit(3000)) { browser.Kill(entireProcessTree: true); browser.WaitForExit(3000); }
        }
    }

    private static void CollectWindows(int processId, HashSet<IntPtr> windows) => Native.EnumWindows((window, _) =>
    {
        Native.GetWindowThreadProcessId(window, out var owner);
        if (owner == processId && Native.ClassNameOf(window) == "Chrome_WidgetWin_1") windows.Add(window);
        return true;
    }, IntPtr.Zero);

    private static void WaitUntil(Func<bool> condition, string operation, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (condition()) return;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            Thread.Sleep(20);
        }
        throw new TimeoutException(operation);
    }
    private sealed class Context : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(Path.GetTempPath(), "WinForge-browser-live.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }
}
