using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using WinForge.Core;
using WinForge.Modules.WindowControl;
using static WinForge.Modules.WindowControl.WindowControlNative;

namespace WinForge.RegressionTests;

internal static class WindowControlParityChecks
{
    private const string FixturePrefix = "WinForge.WindowFixture.";
    private const string FixtureClassName = "WinForge.NativeWindowFixture";
    private const long FixtureStyle = 0x10CF0000; // WS_VISIBLE | WS_OVERLAPPEDWINDOW.
    private const uint RemoveMessage = 1;
    private const string HorizontalScrollProperty = "WinForge.Fixture.HorizontalScroll";
    private const int GrantForegroundMessage = 0x8122;
    private const string FixtureOwnerVariable = "WINFORGE_FIXTURE_OWNER_PID";
    private static readonly WindowProcedure FixtureProcedure = HandleFixtureMessage;
    private static uint _fixtureOwnerProcess;
    private static string _popupSelectionDiagnostic = "";

    internal static void Run(Action<bool, string> check)
    {
        TestGeometry(check);
        var module = new WindowControlModule();
        var button = module.Settings.Single(setting => setting.Key == "LMB");
        var wheel = module.Settings.Single(setting => setting.Key == "Scroll");
        check(button.Options!.Length == 27 && wheel.Options!.Length == 10,
            "Own window engine offers every prior button/wheel action, native zone placement and all-window list");
        var context = new SettingsContext();
        context.Settings.Set("MMBB", "AltTabList");
        context.Settings.Set("MB4", "Mute");
        module.Initialize(context);
        check(module.ReadOptions().Actions["MMBB"] == WindowAction.WindowList && context.Settings.GetString("MMBB") == "Tüm pencereler listesi" &&
            module.Settings.Single(setting => setting.Key == "MMBB").Default as string == "Tüm pencereler listesi",
            "Legacy AltTabList configuration migrates to a working native all-window list and retains the original alternate-middle default");
        check(module.ReadOptions().Actions["MB4"] == WindowAction.MasterMute && button.Options.Contains("Uygulamayı sessize al", StringComparer.Ordinal),
            "Legacy Mute retains master-mute semantics while per-app mute remains an explicitly separate native action");
        check(new[] { "Hotclicks", "SmartAero", "StickyResize", "ResizeCenter", "CenterFraction", "MoveTrans", "MDI", "ResizeAll" }
                .All(key => module.Settings.Any(setting => setting.Key == key)),
            "Own window engine restores hotclick, center/sticky resize, opacity, smart snapping and MDI controls");
        check(new[] { "TTBActions", "LMBT", "LMBTB", "MMBT", "ScrollT", "HScrollTB" }.All(key => module.Settings.Any(setting => setting.Key == key)),
            "Native titlebar modes and normal/alternate button and wheel bindings have their own controls");
        check(module.Settings.Single(setting => setting.Key == "SnapGap").Min == -100 &&
            module.Settings.Single(setting => setting.Key == "AeroHoffset").Min == 1,
            "Native layout retains negative spacing and full original split ranges");
        check(WindowControlEngine.IsHotclick("04 05;06", 3) && WindowControlEngine.IsHotclick("04 05;06", 4) &&
            WindowControlEngine.IsHotclick("04 05;06", 5) && !WindowControlEngine.IsHotclick("04 05;06", 1) &&
            !WindowControlEngine.IsHotclick("invalid", 3), "Hotclick activation accepts only configured middle/side mouse buttons");
        check(module.ReadOptions().Actions["MMB"] == WindowAction.ActionMenu && button.Options.Contains("Windows pencere menüsü", StringComparer.Ordinal) &&
            WindowControlModule.ActionMenuEntries[0].Value == WindowAction.ToggleMaximize && WindowControlModule.ActionMenuEntries[1].Value == WindowAction.Minimize &&
            WindowControlModule.ActionMenuEntries.All(entry => entry.Value is not (WindowAction.ActionMenu or WindowAction.None)),
            "Default middle button opens our own actionable catalogue while the Windows system menu remains explicitly available");
        TestOwnedWindows(check);
    }

    private static void TestGeometry(Action<bool, string> check)
    {
        var bounds = new WindowBounds(100, 100, 800, 600);
        var center = new POINT(500, 400);
        check(WindowGeometry.ResizeRegion(bounds, center, 24, "Taşı") == ResizeEdges.None &&
            WindowGeometry.ResizeRegion(bounds, center, 24, "Sağ alt köşe") == (ResizeEdges.Right | ResizeEdges.Bottom),
            "Center resize supports moving or the lower-right corner");
        var symmetric = WindowGeometry.ResizeRegion(bounds, center, 24, "Tüm yönler");
        check(WindowGeometry.Drag(bounds, center, new POINT(550, 425), symmetric, 100, 100) == new WindowBounds(50, 75, 900, 650),
            "Center resizing grows every side symmetrically around a stable center");
        check(WindowGeometry.Drag(bounds, center, new POINT(-1000, -1000), symmetric, 200, 150) == new WindowBounds(400, 325, 200, 150),
            "Symmetric resize enforces tracking minima without inverting bounds");
        check(WindowGeometry.ResizeRegion(bounds, new POINT(110, 400), 24, "Tüm yönler") == ResizeEdges.Left &&
            WindowGeometry.ResizeRegion(bounds, new POINT(890, 110), 24, "Tüm yönler") == (ResizeEdges.Right | ResizeEdges.Top),
            "Resize regions select a single edge or corner around the center zone");
        var neighbor = new WindowBounds(904, 100, 400, 600);
        check(WindowGeometry.StickyNeighbor(bounds, bounds with { Width = 900 }, neighbor, 4, 8) == new WindowBounds(1004, 100, 300, 600),
            "Sticky resize follows a shared right boundary while preserving the neighbor's outer edge");
        check(WindowGeometry.StickyNeighbor(bounds, bounds with { X = 200, Width = 700 }, new WindowBounds(-204, 100, 300, 600), 4, 8) ==
            new WindowBounds(-204, 100, 400, 600), "Sticky resize follows a shared left boundary");
        check(WindowGeometry.StickyNeighbor(bounds, bounds with { Height = 650 }, new WindowBounds(100, 704, 800, 400), 4, 8) ==
            new WindowBounds(100, 754, 800, 350), "Sticky resize follows a shared bottom boundary");
        check(WindowGeometry.StickyNeighbor(bounds, bounds with { Width = 900 }, new WindowBounds(904, 900, 400, 600), 4, 8) is null,
            "Sticky resize ignores windows without a shared edge");
        var work = new WindowBounds(-1600, 0, 1600, 1000);
        check(WindowGeometry.SideTarget(work, new POINT(-1599, 400), 30, 60, 0) == new WindowBounds(-1600, 0, 480, 1000) &&
            WindowGeometry.SideTarget(work, new POINT(-800, 999), 30, 60, 0) == new WindowBounds(-1600, 600, 1600, 400),
            "Explicit side snap chooses all four edges and custom ratios on negative-coordinate monitors");
        check(WindowGeometry.EdgeTarget(work, new POINT(-1599, 400), 5, 1, 99, -4) == new WindowBounds(-1604, -4, 24, 1008),
            "Edge snapping supports one-percent splits and intentionally overlapping negative spacing");
        check(WindowControlEngine.SmartTarget(work, new WindowBounds(-1600, 0, 800, 1000),
                [new WindowBounds(-1000, 0, 1000, 1000)], new POINT(-1600, 400)) == new WindowBounds(-1600, 0, 600, 1000),
            "Smart snap uses the actual free area beside an existing opposite-edge window");
        check(WindowGeometry.Constrain(new(-2000, 800, 800, 600), work) == new WindowBounds(-1600, 400, 800, 600),
            "Monitor confinement keeps all edges inside the original work area");
        check(WindowGeometry.DoubleResizeTarget(bounds, work, ResizeEdges.Right | ResizeEdges.Bottom, 50, 50, 0, false) ==
            new WindowBounds(-800, 500, 800, 500), "Resize double-click maps a corner region to the corresponding work-area quarter");
        check(WindowGeometry.DoubleResizeTarget(bounds, new(0, 0, 1600, 1000), ResizeEdges.Left | ResizeEdges.Top, 50, 50, 0, true) ==
            new WindowBounds(0, 0, 900, 700), "Shift resize double-click extends only selected leading edges");
        var click = new WindowGestureClick(new IntPtr(100), WindowAction.Move, 1, new POINT(200, 300), 1000);
        check(click.Matches(click with { Timestamp = 1100 }, 200, 4, 4) &&
            !click.Matches(click with { Timestamp = 1300 }, 200, 4, 4) &&
            !click.Matches(click with { Window = new IntPtr(101), Timestamp = 1100 }, 200, 4, 4) &&
            !click.Matches(click with { Point = new POINT(210, 300), Timestamp = 1100 }, 200, 4, 4),
            "Double-click recognition requires the same target/action/button, system interval and pointer tolerance");
        var limits = new WindowResizeLimits(100, 80, 2000, 1600);
        check(WindowGeometry.Zoom(bounds, new(101, 101), 1, 16, 1, 24, false, limits) == new WindowBounds(100, 100, 850, 637),
            "Zoom at the upper-left region grows opposite edges and keeps the clicked corner anchored");
        check(WindowGeometry.Zoom(bounds, new(899, 699), -1, 16, 1, 24, false, limits) == new WindowBounds(150, 137, 750, 563),
            "Zoom-out at the lower-right region shrinks opposite edges while preserving the far corner");
        check(WindowGeometry.Zoom(bounds, new(500, 101), 1, 16, 1, 24, false, limits) == new WindowBounds(100, 100, 800, 637),
            "Zoom leaves a centered axis fixed when only the other region is selected");
        check(WindowGeometry.Zoom(bounds, new(500, 101), 1, 16, 11, 24, true, limits) == new WindowBounds(75, 100, 850, 637),
            "Ctrl zoom enables centered axes without a magnet minimum-step distortion");
        check(WindowGeometry.Zoom(bounds, center, 1, 64, 1, 24, false, limits) == new WindowBounds(94, 96, 812, 608),
            "Fine zoom uses the original Shift divisor and naturally centers an exact-center selection");
        check(WindowGeometry.Zoom(bounds, new(899, 699), -1, 1, 1, 24, false, limits) == new WindowBounds(800, 620, 100, 80),
            "Zoom clamps native tracking minima while preserving the opposite anchor without inverted rectangles");
    }

