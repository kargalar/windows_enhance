using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class NotificationsPlacementChecks
{
    internal static void Run(Assembly extras, string directory, Action<bool, string> check, bool nativeSmoke)
    {
        var type = extras.GetType("WinForge.Modules.Extras.NotificationsPlacementModule")!;
        var geometry = type.GetMethod("CalculateBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        RECT Place(RECT work, RECT source, int anchor, int gap = 12, uint dpi = 96, int stack = 0) =>
            (RECT)geometry.Invoke(null, [work, source, anchor, gap, dpi, stack])!;
        var area = new RECT { Left = -1920, Top = -200, Right = 0, Bottom = 840 };
        var source = new RECT { Left = 20, Top = 30, Right = 420, Bottom = 150 };
        var bottomRight = Place(area, source, 0);
        var topLeft = Place(area, source, 3, 12, 144);
        var center = Place(area, source, 6);
        check(bottomRight.Left == -412 && bottomRight.Top == 708 && bottomRight.Right == -12 && bottomRight.Bottom == 828,
            "Notification placement respects negative monitor coordinates and taskbar-excluded work areas");
        check(topLeft.Left == -1902 && topLeft.Top == -182,
            "Notification edge gaps scale from DIPs using the destination monitor DPI");
        check(center.Left == -1160 && center.Top == 260,
            "Notification center placement uses the monitor work area rather than the desktop origin");
        var stacked = Place(area, source, 0, 12, 96, 132);
        check(stacked.Bottom == bottomRight.Top - 12, "Multiple notification hosts keep separate stack positions rather than overlapping");
        var clamped = Place(new() { Right = 400, Bottom = 120 }, source, 3, 200, 768);
        check(clamped.Left == 0 && clamped.Top == 0 && clamped.Right == 400 && clamped.Bottom == 120,
            "A valid notification on a tiny work area remains inside its available bounds despite a large edge gap");
        var oversizedRejected = false;
        try { Place(new() { Right = 100, Bottom = 100 }, source, 0); }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException) { oversizedRejected = true; }
        check(oversizedRejected, "Notifications larger than the destination work area fail visibly instead of being resized or clipped");

        var classify = type.GetMethod("MatchesToastIdentity", BindingFlags.Static | BindingFlags.NonPublic)!;
        var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps", "ShellExperienceHost_cw5n1h2txyewy", "ShellExperienceHost.exe");
        bool Matches(string title, string path, string className = "Windows.UI.Core.CoreWindow") =>
            (bool)classify.Invoke(null, [className, title, path])!;
        check(Matches("Yeni bildirim", host) && Matches("New notification", host),
            "Only the known Turkish or English Windows toast title is accepted with the system shell host identity");
        check(!Matches("Bildirim Merkezi", host) && !Matches("Action center", host) &&
              !Matches("New notification", @"C:\Other\ShellExperienceHost.exe") && !Matches("New notification", host, "OtherWindow"),
            "Notification classification rejects Action Center, process-name impersonation and unrelated window classes");
        if (nativeSmoke) TestOwnedWindow(type, directory, check);
    }

    private static void TestOwnedWindow(Type type, string directory, Action<bool, string> check)
    {
        var window = new Window { Title = "WinForge notification fixture", ShowActivated = false, ShowInTaskbar = false,
            Width = 360, Height = 150, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        ITweakModule? module = null;
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            Native.GetWindowRect(handle, out var original);
            var foreground = Native.GetForegroundWindow();
            var context = new Context(directory);
            context.Settings.Set("monitor", 1);
            context.Settings.Set("position", "Sol üst");
            module = (ITweakModule)type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke([(Func<IntPtr, bool>)(candidate => candidate == handle)]);
            module.Initialize(context);
            module.Activate();
            Native.GetWindowRect(handle, out var moved);
            var work = DesktopDisplays.Enumerate().Single(display => display.IsPrimary).WorkArea;
            check(moved.Left >= work.Left && moved.Top >= work.Top && moved.Right <= work.Right && moved.Bottom <= work.Bottom && moved.Left != original.Left,
                "The own notification engine repositions an actual owned HWND within the selected monitor work area");
            check(Native.GetForegroundWindow() == foreground, "Notification placement preserves foreground focus while moving the owned fixture");
            context.Settings.Set("position", "Sağ alt");
            module.Apply();
            Native.SetWindowPos(handle, IntPtr.Zero, moved.Left + 25, moved.Top + 25, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | 4);
            module.Apply();
            module.Deactivate();
            Native.GetWindowRect(handle, out var restored);
            check(Equal(original, restored), "Notification disable restores exact first native bounds after settings changes and external shell-style repositioning");
            module.Deactivate();
            check(!module.IsActive && context.Notifications.Count == 0, "Notification cleanup is idempotent without false errors on a successful owned fixture");
            module.Activate();
            window.Close();
            module.Deactivate();
            check(!module.IsActive, "A destroyed notification HWND is discarded without modifying a replacement window");
        }
        finally { module?.Deactivate(); window.Close(); }
    }

    private static bool Equal(RECT a, RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
    private sealed class Context(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "notifications-verification.log"));
        public IHotkeyService Hotkeys => throw new NotSupportedException();
        public List<string> Notifications { get; } = [];
        public void Notify(string message) => Notifications.Add(message);
    }
}
