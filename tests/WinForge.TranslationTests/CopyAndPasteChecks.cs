using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WinForge.Core;
using WinForge.Modules.Extras;

internal static partial class Program
{
    private static int CopyAndPasteCheck()
    {
        const string source = "Merhaba dünya";
        const string translated = "Hello world";
        const string sentinel = "WinForge translation test clipboard";
        var app = new Application();
        var editor = new TextBox { Text = source };
        var other = new TextBox { Text = "Different field" };
        var panel = new StackPanel { Margin = new Thickness(16), Children = { editor, other } };
        var window = new Window { Title = "WinForge translation shortcut test", Width = 400, Height = 150, Content = panel, Topmost = true };
        using var hotkeys = new HotkeyService();
        var context = new Context { NativeHotkeys = hotkeys };
        context.Values["replace-shortcut"] = "Ctrl+Alt+Shift+F22";
        var handler = new ShortcutTranslationHandler();
        using var client = new HttpClient(handler);
        var module = new SelectionTranslationModule(client);
        module.Initialize(context);
        var exit = 1;
        window.Loaded += async (_, _) =>
        {
            DataObject? clipboardBefore = null;
            try
            {
                clipboardBefore = SelectionTranslationModule.SnapshotClipboard();
                var handle = new WindowInteropHelper(window).Handle;
                await WaitUntil(() =>
                {
                    FocusTestWindow(handle);
                    window.Activate();
                    editor.Focus();
                    return GetForegroundWindow() == handle && editor.IsKeyboardFocused;
                });
                module.Activate();

                Clipboard.SetText(sentinel);
                editor.Text = source + "!";
                editor.Select(0, source.Length);
                SendHotkey(0x85); // F22
                await WaitUntil(() => editor.Text == translated + "!");
                if (Clipboard.GetText() != translated || handler.LastQuery is not { } query ||
                    !query.Contains("sl=tr&tl=en&", StringComparison.Ordinal) || !query.EndsWith("q=" + Uri.EscapeDataString(source), StringComparison.Ordinal))
                    throw new Exception("The default Turkish-to-English shortcut did not copy the translated selection.");
                Console.WriteLine("PASS: Windows hotkey translates Turkish to English, pastes over the selection, and keeps the translation on the clipboard.");

                Clipboard.SetText(sentinel);
                editor.IsReadOnly = true;
                editor.Text = source;
                editor.SelectAll();
                SendHotkey(0x85);
                await WaitUntil(() => Clipboard.GetText() == translated);
                if (editor.Text != source) throw new Exception("Read-only selection was modified.");
                Console.WriteLine("PASS: Translation of a read-only selection is copied.");

                Clipboard.SetText(sentinel);
                editor.IsReadOnly = false;
                editor.SelectAll();
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                SendHotkey(0x85);
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Keyboard.Focus(other);
                other.SelectAll();
                handler.Release.SetResult();
                await WaitUntil(() => Clipboard.GetText() == translated);
                await Task.Delay(100);
                if (editor.Text != source || other.Text != "Different field") throw new Exception("Paste went to a different field.");
                Console.WriteLine("PASS: Translation is copied when focus changes before the response.");
                exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine($"{ex}\n{context.LastError}"); }
            finally
            {
                module.Deactivate();
                try
                {
                    await WaitUntil(() =>
                    {
                        try
                        {
                            if (Clipboard.GetText() is translated or sentinel)
                            {
                                if (clipboardBefore is null) Clipboard.Clear();
                                else Clipboard.SetDataObject(clipboardBefore, true);
                            }
                            return true;
                        }
                        catch (System.Runtime.InteropServices.ExternalException) { return false; }
                    });
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
                finally { window.Close(); }
            }
        };
        app.Run(window);
        return exit;

        static async Task WaitUntil(Func<bool> condition)
        {
            for (var i = 0; i < 100; i++)
            {
                if (condition()) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("The expected translation shortcut result did not appear.");
        }
    }

    private static void FocusTestWindow(nint window)
    {
        var foreground = GetForegroundWindow();
        if (foreground == window) return;
        var currentThread = GetCurrentThreadId();
        var foregroundThread = Native.GetWindowThreadProcessId(foreground, out _);
        var targetThread = Native.GetWindowThreadProcessId(window, out _);
        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread && AttachThreadInput(currentThread, targetThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);

    private sealed class ShortcutTranslationHandler : HttpMessageHandler
    {
        public string? LastQuery;
        public TaskCompletionSource? Received;
        public TaskCompletionSource? Release;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastQuery = request.RequestUri?.Query;
            Received?.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[[[\"Hello world\"]],null,\"tr\"]") };
        }
    }
}
