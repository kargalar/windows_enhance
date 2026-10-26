using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class TaskbarFeatureChecks
{
    internal static void Run(Action<bool, string> check, bool nativeSmoke)
    {
        TestApplicationMatching(check);
        TestVisualHitTesting(check);
        TestMouseFilter(check);
        var screen = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        check(!ShellTaskbar.IsTaskbarRevealed(new RECT { Left = 0, Top = 1078, Right = 1920, Bottom = 1126 }, screen),
            "The two-pixel hidden taskbar reveal strip cannot trigger application close or audio actions");
        check(ShellTaskbar.IsTaskbarRevealed(new RECT { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 }, screen),
            "A fully revealed horizontal taskbar permits its configured actions");
        check(!ShellTaskbar.IsTaskbarRevealed(new RECT { Left = -46, Top = 0, Right = 2, Bottom = 1080 }, screen),
            "A hidden vertical taskbar reveal strip is also excluded from actions");
        check(ShellTaskbar.IsTaskbarRevealed(new RECT { Left = 0, Top = 0, Right = 48, Bottom = 1080 }, screen),
            "A fully revealed vertical taskbar remains eligible");
        if (nativeSmoke) TestNativeIdentity(check);
    }

    private static void TestApplicationMatching(Action<bool, string> check)
    {
        ShellTaskbar.TaskbarWindowTarget[] windows =
        [
            new(new(101), 10, 1, "First document", @"C:\Apps\Editor.exe", "Vendor.Editor"),
            new(new(102), 10, 1, "Second document", @"C:\Apps\Editor.exe", "Vendor.Editor"),
            new(new(201), 20, 2, "Vendor.Editor", @"C:\Other\Other.exe", "Other.Application"),
            new(new(301), 30, 3, "Untitled", @"C:\Classic\Classic.exe", ""),
            new(new(302), 31, 4, "Untitled", @"C:\Classic\Other.exe", ""),
            new(new(303), 32, 5, "Profile window", @"C:\Classic\Classic.exe", "Other.Profile")
        ];
        var noPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var group = new ShellTaskbar.ButtonIdentity("Vendor.Editor", "Editor (2 windows)", IntPtr.Zero);
        check(ShellTaskbar.ResolveWindow(group, windows, noPaths, new(102))?.Handle == new IntPtr(102),
            "Grouped taskbar buttons resolve by exact AppID and active group member despite unrelated accessibility text");
        check(ShellTaskbar.ResolveWindow(group, windows, noPaths, new(201)) is null,
            "A grouped button with no known active member never guesses which document to close");
        check(ShellTaskbar.ResolveWindow(group with { Name = "Second document" }, windows, noPaths, new(201))?.Handle == new IntPtr(102),
            "An exact document title selects only within the already identified taskbar application group");
        check(ShellTaskbar.ResolveWindow(group with { ApplicationId = "Unknown.App", Name = "First document" }, windows, noPaths, new(101)) is null,
            "An unmatched AppID never falls back to a similarly named window in another application");
        check(ShellTaskbar.ResolveWindow(new("", "Untitled", IntPtr.Zero), windows, noPaths, new(301)) is null,
            "Duplicate legacy window titles remain ambiguous even if one candidate is foreground");
        check(ShellTaskbar.ResolveWindow(new("", "First document", IntPtr.Zero), windows, noPaths, IntPtr.Zero)?.Handle == new IntPtr(101),
            "A legacy taskbar title resolves only when exactly one eligible window owns that title");
        check(ShellTaskbar.ResolveWindow(new("", "", new(302)), windows, noPaths, IntPtr.Zero)?.Handle == new IntPtr(302),
            "An explicitly exposed application HWND resolves without a title guess");
        var classicPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Classic\Classic.exe" };
        check(ShellTaskbar.ResolveWindow(new("Classic.ExactId", "Classic", IntPtr.Zero), windows, classicPaths, IntPtr.Zero)?.Handle == new IntPtr(301),
            "Exact shell shortcut paths identify a classic app while excluding an explicit different profile using the same executable");
        check(ShellTaskbar.ParseApplicationId("Appid: Vendor.Editor") == "Vendor.Editor" &&
              ShellTaskbar.ParseApplicationId("appid: Package_family!App") == "Package_family!App" &&
              ShellTaskbar.ParseApplicationId("StartButton").Length == 0 &&
              ShellTaskbar.ParseApplicationId("Appid: bad\nvalue").Length == 0,
            "Taskbar identity parsing recognizes only Appid fields and rejects control characters");
        check(ShellApplicationIdentity.TryExpandApplicationPath(@"C:\Apps\Editor.exe", out var direct) && direct == @"C:\Apps\Editor.exe" &&
              !ShellApplicationIdentity.TryExpandApplicationPath("Editor", out _) &&
              !ShellApplicationIdentity.TryExpandApplicationPath("https://example.com/Editor.exe", out _),
            "Classic app identity paths require a fully qualified executable rather than an app-name or URI guess");
    }

    private static ShellVisualNode Node(ulong handle, string type, double x, double width, string appId = "", string name = "") =>
        new(handle, handle == 1 ? 0ul : 1ul, type, "", appId, name, width, 48, 1, "Visible", "", "", x, 1000, width, 48);

    private static void TestVisualHitTesting(Action<bool, string> check)
    {
        ShellVisualNode[] tree =
        [
            Node(1, "Taskbar.TaskbarFrame", 0, 1000),
            Node(2, "Taskbar.StartButton", 0, 45),
            Node(3, "Taskbar.TaskListButton", 100, 48, "Appid: Vendor.Editor", "Editor (2 windows)"),
            Node(4, "Taskbar.TaskListButton", 200, 48, "Appid: Other.App", "Other"),
            Node(5, "Taskbar.TaskListButton", 100, 0, "Appid: Old.Hidden", "Old")
        ];
        var button = ShellTaskbar.ResolveVisualHit(new(110, 1020), tree);
        check(button.Kind == ShellTaskbar.HitKind.ApplicationButton && button.Button?.ApplicationId == "Vendor.Editor",
            "Own native taskbar geometry resolves the precise AppID under physical screen coordinates");
        check(ShellTaskbar.ResolveVisualHit(new(175, 1020), tree).Kind == ShellTaskbar.HitKind.EmptyArea,
            "An empty taskbar gap is recognized only with a measured frame and live measured application buttons");
        check(ShellTaskbar.ResolveVisualHit(new(20, 1020), tree).Kind == ShellTaskbar.HitKind.OtherControl,
            "The Start button is never mistaken for empty taskbar space and cannot trigger mute");
        check(ShellTaskbar.ResolveVisualHit(new(110, 1020), [tree[0]]).Kind == ShellTaskbar.HitKind.Unavailable,
            "A root-only provider cannot prove an area empty when its application buttons are inaccessible");
        check(ShellTaskbar.ResolveVisualHit(new(110, 1020), [.. tree, Node(6, "Taskbar.TaskListButton", 100, 48, "Appid: Overlap")]).Kind == ShellTaskbar.HitKind.Unavailable,
            "Overlapping taskbar application bounds fail closed instead of selecting a different app");
        check(ShellTaskbar.ResolveVisualHit(new(110, 1020), [tree[0], tree[2] with { ScreenWidth = double.NaN }]).Kind == ShellTaskbar.HitKind.Unavailable,
            "Non-finite or missing native geometry is rejected before taskbar hit testing");
    }

    private static void TestMouseFilter(Action<bool, string> check)
    {
        using var hook = new GlobalMouseHook();
        var observed = 0;
        hook.MiddleButtonUp += _ => observed++;
        var input = new GlobalMouseHook.Input(Native.WM_MBUTTONUP, new(10, 20), 0, 0);
        check(!hook.ProcessInput(input) && observed == 1,
            "The optional mouse filter preserves native forwarding and existing observers by default");
        hook.EventFilter = _ => true;
        check(hook.ProcessInput(input) && observed == 2,
            "Handled taskbar input can be consumed while notifying the module observer exactly once");
        hook.EventFilter = _ => throw new InvalidOperationException("fixture");
        check(!hook.ProcessInput(input) && observed == 3,
            "An optional mouse filter failure leaves normal Windows input available");
        var failures = 0;
        hook.ErrorHandler = _ => { failures++; throw new InvalidOperationException("reporting fixture"); };
        hook.EventFilter = _ => true;
        hook.MiddleButtonUp += _ => throw new InvalidOperationException("observer fixture");
        hook.MiddleButtonUp += _ => observed++;
        check(hook.ProcessInput(input) && observed == 5 && failures == 1,
            "A throwing mouse observer and error reporter cannot escape the native hook, skip later observers or reverse input consumption");
    }

    private static void TestNativeIdentity(Action<bool, string> check)
    {
        var window = new Window
        {
            Title = "WinForge taskbar identity fixture", ShowActivated = false, ShowInTaskbar = false,
            Width = 250, Height = 180, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        using var process = Process.GetCurrentProcess();
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            using var resolver = WindowsApplicationResolver.TryCreate();
            var appId = resolver?.ReadWindowId(handle) ?? "";
            check(!string.IsNullOrWhiteSpace(appId), "The Windows shell resolver reads a real classic window's grouping AppID without an external engine");
            var target = new ShellTaskbar.TaskbarWindowTarget(handle, (uint)Environment.ProcessId,
                process.StartTime.ToUniversalTime().Ticks, window.Title, process.MainModule?.FileName ?? "", appId);
            check(ShellTaskbar.IsCurrentTarget(target), "A resolved taskbar target verifies its live HWND, process identity and exact title before closing");
            window.Title = "Changed taskbar target";
            check(!ShellTaskbar.IsCurrentTarget(target), "A target changed during asynchronous lookup is rejected before a close request can be sent");
        }
        finally { window.Close(); }
    }
}