    private static void TestOwnedWindows(Action<bool, string> check)
    {
        using var fixture = StartFixture();
        var first = WaitForWindow(fixture.Id, 0);
        var second = WaitForWindow(fixture.Id, 1);
        check(first != IntPtr.Zero && second != IntPtr.Zero, "Native action fixtures are real windows in an exclusively owned child process");
        if (first == IntPtr.Zero || second == IntPtr.Zero) return;
        var options = new WindowControlOptions("Sol Alt", "Sol Windows", new Dictionary<string, WindowAction>(), false, true,
            "Kapalı", 20, 5, 0, 50, 50, true, "", "");
        var errors = new List<Exception>();
        var previousForeground = Native.GetForegroundWindow();
        try
        {
            using var actions = new WindowActions(errors.Add, candidates: (_, includeMinimized) =>
                new[] { first, second }.Where(window => Native.IsWindow(window) && (includeMinimized || !Native.IsIconic(window))).ToArray());
            var original = Bounds(first);
            actions.Execute(first, WindowAction.Roll, new(original.X, original.Y), options);
            WaitUntil(() => Bounds(first).Height < original.Height);
            check(Bounds(first).Width == original.Width && Bounds(first).Height < original.Height,
                "Native roll reduces an owned window to its title bar");
            actions.Execute(first, WindowAction.Roll, new(original.X, original.Y), options);
            check(WaitUntil(() => Bounds(first) == original), "Native roll toggles back to the exact original rectangle");
            actions.Execute(first, WindowAction.Roll, new(original.X, original.Y), options, 1);
            var rolled = Bounds(first);
            actions.Execute(first, WindowAction.Roll, new(original.X, original.Y), options, 1);
            check(Bounds(first) == rolled, "Wheel roll is directional and repeated upward steps do not unroll");
            actions.Execute(first, WindowAction.Roll, new(original.X, original.Y), options, -1);
            check(WaitUntil(() => Bounds(first) == original), "Wheel roll restores downward");

            var top = new POINT(original.X + original.Width / 2, original.Y);
            var work = WindowActions.WorkArea(first, top);
            actions.Execute(first, WindowAction.MaximizeAxis, top, options);
            check(WaitUntil(() => Bounds(first).Y == work.Y && Bounds(first).Height == work.Height) && Bounds(first).Width == original.Width,
                "Native vertical maximize preserves the horizontal axis");
            actions.Execute(first, WindowAction.MaximizeAxis, top, options);
            check(WaitUntil(() => Bounds(first).Y == work.Y && Bounds(first).Height == work.Height && Bounds(first).Width == original.Width),
                "Repeating vertical maximize preserves original non-toggle action semantics");
            WindowActions.Place(first, original);
            actions.ForgetSnap(first);
            WaitUntil(() => Bounds(first) == original);
            actions.Execute(first, WindowAction.MaximizeAxis, new(original.X, original.Y + original.Height / 2), options, modifiers: new(Shift: true));
            check(WaitUntil(() => Bounds(first).X == work.X && Bounds(first).Width == work.Width) && Bounds(first).Height == original.Height,
                "Native horizontal maximize preserves the vertical axis");
            WindowActions.Place(first, original);
            actions.ForgetSnap(first);
            WaitUntil(() => Bounds(first) == original);

            actions.Execute(first, WindowAction.SideSnap, new(original.X + 1, original.Y + original.Height / 2), options);
            var side = WindowGeometry.SideTarget(work, new(work.X + 1, work.Y + work.Height / 2), 50, 50, 0);
            check(WaitUntil(() => Bounds(first) == side), "Native side snap changes the actual owned HWND to the expected work-area half");
            WindowActions.Place(first, original);
            WaitUntil(() => Bounds(first) == original);
            actions.Execute(first, WindowAction.Zoom, top, options, 1);
            check(WaitUntil(() => Bounds(first).Width == original.Width && Bounds(first).Height > original.Height && Bounds(first).Y == original.Y),
                "Wheel zoom at the top-center region grows only the opposite vertical edge of an actual window");
            WindowActions.Place(first, original);

            actions.Execute(first, WindowAction.MinimizeOthers, top, options);
            check(WaitUntil(() => Native.IsIconic(second)) && !Native.IsIconic(first),
                "Minimize-others changes only the explicitly injected owned fixture candidates");
            actions.Execute(first, WindowAction.MinimizeOthers, top, options);
            check(WaitUntil(() => !Native.IsIconic(second)), "Minimize-others toggles its own minimized window back");

            var invoked = false;
            using (var zoneActions = new WindowActions(errors.Add, (window, point) => invoked = window == first && point.X == top.X))
                zoneActions.Execute(first, WindowAction.PlaceInZone, top, options);
            check(invoked, "Native zone placement invokes the host's own zone provider with exact HWND and cursor");
            var missingZoneReported = false;
            try { actions.Execute(first, WindowAction.PlaceInZone, top, options); }
            catch (InvalidOperationException exception) { missingZoneReported = exception.Message.Contains("Otomatik Döşeme", StringComparison.Ordinal); }
            check(missingZoneReported, "Missing zone provider returns an actionable error instead of a silent action");

            using (var engine = new WindowControlEngine(options, errors.Add))
            {
                var style = Native.GetWindowLongPtr(first, WindowStyleIndex);
                engine.Execute(first, WindowAction.ToggleBorder, top);
                check(((long)Native.GetWindowLongPtr(first, WindowStyleIndex) & BorderStyles) == 0,
                    "Native border action strips the actual window frame");
                engine.Execute(first, WindowAction.ToggleBorder, top);
                check(Native.GetWindowLongPtr(first, WindowStyleIndex) == style, "Native border action restores the exact previous style");
            }

            WindowActions.Place(first, original);
            WaitUntil(() => Bounds(first) == original);
            using (var engine = new WindowControlEngine(options with { MoveOpacity = 128, EdgeSnap = false }, errors.Add,
                candidates: (_, _) => [first, second]))
            {
                var moveStart = new POINT(original.X + original.Width / 2, original.Y + original.Height / 2);
                engine.Execute(first, WindowAction.Move, moveStart);
                check(GetLayeredWindowAttributes(first, out _, out var alpha, out _) && alpha == 128,
                    "Drag opacity changes only the actual selected window during a move");
                engine.UpdateGesture(new(moveStart.X + 40, moveStart.Y + 30));
                check(WaitUntil(() => Bounds(first) == original with { X = original.X + 40, Y = original.Y + 30 }),
                    "Own drag gesture moves the real window to its coalesced cursor displacement");
                engine.FinishGesture();
                check(((long)Native.GetWindowLongPtr(first, Native.GWL_EXSTYLE) & LayeredStyle) == 0,
                    "Finishing a drag restores the window's original layered style");
            }
            WindowActions.Place(first, original);
            WaitUntil(() => Bounds(first) == original);
            using (var engine = new WindowControlEngine(options with { EdgeSnap = false }, errors.Add,
                candidates: (_, _) => [first, second]))
            {
                var resizeStart = new POINT(original.X + original.Width / 2, original.Y + original.Height / 2);
                engine.Execute(first, WindowAction.Resize, resizeStart);
                engine.UpdateGesture(new(resizeStart.X + 20, resizeStart.Y + 15));
                check(WaitUntil(() => Bounds(first) == new WindowBounds(original.X - 20, original.Y - 15, original.Width + 40, original.Height + 30)),
                    "Own centered resize changes all four edges of an actual owned HWND");
            }
            check(WaitUntil(() => Bounds(first) == original), "Deactivating an unfinished gesture restores its original rectangle");

            var adjacent = new WindowBounds(original.Right, original.Y, original.Width, original.Height);
            WindowActions.Place(second, adjacent);
            WaitUntil(() => Bounds(second) == adjacent);
            using (var engine = new WindowControlEngine(options with { StickyResize = "Her zaman", EdgeSnap = false }, errors.Add,
                candidates: (_, _) => [first, second]))
            {
                var resizeStart = new POINT(original.Right - 1, original.Y + original.Height / 2);
                engine.Execute(first, WindowAction.Resize, resizeStart);
                engine.UpdateGesture(new(resizeStart.X + 40, resizeStart.Y));
                check(WaitUntil(() => Bounds(first).Width == original.Width + 40 && Bounds(second).X == original.Right + 40 &&
                    Bounds(second).Right == adjacent.Right), "Sticky resize moves both actual shared boundaries while preserving the outer extent");
            }
            check(WaitUntil(() => Bounds(first) == original && Bounds(second) == adjacent),
                "Cancelling a sticky gesture restores every window actually touched by the gesture");
            TestDefaultGestures(first, second, options, errors, check);
            TestTitlebarBindings(first, options, errors, check);
            TestOriginalActionSemantics(first, options, errors, check);
            TestModifiedActions(first, options, errors, check);
            TestMenuAndZoom(first, options, errors, check);
            TestQueuedWheelModifiers(first, options, errors, check);
            TestPublishedModifierEdges(first, second, options, errors, check);

            var masterMuted = false;
            var endpointWrites = 0;
            using (var masterActions = new WindowActions(errors.Add, masterAudio: new MasterAudioEndpoint(() => masterMuted, value =>
                { masterMuted = value; endpointWrites++; })))
            {
                masterActions.Execute(first, WindowAction.MasterMute, top, options);
                check(masterMuted && endpointWrites == 1, "Master mute routes to the selected endpoint facade without changing global test-machine audio");
                masterActions.Execute(first, WindowAction.MasterMute, top, options);
                check(!masterMuted && endpointWrites == 2, "Master mute toggles its endpoint back while keeping application mute separate");
            }

            var sessions = Array.Empty<AudioSessionService.Session>();
            WaitUntil(() => (sessions = AudioSessionService.GetActiveSessions().Where(session => session.ProcessId == fixture.Id).ToArray()).Length > 0);
            check(sessions.Length > 0, "Owned silent-audio fixture supplies an active per-process session for mute verification");
            actions.Execute(first, WindowAction.ApplicationMute, top, options);
            check(AudioSessionService.GetActiveSessions().Where(session => session.ProcessId == fixture.Id).All(session => session.IsMuted),
                "Per-app mute silences only the selected fixture process audio sessions");
            actions.Execute(first, WindowAction.ApplicationMute, top, options);
            check(AudioSessionService.GetActiveSessions().Where(session => session.ProcessId == fixture.Id).All(session => !session.IsMuted),
                "Per-app mute toggles the selected fixture sessions back on");

            WindowActions.Place(second, original with { X = original.X + 40, Y = original.Y + 40 });
            WaitUntil(() => Bounds(second).X == original.X + 40);
            var overlap = new POINT(original.X + original.Width / 2, original.Y + original.Height / 2);
            WindowActions.Activate(first);
            actions.Execute(first, WindowAction.NextStacked, overlap, options);
            check(WaitUntil(() => Native.GetForegroundWindow() == second), "Next stacked action activates another actual overlapping fixture window");
            actions.Execute(second, WindowAction.PreviousStacked, overlap, options);
            check(WaitUntil(() => Native.GetForegroundWindow() == first), "Previous stacked action activates the previous actual overlapping fixture window");
            actions.Execute(first, WindowAction.SwitchWindow, overlap, options, 1);
            check(WaitUntil(() => Native.GetForegroundWindow() == second), "Window-switch wheel action activates the next eligible actual window");
            actions.Execute(second, WindowAction.RaiseLower, overlap, options, -1);
            actions.Execute(first, WindowAction.RaiseLower, overlap, options, 1);
            check(WaitUntil(() => Native.GetForegroundWindow() == first), "Directional raise/lower brings the selected actual window to the foreground");

            var popupSelection = Task.Run(() => SelectSecondOwnedPopup());
            var stackChoice = actions.ShowWindowList(first, overlap, stacked: true);
            var stackSelected = popupSelection.GetAwaiter().GetResult();
            var stackForeground = PumpUntil(() => Native.GetForegroundWindow() == second);
            check(stackSelected && stackChoice.Window == second && stackForeground,
                "Native stack-list popup accepts a selected real window and activates it" +
                (stackSelected && stackChoice.Window == second && stackForeground ? "" :
                    $": selector={stackSelected}, command={stackChoice.Command}, chosen={stackChoice.Window}, activated={stackChoice.Activated}, expected={second}, {_popupSelectionDiagnostic}, {ForegroundDiagnostic("stack-list-result")}"));
            ShowWindowAsync(second, Minimize);
            WaitUntil(() => Native.IsIconic(second));
            var allWindowSelection = Task.Run(() => SelectSecondOwnedPopup());
            var allChoice = actions.ShowWindowList(first, overlap, stacked: false);
            var allSelected = allWindowSelection.GetAwaiter().GetResult();
            var allForeground = PumpUntil(() => !Native.IsIconic(second) && Native.GetForegroundWindow() == second);
            check(allSelected && allChoice.Window == second && allForeground,
                "Native all-window list includes, restores and activates the selected minimized owned window" +
                (allSelected && allChoice.Window == second && allForeground ? "" :
                    $": selector={allSelected}, command={allChoice.Command}, chosen={allChoice.Window}, activated={allChoice.Activated}, expected={second}, iconic={Native.IsIconic(second)}, {_popupSelectionDiagnostic}, {ForegroundDiagnostic("all-list-result")}"));
            WindowActions.Activate(first);
            var scrollPoint = new POINT(original.X + 30, original.Y + 60);
            actions.Execute(first, WindowAction.HorizontalScroll, scrollPoint, options, 1);
            check(WaitUntil(() => GetProp(first, HorizontalScrollProperty) == new IntPtr(-120)),
                "Horizontal wheel action converts upward input to the original negative horizontal direction");
            actions.Execute(first, WindowAction.HorizontalScroll, scrollPoint, options, -1);
            check(WaitUntil(() => GetProp(first, HorizontalScrollProperty) == new IntPtr(120)),
                "Horizontal wheel action converts downward input to positive horizontal direction");

            var mdi = IntPtr.Zero;
            EnumChildWindows(second, (window, _) =>
            {
                if (((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & MdiChildStyle) != 0) mdi = window;
                return true;
            }, IntPtr.Zero);
            check(mdi != IntPtr.Zero, "Fixture includes a real MDI client and child created by Windows");
            var mdiWork = WindowActions.WorkArea(mdi, overlap);
            var mdiTarget = new WindowBounds(mdiWork.X + 20, mdiWork.Y + 20, 280, 180);
            WindowActions.Place(mdi, mdiTarget);
            check(WaitUntil(() => Bounds(mdi) == mdiTarget), "MDI placement maps screen coordinates into the actual parent client coordinates");
            using (var engine = new WindowControlEngine(options with { EdgeSnap = false }, errors.Add, candidates: (_, _) => [mdi]))
            {
                var start = new POINT(mdiTarget.X + 60, mdiTarget.Y + 60);
                engine.Execute(mdi, WindowAction.Move, start);
                engine.UpdateGesture(new(start.X + 25, start.Y + 15));
                check(WaitUntil(() => Bounds(mdi) == mdiTarget with { X = mdiTarget.X + 25, Y = mdiTarget.Y + 15 }),
                    "Own gesture moves a real MDI child independently of its top-level window");
                engine.FinishGesture();
            }

            actions.Execute(first, WindowAction.Pause, top, options);
            var response = SendMessageTimeoutSimple(first, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out _);
            check(response == IntPtr.Zero && !fixture.HasExited, "Pause suspends only the selected owned fixture process");
            actions.Execute(first, WindowAction.Resume, top, options);
            check(WaitUntil(() => SendMessageTimeoutSimple(first, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out _) != IntPtr.Zero),
                "Resume releases exactly the threads suspended by our engine");
            using (var pauseScope = new WindowActions(errors.Add)) pauseScope.Execute(first, WindowAction.Pause, top, options);
            check(WaitUntil(() => SendMessageTimeoutSimple(first, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 100, out _) != IntPtr.Zero),
                "Deactivation automatically resumes its previously paused process");
            check(errors.Count == 0, "Owned native actions complete without hidden reported exceptions");
            actions.Execute(first, WindowAction.Kill, top, options);
            check(fixture.WaitForExit(3000), "Terminate targets exactly the owned selected process without killing a process tree");
        }
        finally
        {
            if (!fixture.HasExited) { fixture.Kill(entireProcessTree: false); fixture.WaitForExit(3000); }
            if (Native.IsWindow(previousForeground)) WindowActions.Activate(previousForeground);
        }
    }

    private static WindowBounds Bounds(IntPtr window)
    {
        if (!Native.GetWindowRect(window, out var rectangle)) throw new InvalidOperationException("Owned fixture window was lost.");
        return WindowBounds.From(rectangle);
    }

    private static void TestDefaultGestures(IntPtr first, IntPtr second, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var original = Bounds(first);
        var point = new POINT(original.X + 40, original.Y + 15);
        var work = WindowActions.WorkArea(first, point);
        using var engine = new WindowControlEngine(options with { EdgeSnap = false }, errors.Add, candidates: (_, _) => [first, second]);
        DoublePress(engine, first, WindowAction.Move, point, new());
        check(WaitUntil(() => IsZoomed(first)), "Alt move double-click maximizes the actual selected window");
        DoublePress(engine, first, WindowAction.Move, point, new());
        check(WaitUntil(() => !IsZoomed(first) && Bounds(first) == original),
            "Alt double-click restores a maximized window without prematurely restoring on its first click");
        DoublePress(engine, first, WindowAction.Move, point, new(Shift: true));
        check(WaitUntil(() => Bounds(first).Height < original.Height), "Shift move double-click rolls the actual selected window");
        DoublePress(engine, first, WindowAction.Move, point, new(Shift: true));
        check(WaitUntil(() => Bounds(first) == original), "Shift move double-click unrolls to its original bounds");
        DoublePress(engine, first, WindowAction.Move, point, new(Control: true));
        check(WaitUntil(() => Native.IsIconic(first)), "Ctrl move double-click minimizes the actual selected window");
        ShowWindowAsync(first, Restore);
        WaitUntil(() => !Native.IsIconic(first));

        var corner = new POINT(original.Right - 2, original.Bottom - 2);
        DoublePress(engine, first, WindowAction.Resize, corner, new());
        var quarter = WindowGeometry.DoubleResizeTarget(original, work, ResizeEdges.Right | ResizeEdges.Bottom, 50, 50, 0, false);
        check(WaitUntil(() => Bounds(first) == quarter), "Resize double-click places the actual window in the selected screen corner");
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);
        var right = new POINT(original.Right - 2, original.Y + original.Height / 2);
        DoublePress(engine, first, WindowAction.Resize, right, new(Shift: true));
        check(WaitUntil(() => Bounds(first) == original with { Width = work.Right - original.X }),
            "Shift resize double-click extends the actual selected edge to the monitor boundary");
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);
        var center = new POINT(original.X + original.Width / 2, original.Y + original.Height / 2);
        DoublePress(engine, first, WindowAction.Resize, center, new(Control: true));
        check(WaitUntil(() => IsZoomed(first)), "Ctrl resize double-click in the center maximizes the actual window");
        ShowWindowAsync(first, Restore);
        WaitUntil(() => !IsZoomed(first) && Bounds(first) == original);
        var foregroundGranted = GrantFixtureForeground(second, out var foregroundDiagnostic);
        check(foregroundGranted,
            "Owned foreground fixture explicitly grants activation rights like a user-triggered hook" + (foregroundGranted ? "" : ": " + foregroundDiagnostic));
        engine.Press(first, WindowAction.Move, point, 1, new(Control: true), Environment.TickCount64);
        engine.UpdateGesture(point, new(Control: true));
        check(WaitUntil(() => Native.GetForegroundWindow() == first), "Ctrl begins a native drag by focusing the selected actual window");
        engine.UpdateGesture(new(work.Right + 200, work.Bottom + 200), new(Control: true, SuppressSnap: true));
        check(WaitUntil(() => Bounds(first).Right <= work.Right && Bounds(first).Bottom <= work.Bottom),
            "Ctrl drag confines the actual entire window to its original monitor");
        engine.FinishGesture(new(Control: true, SuppressSnap: true));
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);

        using var snapping = new WindowControlEngine(options with { EdgeSnap = true, MagnetMode = "İç ve dış kenarlar" }, errors.Add,
            candidates: (_, _) => [first, second]);
        snapping.Execute(first, WindowAction.Move, center);
        check(!snapping.HandleSnapKey(true, injected: true) && snapping.HandleSnapKey(true, injected: false) &&
            snapping.HandleSnapKey(true, injected: false) && snapping.HandleSnapKey(false, injected: false),
            "Space snap override ignores injected input, suppresses repeat toggles and consumes its matching key release");
        var edge = new POINT(work.X + 1, work.Y + work.Height / 2);
        snapping.UpdateGesture(edge);
        snapping.FinishGesture();
        check(WaitUntil(() => Bounds(first).Width == original.Width && Bounds(first).Height == original.Height && Bounds(first).X < work.X),
            "Space override disables both magnetic and Aero snapping for the actual drag");
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);

        engine.Execute(first, WindowAction.Move, center);
        engine.UpdateGesture(new(center.X + 20, center.Y + 10));
        check(engine.MaximizeWhileMoving(), "Right-click combination recognizes an active native move gesture");
        check(WaitUntil(() => IsZoomed(first)), "Right-click while moving maximizes the actual selected window and finishes its drag");
        ShowWindowAsync(first, Restore);
        WaitUntil(() => !IsZoomed(first));
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);
    }

    private static void TestTitlebarBindings(IntPtr window, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var original = Bounds(window);
        var dpi = Native.GetDpiForWindow(window);
        var captionY = original.Y + GetSystemMetricsForDpi(SizeFrameHeightMetric, dpi) + GetSystemMetricsForDpi(PaddedBorderMetric, dpi) +
            GetSystemMetricsForDpi(CaptionHeightMetric, dpi) / 2;
        var caption = new POINT(original.X + 80, captionY);
        var actions = new Dictionary<string, WindowAction>(options.Actions)
        {
            ["MMBT"] = WindowAction.ToggleTopmost,
            ["MMBTB"] = WindowAction.Roll,
            ["ScrollT"] = WindowAction.Roll
        };
        var titlebar = options with { Actions = actions, TitlebarMode = "Her zaman" };
        using var engine = new WindowControlEngine(titlebar, errors.Add);
        check(engine.TitlebarAction(window, caption, "MMB", activated: false, alternate: false) == WindowAction.ToggleTopmost,
            "Native caption hit-testing resolves an unmodified titlebar button binding");
        check(engine.TitlebarAction(window, new POINT(original.X + original.Width / 2, original.Y + original.Height / 2), "MMB", false, false) == WindowAction.None &&
            engine.TitlebarAction(window, new POINT(original.Right - 20, captionY), "MMB", false, false) == WindowAction.None,
            "Native titlebar bindings protect client content and caption buttons");
        engine.Execute(window, engine.TitlebarAction(window, caption, "MMB", false, false), caption);
        check(WaitUntil(() => ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0),
            "Resolved titlebar binding applies its actual native topmost action");
        engine.Execute(window, WindowAction.ToggleTopmost, caption);
        WaitUntil(() => ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0);
        check(engine.TitlebarAction(window, caption, "MMB", false, true) == WindowAction.Roll &&
            engine.TitlebarAction(window, caption, "Scroll", false, false) == WindowAction.Roll,
            "Alternate titlebar buttons and titlebar wheels resolve independently");
        engine.Update(titlebar with { TitlebarMode = "Etkinleştirme tuşuyla" });
        check(engine.TitlebarAction(window, caption, "MMB", false, false) == WindowAction.None &&
            engine.TitlebarAction(window, caption, "MMB", true, false) == WindowAction.ToggleTopmost,
            "Titlebar mode can require the configured activation key");
        engine.Update(titlebar with { TitlebarMode = "Etkinleştirme tuşu olmadan" });
        check(engine.TitlebarAction(window, caption, "MMB", true, false) == WindowAction.None &&
            engine.TitlebarAction(window, caption, "MMB", false, false) == WindowAction.ToggleTopmost,
            "Titlebar mode can preserve the normal activated bindings");
        engine.Update(titlebar with { TitlebarMode = "Kapalı" });
        check(engine.TitlebarAction(window, caption, "MMB", false, false) == WindowAction.None,
            "Disabling titlebar bindings leaves native caption input alone");
    }

    private static void TestOriginalActionSemantics(IntPtr window, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var original = Bounds(window);
        var point = new POINT(original.X + 2, original.Y + original.Height / 2);
        var work = WindowActions.WorkArea(window, point);
        using var engine = new WindowControlEngine(options with { EdgeSnap = false }, errors.Add, candidates: (_, _) => [window]);
        engine.Execute(window, WindowAction.MaximizeAxis, point, new());
        check(WaitUntil(() => Bounds(window).Y == work.Y && Bounds(window).Height == work.Height && Bounds(window).Width == original.Width),
            "MaximizeHV defaults to vertical even when the user clicked a horizontal window edge");
        var start = new POINT(original.X + 40, work.Y + 15);
        engine.Execute(window, WindowAction.Move, start);
        engine.UpdateGesture(start);
        check(Bounds(window).Height == work.Height, "An own snapped window waits for real movement before restoring its normal size");
        engine.UpdateGesture(new(start.X + 30, start.Y + 50));
        check(WaitUntil(() => Bounds(window).Width == original.Width && Bounds(window).Height == original.Height),
            "Dragging an own axis-maximized window restores the stored original size under the pointer");
        engine.FinishGesture();
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
        engine.Execute(window, WindowAction.MaximizeAxis, point, new(Shift: true));
        check(WaitUntil(() => Bounds(window).X == work.X && Bounds(window).Width == work.Width && Bounds(window).Height == original.Height),
            "Captured Shift changes MaximizeHV to horizontal while preserving height");
        start = new POINT(work.X + 40, original.Y + 15);
        engine.Execute(window, WindowAction.Move, start);
        engine.UpdateGesture(new(start.X + 30, start.Y + 20));
        WaitUntil(() => Bounds(window).Width == original.Width && Bounds(window).Height == original.Height);
        engine.FinishGesture();
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);

        var right = new POINT(original.Right - 2, original.Y + original.Height / 2);
        engine.Execute(window, WindowAction.SideSnap, right, new());
        var target = WindowGeometry.DoubleResizeTarget(original, work, ResizeEdges.Right, 50, 50, 0, false);
        check(WaitUntil(() => Bounds(window) == target), "SideSnap uses the clicked region inside the window instead of the nearest monitor edge");
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
        engine.Execute(window, WindowAction.SideSnap, new(original.X + 2, original.Y + 2), new(Shift: true));
        target = WindowGeometry.DoubleResizeTarget(original, work, ResizeEdges.Left | ResizeEdges.Top, 50, 50, 0, true);
        check(WaitUntil(() => Bounds(window) == target), "Captured Shift changes SideSnap to extension of the selected actual window edges");
        start = new POINT(target.X + 40, target.Y + 15);
        engine.Execute(window, WindowAction.Move, start);
        engine.UpdateGesture(new(start.X + 30, start.Y + 30));
        check(WaitUntil(() => Bounds(window).Width == original.Width && Bounds(window).Height == original.Height),
            "Dragging an own side-snapped or extended window restores its normal size");
        engine.FinishGesture();
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
    }

    private static void TestModifiedActions(IntPtr window, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var bounds = Bounds(window);
        var point = new POINT(bounds.X + 30, bounds.Y + 60);
        using var engine = new WindowControlEngine(options, errors.Add);
        var originalStyle = Native.GetWindowLongPtr(window, WindowStyleIndex);
        engine.Execute(window, WindowAction.ToggleBorder, point, new(Shift: true));
        var style = (long)Native.GetWindowLongPtr(window, WindowStyleIndex);
        check((style & CaptionStyle) == 0 && (style & ResizableStyle) == ((long)originalStyle & ResizableStyle),
            "Shift borderless removes only the caption and preserves the actual resize frame");
        engine.Execute(window, WindowAction.ToggleBorder, point, new(Shift: true));
        check(Native.GetWindowLongPtr(window, WindowStyleIndex) == originalStyle, "Shift borderless restores the original caption style");
        engine.Execute(window, WindowAction.ToggleMaximize, point, new(Shift: true));
        check(WaitUntil(() => Native.IsIconic(window)), "Shift maximize minimizes the selected actual window");
        ShowWindowAsync(window, Restore);
        WaitUntil(() => !Native.IsIconic(window));
        engine.Execute(window, WindowAction.Lower, point, new(Control: true));
        check(WaitUntil(() => ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0),
            "Ctrl lower toggles the selected actual window to topmost");
        engine.Execute(window, WindowAction.Lower, point, new(Control: true));
        check(WaitUntil(() => ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0),
            "Ctrl lower toggles the selected actual window back from topmost");
        engine.Wheel(window, WindowAction.Opacity, -1, point, new());
        check(GetLayeredWindowAttributes(window, out _, out var alpha, out _) && alpha == 191,
            "Ordinary opacity wheel uses the original 64-point step on an actual window");
        engine.Wheel(window, WindowAction.Opacity, -1, point, new(Shift: true));
        check(GetLayeredWindowAttributes(window, out _, out alpha, out _) && alpha == 183,
            "Shift opacity wheel uses the original eight-point fine step");
        engine.Wheel(window, WindowAction.Opacity, 1, point, new());
        engine.Wheel(window, WindowAction.Opacity, 1, point, new());
        check(((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & LayeredStyle) == 0,
            "Returning to full opacity removes the layered style added by our engine");
        var volumeSteps = new List<int>();
        using var actions = new WindowActions(errors.Add, masterAudio: new MasterAudioEndpoint(() => false, _ => { }, volumeSteps.Add));
        actions.Execute(window, WindowAction.Volume, point, options, 1);
        actions.Execute(window, WindowAction.Volume, point, options, -1, new(Shift: true));
        check(volumeSteps.SequenceEqual(new[] { 2, -10 }), "Volume wheel preserves one media step, or five steps with Shift, through the isolated endpoint facade");
        actions.Execute(window, WindowAction.RaiseLower, point, options, 1, new(Shift: true));
        check(WaitUntil(() => IsZoomed(window)), "Shift raise-wheel maximizes the actual selected window");
        actions.Execute(window, WindowAction.RaiseLower, point, options, -1, new(Shift: true));
        check(WaitUntil(() => Native.IsIconic(window)), "Shift lower-wheel minimizes the actual selected window");
        ShowWindowAsync(window, Restore);
        WaitUntil(() => !Native.IsIconic(window));
        // Restoring a window minimized from maximized first restores its maximized
        // state. Leave the owned fixture normal before subsequent independent checks.
        if (IsZoomed(window)) { ShowWindowAsync(window, Restore); WaitUntil(() => !IsZoomed(window)); }
        WindowActions.Place(window, bounds);
        WaitUntil(() => Bounds(window) == bounds);
    }

    private static void TestMenuAndZoom(IntPtr window, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var original = Bounds(window);
        var point = new POINT(original.X + 2, original.Y + 2);
        using var engine = new WindowControlEngine(options, errors.Add);
        var selection = Task.Run(SelectSecondOwnedPopup);
        var chosen = engine.ShowActionMenu(window, point, new());
        var selected = selection.GetAwaiter().GetResult();
        var minimized = WaitUntil(() => Native.IsIconic(window));
        check(selected && chosen == WindowAction.Minimize && minimized,
            "Own native action menu dispatches the selected minimize action to the exact owned HWND" +
            (selected && chosen == WindowAction.Minimize && minimized ? "" : $": selected={selected}, chosen={chosen}, minimized={minimized}, {_popupSelectionDiagnostic}"));
        ShowWindowAsync(window, Restore);
        WaitUntil(() => !Native.IsIconic(window));
        engine.Wheel(window, WindowAction.Zoom, 1, point, new());
        check(WaitUntil(() => Bounds(window).X == original.X && Bounds(window).Y == original.Y &&
            Bounds(window).Width > original.Width && Bounds(window).Height > original.Height),
            "Native wheel zoom anchors the actual clicked corner and grows the opposite edges");
        var normalZoom = Bounds(window);
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
        engine.Wheel(window, WindowAction.Zoom, 1, point, new(Shift: true));
        check(WaitUntil(() => Bounds(window).Width > original.Width && Bounds(window).Width < normalZoom.Width &&
            Bounds(window).Height > original.Height && Bounds(window).Height < normalZoom.Height),
            "Shift wheel zoom applies the smaller original step to the actual owned window");
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
        engine.Wheel(window, WindowAction.Zoom, 1, new(original.X + original.Width / 2, original.Y + 2), new(Control: true));
        check(WaitUntil(() => Bounds(window).X < original.X && Bounds(window).Width > original.Width && Bounds(window).Y == original.Y),
            "Ctrl wheel zoom centers the otherwise unchanged horizontal axis of the actual window");
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
        ShowWindowAsync(window, Maximize);
        WaitUntil(() => IsZoomed(window));
        engine.Wheel(window, WindowAction.Zoom, 1, point, new());
        check(WaitUntil(() => !IsZoomed(window) && Bounds(window).Width > original.Width && Bounds(window).Height > original.Height),
            "Wheel zoom first restores a maximized actual window and then applies the original region-resize action");
        WindowActions.Place(window, original);
        WaitUntil(() => Bounds(window) == original);
    }

    private static void TestQueuedWheelModifiers(IntPtr window, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var bounds = Bounds(window);
        using var engine = new WindowControlEngine(options, errors.Add);
        engine.Start();
        var input = new MSLLHOOKSTRUCT { pt = new(bounds.X + 30, bounds.Y + 60), mouseData = 0xFF880000 };
        engine.QueueInput(window, WindowAction.Opacity, input, 0, true, new(Shift: true));
        check(PumpUntil(() => GetLayeredWindowAttributes(window, out _, out var alpha, out _) && alpha == 247),
            "Deferred physical-input path preserves the captured Shift wheel snapshot when dispatching to the actual HWND");
        Thread.Sleep(35); // Past the engine's intentional wheel-event coalescing interval.
        engine.QueueInput(window, WindowAction.Opacity, input, 0, true, new());
        check(PumpUntil(() => GetLayeredWindowAttributes(window, out _, out var alpha, out _) && alpha == 183),
            "The next deferred wheel event uses its own captured ordinary modifier state rather than a later keyboard read");
    }

    private static void TestPublishedModifierEdges(IntPtr first, IntPtr second, WindowControlOptions options, List<Exception> errors, Action<bool, string> check)
    {
        var original = Bounds(first);
        var point = new POINT(original.X + 20, original.Y + 20);
        var work = WindowActions.WorkArea(first, point);
        var centered = original with { X = work.X + (work.Width - original.Width) / 2, Y = work.Y + (work.Height - original.Height) / 2 };
        using (var engine = new WindowControlEngine(options, errors.Add))
        {
            engine.Execute(first, WindowAction.Lower, point, new(Shift: true));
            check(WaitUntil(() => Native.IsIconic(first)), "Shift lower-button minimizes the actual window instead of lowering its Z-order");
            ShowWindowAsync(first, Restore);
            WaitUntil(() => !Native.IsIconic(first));
            engine.Wheel(first, WindowAction.RaiseLower, 1, point, new(Shift: true));
            check(WaitUntil(() => IsZoomed(first)), "Shift raise-wheel first maximizes a normal actual window");
            engine.Wheel(first, WindowAction.RaiseLower, 1, point, new(Shift: true));
            check(WaitUntil(() => !IsZoomed(first)), "Repeated Shift raise-wheel restores the actual maximized window");
            WindowActions.Place(first, original);
            WaitUntil(() => Bounds(first) == original);
            engine.Execute(first, WindowAction.MaximizeAxis, point, new());
            WaitUntil(() => Bounds(first).Height == work.Height);
            engine.Execute(first, WindowAction.Center, point, new());
            check(WaitUntil(() => Bounds(first) == centered), "Ordinary center restores an own axis-snapped window to its saved normal size before centering");
            WindowActions.Place(first, original);
            WaitUntil(() => Bounds(first) == original);
            engine.Execute(first, WindowAction.MaximizeAxis, point, new());
            WaitUntil(() => Bounds(first).Height == work.Height);
            engine.Execute(first, WindowAction.Center, point, new(Shift: true));
            var centeredAxis = centered with { Y = work.Y, Height = work.Height };
            check(WaitUntil(() => Bounds(first) == centeredAxis), "Shift center preserves the current actual snapped size instead of restoring the normal size");
        }
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);
        using (var engine = new WindowControlEngine(options, errors.Add))
        {
            ShowWindowAsync(first, Maximize);
            WaitUntil(() => IsZoomed(first));
            engine.Execute(first, WindowAction.Center, point, new());
            check(WaitUntil(() => !IsZoomed(first) && Bounds(first) == centered),
                "Ordinary center restores the native normal-placement size of an actual maximized window");
        }
        WindowActions.Place(first, original);
        WaitUntil(() => Bounds(first) == original);
        using var actions = new WindowActions(errors.Add, candidates: (_, includeMinimized) =>
            new[] { first, second }.Where(window => includeMinimized || !Native.IsIconic(window)).ToArray());
        actions.Execute(first, WindowAction.MinimizeOthers, point, options);
        check(WaitUntil(() => Native.IsIconic(second) && !Native.IsIconic(first)), "Minimize-others establishes the first exact selected owner's native window set");
        ShowWindowAsync(second, Restore);
        WaitUntil(() => !Native.IsIconic(second));
        actions.Execute(second, WindowAction.MinimizeOthers, point, options);
        check(WaitUntil(() => Native.IsIconic(first) && !Native.IsIconic(second)),
            "Choosing a different minimize-others owner starts its own set instead of restoring the previous owner's set");
        actions.Execute(second, WindowAction.MinimizeOthers, point, options);
        check(WaitUntil(() => !Native.IsIconic(first) && !Native.IsIconic(second)),
            "Repeating minimize-others on the same exact owner restores the windows saved for that owner");
    }

    private static void DoublePress(WindowControlEngine engine, IntPtr window, WindowAction action, POINT point, WindowGestureModifiers modifiers)
    {
        var now = Environment.TickCount64;
        engine.Press(window, action, point, action == WindowAction.Move ? 1 : 2, modifiers, now);
        engine.UpdateGesture(point, modifiers);
        engine.FinishGesture(modifiers);
        engine.Press(window, action, point, action == WindowAction.Move ? 1 : 2, modifiers, now + 1);
    }

    private static bool GrantFixtureForeground(IntPtr window, out string diagnostic)
    {
        diagnostic = "";
        var editor = new System.Windows.Controls.TextBox();
        var seed = new System.Windows.Window
        {
            Title = "WinForge foreground setup fixture", Width = 200, Height = 90, Content = editor,
            ShowInTaskbar = false, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen
        };
        try
        {
            seed.Show();
            var seedHandle = new System.Windows.Interop.WindowInteropHelper(seed).Handle;
            var foreground = Native.GetForegroundWindow();
            var currentThread = GetCurrentThreadId();
            var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
            if (foreground != IntPtr.Zero && SendWindowMessageTimeout(foreground, 0, IntPtr.Zero, IntPtr.Zero,
                Native.SMTO_ABORTIFHUNG, 150, out _) == IntPtr.Zero)
            { diagnostic = ForegroundDiagnostic("foreground-unresponsive"); return false; }
            var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
            try
            {
                // Attach only to focus a test window on this very UI thread; never focus
                // a foreign child while borrowing the foreground thread's input queue.
                Native.SetForegroundWindow(seedHandle);
                seed.Activate();
                editor.Focus();
                System.Windows.Input.Keyboard.Focus(editor);
            }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            if (Native.GetForegroundWindow() != seedHandle)
            { diagnostic = ForegroundDiagnostic("parent-seed-not-foreground"); return false; }
            // Becoming foreground through an attached queue does not by itself make
            // this process the recipient of the last input event. Deliver benign
            // input only after checking our own editor is the actual foreground.
            ForegroundInput[] input =
            [
                new() { Type = 1, Keyboard = new() { ScanCode = 'x', Flags = 4 } },
                new() { Type = 1, Keyboard = new() { ScanCode = 'x', Flags = 6 } }
            ];
            if (SendInput((uint)input.Length, input, Marshal.SizeOf<ForegroundInput>()) != input.Length ||
                !PumpUntil(() => editor.Text == "x"))
            { diagnostic = ForegroundDiagnostic("owned-seed-input-not-delivered"); return false; }
            Native.GetWindowThreadProcessId(window, out var childProcess);
            var parentGrant = AllowSetForegroundWindow(childProcess);
            var parentGrantError = Marshal.GetLastWin32Error();
            Native.SetForegroundWindow(window);
            SendWindowMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 150, out _);
            if (SendWindowMessageTimeout(window, GrantForegroundMessage, new IntPtr(Environment.ProcessId), IntPtr.Zero,
                Native.SMTO_ABORTIFHUNG, 500, out var granted) == IntPtr.Zero || (granted != new IntPtr(1) && granted != new IntPtr(2)))
            {
                diagnostic = ForegroundDiagnostic($"child-grant-result={granted}, child={childProcess}, parentGrant={parentGrant}, parentGrantError={parentGrantError}");
                return false;
            }
            seed.Close();
            if (Native.GetForegroundWindow() != window)
            {
                Native.SetForegroundWindow(window);
                SendWindowMessageTimeout(window, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 150, out _);
            }
            if (Native.GetForegroundWindow() == window) return true;
            diagnostic = ForegroundDiagnostic("child-not-foreground-after-seed-close");
            return false;
        }
        finally { if (seed.IsVisible) seed.Close(); }
    }

    private static bool PumpUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || timeout.ElapsedMilliseconds >= 2000) frame.Continue = false; };
        try { timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        return condition();
    }

    private static string ForegroundDiagnostic(string stage)
    {
        var error = Marshal.GetLastWin32Error();
        var foreground = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(foreground, out var process);
        var text = new System.Text.StringBuilder(256);
        Native.GetClassName(foreground, text, text.Capacity);
        var className = text.ToString();
        Native.GetWindowText(foreground, text, text.Capacity);
        return $"stage={stage}, lastError={error}, foreground={foreground}, class={className}, title={text}, process={process}, thread={thread}, caller={Environment.ProcessId}";
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < 3000)
        {
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static Process StartFixture()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("No regression host executable."))
        { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--window-fixture");
        start.Environment[FixtureOwnerVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Process.Start(start) ?? throw new InvalidOperationException("Owned native fixture failed to launch.");
    }

    private static IntPtr WaitForWindow(int processId, int index)
    {
        var result = IntPtr.Zero;
        WaitUntil(() =>
        {
            var window = Native.FindWindow(FixtureClassName, FixturePrefix + processId + "." + index);
            Native.GetWindowThreadProcessId(window, out var actual);
            if (actual == processId) result = window;
            return result != IntPtr.Zero;
        });
        return result;
    }

    internal static int RunFixture()
    {
        var windows = new List<IntPtr>();
        var audio = IntPtr.Zero;
        var registered = false;
        var instance = Native.GetModuleHandle(null);
        try
        {
            uint.TryParse(Environment.GetEnvironmentVariable(FixtureOwnerVariable), out _fixtureOwnerProcess);
            var definition = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(FixtureProcedure),
                Instance = instance,
                Background = new IntPtr(6),
                ClassName = FixtureClassName
            };
            if (RegisterClassEx(ref definition) == 0) return 5;
            registered = true;
            var wave = SilentWave();
            audio = Marshal.AllocHGlobal(wave.Length);
            Marshal.Copy(wave, 0, audio, wave.Length);
            PlaySound(audio, IntPtr.Zero, 0x000F); // SND_ASYNC | SND_NODEFAULT | SND_MEMORY | SND_LOOP.
            for (var index = 0; index < 2; index++)
            {
                var handle = CreateWindowEx(0, FixtureClassName, FixturePrefix + Environment.ProcessId + "." + index,
                    FixtureStyle, 100 + index * 80, 100 + index * 60, 640, 480, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (handle == IntPtr.Zero) return 2;
                windows.Add(handle);
            }
            var clientConfiguration = new ClientCreate();
            var configuration = Marshal.AllocHGlobal(Marshal.SizeOf<ClientCreate>());
            try
            {
                Marshal.StructureToPtr(clientConfiguration, configuration, false);
                var client = CreateWindowEx(0, "MDICLIENT", "", 0x50000000, 0, 0, 600, 400, windows[1], IntPtr.Zero, IntPtr.Zero, configuration);
                if (client == IntPtr.Zero) return 3;
                var child = CreateMdiWindow(FixtureClassName, "Owned MDI fixture", 0x10CF0000, 10, 10, 280, 180, client, instance, IntPtr.Zero);
                if (child == IntPtr.Zero) return 4;
            }
            finally { Marshal.FreeHGlobal(configuration); }
            while (windows.Any(Native.IsWindow))
            {
                while (PeekMessage(out var message, IntPtr.Zero, 0, 0, RemoveMessage))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                Thread.Sleep(5);
            }
            return 0;
        }
        finally
        {
            foreach (var window in windows) if (Native.IsWindow(window)) DestroyWindow(window);
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            if (audio != IntPtr.Zero) Marshal.FreeHGlobal(audio);
            if (registered) UnregisterClass(FixtureClassName, instance);
        }
    }

    private static byte[] SilentWave()
    {
        const int sampleRate = 48000;
        const short bytesPerSample = 2;
        var dataLength = sampleRate * bytesPerSample;
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + dataLength); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * bytesPerSample);
        writer.Write(bytesPerSample); writer.Write((short)16); writer.Write("data"u8); writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }

    private static bool SelectSecondOwnedPopup()
    {
        var selected = false;
        _popupSelectionDiagnostic = "owned-popup-not-observed";
        Native.GetCursorPos(out var originalCursor);
        POINT? menuPoint = null;
        try
        {
            WaitUntil(() =>
            {
                var popup = FindOwnedPopup("#32768");
                if (popup == IntPtr.Zero) return false;
                var owner = FindOwnedPopup(WindowMenuOwner.ClassName, WindowMenuOwner.Title);
                if (owner == IntPtr.Zero || Native.GetForegroundWindow() != owner)
                { _popupSelectionDiagnostic = ForegroundDiagnostic($"own-popup-owner-not-foreground, owner={owner}, popup={popup}"); return false; }
                var info = new MenuBarInformation { Size = (uint)Marshal.SizeOf<MenuBarInformation>() };
                if (!GetMenuBarInfo(popup, -4, 0, ref info) || info.Menu == IntPtr.Zero ||
                    !GetMenuItemRect(IntPtr.Zero, info.Menu, 1, out var rectangle) || GetMenuItemId(info.Menu, 1) != 2)
                { _popupSelectionDiagnostic = ForegroundDiagnostic("owned-popup-second-item-unavailable"); return false; }
                var point = new POINT((rectangle.Left + rectangle.Right) / 2, (rectangle.Top + rectangle.Bottom) / 2);
                if (rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top || !Native.SetCursorPos(point.X, point.Y))
                { _popupSelectionDiagnostic = ForegroundDiagnostic("owned-popup-item-position-unavailable"); return false; }
                menuPoint = point;
                if (Native.WindowFromPoint(point) != popup || Native.GetForegroundWindow() != owner)
                { _popupSelectionDiagnostic = ForegroundDiagnostic("owned-popup-item-not-under-pointer"); return false; }
                ForegroundInput[] input = [new() { Mouse = new() { Flags = 2 } }, new() { Mouse = new() { Flags = 4 } }];
                selected = SendInput((uint)input.Length, input, Marshal.SizeOf<ForegroundInput>()) == input.Length;
                _popupSelectionDiagnostic = selected ? "owned-popup-item2-click-delivered" : ForegroundDiagnostic("owned-popup-item2-click-rejected");
                return selected;
            });
            if (selected && WaitUntil(() => FindOwnedPopup(WindowMenuOwner.ClassName, WindowMenuOwner.Title) == IntPtr.Zero)) return true;
            // TrackPopupMenu owns a modal loop. A failed selector must terminate only
            // this test process's menu instead of leaving the entire test run blocked.
            var owner = FindOwnedPopup(WindowMenuOwner.ClassName, WindowMenuOwner.Title);
            if (owner != IntPtr.Zero) Native.PostMessage(owner, 0x001F, IntPtr.Zero, IntPtr.Zero); // WM_CANCELMODE.
            return false;
        }
        finally
        {
            if (menuPoint is { } placed && Native.GetCursorPos(out var current) && current.X == placed.X && current.Y == placed.Y)
                Native.SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static IntPtr FindOwnedPopup(string className, string? title = null)
    {
        var result = IntPtr.Zero;
        var text = new System.Text.StringBuilder(256);
        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window)) return true;
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId != Environment.ProcessId) return true;
            Native.GetClassName(window, text, text.Capacity);
            if (!string.Equals(text.ToString(), className, StringComparison.Ordinal)) return true;
            if (title is not null)
            {
                Native.GetWindowText(window, text, text.Capacity);
                if (!string.Equals(text.ToString(), title, StringComparison.Ordinal)) return true;
            }
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr HandleFixtureMessage(IntPtr window, uint message, nuint wParam, nint lParam)
    {
        if (message == GrantForegroundMessage)
        {
            if (_fixtureOwnerProcess == 0 || wParam != _fixtureOwnerProcess) return new IntPtr(-1);
            if (!Native.SetForegroundWindow(window)) return new IntPtr(-2);
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var foregroundProcess);
            if (foregroundProcess != Environment.ProcessId) return new IntPtr(-3);
            // Foreground ownership is the observable prerequisite; retain the grant
            // result separately because it is not guaranteed to be needed by Windows.
            return AllowSetForegroundWindow(_fixtureOwnerProcess) ? new IntPtr(1) : new IntPtr(2);
        }
        if (message == HorizontalWheel)
        {
            SetProp(window, HorizontalScrollProperty, new IntPtr(unchecked((short)((ulong)wParam >> 16))));
            return IntPtr.Zero;
        }
        return ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & MdiChildStyle) != 0
            ? DefMdiChildProc(window, message, wParam, lParam) : DefWindowProc(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public POINT Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientCreate { public IntPtr Menu; public uint FirstChild; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct ForegroundInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ForegroundKeyboardInput Keyboard;
        [FieldOffset(8)] public ForegroundMouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ForegroundKeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ForegroundMouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MenuBarInformation
    {
        public uint Size;
        public RECT Rectangle;
        public IntPtr Menu, MenuWindow;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, nuint wParam, nint lParam);
    private sealed class SettingsContext : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(string.Empty);
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public void Notify(string message) { }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(long exStyle, string className, string title, long style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeoutSimple(IntPtr window, int message,
        IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    [DllImport("user32.dll", EntryPoint = "CreateMDIWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMdiWindow(string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "DefMDIChildProcW")] private static extern IntPtr DefMdiChildProc(IntPtr window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, ForegroundInput[] input, int size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMenuBarInfo(IntPtr window, int objectId, int itemId, ref MenuBarInformation information);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint index, out RECT rectangle);
    [DllImport("user32.dll", EntryPoint = "GetMenuItemID")] private static extern uint GetMenuItemId(IntPtr menu, int index);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint firstThread, uint secondThread, [MarshalAs(UnmanagedType.Bool)] bool attach);
}
