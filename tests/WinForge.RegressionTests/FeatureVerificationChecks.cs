using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class FeatureVerificationChecks
{
    internal static void Run(string directory, Action<bool, string> check, bool nativeSmoke)
    {
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        TestWorkspacePlacement(extras, check);
        TestPasteCoordination(extras, check);
        TestMouseSettings(extras, directory, check);
        TestAnimationSettings(extras, check);
        TestWordCorrectionEngine(extras, check);
        TaskbarFeatureChecks.Run(check, nativeSmoke);
        NotificationsPlacementChecks.Run(extras, directory, check, nativeSmoke);
        DisplayBrightnessChecks.Run(extras, check);
        if (!nativeSmoke) return;
        PdfPreviewPipelineChecks.Run(directory, check);
        TestScreenCapture(check);
        TestOcr(check);
        TestPdfPreview(extras, directory, check);
        TestTilingExclusions(check);
        TestWindowCorners(extras, check);
        TestAlwaysOnTop(directory, check);
        TestKeyboardDelivery(extras, check);
        TestMediaPreview(extras, directory, check);
    }

    private static void TestAlwaysOnTop(string directory, Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.AlwaysOnTop.dll"));
        var moduleType = assembly.GetType("WinForge.Modules.AlwaysOnTop.AlwaysOnTopModule")!;
        var module = (ITweakModule)Activator.CreateInstance(moduleType)!;
        var context = new FeatureContext(directory);
        context.Settings.Set("border-enabled", false);
        module.Initialize(context);
        var window = new Window
        {
            Title = "WinForge topmost restoration fixture", ShowActivated = false, ShowInTaskbar = false,
            Width = 250, Height = 180, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        var toggle = moduleType.GetMethod("ToggleWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            module.Activate();
            toggle.Invoke(module, [handle]);
            check(IsTopmost(handle), "Always On Top sets the real native topmost style on an owned window");
            toggle.Invoke(module, [handle]);
            check(!IsTopmost(handle), "Always On Top toggles an originally normal window back to its original state");
            toggle.Invoke(module, [handle]);
            module.Deactivate();
            check(!IsTopmost(handle), "Disabling Always On Top restores its pinned window's original native style");

            if (!Native.SetWindowPos(handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE))
                throw new InvalidOperationException("Topmost fixture baseline could not be set.");
            module.Activate();
            toggle.Invoke(module, [handle]);
            module.Deactivate();
            check(IsTopmost(handle), "Always On Top preserves a window that was already topmost before WinForge pinned it");
            window.Close();
            module.Activate();
            var before = context.Notifications.Count;
            toggle.Invoke(module, [handle]);
            var tracked = (System.Collections.IDictionary)moduleType.GetField("_pinned", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
            check(tracked.Count == 0 && context.Notifications.Count == before + 1,
                "Always On Top reports a destroyed target without adding a falsely pinned window");
        }
        finally
        {
            module.Deactivate();
            window.Close();
        }
    }

    private static bool IsTopmost(IntPtr handle) => (Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64() & 8) != 0;

    private static void TestAnimationSettings(Assembly extras, Action<bool, string> check)
    {
        var moduleType = extras.GetType("WinForge.Modules.Extras.WindowTweaksModule")!;
        var handlerType = moduleType.GetNestedType("SystemParametersHandler", BindingFlags.NonPublic)!;
        var fixture = new AnimationSettingsFixture();
        var handler = Delegate.CreateDelegate(handlerType, fixture, nameof(AnimationSettingsFixture.Invoke));
        var module = moduleType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single().Invoke([handler]);
        var original = (int)moduleType.GetMethod("ReadAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(module, null)!;
        check(original == 1 && fixture.Calls.Single() == (0x48u, 8u),
            "Animation preference reads pass the native ANIMATIONINFO size in both required fields");
        moduleType.GetMethod("WriteAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(module, [0]);
        check(fixture.Animation == 0 && fixture.Calls.Last() == (0x49u, 8u),
            "Animation preference writes pass the required native size and the requested setting");
    }

    private sealed class AnimationSettingsFixture
    {
        public List<(uint Action, uint Parameter)> Calls { get; } = [];
        public int Animation { get; private set; } = 1;

        public bool Invoke(uint action, uint parameter, IntPtr data, uint flags)
        {
            Calls.Add((action, parameter));
            if (parameter != 8 || Marshal.ReadInt32(data) != 8) return false;
            if (action == 0x48) Marshal.WriteInt32(data, 4, Animation);
            else if (action == 0x49) Animation = Marshal.ReadInt32(data, 4);
            else return false;
            return true;
        }
    }

    private static void TestWindowCorners(Assembly extras, Action<bool, string> check)
    {
        const int cornerAttribute = 33;
        const int initialPreference = 2;
        var window = new Window
        {
            Title = "WinForge corner restoration fixture", ShowActivated = false, ShowInTaskbar = false,
            Width = 250, Height = 180, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        var moduleType = extras.GetType("WinForge.Modules.Extras.SquareCornersModule")!;
        var module = (ITweakModule)Activator.CreateInstance(moduleType)!;
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var preference = initialPreference;
            Marshal.ThrowExceptionForHR(Native.DwmSetWindowAttribute(handle, cornerAttribute, ref preference, sizeof(int)));
            moduleType.GetProperty("IsActive")!.SetValue(module, true);
            moduleType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(IntPtr)], null)!.Invoke(module, [handle]);
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, cornerAttribute, out var applied, sizeof(int)));
            check(applied == 1, "Window corner module applies its actual DWM preference to an owned fixture window");
            module.Deactivate();
            Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, cornerAttribute, out var restored, sizeof(int)));
            check(restored == initialPreference,
                "Disabling window corner customization restores the window's original explicit preference instead of replacing it with a default");
        }
        finally
        {
            module.Deactivate();
            window.Close();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int valueSize);

    private static void TestMouseSettings(Assembly extras, string directory, Action<bool, string> check)
    {
        var moduleType = extras.GetType("WinForge.Modules.Extras.MouseTweaksModule")!;
        var handlerType = moduleType.GetNestedType("SystemParametersHandler", BindingFlags.NonPublic)!;
        var constructor = moduleType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var backend = new MouseSettingsFixture();
        var handler = Delegate.CreateDelegate(handlerType, backend, nameof(MouseSettingsFixture.Invoke));
        var module = (ITweakModule)constructor.Invoke([handler, (Func<uint>)(() => backend.DoubleClickTime)]);
        var context = new FeatureContext(directory);
        module.Initialize(context);
        module.Activate();
        check(backend.DoubleClickTime == 612 && backend.Calls.All(call => call.Action != 0x21),
            "Mouse module reads double-click timing without resetting it or swapping the left/right buttons");
        context.Settings.Set("double-click", 400);
        context.Settings.Set("scroll-lines", 7);
        context.Settings.Set("acceleration", true);
        module.Apply();
        check(backend.DoubleClickTime == 400 && backend.WheelLines == 7 && backend.Mouse.SequenceEqual(new[] { 0, 0, 0 }) &&
              backend.Calls.Any(call => call.Action == 0x20 && call.Parameter == 400) && backend.Calls.All(call => call.Action != 0x21),
            "Mouse module applies double-click timing through SPI_SETDOUBLECLICKTIME while preserving the mouse-button mapping");
        module.Deactivate();
        check(backend.DoubleClickTime == 612 && backend.WheelLines == 3 && backend.Mouse.SequenceEqual(new[] { 6, 10, 1 }),
            "Disabling mouse tweaks restores its exact captured timing, wheel and acceleration preferences");
        var callsAfterRestore = backend.Calls.Count;
        module.Deactivate();
        check(backend.Calls.Count == callsAfterRestore, "Mouse preference restoration is idempotent");

        var failingBackend = new MouseSettingsFixture { FailRead = true };
        var failingHandler = Delegate.CreateDelegate(handlerType, failingBackend, nameof(MouseSettingsFixture.Invoke));
        var failingModule = (ITweakModule)constructor.Invoke([failingHandler, (Func<uint>)(() => failingBackend.DoubleClickTime)]);
        failingModule.Initialize(new FeatureContext(directory));
        var failed = false;
        try { failingModule.Activate(); }
        catch (System.ComponentModel.Win32Exception) { failed = true; }
        failingModule.Deactivate();
        check(failed && !failingModule.IsActive && failingBackend.Calls.All(call => call.Action == 0x03),
            "Failed mouse preference capture leaves the module inactive and does not restore invented defaults");
    }

    private sealed class MouseSettingsFixture
    {
        public List<(uint Action, uint Parameter)> Calls { get; } = [];
        public int[] Mouse { get; private set; } = [6, 10, 1];
        public uint DoubleClickTime { get; private set; } = 612;
        public int WheelLines { get; private set; } = 3;
        public bool FailRead { get; init; }

        public bool Invoke(uint action, uint parameter, IntPtr data, uint flags)
        {
            Calls.Add((action, parameter));
            switch (action)
            {
                case 0x03:
                    if (FailRead) return false;
                    Marshal.Copy(Mouse, 0, data, Mouse.Length);
                    return true;
                case 0x04:
                    Marshal.Copy(data, Mouse, 0, Mouse.Length);
                    return true;
                case 0x68:
                    Marshal.WriteInt32(data, WheelLines);
                    return true;
                case 0x69:
                    WheelLines = checked((int)parameter);
                    return true;
                case 0x20:
                    DoubleClickTime = parameter;
                    return true;
                default:
                    throw new InvalidOperationException($"Unexpected mouse settings API action: {action:X}");
            }
        }
    }

    private sealed class FeatureContext(string directory) : IModuleContext
    {
        public IModuleSettings Settings { get; } = new ModuleSettings(new Dictionary<string, string>(), () => { });
        public ILogger Log { get; } = new FileLogger(Path.Combine(directory, "feature-verification.log"));
        public IHotkeyService Hotkeys { get; } = new FakeHotkeys();
        public List<string> Notifications { get; } = [];
        public void Notify(string message) => Notifications.Add(message);
    }

    private static void TestPasteCoordination(Assembly extras, Action<bool, string> check)
    {
        var operation = extras.GetType("WinForge.Modules.Extras.PasteOperation")!
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(method => method.Name == "ExecuteAsync" && method.GetParameters().Length == 6);
        var target = new IntPtr(100);
        var foreground = target;
        var checks = 0;
        var deliveries = 0;
        var delays = 0;
        var released = (Task<bool>)operation.Invoke(null,
            [target, (Func<IntPtr>)(() => foreground), (Func<bool>)(() => ++checks <= 3),
             (Action)(() => deliveries++), CancellationToken.None,
             (Func<TimeSpan, CancellationToken, Task>)((_, _) => { delays++; return Task.CompletedTask; })])!;
        check(released.GetAwaiter().GetResult() && delays == 3 && deliveries == 1,
            "Paste waits for physical activation modifiers to release before delivering exactly one shortcut");

        deliveries = 0;
        var moved = (Task<bool>)operation.Invoke(null,
            [target, (Func<IntPtr>)(() => foreground), (Func<bool>)(() => true),
             (Action)(() => deliveries++), CancellationToken.None,
             (Func<TimeSpan, CancellationToken, Task>)((_, _) => { foreground = new IntPtr(200); return Task.CompletedTask; })])!;
        check(!moved.GetAwaiter().GetResult() && deliveries == 0,
            "Paste cancels if foreground focus changes while the activation keys are held");

        foreach (var moduleName in new[] { "PlainPasteModule", "AdvancedPasteModule" })
        {
            foreground = target;
            deliveries = 0;
            using var cancellation = new CancellationTokenSource();
            var moduleType = extras.GetType("WinForge.Modules.Extras." + moduleName)!;
            var module = (ITweakModule)Activator.CreateInstance(moduleType)!;
            moduleType.GetField("_pendingPaste", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, cancellation);
            var pending = (Task<bool>)operation.Invoke(null,
                [target, (Func<IntPtr>)(() => foreground), (Func<bool>)(() => true),
                 (Action)(() => deliveries++), cancellation.Token,
                 (Func<TimeSpan, CancellationToken, Task>)((_, _) => { module.Deactivate(); return Task.CompletedTask; })])!;
            try { pending.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            check(pending.IsCanceled && deliveries == 0 && cancellation.IsCancellationRequested,
                $"Disabling {moduleName} cancels pending delivery without injecting input");
        }

        foreground = target;
        deliveries = 0;
        var held = (Task<bool>)operation.Invoke(null,
            [target, (Func<IntPtr>)(() => foreground), (Func<bool>)(() => true),
             (Action)(() => deliveries++), CancellationToken.None,
             (Func<TimeSpan, CancellationToken, Task>)((_, _) => Task.CompletedTask)])!;
        check(!held.GetAwaiter().GetResult() && deliveries == 0,
            "Paste has a bounded wait and never injects a modified shortcut after its key-release deadline");
        check(Marshal.SizeOf(extras.GetType("WinForge.Modules.Extras.KeyboardInputSender+INPUT")!) == 40,
            "Shared keyboard injection reserves the native x64 INPUT union required by SendInput");
    }

    private static void TestKeyboardDelivery(Assembly extras, Action<bool, string> check)
    {
        var originalForeground = Native.GetForegroundWindow();
        var editor = new TextBox();
        var window = new Window
        {
            Title = "WinForge keyboard delivery fixture", Width = 360, Height = 160,
            Content = editor, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        try
        {
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            PumpUntil(() => window.IsLoaded, TimeSpan.FromSeconds(3), "Keyboard fixture load");
            FocusFixture(window, editor, handle);
            PumpUntil(() => Native.GetForegroundWindow() == handle && editor.IsKeyboardFocused,
                TimeSpan.FromSeconds(3), "Keyboard fixture focus");
            const string fixture = "WinForge 12345";
            var accepted = (bool)extras.GetType("WinForge.Modules.Extras.KeyboardManagerModule")!
                .GetMethod("SendText", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [fixture, Array.Empty<int>()])!;
            check(accepted, "Own keyboard engine submits real Unicode input using the Windows INPUT ABI");
            PumpUntil(() => editor.Text == fixture, TimeSpan.FromSeconds(3), "Keyboard Unicode delivery");
            check(editor.Text == fixture, "Own keyboard engine delivers the exact Unicode text to a real fixture editor");
            extras.GetType("WinForge.Modules.Extras.QuickAccentModule")!
                .GetMethod("SendUnicode", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, ['ğ']);
            PumpUntil(() => editor.Text == fixture + "ğ", TimeSpan.FromSeconds(3), "Quick Accent Unicode delivery");
            check(editor.Text == fixture + "ğ", "Quick Accent injects its actual Unicode character through the shared native keyboard engine");
        }
        finally
        {
            window.Close();
            if (originalForeground != IntPtr.Zero && Native.IsWindow(originalForeground)) SetForegroundWindow(originalForeground);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static void FocusFixture(Window window, TextBox editor, IntPtr handle)
    {
        var currentThread = GetCurrentThreadId();
        var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(handle);
            window.Activate();
            editor.Focus();
            System.Windows.Input.Keyboard.Focus(editor);
        }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint firstThread, uint secondThread, bool attach);

    private static void TestWorkspacePlacement(Assembly extras, Action<bool, string> check)
    {
        var native = extras.GetType("WinForge.Modules.Extras.WorkspaceNative")!;
        var savedType = extras.GetType("WinForge.Modules.Extras.WorkspaceWindow")!;
        var boundsType = extras.GetType("WinForge.Modules.Extras.WorkspaceBounds")!;
        var saved = Activator.CreateInstance(savedType,
            "app.exe", "app", "fixture", -2000, -50, 1200, 1000, false, "old", -2000, -50, 1920, 1080)!;
        var currentMonitor = Activator.CreateInstance(boundsType, -1280, 40, 0, 720, "new", true)!;
        var map = native.GetMethod("MapToMonitor", BindingFlags.NonPublic | BindingFlags.Static)!;
        var placed = (RECT)map.Invoke(null, [saved, currentMonitor])!;
        check(placed.Left >= -1280 && placed.Top >= 40 && placed.Right <= 0 && placed.Bottom <= 720,
            "Workspace remapping keeps a large saved window inside a negative-coordinate monitor work area");
        var smallMonitor = Activator.CreateInstance(boundsType, 0, 0, 200, 100, "tiny", true)!;
        var smallPlacement = (RECT)map.Invoke(null, [saved, smallMonitor])!;
        check(smallPlacement.Left == 0 && smallPlacement.Top == 0 && smallPlacement.Right == 200 && smallPlacement.Bottom == 100,
            "Workspace remapping handles work areas smaller than its preferred minimum without throwing or overflowing");
        var variant = extras.GetType("WinForge.Modules.Extras.WindowsAppUserModel+PropVariant")!;
        check(Marshal.SizeOf(variant) == 24,
            "Workspace property-store interop reserves the full native x64 PROPVARIANT buffer");
        try
        {
            native.GetMethod("RestoreWindow")!.Invoke(null, [IntPtr.Zero, placed, false]);
            check(false, "Workspace restoration rejects a destroyed window instead of reporting success");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
        {
            check(true, "Workspace restoration rejects a destroyed window instead of reporting success");
        }
    }

    private static void TestScreenCapture(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.TextGrabber.dll"));
        var capture = assembly.GetType("WinForge.Modules.TextGrabber.ScreenCapture")!.GetMethod("Capture")!;
        var pixels = ((byte[] Pixels, int Width, int Height))capture.Invoke(null, [0, 0, 32, 24])!;
        check(pixels.Width == 32 && pixels.Height == 24 && pixels.Pixels.Length == 32 * 24 * 4,
            "Screen capture reads every scanline from a detached GDI bitmap");
        try
        {
            capture.Invoke(null, [0, 0, int.MaxValue, int.MaxValue]);
            check(false, "Screen capture rejects an overflowing allocation before requesting GDI resources");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException)
        {
            check(true, "Screen capture rejects an overflowing allocation before requesting GDI resources");
        }
    }

    private static void TestOcr(Action<bool, string> check)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 4000, 500));
            drawing.DrawText(new FormattedText("WinForge 12345", CultureInfo.GetCultureInfo("en-US"),
                FlowDirection.LeftToRight, new Typeface("Arial"), 110, Brushes.Black, 1), new Point(120, 100));
        }
        var bitmap = new RenderTargetBitmap(4000, 500, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[4000 * 500 * 4];
        bitmap.CopyPixels(pixels, 4000 * 4, 0);
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 0;
        var screenStyleImage = BitmapSource.Create(4000, 500, 96, 96, PixelFormats.Bgra32, null, pixels, 4000 * 4);
        var task = OcrRecognitionService.RecognizeAsync(screenStyleImage);
        PumpUntil(() => task.IsCompleted, TimeSpan.FromSeconds(15), "Local OCR recognition");
        var text = task.GetAwaiter().GetResult();
        check(text.Contains("WinForge", StringComparison.OrdinalIgnoreCase) && text.Contains("12345", StringComparison.Ordinal),
            "Windows OCR recognizes a 4000-pixel-wide fixture with GDI-style zero alpha after bounded scaling");
    }

    private static void TestMediaPreview(Assembly extras, string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "preview-lifecycle.wav");
        WriteSilentWave(path);
        var documentType = extras.GetType("WinForge.Modules.Extras.PeekPreviewDocument")!;
        var document = Activator.CreateInstance(documentType, "audio", "Fixture", "", null, false, path, 0)!;
        var windowType = extras.GetType("WinForge.Modules.Extras.PeekPreviewWindow")!;
        var window = (Window)Activator.CreateInstance(windowType, path, document)!;
        var media = (MediaElement)windowType.GetField("_mediaElement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Left = -10000;
        window.Top = -10000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Exception? mediaError = null;
        media.MediaFailed += (_, args) => mediaError = args.ErrorException;
        try
        {
            window.Show();
            var opened = windowType.GetField("_mediaOpened", BindingFlags.Instance | BindingFlags.NonPublic)!;
            PumpUntil(() => (bool)opened.GetValue(window)! || mediaError is not null,
                TimeSpan.FromSeconds(10), "Media preview preparation");
            if (mediaError is not null) throw new InvalidOperationException("Windows failed to load the PCM WAV fixture.", mediaError);
            check(media.NaturalDuration.HasTimeSpan && media.Position == TimeSpan.Zero,
                "Media preview opens a real PCM WAV while paused before the first Play click");
            windowType.GetMethod("TogglePlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            PumpUntil(() => media.Position > TimeSpan.Zero, TimeSpan.FromSeconds(5), "Media preview first playback");
            check(media.Position > TimeSpan.Zero, "Media preview responds to the first Play click");
            windowType.GetMethod("StopPlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            check(media.Position == TimeSpan.Zero, "Media preview Stop resets playback to its beginning");
        }
        finally { window.Close(); }
        PumpUntil(() => !media.NaturalDuration.HasTimeSpan, TimeSpan.FromSeconds(5), "Media preview resource release");
        check(!media.NaturalDuration.HasTimeSpan, "Closing a media preview closes the native media resource");
    }

    private static void TestPdfPreview(Assembly extras, string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "tall-preview.pdf");
        WritePdf(path, 100, 10000);
        var loader = extras.GetType("WinForge.Modules.Extras.PeekPreviewLoader")!;
        var operation = (Task)loader.GetMethod("LoadAsync")!.Invoke(null, [path, 1024, 1024L * 1024, 1024L * 1024])!;
        PumpUntil(() => operation.IsCompleted, TimeSpan.FromSeconds(15), "PDF preview rendering");
        operation.GetAwaiter().GetResult();
        var document = operation.GetType().GetProperty("Result")!.GetValue(operation)!;
        var image = (BitmapSource)document.GetType().GetProperty("Image")!.GetValue(document)!;
        check((string)document.GetType().GetProperty("Kind")!.GetValue(document)! == "pdf" &&
              image.PixelWidth > 0 && image.PixelHeight > image.PixelWidth &&
              image.PixelWidth <= 1400 && image.PixelHeight <= 1400,
            "PDF preview renders an actual tall PDF page within bounded width and height before allocating its image");
    }

    private static void WritePdf(string path, int width, int height)
    {
        const string content = "BT /F1 12 Tf 5 20 Td (WinForge PDF fixture) Tj ET\n";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        ];
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        builder.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
    }

    private static void TestTilingExclusions(Action<bool, string> check)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Tiling.dll"));
        var moduleType = assembly.GetType("WinForge.Modules.Tiling.TilingModule")!;
        var module = Activator.CreateInstance(moduleType)!;
        moduleType.GetField("_noMinimize", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(module, true);
        var window = new Window
        {
            Title = "WinForge tiling exclusion fixture", ShowActivated = false, ShowInTaskbar = false,
            Width = 200, Height = 160, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        try
        {
            window.Show();
            window.WindowState = WindowState.Minimized;
            var handle = new WindowInteropHelper(window).Handle;
            PumpUntil(() => Native.IsIconic(handle), TimeSpan.FromSeconds(3), "Excluded window minimization");
            object[] parameters = [handle, IntPtr.Zero, new RECT()];
            var eligible = (bool)moduleType.GetMethod("TryTileable", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(module, parameters)!;
            check(!eligible && Native.IsIconic(handle),
                "Tiling checks process exclusions before restoring minimized windows, leaving WinForge's own windows minimized");
        }
        finally { window.Close(); }
    }

    private static void WriteSilentWave(string path)
    {
        const int sampleRate = 8000;
        const int dataLength = sampleRate * 2 * 3;
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string operation)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed >= timeout) throw new TimeoutException(operation);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }

    internal static void TestWordCorrectionEngine(Assembly extras, Action<bool, string> check)
    {
        var spellEngineType = extras.GetType("WinForge.Modules.Extras.SpellEngine")!;
        var instanceProp = spellEngineType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)!;
        var engine = instanceProp.GetValue(null)!;
        var isAvailableProp = spellEngineType.GetProperty("IsAvailable", BindingFlags.Instance | BindingFlags.Public)!;
        var isAvailable = (bool)isAvailableProp.GetValue(engine)!;
        check(isAvailable, "Native spell engine is available and independent of Windows COM or language packs");

        var isCorrectMethod = spellEngineType.GetMethod("IsCorrect", BindingFlags.Instance | BindingFlags.Public)!;
        var findCorrectionMethod = spellEngineType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name == "FindCorrection" && m.GetParameters().Length == 8);

        // Real-world typing checks
        var realWorldTypos = new (string Input, string Expected)[]
        {
            ("tamma", "tamam"),
            ("ncak", "ancak"),
            ("düzetlemdi", "düzeltmedi"),
            ("melela", "mesela"),
            ("birsürü", "bir sürü")
        };
        foreach (var (input, expected) in realWorldTypos)
        {
            var isCorrect = (bool)isCorrectMethod.Invoke(engine, [input, "tr-TR"])!;
            var correction = (string?)findCorrectionMethod.Invoke(engine, [input, "tr-TR", null, true, true, true, true, null]);
            check(!isCorrect && correction == expected, $"Real-world typo '{input}' is corrected to '{expected}'");
        }
        foreach (var (input, expected) in new[]
                 {
                     ("dahaa", "daha"),
                     ("yaaa", "ya"),
                     ("tatlımmm", "tatlım")
                 })
        {
            var correction = (string?)findCorrectionMethod.Invoke(engine,
                [input, "tr-TR", null, true, true, true, false, null]);
            check(correction == expected,
                $"High-confidence repeated-character typo '{input}' collapses to '{expected}'");
        }
        check(findCorrectionMethod.Invoke(engine, ["kooperatif", "tr-TR", null, true, true, true, false, null]) is null,
            "Valid Turkish spelling with a legitimate repeated character is preserved");
        check(findCorrectionMethod.Invoke(engine, ["alla", "tr-TR", "ya", true, true, true, false, null]) is null,
            "An internal double letter is not collapsed into a different valid word");
        check((bool)isCorrectMethod.Invoke(engine, ["düzeltmedi", "tr-TR"])!, "Valid conjugated verb 'düzeltmedi' is recognized as correct");

        // Turkish common typos
        var trTypos = new (string Typo, string Expected)[]
        {
            ("herkez", "herkes"),
            ("yanlız", "yalnız"),
            ("yalnış", "yanlış"),
            ("orjinal", "orijinal"),
            ("kiprik", "kirpik"),
            ("süpriz", "sürpriz"),
            ("traş", "tıraş"),
            ("şarz", "şarj"),
            ("egsoz", "egzoz"),
            ("birşey", "bir şey"),
            ("herşey", "her şey"),
            ("tabiki", "tabii ki"),
            ("saol", "sağol")
        };
        foreach (var (typo, expected) in trTypos)
        {
            var isCorrect = (bool)isCorrectMethod.Invoke(engine, [typo, "tr-TR"])!;
            var correction = (string?)findCorrectionMethod.Invoke(engine, [typo, "tr-TR", null, true, true, true, true, null]);
            check(!isCorrect && correction == expected, $"Turkish typo '{typo}' is corrected to '{expected}'");
        }

        // Turkish de-asciification
        var trDeasciify = new (string Ascii, string Expected)[]
        {
            ("calisiyorum", "çalışıyorum"),
            ("tesekkurler", "teşekkürler"),
            ("gorusuruz", "görüşürüz"),
            ("ogrenci", "öğrenci"),
            ("hoscakal", "hoşça kal"),
            ("gunaydin", "günaydın")
        };
        foreach (var (ascii, expected) in trDeasciify)
        {
            var correction = (string?)findCorrectionMethod.Invoke(engine, [ascii, "tr-TR", null, true, true, true, true, null]);
            check(correction == expected, $"Turkish de-asciifier converts '{ascii}' to '{expected}'");
        }

        // English common typos & contractions
        var enTypos = new (string Typo, string Expected)[]
        {
            ("teh", "the"),
            ("dont", "don't"),
            ("cant", "can't"),
            ("recieve", "receive"),
            ("seperate", "separate"),
            ("definately", "definitely")
        };
        foreach (var (typo, expected) in enTypos)
        {
            var isCorrect = (bool)isCorrectMethod.Invoke(engine, [typo, "en-US"])!;
            var correction = (string?)findCorrectionMethod.Invoke(engine, [typo, "en-US", null, true, true, true, true, null]);
            check(!isCorrect && correction == expected, $"English typo '{typo}' is corrected to '{expected}'");
        }

        // Correct words (should not be marked incorrect)
        var correctWords = new (string Word, string Tag)[]
        {
            ("kitap", "tr-TR"),
            ("bilgisayar", "tr-TR"),
            ("kitaplar", "tr-TR"),
            ("evde", "tr-TR"),
            ("computer", "en-US"),
            ("definitely", "en-US"),
            ("playing", "en-US")
        };
        foreach (var (word, tag) in correctWords)
        {
            var isCorrect = (bool)isCorrectMethod.Invoke(engine, [word, tag])!;
            check(isCorrect, $"Valid word '{word}' ({tag}) is recognized as correct");
        }

        // Case preservation
        var autoCorrectModuleType = extras.GetType("WinForge.Modules.Extras.AutoCorrectModule")!;
        var matchCaseMethod = autoCorrectModuleType.GetMethod("MatchCase", BindingFlags.Static | BindingFlags.NonPublic)!;
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");
        var allCaps = (string)matchCaseMethod.Invoke(null, ["HERKEZ", "herkes", trCulture])!;
        var titleCase = (string)matchCaseMethod.Invoke(null, ["Herkez", "herkes", trCulture])!;
        var lowerCase = (string)matchCaseMethod.Invoke(null, ["herkez", "herkes", trCulture])!;
        check(allCaps == "HERKES" && titleCase == "Herkes" && lowerCase == "herkes", "Case matching handles uppercase, titlecase, and lowercase with culture sensitivity");

        // Context analysis tests (Bigram disambiguation & collocation typo fix)
        var contextModelType = extras.GetType("WinForge.Modules.Extras.SpellContextModel")!;
        var tryFindContextMethod = contextModelType.GetMethod("TryFindContextCorrection", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var contextFixTr = (string?)tryFindContextMethod.Invoke(null, ["her", "ver", "tr-TR"]);
        check(contextFixTr == "yer", "Context model correctly disambiguates collocation typo 'her ver' -> 'her yer'");
        var contextFixEn = (string?)tryFindContextMethod.Invoke(null, ["in", "teh", "en-US"]);
        check(contextFixEn == "the", "Context model correctly fixes English collocation typo 'in teh' -> 'in the'");

        var fullContextCorrection = (string?)findCorrectionMethod.Invoke(engine, ["ver", "tr-TR", "her", true, true, true, true, null]);
        check(fullContextCorrection == "yer", "SpellEngine resolves context correction when previous word is provided");

        // Personalized frequency & adaptive learning tests
        var trackerType = extras.GetType("WinForge.Modules.Extras.UserFrequencyTracker")!;
        var trackerPath = Path.Combine(Path.GetTempPath(), "WinForge.Tests", $"frequency-{Guid.NewGuid():N}.json");
        var tracker = Activator.CreateInstance(trackerType, [trackerPath])!;
        var recordWordMethod = trackerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "RecordWord" && method.GetParameters().Length == 2);
        var isLearnedMethod = trackerType.GetMethod("IsLearned", BindingFlags.Instance | BindingFlags.Public)!;
        var clearMethod = trackerType.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)!;

        clearMethod.Invoke(tracker, null);
        const string customJargon = "AntigravityCore";
        for (var index = 0; index < 10; index++) recordWordMethod.Invoke(tracker, [customJargon, false]);
        check(!(bool)isLearnedMethod.Invoke(tracker, [customJargon])!,
            "Repeatedly typed unknown words are not silently added as correct vocabulary");
        trackerType.GetMethod("LearnWord", BindingFlags.Instance | BindingFlags.Public)!.Invoke(tracker, [customJargon]);
        check((bool)isLearnedMethod.Invoke(tracker, [customJargon])!, "Explicitly accepted words can be added to the personal dictionary");
        clearMethod.Invoke(tracker, null);

        // Module descriptor check
        var module = (ITweakModule)Activator.CreateInstance(autoCorrectModuleType)!;
        check(module.Id == "auto-correct", "AutoCorrectModule ID is 'auto-correct'");
        check(module.Settings.Any(s => s.Key == "undo-on-backspace"), "AutoCorrectModule exposes undo-on-backspace toggle");
        check(module.Settings.Any(s => s.Key == "context-analysis"), "AutoCorrectModule exposes context-analysis toggle");
        check(module.Settings.Any(s => s.Key == "suggestion-strip"), "AutoCorrectModule exposes a selectable suggestion-strip toggle");
        check(module.Settings.Any(s => s.Key == "personalized-frequency" && s.Description!.Contains("birlikte", StringComparison.OrdinalIgnoreCase)),
            "AutoCorrectModule explains personalized word and phrase ranking");
        check(module.Settings.Any(s => s.Key == "personal-dictionary" && s.Kind == SettingKind.Text),
            "AutoCorrectModule exposes a personal dictionary setting for explicit vocabulary");
        check(module.Settings.Any(s => s.Key == "deasciify-turkish"), "AutoCorrectModule exposes deasciify-turkish toggle");
        check(module.Settings.Any(s => s.Key == "text-expansions"), "AutoCorrectModule exposes text-expansions toggle");
        AutoCorrectQualityChecks.Run(extras, check);
    }
}
