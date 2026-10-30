using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Text;
using WinForge.ClipboardLine;
using WinForge.Core;

internal static class PasteTargetChecks
{
    internal static void Run(Action<bool, string> check, Action<Func<bool>> pumpUntil, string root, IModuleContext context)
    {
        var previous = GetForegroundWindow();
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(PasteTargetChecks).Assembly.Location);
        start.ArgumentList.Add("--uia-fixture");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("UIA fixture could not start.");
        try
        {
            string Read() { var task = process.StandardOutput.ReadLineAsync(); pumpUntil(() => task.IsCompleted); return task.GetAwaiter().GetResult() ?? throw new InvalidOperationException("UIA fixture exited: " + process.StandardError.ReadToEnd()); }
            void Command(string command) { process.StandardInput.WriteLine(command); process.StandardInput.Flush(); check(Read() == "OK", "The owned UIA fixture accepted " + command + "."); }
            check(Read() == "READY", "The owned external editable-field fixture is running.");
            process.Refresh();
            var fixtureWindow = process.MainWindowHandle;
            GetWindowThreadProcessId(fixtureWindow, out var ownerPid);
            check(fixtureWindow != IntPtr.Zero && ownerPid == process.Id, "The fixture window belongs to the started test process.");
            SetForegroundForTest(fixtureWindow);
            using var tracker = new PasteTargetTracker();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                SetForegroundForTest(fixtureWindow); Command("locked"); Command("editable");
                GetWindowThreadProcessId(GetForegroundWindow(), out var active);
                if (active == process.Id) break;
            }
            GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
            check(foregroundPid == process.Id, $"The external fixture is the actual foreground process (expected {process.Id}, got {foregroundPid}).");
            var capture = tracker.CaptureFocusedAsync(); pumpUntil(() => capture.IsCompleted);
            var target = capture.Result;
            check(target?.ProcessId == process.Id, "The focused external field is captured without relying on a previous event.");
            if (target is null) throw new InvalidOperationException("Fixture target was not captured.");
            var valid = tracker.IsCurrentAsync(target); pumpUntil(() => valid.IsCompleted);
            check(valid.Result, "The actual external writable field is captured and revalidated.");
            CheckPasteIntoFixture(process, fixtureWindow, root, context, check, pumpUntil, Read, Command);
            PasteTarget? restored = null;
            for (var attempt = 0; attempt < 5 && restored is null; attempt++)
            {
                SetForegroundForTest(fixtureWindow); Command("editable");
                var refreshed = tracker.CaptureFocusedAsync(); pumpUntil(() => refreshed.IsCompleted); restored = refreshed.Result;
            }
            target = restored ?? throw new InvalidOperationException("The writable fixture focus was not restored.");
            Command("locked");
            var changed = tracker.IsCurrentAsync(target); pumpUntil(() => changed.IsCompleted);
            check(!changed.Result, "Changed focus cannot receive the old paste target.");
            var readOnly = tracker.CaptureFocusedAsync(); pumpUntil(() => readOnly.IsCompleted);
            check(readOnly.Result is null, "A real read-only field is rejected.");
            Command("password"); var secure = tracker.CaptureFocusedAsync(); pumpUntil(() => secure.IsCompleted);
            check(secure.Result is null, "A real password field is rejected.");
            Command("quit");
        }
        finally
        {
            if (!process.WaitForExit(3000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            if (previous != IntPtr.Zero) SetForegroundForTest(previous);
        }
    }

    private static void CheckPasteIntoFixture(Process process, IntPtr fixtureWindow, string root, IModuleContext context, Action<bool, string> check,
        Action<Func<bool>> pumpUntil, Func<string> read, Action<string> command)
    {
        var previous = System.Windows.Clipboard.GetDataObject(); var before = GetClipboardSequenceNumber(); uint written = 0;
        ClipboardShelfWindow? shelf = null;
        try
        {
            var notices = new List<string>();
            var testContext = new NoticeContext(context, notices);
            var library = new ClipboardLibrary(Path.Combine(root, "paste-library"));
            library.SetPreferences(library.Preferences with { IndexImages = false, FetchLinkMetadata = false });
            var text = "WinForge paste fixture " + Guid.NewGuid().ToString("N"); library.AddText(text);
            using var capture = new ClipboardCapture(library, testContext);
            PasteTarget? observed = null;
            for (var attempt = 0; attempt < 5 && observed?.ProcessId != process.Id; attempt++)
            {
                SetForegroundForTest(fixtureWindow); command("locked"); command("editable");
                var focus = capture.PasteTargets.CaptureFocusedAsync(); pumpUntil(() => focus.IsCompleted);
                observed = focus.Result;
            }
            check(observed?.ProcessId == process.Id, "The shelf observes the real editable target before opening.");
            shelf = new ClipboardShelfWindow(library, capture, testContext); shelf.BeforeShow(); shelf.Show();
            var shelfWindow = new WindowInteropHelper(shelf).Handle;
            SetForegroundForTest(shelfWindow);
            PasteTarget? trackedTarget = null;
            for (var attempt = 0; attempt < 5 && trackedTarget?.Window != fixtureWindow; attempt++)
            {
                SetForegroundForTest(fixtureWindow); command("locked"); command("editable");
                var tracked = capture.PasteTargets.CaptureFocusedAsync(); pumpUntil(() => tracked.IsCompleted); trackedTarget = tracked.Result;
            }
            check(trackedTarget?.Window == fixtureWindow, "An already-open shelf can observe a newly focused external field.");
            var observedDelay = Task.Delay(300); pumpUntil(() => observedDelay.IsCompleted);
            SetForegroundForTest(shelfWindow);
            var activationDelay = Task.Delay(200); pumpUntil(() => activationDelay.IsCompleted);
            check(shelf.PreviousWindowForTest == fixtureWindow && shelf.PasteTargetForTest?.Window == fixtureWindow, $"Returning to the open shelf refreshes the previous editable target (previous={shelf.PreviousWindowForTest}, target={shelf.PasteTargetForTest?.Window}, observed={capture.PasteTargets.CaptureCurrent()?.Window}, fixture={fixtureWindow}, foreground={GetForegroundWindow()}, shelf={shelfWindow}).");
            var paste = NativeIntegrationChecks.FindButton(shelf, "Yapıştır");
            paste.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var settle = Task.Delay(1800); pumpUntil(() => settle.IsCompleted);
            var clipboard = System.Windows.Clipboard.GetDataObject();
            if (clipboard is not null && clipboard.GetDataPresent(ClipboardCapture.OwnCopyFormat) && clipboard.GetData(System.Windows.DataFormats.UnicodeText) is string copied && copied == text)
                written = GetClipboardSequenceNumber();
            process.StandardInput.WriteLine("read"); process.StandardInput.Flush();
            var actual = Encoding.UTF8.GetString(Convert.FromBase64String(read()));
            GetWindowThreadProcessId(GetForegroundWindow(), out var currentPid);
            check(actual == text, $"The clipboard shelf pastes into the external editor (visible={shelf.IsVisible}, clipboardChanged={written != before}, foreground={currentPid}, notices={string.Join("; ", notices)}).");
            shelf.Close(); shelf = null;
        }
        finally
        {
            shelf?.Close();
            if (previous is not null && written != 0 && GetClipboardSequenceNumber() == written)
            {
                try { ClipboardAccess.SetData(previous); }
                catch (ExternalException error) { Console.Error.WriteLine("Clipboard could not be restored: " + error.Message); }
            }
            else if (written == 0 && GetClipboardSequenceNumber() != before) Console.Error.WriteLine("Clipboard changed by another application; it was not restored.");
        }
    }

    private sealed class NoticeContext(IModuleContext inner, List<string> notices) : IModuleContext
    {
        public IModuleSettings Settings => inner.Settings;
        public ILogger Log => inner.Log;
        public IHotkeyService Hotkeys => inner.Hotkeys;
        public void Notify(string message) => notices.Add(message);
    }

    internal static void RunFixture()
    {
        Window? fixture = null; TextBox? editable = null; TextBox? locked = null; PasswordBox? password = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            editable = new TextBox(); locked = new TextBox { IsReadOnly = true }; password = new PasswordBox();
            var fields = new StackPanel(); fields.Children.Add(editable); fields.Children.Add(locked); fields.Children.Add(password);
            fixture = new Window { Content = fields, Width = 350, Height = 180, Title = "WinForge test target", Topmost = true };
            fixture.Loaded += (_, _) => ready.Set(); fixture.Show(); app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait(); Console.WriteLine("READY");
        while (Console.ReadLine() is { } command)
        {
            var response = "OK";
            fixture!.Dispatcher.Invoke(() =>
            {
                fixture.Activate();
                if (command == "editable") editable!.Focus();
                else if (command == "locked") locked!.Focus();
                else if (command == "password") password!.Focus();
                else if (command == "read") response = Convert.ToBase64String(Encoding.UTF8.GetBytes(editable!.Text));
                else if (command == "quit") Application.Current.Shutdown();
            });
            Console.WriteLine(response);
            if (command == "quit") break;
        }
        thread.Join();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    internal static void SetForegroundForTest(IntPtr window)
    {
        var current = GetCurrentThreadId(); var foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var target = GetWindowThreadProcessId(window, out _);
        var foregroundAttached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        var targetAttached = target != 0 && target != current && target != foreground && AttachThreadInput(current, target, true);
        try { BringWindowToTop(window); SetForegroundWindow(window); SetActiveWindow(window); SetFocus(window); }
        finally
        {
            if (targetAttached) AttachThreadInput(current, target, false);
            if (foregroundAttached) AttachThreadInput(current, foreground, false);
        }
    }
}
