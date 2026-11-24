using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using WinForge.Core;
using WinForge.Modules.Capture;

internal static class TextOutputChecks
{
    private static readonly string LongText = new string('a', 31) + "😀 İstanbul İkinci satır " + new string('b', 100);

    internal static async Task RunGuardsAsync(Action<bool, string> check)
    {
        var target = new TextTarget(1, 1, "test");
        check(await FocusedTextOutput.InsertAsync(target, "bir\nikİ", default) == TextInsertion.UnsupportedLineBreak,
            "Multiline Unicode output does not send newline key events");
        check(await FocusedTextOutput.PasteAsync(null, 1, default) == TextInsertion.Blocked,
            "Paste requires a captured target");
        check(await FocusedTextOutput.PasteAsync(target, 0, default) == TextInsertion.Blocked,
            "Paste requires a valid clipboard sequence");
        check(await FocusedTextOutput.PasteAsync(null, () => throw new InvalidOperationException("Clipboard touched"), default) == TextInsertion.Blocked,
            "Paste without a target leaves the clipboard untouched");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await FocusedTextOutput.PasteAsync(target, 1, cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "Cancelled paste does not send keys"); }
    }
    internal static int RunFixture()
    {
        var app = new Application(); var first = new TextBox { AcceptsReturn = true }; var second = new TextBox();
        var originalClipboard = Clipboard.GetDataObject(); uint testClipboardSequence = 0;
        var readOnly = new TextBox { IsReadOnly = true, Text = "salt okunur" }; var password = new PasswordBox();
        var panel = new StackPanel(); foreach (var control in new Control[] { first, second, readOnly, password }) { control.Height = 40; panel.Children.Add(control); }
        var nativeEdit = new NativeEdit { Height = 30 };
        panel.Children.Add(nativeEdit);
        var window = new Window { Title = "WinForge metin yazma testi", Width = 420, Height = 280, Content = panel, Topmost = true };
        WebView2? web = null;
        async Task RunWebCommandAsync(string command)
        {
            if (web is null)
            {
                web = new WebView2 { Height = 130 };
                panel.Children.Add(web);
                window.Height += web.Height;
                await web.EnsureCoreWebView2Async();
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                web.NavigationCompleted += (_, args) =>
                {
                    if (args.IsSuccess) loaded.TrySetResult();
                    else loaded.TrySetException(new InvalidOperationException(args.WebErrorStatus.ToString()));
                };
                web.NavigateToString("""
                    <div id="editor" contenteditable="true" role="textbox" aria-label="Editor">Başlangıç</div>
                    <input id="second" aria-label="Other field">
                    <input id="readonly" aria-label="Read only" readonly value="Protected">
                    """);
                await loaded.Task;
            }
            if (command == "web-read")
            {
                var result = await web.ExecuteScriptAsync("document.getElementById('editor').innerText");
                Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(result)));
                return;
            }
            window.Activate();
            web.Focus();
            var field = command switch { "web-editor" => "editor", "web-second" => "second", _ => "readonly" };
            await web.ExecuteScriptAsync($"document.getElementById('{field}').focus();");
            Console.WriteLine("OK");
        }
        window.Loaded += (_, _) =>
        {
            first.Focus(); Console.WriteLine("READY " + new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = Task.Run(() =>
            {
                while (Console.ReadLine() is { } command)
                {
                    if (command == "quit") { window.Dispatcher.Invoke(window.Close); return; }
                    if (command.StartsWith("web-", StringComparison.Ordinal))
                    {
                        try { window.Dispatcher.InvokeAsync(() => RunWebCommandAsync(command)).Task.Unwrap().GetAwaiter().GetResult(); }
                        catch (Exception error) { Console.WriteLine(error); }
                        continue;
                    }
                    window.Dispatcher.Invoke(() =>
                    {
                        if (command == "read") { Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(first.Text))); return; }
                        if (command == "native-read") { Console.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(nativeEdit.ReadText()))); return; }
                        if (command == "clear") { first.Clear(); Console.WriteLine("OK"); return; }
                        if (command is "clipboard" or "clipboard-other" or "clipboard-long")
                        {
                            Clipboard.SetText(command switch
                            {
                                "clipboard" => "İlk satır\r\nİkinci satır",
                                "clipboard-long" => LongText,
                                _ => "başka metin"
                            });
                            testClipboardSequence = FocusedTextOutput.GetClipboardSequenceNumber();
                            Console.WriteLine("OK"); return;
                        }
                        window.Activate();
                        if (command is "native" or "native-readonly") nativeEdit.FocusField(command == "native-readonly");
                        else _ = command switch { "first" => first.Focus(), "second" => second.Focus(), "readonly" => readOnly.Focus(), "password" => password.Focus(), _ => false };
                        Console.WriteLine("OK");
                    });
                }
            });
        };
        window.Closed += (_, _) =>
        {
            web?.Dispose();
            if (testClipboardSequence != 0 && FocusedTextOutput.GetClipboardSequenceNumber() == testClipboardSequence)
            {
                if (originalClipboard is null) Clipboard.Clear();
                else Clipboard.SetDataObject(originalClipboard, true);
            }
        };
        return app.Run(window);
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var previous = Native.GetForegroundWindow();
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(TextOutputChecks).Assembly.Location);
        start.ArgumentList.Add("--text-fixture");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Metin testi başlatılamadı.");
        nint handle = 0;
        async Task<string> Read() => await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? throw new IOException("Test penceresi kapandı.");
        async Task Command(string command)
        {
            if (Native.GetForegroundWindow() != handle) Focus(handle);
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            var result = await Read();
            if (result != "OK") throw new InvalidOperationException("Test komutu kabul edilmedi: " + result);
            await Task.Delay(100);
        }
        async Task<TextTarget?> CaptureField(string command, TextTarget? previousTarget = null)
        {
            await Command(command);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var captured = FocusedTextOutput.Capture();
                if (captured is not null && captured != previousTarget) return captured;
                await Task.Delay(100);
            }
            return null;
        }
        try
        {
            var ready = await Read(); handle = (nint)long.Parse(ready.Split(' ')[1]); await Command("first");
            var target = FocusedTextOutput.Capture();
            for (var attempt = 0; attempt < 10 && target is null; attempt++)
            {
                await Command("first");
                target = FocusedTextOutput.Capture();
            }
            check(target is not null, $"External editable field identity captured (fixture foreground: {Native.GetForegroundWindow() == handle})");
            await Command("second");
            check(await FocusedTextOutput.InsertAsync(target, "yanlış alan", default) == TextInsertion.TargetChanged, "Changing the focused field prevents dictation insertion");
            await Command("first"); target = FocusedTextOutput.Capture();
            var unicodeInsertion = await FocusedTextOutput.InsertAsync(target, "İstanbul ölçüm 42", default);
            check(unicodeInsertion == TextInsertion.Inserted, $"Unicode dictation inserted into owned external field ({unicodeInsertion})");
            await Task.Delay(150); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            var received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            Console.WriteLine("Owned fixture received: " + System.Text.Json.JsonSerializer.Serialize(received));
            check(received == "İstanbul ölçüm 42", "The target receives exact Turkish text");
            await Command("clear"); await Command("first"); target = FocusedTextOutput.Capture();
            await Command("clipboard-long");
            check(await FocusedTextOutput.PasteAsync(target, FocusedTextOutput.GetClipboardSequenceNumber(), default) == TextInsertion.Inserted,
                "Long Unicode dictation uses a single paste");
            await Task.Delay(500); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(received == LongText, "Long dictation pastes the complete text and preserves surrogate pairs");
            await Command("clear"); await Command("first"); target = FocusedTextOutput.Capture();
            check(await FocusedTextOutput.InsertAsync(target, "İlk satır\r\nİkinci satır", default) == TextInsertion.UnsupportedLineBreak,
                "Multiline text is not silently flattened or submitted with Enter");
            await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            var beforePaste = await Read();
            check(beforePaste == "", "Unsupported multiline output leaves the target untouched: " + beforePaste);
            await Command("clipboard"); var sequence = FocusedTextOutput.GetClipboardSequenceNumber();
            await Command("second");
            check(await FocusedTextOutput.PasteAsync(target, sequence, default) == TextInsertion.TargetChanged, "Paste refuses a changed target");
            await Command("first"); await Command("clipboard-other");
            var changedClipboard = await FocusedTextOutput.PasteAsync(target, sequence, default);
            check(changedClipboard == TextInsertion.Blocked, $"Paste refuses a changed clipboard ({changedClipboard})");
            await Command("clipboard"); sequence = FocusedTextOutput.GetClipboardSequenceNumber();
            check(await FocusedTextOutput.PasteAsync(target, sequence, default) == TextInsertion.Inserted, "Multiline text can be pasted into the original target");
            await Task.Delay(500); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(received == "İlk satır\r\nİkinci satır", "Paste preserves Turkish text and line breaks");
            await Command("clipboard-other"); sequence = FocusedTextOutput.GetClipboardSequenceNumber();
            keybd_event(0x10, 0, 0, 0);
            try
            {
                await Task.Delay(30);
                check(!FocusedTextOutput.CanPasteImmediately() && FocusedTextOutput.CanPasteImmediately(allowHeldShift: true),
                    "Live dictation recognizes the held Shift shortcut");
                check(await FocusedTextOutput.PasteAsync(target, () => sequence, default, useShiftInsert: true, waitForModifiers: false) == TextInsertion.Inserted,
                    "Live dictation pastes while the original Shift key is held");
                check(!FocusedTextOutput.CanPasteImmediately(), "Live paste leaves the original Shift key held");
            }
            finally { keybd_event(0x10, 0, 2, 0); }
            await Task.Delay(250); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(received == "İlk satır\r\nİkinci satırbaşka metin", "A second paste appends only the new text");
            await Command("readonly"); check(FocusedTextOutput.Capture() is null, "Read-only fields reject dictation");
            await Command("password"); check(FocusedTextOutput.Capture() is null, "Password fields reject dictation");
            var nativeTarget = await CaptureField("native");
            check(nativeTarget is not null && nativeTarget.FocusWindow != handle, "The native edit control is captured by its own window handle");
            await Command("clipboard-other");
            check(await FocusedTextOutput.PasteAsync(nativeTarget, FocusedTextOutput.GetClipboardSequenceNumber(), default) == TextInsertion.Inserted,
                "Dictation can paste into a native edit control");
            await Task.Delay(250); await process.StandardInput.WriteLineAsync("native-read"); await process.StandardInput.FlushAsync();
            check(Encoding.UTF8.GetString(Convert.FromBase64String(await Read())) == "başka metin", "The native edit control receives the text");
            await Command("native-readonly"); check(FocusedTextOutput.Capture() is null, "A read-only native edit control rejects dictation");

            var webTarget = await CaptureField("web-editor") ?? throw new InvalidOperationException("Embedded browser contenteditable field was not captured");
            check(webTarget.Window == handle, "Embedded browser contenteditable field is captured in the hosting window");
            var secondWebTarget = await CaptureField("web-second", webTarget);
            check(secondWebTarget is not null && secondWebTarget.FocusWindow == webTarget.FocusWindow,
                "Browser fields have distinct identities even when they share a native window");
            check(await FocusedTextOutput.PasteAsync(webTarget, () => throw new InvalidOperationException("Clipboard touched"), default) == TextInsertion.TargetChanged,
                "Switching browser fields blocks paste even within the same native window");
            check(await CaptureField("web-editor", secondWebTarget) == webTarget, "Refocusing the browser editor restores its identity");
            var recovered = await DictationModule.RecoverTargetAsync(handle, webTarget.FocusWindow, default);
            check(recovered == webTarget, "Stop-time recovery resolves the focused browser editor");
            await Command("clipboard-other"); sequence = FocusedTextOutput.GetClipboardSequenceNumber();
            check(await FocusedTextOutput.PasteAsync(recovered, () => sequence, default) == TextInsertion.Inserted,
                "Dictation pastes into the focused browser editor");
            await Task.Delay(500); await process.StandardInput.WriteLineAsync("web-read"); await process.StandardInput.FlushAsync();
            var browserText = Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(System.Text.Json.JsonSerializer.Deserialize<string>(browserText)!.Contains("başka metin", StringComparison.Ordinal),
                "Browser editor receives Turkish dictation text: " + browserText);
            await Command("web-readonly"); check(FocusedTextOutput.Capture() is null, "Read-only browser fields reject dictation");
        }
        finally
        {
            try { if (!process.HasExited) { await process.StandardInput.WriteLineAsync("quit"); await process.StandardInput.FlushAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } }
            finally { if (!process.HasExited) process.Kill(true); if (previous != 0) Focus(previous); }
        }
    }

    private sealed class NativeEdit : HwndHost
    {
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            const int childVisibleBorder = 0x50800000;
            var handle = CreateWindowEx(0, "Edit", null, childVisibleBorder, 0, 0, 360, 30, parent.Handle, 0, 0, 0);
            if (handle == 0) throw new System.ComponentModel.Win32Exception();
            return new HandleRef(this, handle);
        }

        protected override void DestroyWindowCore(HandleRef window) => DestroyWindow(window.Handle);

        internal void FocusField(bool readOnly)
        {
            const int setReadOnly = 0xcf;
            SendMessage(Handle, setReadOnly, readOnly ? 1 : 0, 0);
            SetFocus(Handle);
        }

        internal string ReadText()
        {
            var text = new StringBuilder(256);
            Native.GetWindowText(Handle, text, text.Capacity);
            return text.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(int exStyle, string className, string? name, int style,
            int x, int y, int width, int height, nint parent, nint menu, nint module, nint parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
        [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
        [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    }

    internal static void Focus(nint window)
    {
        var current = GetCurrentThreadId(); var foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _); var target = Native.GetWindowThreadProcessId(window, out _);
        var attachedForeground = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        var attachedTarget = target != 0 && target != current && target != foreground && AttachThreadInput(current, target, true);
        try { BringWindowToTop(window); Native.SetForegroundWindow(window); SetActiveWindow(window); }
        finally { if (attachedTarget) AttachThreadInput(current, target, false); if (attachedForeground) AttachThreadInput(current, foreground, false); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SetActiveWindow(nint window);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
