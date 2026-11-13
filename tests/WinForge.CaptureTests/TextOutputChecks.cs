using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using WinForge.Core;

internal static class TextOutputChecks
{
    internal static async Task RunGuardsAsync(Action<bool, string> check)
    {
        var target = new TextTarget(1, 1, "test");
        check(await FocusedTextOutput.InsertAsync(target, "bir\nikİ", default) == TextInsertion.UnsupportedLineBreak,
            "Multiline Unicode output does not send newline key events");
        check(await FocusedTextOutput.PasteAsync(null, 1, default) == TextInsertion.Blocked,
            "Paste requires a captured target");
        check(await FocusedTextOutput.PasteAsync(target, 0, default) == TextInsertion.Blocked,
            "Paste requires a valid clipboard sequence");
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
        var window = new Window { Title = "WinForge metin yazma testi", Width = 420, Height = 260, Content = panel, Topmost = true };
        window.Loaded += (_, _) =>
        {
            first.Focus(); Console.WriteLine("READY " + new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = Task.Run(() =>
            {
                while (Console.ReadLine() is { } command)
                {
                    if (command == "quit") { window.Dispatcher.Invoke(window.Close); return; }
                    window.Dispatcher.Invoke(() =>
                    {
                        if (command == "read") { Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(first.Text))); return; }
                        if (command == "clear") { first.Clear(); Console.WriteLine("OK"); return; }
                        if (command is "clipboard" or "clipboard-other")
                        {
                            Clipboard.SetText(command == "clipboard" ? "İlk satır\r\nİkinci satır" : "başka metin");
                            testClipboardSequence = FocusedTextOutput.GetClipboardSequenceNumber();
                            Console.WriteLine("OK"); return;
                        }
                        window.Activate();
                        _ = command switch { "first" => first.Focus(), "second" => second.Focus(), "readonly" => readOnly.Focus(), "password" => password.Focus(), _ => false };
                        Console.WriteLine("OK");
                    });
                }
            });
        };
        window.Closed += (_, _) =>
        {
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
        async Task Command(string command) { Focus(handle); await process.StandardInput.WriteLineAsync(command); await process.StandardInput.FlushAsync(); if (await Read() != "OK") throw new InvalidOperationException("Test komutu kabul edilmedi."); await Task.Delay(100); }
        try
        {
            var ready = await Read(); handle = (nint)long.Parse(ready.Split(' ')[1]); await Command("first");
            var target = FocusedTextOutput.Capture(); check(target is not null, "External editable field identity captured");
            await Command("second");
            check(await FocusedTextOutput.InsertAsync(target, "yanlış alan", default) == TextInsertion.TargetChanged, "Changing the focused field prevents dictation insertion");
            await Command("first"); target = FocusedTextOutput.Capture();
            check(await FocusedTextOutput.InsertAsync(target, "İstanbul ölçüm 42", default) == TextInsertion.Inserted, "Unicode dictation inserted into owned external field");
            await Task.Delay(150); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            var received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            Console.WriteLine("Owned fixture received: " + System.Text.Json.JsonSerializer.Serialize(received));
            check(received == "İstanbul ölçüm 42", "The target receives exact Turkish text");
            await Command("clear"); await Command("first"); target = FocusedTextOutput.Capture();
            var longText = new string('a', 31) + "😀 İstanbul İkinci satır " + new string('b', 100);
            check(await FocusedTextOutput.InsertAsync(target, longText, default) == TextInsertion.Inserted, "Long Unicode dictation reports insertion");
            await Task.Delay(1500); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(received == longText, "Long dictation preserves surrogate pairs across input batches");
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
            check(await FocusedTextOutput.PasteAsync(target, sequence, default) == TextInsertion.Blocked, "Paste refuses a changed clipboard");
            await Command("clipboard"); sequence = FocusedTextOutput.GetClipboardSequenceNumber();
            check(await FocusedTextOutput.PasteAsync(target, sequence, default) == TextInsertion.Inserted, "Multiline text can be pasted into the original target");
            await Task.Delay(500); await process.StandardInput.WriteLineAsync("read"); await process.StandardInput.FlushAsync();
            received = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(await Read()));
            check(received == "İlk satır\r\nİkinci satır", "Paste preserves Turkish text and line breaks");
            await Command("readonly"); check(FocusedTextOutput.Capture() is null, "Read-only fields reject dictation");
            await Command("password"); check(FocusedTextOutput.Capture() is null, "Password fields reject dictation");
        }
        finally
        {
            try { if (!process.HasExited) { await process.StandardInput.WriteLineAsync("quit"); await process.StandardInput.FlushAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } }
            finally { if (!process.HasExited) process.Kill(true); if (previous != 0) Focus(previous); }
        }
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
}
