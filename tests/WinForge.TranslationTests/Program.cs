using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;
using WinForge.Core;
using WinForge.Modules.Extras;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selection-guard")) return SelectionGuardCheck();
        if (args.Contains("--shortcut-languages")) return ShortcutLanguagesCheck();
        if (args.Contains("--copy-and-paste")) return CopyAndPasteCheck();
        if (args.Length == 2 && args[0] == "--host") return HostIntegrationCheck(args[1]);
        if (args.Length == 2 && args[0] == "--chrome") return ChromeIntegrationCheck(args[1]);
        if (args.Length == 2 && (args[0] == "--vscode" || args[0] == "--vscode-screenreader")) return VsCodeIntegrationCheck(args[1], args[0] == "--vscode-screenreader");
        if (args.Contains("--live-service-mymemory")) return LiveMyMemoryServiceCheck();
        if (args.Contains("--live-native") || args.Contains("--live-mymemory")) return LiveNativeCheck(args.Contains("--live-mymemory"));
        if (args.Contains("--fixture-scintilla"))
        {
            var path = Environment.GetEnvironmentVariable("WINFORGE_TEST_SCINTILLA_DLL") ?? throw new Exception("Scintilla fixture DLL path is missing.");
            var fixtureApp = new Application();
            var fixtureEdit = new ScintillaFixture(path);
            var fixtureWindow = new Window { Title = "WinForge cross-process Scintilla fixture", Width = 400, Height = 150, Content = fixtureEdit, Topmost = true };
            fixtureWindow.Loaded += (_, _) => { fixtureWindow.Activate(); fixtureEdit.FocusAndSelect(0, 5); };
            fixtureApp.Run(fixtureWindow);
            return 0;
        }
        if (args.Contains("--fixture-nativerich"))
        {
            var fixtureApp = new Application();
            var fixtureEdit = new NativeEdit(rich: true);
            var fixtureWindow = new Window { Title = "WinForge cross-process RichEdit fixture", Width = 400, Height = 150, Content = fixtureEdit, Topmost = true };
            fixtureWindow.Loaded += (_, _) => { fixtureWindow.Activate(); fixtureEdit.FocusInput(); };
            fixtureApp.Run(fixtureWindow);
            return 0;
        }
        if (args.Contains("--fixture-rich"))
        {
            var fixtureApp = new Application();
            var richEditor = new RichTextBox();
            var run = new System.Windows.Documents.Run("hello world");
            richEditor.Document.Blocks.Clear();
            richEditor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(run));
            var fixtureWindow = new Window { Title = "WinForge cross-process rich text fixture", Width = 400, Height = 150, Content = richEditor, Topmost = true };
            fixtureWindow.Loaded += (_, _) => { fixtureWindow.Activate(); richEditor.Focus(); Keyboard.Focus(richEditor); richEditor.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(5)!); };
            fixtureApp.Run(fixtureWindow);
            return 0;
        }
        if (args.Contains("--fixture"))
        {
            var fixtureApp = new Application();
            var fixtureEdit = new NativeEdit();
            var fixtureWindow = new Window { Title = "WinForge cross-process translation fixture", Width = 400, Height = 150, Content = fixtureEdit, Topmost = true };
            fixtureWindow.Loaded += (_, _) => { fixtureWindow.Activate(); fixtureEdit.FocusInput(); };
            fixtureApp.Run(fixtureWindow);
            return 0;
        }
        if (!args.Contains("--native")) { Console.WriteLine("Native UI test skipped; pass --native on an interactive desktop."); return 0; }
        var app = new Application();
        var editor = new TextBox { Text = "hello world", FontSize = 20, Margin = new Thickness(20) };
        var window = new Window { Title = "WinForge translation test", Width = 400, Height = 150, Content = editor, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var context = new Context();
        var handler = new FakeHandler();
        var module = new SelectionTranslationModule(new HttpClient(handler));
        module.Initialize(context);
        module.Activate();
        var snapshotMethod = typeof(SelectionTranslationModule).GetMethod("SnapshotClipboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var restoreMethod = typeof(SelectionTranslationModule).GetMethod("RestoreClipboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var originalClipboard = snapshotMethod.Invoke(null, null);
        var testClipboard = new DataObject();
        testClipboard.SetData(DataFormats.UnicodeText, "clipboard sentinel");
        testClipboard.SetData(DataFormats.Html, "<b>clipboard sentinel</b>");
        Clipboard.SetDataObject(testClipboard, true);
        var exit = 1;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var initialWindow = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                for (var i = 0; i < 30 && (GetForegroundWindow() != initialWindow || !editor.IsKeyboardFocused); i++)
                {
                    SetForegroundWindow(initialWindow); window.Activate(); editor.Focus(); Keyboard.Focus(editor);
                    await Task.Delay(100);
                }
                if (GetForegroundWindow() != initialWindow || !editor.IsKeyboardFocused) throw new Exception("Test window did not acquire foreground focus.");
                editor.Select(0, 5);
                context.Callbacks[1]();
                for (var i = 0; i < 100 && editor.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (editor.Text != "merhaba world") throw new Exception($"Replacement failed. Text: {editor.Text}; message: {context.LastError}");
                Console.WriteLine("PASS: Native WPF selection, translation, guarded replacement and paste");
                await Task.Delay(400);
                if (Clipboard.GetText() != "clipboard sentinel" || !Clipboard.GetText(TextDataFormat.Html).Contains("clipboard sentinel"))
                    throw new Exception("Clipboard text or HTML was not restored.");
                Console.WriteLine("PASS: Text and HTML clipboard formats restored");
                editor.Text = "hello world"; editor.Select(0, 5);
                var copyMethod = typeof(SelectionTranslationModule).GetMethod("CopySelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var originChecks = 0;
                Func<bool> changingCopyOrigin = () => { if (++originChecks == 2) Clipboard.SetText("new clipboard data"); return true; };
                try
                {
                    await (Task<string?>)copyMethod.Invoke(null, [CancellationToken.None, changingCopyOrigin])!;
                    throw new Exception("Clipboard change during selection read was accepted.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("Pano değişti")) { }
                if (Clipboard.GetText() != "new clipboard data" || editor.Text != "hello world") throw new Exception("Concurrent clipboard content was overwritten during selection read.");
                Console.WriteLine("PASS: Clipboard changes during selection read are preserved and not translated");
                Clipboard.SetDataObject(testClipboard, true);
                editor.Select(0, 5);
                var pasteMethod = typeof(SelectionTranslationModule).GetMethod("Paste", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                Func<bool> changingPasteTarget = () => { Clipboard.SetText("new clipboard data"); return true; };
                try
                {
                    await (Task)pasteMethod.Invoke(null, ["merhaba", changingPasteTarget, CancellationToken.None])!;
                    throw new Exception("Clipboard change immediately before paste was accepted.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("Pano değişti")) { }
                if (Clipboard.GetText() != "new clipboard data" || editor.Text != "hello world") throw new Exception("Concurrent clipboard content was pasted or overwritten.");
                Console.WriteLine("PASS: Clipboard changes immediately before paste are preserved and not inserted");
                Clipboard.SetDataObject(testClipboard, true);
                editor.Text = "hello world"; editor.Select(0, 5);
                handler.Status = HttpStatusCode.TooManyRequests;
                context.LastError = null; context.Callbacks[1]();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || !context.LastError.Contains("istek sınırına") || editor.Text != "hello world" || Clipboard.GetText() != "clipboard sentinel")
                    throw new Exception("Rate-limited translation did not fail safely: " + context.LastError);
                handler.Status = HttpStatusCode.OK;
                Console.WriteLine("PASS: Rate-limited translation reports the problem and preserves text and clipboard");
                editor.Select(0, 5); handler.ForceTimeout = true;
                context.LastError = null; context.Callbacks[1]();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || !context.LastError.Contains("zaman aşımına") || editor.Text != "hello world" || Clipboard.GetText() != "clipboard sentinel")
                    throw new Exception("Timed-out translation did not fail safely: " + context.LastError);
                handler.ForceTimeout = false;
                Console.WriteLine("PASS: Translation timeout is reported and preserves text and clipboard");
                context.Values["provider"] = "mymemory";
                context.Values["source"] = "auto";
                module.Apply();
                editor.Select(0, 5); context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 50 && editor.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (editor.Text != "merhaba world") throw new Exception("MyMemory automatic source was not applied to selected text: " + context.LastError);
                Console.WriteLine("PASS: MyMemory automatic source and replacement through the module");
                await Task.Delay(400);
                context.Values.Remove("provider"); context.Values.Remove("source"); module.Apply();
                editor.Text = "hello world"; editor.Select(0, 5);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null; context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if ((GetAsyncKeyState(0x10) & 0x8000) != 0) throw new Exception("Shift was already held before the test.");
                try
                {
                    SendTestKey(0x10, false);
                    await Task.Delay(100);
                    if ((GetAsyncKeyState(0x10) & 0x8000) == 0) throw new Exception("Test Shift key was not pressed.");
                    var beforeHeldModifier = GetClipboardSequenceNumber();
                    handler.Gate.SetResult();
                    await Task.Delay(400);
                    if (editor.Text != "hello world" || GetClipboardSequenceNumber() != beforeHeldModifier || context.LastError is not null)
                        throw new Exception("Replacement started while Shift was held.");
                }
                finally { SendTestKey(0x10, true); }
                for (var i = 0; i < 50 && editor.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (editor.Text != "merhaba world") throw new Exception("Replacement did not resume after modifier release: " + context.LastError);
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: Replacement waits for a held modifier and resumes safely");
                await Task.Delay(400);
                editor.Text = "hello world"; editor.Select(0, 5);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null; context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if ((GetAsyncKeyState(0x10) & 0x8000) != 0) throw new Exception("Shift was already held before the test.");
                try
                {
                    SendTestKey(0x10, false);
                    await Task.Delay(100);
                    handler.Gate.SetResult();
                    await Task.Delay(200);
                    editor.Select(6, 5);
                }
                finally { SendTestKey(0x10, true); }
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (editor.Text != "hello world" || context.LastError is null) throw new Exception("Selection change during modifier wait was not rejected.");
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: Selection change during modifier wait is not overwritten");
                editor.Text = new string('a', 100_000);
                editor.SelectAll();
                context.LastError = null;
                var largeRequestCount = handler.RequestCount;
                var beforeLargeSelection = GetClipboardSequenceNumber();
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || !context.LastError.Contains("30.000") || handler.RequestCount != largeRequestCount || GetClipboardSequenceNumber() != beforeLargeSelection || editor.Text.Length != 100_000)
                    throw new Exception("Oversized editable selection was not rejected safely: " + context.LastError);
                Console.WriteLine("PASS: Oversized editable selections do not reach network or clipboard");
                var rich = new RichTextBox();
                var richRun = new System.Windows.Documents.Run("hello world");
                rich.Document.Blocks.Clear();
                rich.Document.Blocks.Add(new System.Windows.Documents.Paragraph(richRun));
                window.Content = rich;
                await Task.Delay(250);
                window.Activate(); rich.Focus(); Keyboard.Focus(rich);
                rich.Selection.Select(richRun.ContentStart, richRun.ContentStart.GetPositionAtOffset(5)!);
                await Task.Delay(250);
                var richFocused = System.Windows.Automation.AutomationElement.FocusedElement;
                var richPatterns = richFocused?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [];
                var richTextPattern = richFocused?.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern) as System.Windows.Automation.TextPattern;
                var readOnlyAttribute = richTextPattern?.GetSelection().FirstOrDefault()?.GetAttributeValue(System.Windows.Automation.TextPattern.IsReadOnlyAttribute);
                Console.WriteLine($"WPF RichTextBox class: {richFocused?.Current.ClassName}; type: {richFocused?.Current.ControlType.ProgrammaticName}; patterns: {string.Join(", ", richPatterns)}; selection IsReadOnly: {readOnlyAttribute}");
                context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 100 && context.LastError is null; i++)
                {
                    await Task.Delay(100);
                    if (new System.Windows.Documents.TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.StartsWith("merhaba world")) break;
                }
                if (!new System.Windows.Documents.TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.StartsWith("merhaba world"))
                    throw new Exception("WPF RichTextBox replacement failed: " + context.LastError);
                Console.WriteLine("PASS: WPF RichTextBox selection and replacement");
                await Task.Delay(400);
                richRun = new System.Windows.Documents.Run("hello world");
                rich.Document.Blocks.Clear();
                rich.Document.Blocks.Add(new System.Windows.Documents.Paragraph(richRun));
                rich.Focus(); Keyboard.Focus(rich);
                rich.Selection.Select(richRun.ContentStart, richRun.ContentStart.GetPositionAtOffset(5)!);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                rich.IsReadOnly = true;
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || !new System.Windows.Documents.TextRange(rich.Document.ContentStart, rich.Document.ContentEnd).Text.StartsWith("hello world"))
                    throw new Exception("RichTextBox changed to read-only was overwritten.");
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: RichTextBox changed to read-only is not overwritten");
                rich.Focus(); Keyboard.Focus(rich);
                rich.Selection.Select(richRun.ContentStart, richRun.ContentStart.GetPositionAtOffset(5)!);
                context.LastError = null;
                var readOnlyRichRequests = handler.RequestCount;
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != readOnlyRichRequests)
                    throw new Exception("Read-only RichTextBox was not rejected before translation.");
                Console.WriteLine("PASS: Read-only RichTextBox is not sent for replacement");
                var nativeEdit = new NativeEdit();
                window.Content = nativeEdit;
                await Task.Delay(300);
                window.Activate(); nativeEdit.FocusInput();
                await Task.Delay(300);
                var nativeFocused = System.Windows.Automation.AutomationElement.FocusedElement;
                Console.WriteLine($"Win32 Edit focus: {nativeFocused?.Current.ControlType.ProgrammaticName}; patterns: {string.Join(", ", nativeFocused?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 100 && nativeEdit.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (nativeEdit.Text != "merhaba world") throw new Exception($"Win32 Edit replacement failed: {context.LastError}");
                Console.WriteLine("PASS: Classic Win32 Edit selection and replacement");
                await Task.Delay(400);
                nativeEdit.ResetAndSelect();
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                nativeEdit.MakeReadOnly();
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (nativeEdit.Text != "hello world" || context.LastError is null) throw new Exception("Win32 edit changed to read-only was overwritten.");
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: Win32 edit changed to read-only is not overwritten");
                var readOnlyEdit = new NativeEdit(0x800);
                window.Content = readOnlyEdit;
                await Task.Delay(250);
                window.Activate(); readOnlyEdit.FocusInput();
                await Task.Delay(250);
                context.LastError = null;
                var requestsBeforeReadOnly = handler.RequestCount;
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != requestsBeforeReadOnly || readOnlyEdit.Text != "hello world")
                    throw new Exception("Read-only Win32 Edit was not rejected before translation.");
                Console.WriteLine("PASS: Read-only Win32 Edit is not translated for replacement");
                var passwordEdit = new NativeEdit(0x20, multiline: false);
                window.Content = passwordEdit;
                await Task.Delay(250);
                window.Activate(); passwordEdit.FocusInput();
                await Task.Delay(250);
                context.LastError = null;
                var requestsBeforePassword = handler.RequestCount;
                context.Callbacks[0]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != requestsBeforePassword)
                    throw new Exception("Win32 password edit was not rejected before translation.");
                Console.WriteLine("PASS: Win32 password fields are not sent for translation");
                var dynamicPassword = new NativeEdit(0, multiline: false);
                window.Content = dynamicPassword;
                await Task.Delay(250);
                window.Activate(); dynamicPassword.FocusInput(); dynamicPassword.MaskInput();
                await Task.Delay(250);
                context.LastError = null;
                requestsBeforePassword = handler.RequestCount;
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != requestsBeforePassword)
                    throw new Exception("Dynamically masked Win32 edit was not rejected before translation.");
                Console.WriteLine("PASS: Dynamically masked Win32 Edit is not sent for replacement");
                var nativeRich = new NativeEdit(rich: true);
                window.Content = nativeRich;
                await Task.Delay(300);
                window.Activate(); nativeRich.FocusInput();
                await Task.Delay(250);
                var nativeRichFocus = System.Windows.Automation.AutomationElement.FocusedElement;
                Console.WriteLine($"Win32 RichEdit class: {nativeRichFocus?.Current.ClassName}; patterns: {string.Join(", ", nativeRichFocus?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 100 && nativeRich.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (nativeRich.Text != "merhaba world") throw new Exception("Win32 RichEdit replacement failed: " + context.LastError);
                Console.WriteLine("PASS: Win32 RichEdit selection and replacement");
                await Task.Delay(400);
                nativeRich.SetTextAndSelect("first\r\nhello world", 6, 11);
                context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 100 && context.LastError is null && !nativeRich.Text.Contains("merhaba world"); i++) await Task.Delay(100);
                if (!nativeRich.Text.Contains("merhaba world") || handler.LastQuery != "hello") throw new Exception("Multiline RichEdit selection was not translated correctly: " + context.LastError);
                Console.WriteLine("PASS: Win32 RichEdit selection after a newline");
                await Task.Delay(400);
                nativeRich.ResetAndSelect();
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                nativeRich.SetTextAndSelect("jello world", 0, 5);
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (nativeRich.Text != "jello world" || context.LastError is null) throw new Exception("Changed Win32 RichEdit text was overwritten.");
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: Changed Win32 RichEdit text is not overwritten");
                nativeRich.ResetAndSelect();
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                nativeRich.MakeReadOnly();
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                if (nativeRich.Text != "hello world" || context.LastError is null) throw new Exception("Win32 RichEdit changed to read-only was overwritten.");
                handler.Gate = handler.Received = null;
                Console.WriteLine("PASS: Win32 RichEdit changed to read-only is not overwritten");
                var readOnlyNativeRich = new NativeEdit(0x800, rich: true);
                window.Content = readOnlyNativeRich;
                await Task.Delay(250);
                window.Activate(); readOnlyNativeRich.FocusInput();
                await Task.Delay(250);
                context.LastError = null;
                var readOnlyNativeRichRequests = handler.RequestCount;
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != readOnlyNativeRichRequests) throw new Exception("Read-only Win32 RichEdit was not rejected.");
                Console.WriteLine("PASS: Read-only Win32 RichEdit is not sent for replacement");
                var passwordNativeRich = new NativeEdit(0x20, multiline: false, rich: true);
                window.Content = passwordNativeRich;
                await Task.Delay(250);
                window.Activate(); passwordNativeRich.FocusInput();
                await Task.Delay(250);
                context.LastError = null;
                var passwordNativeRichRequests = handler.RequestCount;
                context.Callbacks[1]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is null || handler.RequestCount != passwordNativeRichRequests) throw new Exception("Password Win32 RichEdit was not rejected.");
                Console.WriteLine("PASS: Password Win32 RichEdit is not sent for replacement");
                var scintillaPath = Environment.GetEnvironmentVariable("WINFORGE_TEST_SCINTILLA_DLL");
                if (!string.IsNullOrWhiteSpace(scintillaPath))
                {
                    var scintilla = new ScintillaFixture(scintillaPath);
                    window.Content = scintilla;
                    await Task.Delay(300);
                    window.Activate(); scintilla.FocusAndSelect(0, 5);
                    await Task.Delay(250);
                    var scintillaFocus = System.Windows.Automation.AutomationElement.FocusedElement;
                    Console.WriteLine($"Scintilla class: {scintillaFocus?.Current.ClassName}; patterns: {string.Join(", ", scintillaFocus?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                    context.LastError = null;
                    context.Callbacks[1]();
                    for (var i = 0; i < 100 && scintilla.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                    if (scintilla.Text != "merhaba world") throw new Exception("Scintilla replacement failed: " + context.LastError);
                    Console.WriteLine("PASS: Scintilla selection and replacement");
                    await Task.Delay(400);
                    scintilla.SetText("hello world"); scintilla.FocusAndSelect(0, 5);
                    handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    context.LastError = null;
                    context.Callbacks[1]();
                    await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    scintilla.SetText("jello world"); scintilla.FocusAndSelect(0, 5);
                    handler.Gate.SetResult();
                    for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || scintilla.Text != "jello world") throw new Exception("Changed Scintilla selection text was overwritten.");
                    handler.Gate = handler.Received = null;
                    Console.WriteLine("PASS: Changed Scintilla selection text is not overwritten");
                    scintilla.SetText("hello world"); scintilla.FocusAndSelect(0, 5);
                    handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    context.LastError = null;
                    context.Callbacks[1]();
                    await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    scintilla.SetReadOnly(true);
                    handler.Gate.SetResult();
                    for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || scintilla.Text != "hello world") throw new Exception("Scintilla changed to read-only was overwritten.");
                    handler.Gate = handler.Received = null;
                    Console.WriteLine("PASS: Scintilla changed to read-only is not overwritten");
                    context.LastError = null;
                    var readOnlyScintillaRequests = handler.RequestCount;
                    context.Callbacks[1]();
                    for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || handler.RequestCount != readOnlyScintillaRequests) throw new Exception("Read-only Scintilla was not rejected.");
                    Console.WriteLine("PASS: Read-only Scintilla is not sent for replacement");
                    scintilla.SetReadOnly(false); scintilla.FocusAndSelect(0, 5); scintilla.AddSelection(6, 11);
                    context.LastError = null;
                    var multiScintillaRequests = handler.RequestCount;
                    context.Callbacks[1]();
                    for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || handler.RequestCount != multiScintillaRequests || scintilla.Text != "hello world") throw new Exception("Multiple Scintilla selections were not rejected.");
                    Console.WriteLine("PASS: Multiple Scintilla selections are not overwritten");
                    scintilla.SetSelectionMode(0); scintilla.FocusAndSelect(0, 5); scintilla.SetSelectionMode(1);
                    context.LastError = null;
                    var rectangleRequests = handler.RequestCount;
                    context.Callbacks[1]();
                    for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || handler.RequestCount != rectangleRequests || scintilla.Text != "hello world") throw new Exception("Rectangular Scintilla selection was not rejected.");
                    Console.WriteLine("PASS: Rectangular Scintilla selections are not overwritten");
                    scintilla.SetSelectionMode(0); scintilla.SetText("hello"); scintilla.FocusAndSelect(0, 5); scintilla.SetCaretVirtualSpace(2);
                    if (scintilla.GetCaretVirtualSpace() != 2) throw new Exception("Could not configure Scintilla virtual-space fixture.");
                    context.LastError = null;
                    var virtualRequests = handler.RequestCount;
                    context.Callbacks[1]();
                    for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || handler.RequestCount != virtualRequests || scintilla.Text != "hello") throw new Exception("Scintilla virtual-space selection was not rejected.");
                    Console.WriteLine("PASS: Scintilla virtual-space selections are not overwritten");
                    scintilla.SetText("😀 hello"); scintilla.FocusAndSelect(5, 10);
                    context.LastError = null;
                    context.Callbacks[1]();
                    for (var i = 0; i < 100 && context.LastError is null && scintilla.Text != "😀 merhaba"; i++) await Task.Delay(100);
                    if (scintilla.Text != "😀 merhaba" || handler.LastQuery != "hello") throw new Exception("Unicode Scintilla byte offsets were not preserved: " + context.LastError);
                    Console.WriteLine("PASS: Scintilla Unicode byte offsets select the intended text");
                    await Task.Delay(400);
                }
                var fixtureKinds = new List<string> { "edit", "wpf-rich", "native-rich" };
                if (!string.IsNullOrWhiteSpace(scintillaPath)) fixtureKinds.Add("scintilla");
                foreach (var fixtureKind in fixtureKinds)
                using (var fixture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, fixtureKind switch { "wpf-rich" => "--fixture-rich", "native-rich" => "--fixture-nativerich", "scintilla" => "--fixture-scintilla", _ => "--fixture" }) { UseShellExecute = false })!)
                {
                    try
                    {
                        for (var i = 0; i < 100 && fixture.MainWindowHandle == IntPtr.Zero && !fixture.HasExited; i++) { await Task.Delay(100); fixture.Refresh(); }
                        if (fixture.MainWindowHandle == IntPtr.Zero) throw new Exception("Cross-process fixture did not start.");
                        var root = System.Windows.Automation.AutomationElement.FromHandle(fixture.MainWindowHandle);
                        System.Windows.Automation.AutomationElement? remoteEdit = null;
                        for (var i = 0; i < 50 && remoteEdit is null; i++)
                        {
                            remoteEdit = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, fixtureKind switch { "wpf-rich" => "RichTextBox", "native-rich" => "RICHEDIT50W", "scintilla" => "Scintilla", _ => "Edit" }));
                            if (remoteEdit is null) await Task.Delay(100);
                        }
                        if (remoteEdit is null) throw new Exception("Cross-process edit control not found.");
                        SetForegroundWindow(fixture.MainWindowHandle);
                        remoteEdit.SetFocus();
                        await Task.Delay(300);
                        var remoteText = fixtureKind is "native-rich" or "scintilla" ? null : (System.Windows.Automation.TextPattern)remoteEdit.GetCurrentPattern(System.Windows.Automation.TextPattern.Pattern);
                        if (fixtureKind == "wpf-rich")
                        {
                            var range = remoteText!.DocumentRange.Clone();
                            range.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, range, System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
                            range.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, System.Windows.Automation.Text.TextUnit.Character, 5);
                            range.Select();
                        }
                        else SendMessage(new IntPtr(remoteEdit.Current.NativeWindowHandle), fixtureKind == "scintilla" ? 2160u : 0xB1, IntPtr.Zero, (IntPtr)5);
                        context.LastError = null;
                        context.Callbacks[1]();
                        string actual = "";
                        for (var i = 0; i < 100 && context.LastError is null; i++)
                        {
                            await Task.Delay(100);
                            if (remoteText is not null) actual = remoteText.DocumentRange.GetText(-1);
                            else { var buffer = new System.Text.StringBuilder(128); SendMessageText(new IntPtr(remoteEdit.Current.NativeWindowHandle), 0xD, (IntPtr)buffer.Capacity, buffer); actual = buffer.ToString(); }
                            if (actual.StartsWith("merhaba world")) break;
                        }
                        if (!actual.StartsWith("merhaba world")) throw new Exception($"Cross-process {fixtureKind} replacement failed: {context.LastError}");
                        Console.WriteLine($"PASS: Selection replacement in another {fixtureKind} process");
                        if (fixtureKind == "native-rich")
                        {
                            await Task.Delay(400);
                            var handle = new IntPtr(remoteEdit.Current.NativeWindowHandle);
                            SendMessage(handle, 0x435 /* EM_EXLIMITTEXT */, IntPtr.Zero, (IntPtr)100_000);
                            SendMessageString(handle, 0xC /* WM_SETTEXT */, IntPtr.Zero, new string('x', 66_000) + "hello world");
                            SendMessage(handle, 0xB1 /* EM_SETSEL */, (IntPtr)66_000, (IntPtr)66_005);
                            var ok = GetSelectionFull(handle, 0xB0, out var start, out var end, 3, 1000, out _);
                            Console.WriteLine($"Cross-process full RichEdit selection: success={ok != IntPtr.Zero}, start={start}, end={end}");
                            if (ok == IntPtr.Zero || start != 66_000 || end != 66_005) throw new Exception("Full RichEdit selection offsets were not marshalled correctly.");
                            context.LastError = null;
                            context.Callbacks[1]();
                            actual = "";
                            for (var i = 0; i < 100 && context.LastError is null; i++)
                            {
                                await Task.Delay(100);
                                var buffer = new System.Text.StringBuilder(66_100);
                                SendMessageText(handle, 0xD, (IntPtr)buffer.Capacity, buffer);
                                actual = buffer.ToString();
                                if (actual.EndsWith("merhaba world")) break;
                            }
                            if (!actual.EndsWith("merhaba world") || handler.LastQuery != "hello") throw new Exception("RichEdit selection past 64K was not translated: " + context.LastError);
                            Console.WriteLine("PASS: Cross-process RichEdit selection beyond 64K is replaced");
                        }
                    }
                    finally
                    {
                        if (!fixture.HasExited) { fixture.CloseMainWindow(); if (!fixture.WaitForExit(3000)) fixture.Kill(); }
                    }
                }
                var browser = new WebView2();
                window.Content = browser;
                await browser.EnsureCoreWebView2Async();
                var loaded = new TaskCompletionSource();
                browser.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
                browser.NavigateToString("<html><body><textarea id='edit'>hello world</textarea><div id='rich' contenteditable='true'>hello world</div><p id='read'>hello world</p><input id='password' type='password' value='secret'></body></html>");
                await loaded.Task;
                window.Activate(); browser.Focus(); Keyboard.Focus(browser);
                await browser.CoreWebView2.ExecuteScriptAsync("let e=document.querySelector('#edit'); e.focus(); e.setSelectionRange(0,5);");
                await Task.Delay(500);
                context.LastError = null;
                var focused = System.Windows.Automation.AutomationElement.FocusedElement;
                Console.WriteLine($"Browser focus: {focused?.Current.Name} {focused?.Current.ControlType.ProgrammaticName}; patterns: {string.Join(", ", focused?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                context.Callbacks[1]();
                string result = "";
                for (var i = 0; i < 100 && context.LastError is null; i++)
                {
                    await Task.Delay(100);
                    result = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#edit').value");
                    if (result.Contains("merhaba")) break;
                }
                if (result != "\"merhaba world\"") throw new Exception($"Browser replacement failed. Text: {result}; message: {context.LastError}");
                Console.WriteLine("PASS: Chromium textarea selection and guarded replacement");
                await browser.CoreWebView2.ExecuteScriptAsync("let r=document.querySelector('#rich'); r.focus(); let range=document.createRange(); range.setStart(r.firstChild,0); range.setEnd(r.firstChild,5); let s=getSelection(); s.removeAllRanges(); s.addRange(range);");
                await Task.Delay(250);
                focused = System.Windows.Automation.AutomationElement.FocusedElement;
                Console.WriteLine($"Rich editor focus: {focused?.Current.ControlType.ProgrammaticName}; patterns: {string.Join(", ", focused?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                context.LastError = null;
                context.Callbacks[1]();
                for (var i = 0; i < 100 && context.LastError is null; i++)
                {
                    await Task.Delay(100);
                    result = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#rich').textContent");
                    if (result.Contains("merhaba")) break;
                }
                if (result != "\"merhaba world\"") throw new Exception($"Rich editor replacement failed. Text: {result}; message: {context.LastError}");
                Console.WriteLine("PASS: Chromium contenteditable selection and guarded replacement");
                await Task.Delay(400);
                window.Activate(); browser.Focus(); Keyboard.Focus(browser);
                await browser.CoreWebView2.ExecuteScriptAsync("{ let e=document.querySelector('#edit'); e.value='hello world'; e.focus(); e.setSelectionRange(0,5); }");
                await Task.Delay(250);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                var clipboardSequence = GetClipboardSequenceNumber();
                context.Callbacks[1]();
                try { await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { throw new Exception("Translation request did not start: " + context.LastError); }
                if (GetClipboardSequenceNumber() != clipboardSequence) throw new Exception("Clipboard changed while starting editable selection translation.");
                Console.WriteLine("PASS: Editable selection capture leaves clipboard untouched");
                Clipboard.SetText("new clipboard data");
                handler.Gate.SetResult();
                for (var i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    result = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#edit').value");
                    if (result.Contains("merhaba")) break;
                }
                await Task.Delay(400);
                if (result != "\"merhaba world\"" || Clipboard.GetText() != "new clipboard data") throw new Exception("Clipboard change during request was lost.");
                Console.WriteLine("PASS: Clipboard changes during translation are preserved");
                await browser.CoreWebView2.ExecuteScriptAsync("{ let e=document.querySelector('#edit'); e.value='hello world'; e.focus(); e.setSelectionRange(0,5); }");
                await Task.Delay(250);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                try { await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { throw new Exception("Translation request did not start: " + context.LastError); }
                await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#edit').setSelectionRange(6,11)");
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                result = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#edit').value");
                if (result != "\"hello world\"" || context.LastError is null) throw new Exception("Changed selection was not rejected.");
                Console.WriteLine("PASS: Changed selection is not overwritten");
                await browser.CoreWebView2.ExecuteScriptAsync("{let e=document.querySelector('#edit'); e.focus(); e.setSelectionRange(0,5);}");
                await Task.Delay(250);
                handler.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                handler.Received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.LastError = null;
                context.Callbacks[1]();
                try { await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { throw new Exception("Translation request did not start: " + context.LastError); }
                await browser.CoreWebView2.ExecuteScriptAsync("{let e=document.querySelector('#edit'); e.value='jello world'; e.setSelectionRange(0,5);}");
                handler.Gate.SetResult();
                for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                result = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#edit').value");
                if (result != "\"jello world\"" || context.LastError is null) throw new Exception("Changed selection text was not rejected.");
                Console.WriteLine("PASS: Changed selected text is not overwritten");
                handler.Gate = handler.Received = null;
                context.LastError = null;
                await browser.CoreWebView2.ExecuteScriptAsync("{ let r=document.querySelector('#read'); let range=document.createRange(); range.setStart(r.firstChild,0); range.setEnd(r.firstChild,5); let s=getSelection(); s.removeAllRanges(); s.addRange(range); }");
                await Task.Delay(250);
                context.Callbacks[0]();
                Window? resultWindow = null;
                for (var i = 0; i < 50 && context.LastError is null && resultWindow is null; i++)
                {
                    await Task.Delay(100);
                    resultWindow = app.Windows.OfType<Window>().FirstOrDefault(w => w.Title.StartsWith("Çeviri ·"));
                }
                if (resultWindow?.Content is not TextBox { Text: "merhaba" }) throw new Exception("Web selection display failed: " + context.LastError);
                Console.WriteLine("PASS: Selected web page text appears in translation window");
                SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                window.Activate(); browser.Focus(); Keyboard.Focus(browser);
                await browser.CoreWebView2.ExecuteScriptAsync("{ let r=document.querySelector('#read'); let range=document.createRange(); range.setStart(r.firstChild,0); range.setEnd(r.firstChild,5); let s=getSelection(); s.removeAllRanges(); s.addRange(range); }");
                await Task.Delay(300);
                context.Callbacks[0]();
                Window? nextResult = null;
                for (var i = 0; i < 50 && context.LastError is null && nextResult is null; i++)
                {
                    await Task.Delay(100);
                    nextResult = app.Windows.OfType<Window>().FirstOrDefault(w => w != resultWindow && w.Title.StartsWith("Çeviri ·"));
                }
                if (nextResult is null || resultWindow.IsVisible) throw new Exception("A new translation did not close the prior result window: " + context.LastError);
                module.Deactivate();
                if (nextResult.IsVisible) throw new Exception("Deactivation did not close the translation window.");
                module.Activate();
                Console.WriteLine("PASS: Result windows are replaced and closed on deactivation");
                window.Activate(); browser.Focus(); Keyboard.Focus(browser);
                await browser.CoreWebView2.ExecuteScriptAsync("{let p=document.querySelector('#password'); p.focus(); p.select();}");
                await Task.Delay(400);
                var previousRequests = handler.RequestCount;
                context.LastError = null;
                context.Callbacks[0]();
                for (var i = 0; i < 30 && context.LastError is null; i++) await Task.Delay(100);
                if (handler.RequestCount != previousRequests || context.LastError is null || !context.LastError.Contains("Parola")) throw new Exception("Password was not blocked before network access.");
                Console.WriteLine("PASS: Password fields are not sent for translation");
                browser.Dispose();
                module.Deactivate();
                using var hotkeys = new HotkeyService();
                context.NativeHotkeys = hotkeys;
                context.LastError = null;
                context.Values["view-shortcut"] = "Ctrl+Alt+Shift+F21";
                context.Values["replace-shortcut"] = "Ctrl+Alt+Shift+F22";
                module.Activate();
                window.Content = editor; editor.Text = "hello world";
                await Task.Delay(300);
                for (var i = 0; i < 30 && !editor.IsKeyboardFocused; i++)
                {
                    SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                    window.Activate(); editor.Focus(); Keyboard.Focus(editor);
                    await Task.Delay(100);
                }
                if (!editor.IsKeyboardFocused) throw new Exception("Test window did not reacquire focus for hotkey test.");
                editor.Select(0, 5);
                Console.WriteLine($"Hotkey focus: {System.Windows.Automation.AutomationElement.FocusedElement?.Current.ControlType.ProgrammaticName}; editor keyboard focus: {editor.IsKeyboardFocused}");
                SendHotkey(0x85); // F22, the customized replacement shortcut.
                for (var i = 0; i < 100 && editor.Text == "hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (editor.Text != "merhaba world") throw new Exception("Native assigned shortcut failed: " + context.LastError);
                Console.WriteLine("PASS: Custom Windows hotkey triggers replacement");
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                module.Deactivate();
                // Do not overwrite a clipboard change made independently while
                // this interactive test was running.
                var sequence = GetClipboardSequenceNumber();
                var current = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if ((current == "clipboard sentinel" || current == "new clipboard data") && GetClipboardSequenceNumber() == sequence)
                    restoreMethod.Invoke(null, [originalClipboard]);
                window.Close();
            }
        };
        app.Run(window);
        return exit;
    }
    private static int ChromeIntegrationCheck(string executable)
    {
        var tempBase = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var id = Guid.NewGuid().ToString("N");
        var title = "WinForge Chrome fixture " + id;
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempBase, "WinForge-chrome-test-" + id));
        if (!root.StartsWith(tempBase + "WinForge-chrome-test-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary test path.");
        System.IO.Directory.CreateDirectory(root);
        var page = System.IO.Path.Combine(root, "sample.html");
        System.IO.File.WriteAllText(page, "<!doctype html><html><head><title>" + title + "</title></head><body><textarea aria-label='WinForge editable sample' autofocus>hello world</textarea><p id='read' tabindex='0'>hello world</p><button onclick=\"let p=document.querySelector('#read');p.focus();let r=document.createRange();r.selectNodeContents(p);let s=getSelection();s.removeAllRanges();s.addRange(r);\">Select sample text</button><iframe title='sample frame' srcdoc=\"&lt;textarea aria-label='WinForge iframe sample'&gt;hello world&lt;/textarea&gt;\"></iframe><iframe title='isolated frame' sandbox='allow-scripts' srcdoc=\"&lt;textarea aria-label='WinForge isolated iframe sample'&gt;hello world&lt;/textarea&gt;\"></iframe><textarea readonly aria-label='WinForge readonly sample'>hello world</textarea><input type='password' aria-label='WinForge password sample' value='secret'><div id='shadow-host'></div><script>document.querySelector('#shadow-host').attachShadow({mode:'open'}).innerHTML=\"<div contenteditable='true' role='textbox' aria-label='WinForge shadow sample'>hello world</div>\";</script></body></html>");
        var app = new Application();
        var hostWindow = new Window { Title = "WinForge Chrome test", Width = 200, Height = 100, ShowInTaskbar = false, ShowActivated = false, WindowState = WindowState.Minimized };
        var context = new Context(); var handler = new FakeHandler();
        var module = new SelectionTranslationModule(new HttpClient(handler));
        module.Initialize(context); module.Activate();
        var exit = 1;
        System.Diagnostics.Process? process = null;
        System.Diagnostics.Process? launcher = null;
        hostWindow.Loaded += async (_, _) =>
        {
            const string sentinel = "WinForge Chrome test clipboard";
            DataObject? clipboardBefore = null;
            try
            {
                clipboardBefore = SelectionTranslationModule.SnapshotClipboard();
                var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetFullPath(executable)) { UseShellExecute = false };
                foreach (var directory in new[] { "user", "roaming", "local" }) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, directory));
                start.Environment["USERPROFILE"] = System.IO.Path.Combine(root, "user");
                start.Environment["APPDATA"] = System.IO.Path.Combine(root, "roaming");
                start.Environment["LOCALAPPDATA"] = System.IO.Path.Combine(root, "local");
                foreach (var arg in new[] { "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-sync", "--disable-background-networking", "--disable-component-update", "--user-data-dir=" + System.IO.Path.Combine(root, "profile"), "--app=" + new Uri(page).AbsoluteUri }) start.ArgumentList.Add(arg);
                launcher = System.Diagnostics.Process.Start(start) ?? throw new Exception("Chrome test process did not start.");
                for (var i = 0; i < 250 && process is null; i++)
                {
                    foreach (var candidate in System.Diagnostics.Process.GetProcessesByName("chrome"))
                    {
                        try { if (candidate.MainWindowHandle != IntPtr.Zero && candidate.MainWindowTitle.Contains(title, StringComparison.Ordinal)) { process = candidate; break; } }
                        catch (InvalidOperationException) { }
                        if (candidate != process) candidate.Dispose();
                    }
                    if (process is null) await Task.Delay(100);
                }
                if (process is null) throw new Exception("Chrome test window did not appear.");
                var targetWindow = process.MainWindowHandle;
                FocusTestWindow(targetWindow);
                var rootElement = System.Windows.Automation.AutomationElement.FromHandle(targetWindow);
                System.Windows.Automation.AutomationElement? edit = null;
                for (var i = 0; i < 100 && edit is null; i++)
                {
                    edit = rootElement.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "WinForge editable sample"));
                    if (edit is null) await Task.Delay(100);
                }
                if (edit is null) throw new Exception("Chrome sample text field was not found.");
                FocusTestWindow(targetWindow); edit.SetFocus(); await Task.Delay(300);
                if (GetForegroundWindow() != targetWindow) throw new Exception("Chrome test window did not get foreground focus.");
                SendControlKey(0x41); await Task.Delay(300);
                context.LastError = null; context.Callbacks.Single()();
                var valuePattern = edit.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern) as System.Windows.Automation.ValuePattern;
                for (var i = 0; i < 100 && context.LastError is null && valuePattern?.Current.Value == "hello world"; i++) await Task.Delay(100);
                if (valuePattern?.Current.Value != "merhaba" || Clipboard.GetText() != "merhaba") throw new Exception("Chrome field copy and paste failed: " + context.LastError);
                Console.WriteLine("PASS: Chrome text field selection, copy and paste");
                await Task.Delay(400);
                var button = rootElement.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Select sample text"))
                    ?? throw new Exception("Chrome page selection control was not found.");
                ((System.Windows.Automation.InvokePattern)button.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
                Clipboard.SetText(sentinel);
                await Task.Delay(400); context.LastError = null; context.Callbacks.Single()();
                for (var i = 0; i < 100 && context.LastError is null && Clipboard.GetText() != "merhaba"; i++) await Task.Delay(100);
                if (Clipboard.GetText() != "merhaba" || handler.LastQuery != "hello world") throw new Exception("Chrome page selection copy failed: " + context.LastError);
                Console.WriteLine("PASS: Chrome selected page text is translated and copied");
                foreach (var label in new[] { "WinForge iframe sample", "WinForge isolated iframe sample", "WinForge shadow sample" })
                {
                    var embedded = rootElement.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, label))
                        ?? throw new Exception("Chrome embedded field was not found: " + label);
                    FocusTestWindow(targetWindow); embedded.SetFocus(); await Task.Delay(300);
                    if (GetForegroundWindow() != targetWindow) throw new Exception("Chrome embedded field did not get foreground focus.");
                    SendControlKey(0x41); await Task.Delay(300);
                    context.LastError = null; context.Callbacks.Single()();
                    var embeddedValue = embedded.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern) as System.Windows.Automation.ValuePattern;
                    for (var i = 0; i < 100 && context.LastError is null && embeddedValue?.Current.Value == "hello world"; i++) await Task.Delay(100);
                    if (embeddedValue?.Current.Value != "merhaba") throw new Exception("Chrome embedded field replacement failed for " + label + ": " + context.LastError);
                    Console.WriteLine("PASS: Chrome embedded field selection and replacement: " + label);
                    await Task.Delay(400);
                }
                foreach (var label in new[] { "WinForge readonly sample", "WinForge password sample" })
                {
                    var protectedField = rootElement.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, label))
                        ?? throw new Exception("Chrome protected field was not found: " + label);
                    FocusTestWindow(targetWindow); protectedField.SetFocus(); await Task.Delay(300); SendControlKey(0x41); await Task.Delay(300);
                    Clipboard.SetText(sentinel);
                    context.LastError = null;
                    var requestsBefore = handler.RequestCount;
                    context.Callbacks.Single()();
                    if (label.Contains("password"))
                    {
                        for (var i = 0; i < 40 && context.LastError is null; i++) await Task.Delay(100);
                        if (context.LastError is null || handler.RequestCount != requestsBefore || Clipboard.GetText() != sentinel)
                            throw new Exception("Chrome password field was not rejected.");
                        Console.WriteLine("PASS: Chrome password field is not sent");
                    }
                    else
                    {
                        for (var i = 0; i < 40 && context.LastError is null && Clipboard.GetText() != "merhaba"; i++) await Task.Delay(100);
                        var fieldValue = (System.Windows.Automation.ValuePattern)protectedField.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                        if (Clipboard.GetText() != "merhaba" || fieldValue.Current.Value != "hello world")
                            throw new Exception("Chrome read-only field was not safely translated: " + context.LastError);
                        Console.WriteLine("PASS: Chrome read-only field translation is copied");
                    }
                }
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                module.Deactivate();
                try
                {
                    if (Clipboard.GetText() is "merhaba" or sentinel)
                    {
                        if (clipboardBefore is null) Clipboard.Clear();
                        else Clipboard.SetDataObject(clipboardBefore, true);
                    }
                }
                catch (System.Runtime.InteropServices.ExternalException error) { Console.Error.WriteLine(error); exit = 1; }
                if (process is not null && !process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
                if (launcher is not null && !launcher.HasExited) { launcher.Kill(entireProcessTree: true); launcher.WaitForExit(5000); }
                process?.Dispose(); launcher?.Dispose(); hostWindow.Close();
            }
        };
        try { app.Run(hostWindow); }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
        return exit;
    }
    private static int VsCodeIntegrationCheck(string executable, bool screenReader)
    {
        var tempBase = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempBase, "WinForge-vscode-test-" + Guid.NewGuid().ToString("N")));
        if (!root.StartsWith(tempBase + "WinForge-vscode-test-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary test path.");
        var profile = System.IO.Path.Combine(root, "profile");
        var userSettings = System.IO.Path.Combine(profile, "User");
        System.IO.Directory.CreateDirectory(userSettings);
        System.IO.File.WriteAllText(System.IO.Path.Combine(userSettings, "settings.json"), "{\"telemetry.telemetryLevel\":\"off\",\"update.mode\":\"none\",\"extensions.autoUpdate\":false,\"workbench.startupEditor\":\"none\",\"security.workspace.trust.enabled\":false,\"editor.accessibilitySupport\":\"" + (screenReader ? "on" : "auto") + "\"}");
        var sample = System.IO.Path.Combine(root, "sample.txt");
        System.IO.File.WriteAllText(sample, "hello world", new System.Text.UTF8Encoding(false));
        var app = new Application();
        var hostWindow = new Window { Title = "WinForge VS Code test", Width = 200, Height = 100, ShowInTaskbar = false, ShowActivated = false, WindowState = WindowState.Minimized };
        var context = new Context();
        var handler = new FakeHandler();
        var module = new SelectionTranslationModule(new HttpClient(handler));
        module.Initialize(context);
        module.Activate();
        var exit = 1;
        System.Diagnostics.Process? process = null;
        hostWindow.Loaded += async (_, _) =>
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetFullPath(executable)) { UseShellExecute = false };
                // VS Code has additional shared storage outside --user-data-dir;
                // isolate the child process home and app-data paths as well.
                var isolatedUser = System.IO.Path.Combine(root, "user");
                var isolatedRoaming = System.IO.Path.Combine(root, "roaming");
                var isolatedLocal = System.IO.Path.Combine(root, "local");
                System.IO.Directory.CreateDirectory(isolatedUser);
                System.IO.Directory.CreateDirectory(isolatedRoaming);
                System.IO.Directory.CreateDirectory(isolatedLocal);
                start.Environment["USERPROFILE"] = isolatedUser;
                start.Environment["APPDATA"] = isolatedRoaming;
                start.Environment["LOCALAPPDATA"] = isolatedLocal;
                foreach (var arg in new[] { "--new-window", "--disable-extensions", "--skip-welcome", "--skip-release-notes", "--disable-workspace-trust", "--user-data-dir", profile, "--extensions-dir", System.IO.Path.Combine(root, "extensions"), sample }) start.ArgumentList.Add(arg);
                process = System.Diagnostics.Process.Start(start) ?? throw new Exception("VS Code test process did not start.");
                for (var i = 0; i < 250 && !process.HasExited && process.MainWindowHandle == IntPtr.Zero; i++) { await Task.Delay(100); process.Refresh(); }
                if (process.HasExited || process.MainWindowHandle == IntPtr.Zero) throw new Exception("VS Code test window did not appear.");
                var targetWindow = process.MainWindowHandle;
                SetForegroundWindow(targetWindow);
                await Task.Delay(3000);
                if (GetForegroundWindow() != targetWindow) throw new Exception("VS Code test window did not get foreground focus.");
                var rootElement = System.Windows.Automation.AutomationElement.FromHandle(targetWindow);
                System.Windows.Automation.AutomationElement? editorElement = null;
                for (var i = 0; i < 150 && editorElement is null; i++)
                {
                    var candidates = rootElement.FindAll(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit));
                    editorElement = candidates.Cast<System.Windows.Automation.AutomationElement>().FirstOrDefault(candidate => candidate.Current.ClassName == "native-edit-context");
                    if (editorElement is null) await Task.Delay(100);
                }
                if (editorElement is null) throw new Exception("VS Code editor surface was not found.");
                editorElement.SetFocus();
                await Task.Delay(500);
                if (!screenReader)
                {
                    // VS Code copies the current line on Ctrl+C with no selection.
                    // That line must not be sent to the translation service.
                    context.LastError = null;
                    var beforeRequests = handler.RequestCount;
                    context.Callbacks[0]();
                    for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || !context.LastError.Contains("Metin seçilmedi") || handler.RequestCount != beforeRequests)
                        throw new Exception("VS Code copy without selection was not rejected: " + context.LastError);
                    Console.WriteLine("PASS: VS Code's copied current line is not sent when nothing is selected");
                    context.LastError = null;
                    context.Callbacks[1]();
                    for (var i = 0; i < 50 && context.LastError is null; i++) await Task.Delay(100);
                    if (context.LastError is null || !context.LastError.Contains("Metin seçilmedi") || handler.RequestCount != beforeRequests)
                        throw new Exception("VS Code replacement without selection was not rejected: " + context.LastError);
                    Console.WriteLine("PASS: VS Code replacement shortcut does not translate an unselected line");
                    context.LastError = null;
                }
                SendControlKey(0x41); // Select the disposable sample text.
                await Task.Delay(500);
                var element = System.Windows.Automation.AutomationElement.FocusedElement;
                Console.WriteLine($"VS Code focus class: {element?.Current.ClassName}; type: {element?.Current.ControlType.ProgrammaticName}; patterns: {string.Join(", ", element?.GetSupportedPatterns().Select(p => p.ProgrammaticName) ?? [])}");
                if (element?.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var pattern) == true)
                    Console.WriteLine("VS Code UIA selections: " + string.Join(" | ", ((System.Windows.Automation.TextPattern)pattern).GetSelection().Select(range => "length=" + range.GetText(100).Length)));
                context.Callbacks[1]();
                for (var i = 0; i < 150 && context.LastError is null && handler.RequestCount == 0; i++) await Task.Delay(100);
                if (!screenReader)
                {
                    Window? preview = null;
                    for (var i = 0; i < 100 && preview is null; i++)
                    {
                        await Task.Delay(100);
                        preview = app.Windows.OfType<Window>().FirstOrDefault(w => w.Title.StartsWith("Çeviri ·"));
                    }
                    if (preview?.Content is not TextBox previewText || previewText.Text.Trim() != "merhaba" || handler.LastQuery != "hello world" || System.IO.File.ReadAllText(sample) != "hello world" || context.LastError is null || !context.LastError.Contains("VS Code"))
                        throw new Exception($"VS Code preview fallback failed: {context.LastError}; query={handler.LastQuery}; result={(preview?.Content as TextBox)?.Text}; file={System.IO.File.ReadAllText(sample)}");
                    preview.Close();
                    Console.WriteLine("PASS: VS Code replacement shortcut safely shows a translation preview and guidance");
                }
                else
                {
                    if (context.LastError is not null || handler.RequestCount == 0) throw new Exception("VS Code selection could not be translated: " + context.LastError);
                    await Task.Delay(900);
                    if (GetForegroundWindow() != targetWindow) throw new Exception("VS Code focus changed during the test.");
                    SendControlKey(0x53); // Save only the disposable test file.
                    for (var i = 0; i < 100 && System.IO.File.ReadAllText(sample) == "hello world"; i++) await Task.Delay(100);
                    if (System.IO.File.ReadAllText(sample).Trim() != "merhaba") throw new Exception("VS Code translation was not inserted into the sample file.");
                    Console.WriteLine("PASS: Selection in an isolated VS Code editor is translated and replaced");
                }
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                module.Deactivate();
                if (process is not null && !process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
                process?.Dispose();
                hostWindow.Close();
            }
        };
        try { app.Run(hostWindow); }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
        return exit;
    }
    private static void SendControlKey(ushort key)
    {
        static KeyboardInputSender.INPUT Input(ushort code, uint flags) => new() { Type = 1, Union = new KeyboardInputSender.InputUnion { Keyboard = new KeyboardInputSender.KEYBDINPUT { VirtualKey = code, Flags = flags } } };
        KeyboardInputSender.INPUT[] keys = [Input(0x11, 0), Input(key, 0), Input(key, 2), Input(0x11, 2)];
        if (KeyboardInputSender.SendInput((uint)keys.Length, keys, System.Runtime.InteropServices.Marshal.SizeOf<KeyboardInputSender.INPUT>()) != keys.Length) throw new Exception("Test keyboard input failed.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static int LiveMyMemoryServiceCheck()
    {
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            var service = new SelectionTranslationService(client);
            var turkish = service.TranslateAsync("Hello world", "auto", "tr", default, "mymemory").GetAwaiter().GetResult();
            var english = service.TranslateAsync("Merhaba dünya", "auto", "en", default, "mymemory").GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(turkish) || string.IsNullOrWhiteSpace(english)) throw new Exception("Service returned an empty translation.");
            Console.WriteLine("PASS: Live MyMemory English→Turkish: " + turkish);
            Console.WriteLine("PASS: Live MyMemory Turkish→English: " + english);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static int LiveNativeCheck(bool myMemory)
    {
        var app = new Application();
        var editor = new TextBox { Text = "Hello world", FontSize = 20, Margin = new Thickness(20) };
        var window = new Window { Title = "WinForge live translation test", Width = 400, Height = 150, Content = editor, Topmost = true };
        var context = new Context();
        if (myMemory) { context.Values["provider"] = "mymemory"; context.Values["source"] = "en"; }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        var module = new SelectionTranslationModule(client);
        module.Initialize(context);
        module.Activate();
        var exit = 1;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                for (var i = 0; i < 30 && (GetForegroundWindow() != handle || !editor.IsKeyboardFocused); i++)
                {
                    SetForegroundWindow(handle); window.Activate(); editor.Focus(); Keyboard.Focus(editor);
                    await Task.Delay(100);
                }
                if (GetForegroundWindow() != handle || !editor.IsKeyboardFocused) throw new Exception("Live test did not acquire foreground focus.");
                editor.SelectAll();
                await Task.Delay(200);
                context.Callbacks[1]();
                for (var i = 0; i < 220 && editor.Text == "Hello world" && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is not null || string.IsNullOrWhiteSpace(editor.Text) || editor.Text == "Hello world")
                    throw new Exception("Live translation replacement failed: " + context.LastError);
                Console.WriteLine("PASS: Real online translation replaced a selected Windows text field: " + editor.Text);
                editor.Text = "Merhaba dünya";
                editor.SelectAll();
                context.LastError = null;
                context.Callbacks[^1]();
                for (var i = 0; i < 220 && editor.Text == "Merhaba dünya" && context.LastError is null; i++) await Task.Delay(100);
                if (context.LastError is not null || string.IsNullOrWhiteSpace(editor.Text) || editor.Text == "Merhaba dünya")
                    throw new Exception("Live reverse-language replacement failed: " + context.LastError);
                Console.WriteLine("PASS: The same shortcut translates in the other direction with the real provider: " + editor.Text);
                await Task.Delay(400);
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { module.Deactivate(); window.Close(); }
        };
        app.Run(window);
        return exit;
    }
    private static int ShortcutLanguagesCheck()
    {
        (string Text, string Source, string Target)[] samples =
        [
            ("Merhaba dünya", "tr", "en"),
            ("Bugun nasilsin?", "tr", "en"),
            ("TEŞEKKÜR EDERİM", "tr", "en"),
            ("Bunu yarın tekrar konuşalım.", "tr", "en"),
            ("Hello world", "en", "tr"),
            ("hello", "en", "tr"),
            ("THANK YOU", "en", "tr"),
            ("I don't know when it will be ready.", "en", "tr")
        ];
        foreach (var (input, source, target) in samples)
        {
            var actual = SelectionTranslationLanguage.ShortcutPair(input, "en");
            if (actual != (source, target))
                throw new Exception($"Wrong shortcut direction for '{input}': {actual}.");
        }
        if (SelectionTranslationLanguage.ShortcutPair("Merhaba", "fr") != ("tr", "fr"))
            throw new Exception("An existing custom shortcut target did not keep its language pair.");
        try
        {
            SelectionTranslationLanguage.ShortcutPair("123 !", "en");
            throw new Exception("A selection containing no letters was accepted.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("dili belirlenemedi")) { }
        Console.WriteLine("PASS: Automatic Turkish/English shortcut direction, custom target and non-text selection.");
        return 0;
    }

    private static int HostIntegrationCheck(string modulesDirectory)
    {
        var source = System.IO.Path.GetFullPath(modulesDirectory);
        var tempBase = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempBase, "WinForge-translation-host-" + Guid.NewGuid().ToString("N")));
        if (!root.StartsWith(tempBase + "WinForge-translation-host-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid temporary test path.");
        System.IO.Directory.CreateDirectory(root);
        System.Reflection.Assembly? Resolve(System.Runtime.Loader.AssemblyLoadContext context, System.Reflection.AssemblyName name)
        {
            var candidate = System.IO.Path.Combine(source, name.Name + ".dll");
            return System.IO.File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var settingsFile = System.IO.Path.Combine(root, "settings.json");
            using var store = new SettingsStore(settingsFile);
            using var logger = new FileLogger(System.IO.Path.Combine(root, "test.log"));
            var hotkeys = new HostHotkeys();
            var host = new ModuleHost(store, logger, hotkeys, source, _ => { });
            host.Load();
            var module = host.Modules.SingleOrDefault(item => item.Id == "selection-translation")
                ?? throw new Exception("Packaged module was not discovered. " + System.IO.File.ReadAllText(logger.Path));
            host.SetEnabled(module, true);
            if (!module.IsActive || hotkeys.Count != 1) throw new Exception("Host activation did not register the translation hotkey.");
            var target = module.Settings.Single(setting => setting.Key == "target");
            host.UpdateSettingAsync(module, target, "de").GetAwaiter().GetResult();
            if (host.SettingsOf(module).GetString("target") != "de" || hotkeys.Count != 1) throw new Exception("Target update failed.");
            var shortcutTarget = module.Settings.Single(setting => setting.Key == "translation-target");
            host.UpdateSettingAsync(module, shortcutTarget, "fr").GetAwaiter().GetResult();
            var provider = module.Settings.Single(setting => setting.Key == "provider");
            host.UpdateSettingAsync(module, provider, "mymemory").GetAwaiter().GetResult();
            var sourceLanguage = module.Settings.Single(setting => setting.Key == "source");
            host.UpdateSettingAsync(module, sourceLanguage, "en").GetAwaiter().GetResult();
            if (host.SettingsOf(module).GetString("provider") != "mymemory" || host.SettingsOf(module).GetString("source") != "en" || hotkeys.Count != 1)
                throw new Exception("MyMemory provider configuration failed.");
            try { host.UpdateSettingAsync(module, target, "invalid&target").GetAwaiter().GetResult(); throw new Exception("Invalid language was accepted."); }
            catch (InvalidOperationException error) when (error.Message.Contains("önceki değer")) { }
            if (host.SettingsOf(module).GetString("target") != "de" || hotkeys.Count != 1) throw new Exception("Invalid language did not restore the prior setting.");
            try { host.UpdateSettingAsync(module, shortcutTarget, "tr").GetAwaiter().GetResult(); throw new Exception("Turkish was accepted as the shortcut target."); }
            catch (InvalidOperationException error) when (error.Message.Contains("önceki değer")) { }
            if (host.SettingsOf(module).GetString("translation-target") != "fr" || hotkeys.Count != 1) throw new Exception("Invalid shortcut target did not restore the prior setting.");
            var shortcut = module.Settings.Single(setting => setting.Key == "replace-shortcut");
            try { host.UpdateSettingAsync(module, shortcut, "Ctrl+C").GetAwaiter().GetResult(); throw new Exception("Invalid shortcut was accepted."); }
            catch (InvalidOperationException error) when (error.Message.Contains("önceki değer")) { }
            if (host.SettingsOf(module).GetString("replace-shortcut", "Ctrl+Alt+Y") != "Ctrl+Alt+Y" || hotkeys.Count != 1)
                throw new Exception("Invalid shortcut did not restore the previous bindings.");
            using (var reloaded = new SettingsStore(settingsFile))
                if (reloaded.For(module.Id).GetValueOrDefault("target") != "de" || reloaded.For(module.Id).GetValueOrDefault("translation-target") != "fr" || reloaded.For(module.Id).GetValueOrDefault("provider") != "mymemory" || reloaded.For(module.Id).GetValueOrDefault("source") != "en" || !reloaded.Enabled.Contains(module.Id))
                    throw new Exception("Host did not persist module state and language.");
            host.SetEnabled(module, false);
            if (module.IsActive || hotkeys.Count != 0) throw new Exception("Host deactivation did not release shortcuts.");
            Console.WriteLine("PASS: Packaged module discovery, activation, settings persistence, shortcut rollback and cleanup");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving -= Resolve;
            // root is a freshly-created child of the OS temp directory.
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }
    private sealed class HostHotkeys : IHotkeyService
    {
        private readonly HashSet<(uint, uint)> _registered = [];
        public int Count => _registered.Count;
        public IDisposable Register(uint modifiers, uint key, Action callback)
        {
            var chord = (modifiers, key);
            if (!_registered.Add(chord)) throw new InvalidOperationException("Shortcut already registered.");
            return new HostBinding(() => _registered.Remove(chord));
        }
    }
    private sealed class HostBinding(Action release) : IDisposable { public void Dispose() => release(); }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    private static void SendTestKey(ushort key, bool up)
    {
        KeyboardInputSender.INPUT[] inputs = [new() { Type = 1, Union = new KeyboardInputSender.InputUnion { Keyboard = new KeyboardInputSender.KEYBDINPUT { VirtualKey = key, Flags = up ? 2u : 0u } } }];
        if (KeyboardInputSender.SendInput(1, inputs, System.Runtime.InteropServices.Marshal.SizeOf<KeyboardInputSender.INPUT>()) != 1) throw new Exception("Test keyboard input failed.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageText(IntPtr hwnd, uint message, IntPtr wParam, System.Text.StringBuilder text);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageString(IntPtr hwnd, uint message, IntPtr wParam, string text);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr GetSelectionFull(IntPtr hwnd, uint message, out uint start, out uint end, uint flags, uint timeout, out UIntPtr result);
    private sealed class ScintillaFixture(string dllPath) : System.Windows.Interop.HwndHost
    {
        private IntPtr _library, _handle;
        protected override System.Runtime.InteropServices.HandleRef BuildWindowCore(System.Runtime.InteropServices.HandleRef parent)
        {
            _library = LoadLibrary(System.IO.Path.GetFullPath(dllPath));
            if (_library == IntPtr.Zero) throw new Exception("Could not load the Scintilla fixture DLL.");
            _handle = CreateWindowEx(0, "Scintilla", "", 0x50010000, 0, 0, 360, 80, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_handle == IntPtr.Zero) throw new Exception("Could not create Scintilla fixture.");
            SendMessage(_handle, 2037 /* SCI_SETCODEPAGE */, (IntPtr)65001, IntPtr.Zero);
            SetText("hello world");
            return new System.Runtime.InteropServices.HandleRef(this, _handle);
        }
        protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd) { DestroyWindow(hwnd.Handle); if (_library != IntPtr.Zero) { FreeLibrary(_library); _library = IntPtr.Zero; } }
        public void FocusAndSelect(int start, int end) { SetFocus(_handle); SendMessage(_handle, 2160 /* SCI_SETSEL */, (IntPtr)start, (IntPtr)end); }
        public void SetReadOnly(bool value) => SendMessage(_handle, 2171 /* SCI_SETREADONLY */, (IntPtr)(value ? 1 : 0), IntPtr.Zero);
        public void AddSelection(int start, int end) { SendMessage(_handle, 2563 /* SCI_SETMULTIPLESELECTION */, (IntPtr)1, IntPtr.Zero); SendMessage(_handle, 2573 /* SCI_ADDSELECTION */, (IntPtr)end, (IntPtr)start); }
        public void SetSelectionMode(int mode) => SendMessage(_handle, 2422 /* SCI_SETSELECTIONMODE */, (IntPtr)mode, IntPtr.Zero);
        public void SetCaretVirtualSpace(int count) => SendMessage(_handle, 2580 /* SCI_SETSELECTIONNCARETVIRTUALSPACE */, IntPtr.Zero, (IntPtr)count);
        public long GetCaretVirtualSpace() => SendMessage(_handle, 2581 /* SCI_GETSELECTIONNCARETVIRTUALSPACE */, IntPtr.Zero, IntPtr.Zero).ToInt64();
        public void SetText(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
            var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
            try { System.Runtime.InteropServices.Marshal.Copy(bytes, 0, memory, bytes.Length); SendMessage(_handle, 2181 /* SCI_SETTEXT */, IntPtr.Zero, memory); }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }
        }
        public string Text
        {
            get
            {
                var length = SendMessage(_handle, 2006 /* SCI_GETLENGTH */, IntPtr.Zero, IntPtr.Zero).ToInt32();
                var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(length + 1);
                try { SendMessage(_handle, 2182 /* SCI_GETTEXT */, (IntPtr)(length + 1), memory); return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(memory) ?? ""; }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }
            }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int exStyle, string className, string text, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
    }
    private sealed class NativeEdit : System.Windows.Interop.HwndHost
    {
        private readonly int _style;
        private readonly bool _rich;
        private IntPtr _handle;
        private IntPtr _library;
        public NativeEdit(int style = 0, bool multiline = true, bool rich = false) { _style = 0x50010000 | style | (multiline ? 0x4 : 0); _rich = rich; }
        protected override System.Runtime.InteropServices.HandleRef BuildWindowCore(System.Runtime.InteropServices.HandleRef parent)
        {
            if (_rich) { _library = LoadLibrary("Msftedit.dll"); if (_library == IntPtr.Zero) throw new Exception("Could not load RichEdit fixture."); }
            _handle = CreateWindowEx(0, _rich ? "RICHEDIT50W" : "EDIT", "hello world", _style, 0, 0, 360, 80, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_handle == IntPtr.Zero) throw new Exception("Could not create native edit fixture.");
            return new System.Runtime.InteropServices.HandleRef(this, _handle);
        }
        protected override void DestroyWindowCore(System.Runtime.InteropServices.HandleRef hwnd) { DestroyWindow(hwnd.Handle); if (_library != IntPtr.Zero) { FreeLibrary(_library); _library = IntPtr.Zero; } }
        public void FocusInput() { SetFocus(_handle); SendMessage(_handle, 0xB1, (IntPtr)0, (IntPtr)5); }
        public void ResetAndSelect() { SetWindowText(_handle, "hello world"); FocusInput(); }
        public void SetTextAndSelect(string text, int start, int end) { SetWindowText(_handle, text); SetFocus(_handle); SendMessage(_handle, 0xB1, (IntPtr)start, (IntPtr)end); }
        public void MakeReadOnly() => SendMessage(_handle, 0xCF, (IntPtr)1, IntPtr.Zero);
        public void MaskInput() => SendMessage(_handle, 0xCC, (IntPtr)'*', IntPtr.Zero);
        public string Text
        {
            get
            {
                var buffer = new System.Text.StringBuilder(128);
                SendMessageText(_handle, 0xD, (IntPtr)buffer.Capacity, buffer);
                return buffer.ToString();
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string text, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string text);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern IntPtr SendMessageText(IntPtr hwnd, uint message, IntPtr wParam, System.Text.StringBuilder text);
    }
    private static void SendHotkey(ushort key)
    {
        static KeyboardInputSender.INPUT Input(ushort code, uint flags) => new() { Type = 1, Union = new KeyboardInputSender.InputUnion { Keyboard = new KeyboardInputSender.KEYBDINPUT { VirtualKey = code, Flags = flags } } };
        KeyboardInputSender.INPUT[] keys = [Input(0x11, 0), Input(0x12, 0), Input(0x10, 0), Input(key, 0), Input(key, 2), Input(0x10, 2), Input(0x12, 2), Input(0x11, 2)];
        if (KeyboardInputSender.SendInput((uint)keys.Length, keys, System.Runtime.InteropServices.Marshal.SizeOf<KeyboardInputSender.INPUT>()) != keys.Length) throw new Exception("Hotkey injection failed");
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public TaskCompletionSource? Gate, Received;
        public int RequestCount;
        public string? LastQuery;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool ForceTimeout;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref RequestCount);
            LastQuery = request.RequestUri?.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("q=", StringComparison.Ordinal)).Select(part => Uri.UnescapeDataString(part[2..])).FirstOrDefault();
            Received?.TrySetResult();
            if (Gate is not null) await Gate.Task.WaitAsync(token);
            if (ForceTimeout) throw new TaskCanceledException("Simulated provider timeout.");
            var body = request.RequestUri?.Host == "api.mymemory.translated.net"
                ? "{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"merhaba\"}}"
                : "[[[\"merhaba\",\"hello\"]],null,\"en\"]";
            return new HttpResponseMessage(Status) { Content = new StringContent(body) };
        }
    }
    private sealed class Context : IModuleContext, IModuleSettings, IHotkeyService, ILogger
    {
        public List<Action> Callbacks { get; } = [];
        public Dictionary<string, string> Values { get; } = [];
        public IHotkeyService? NativeHotkeys;
        public string? LastError;
        public IModuleSettings Settings => this;
        public IHotkeyService Hotkeys => this;
        public ILogger Log => this;
        public string Path => "";
        public void Notify(string message) => LastError = message;
        public void Info(string message) { }
        public void Error(string message, Exception? exception = null) => this.LastError = message + " " + exception;
        public bool GetBool(string key, bool fallback = false) => fallback;
        public int GetInt(string key, int fallback = 0) => fallback;
        public string GetString(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
        public void Set(string key, object? value) { }
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) { if (NativeHotkeys is not null) return NativeHotkeys.Register(modifiers, virtualKey, callback); Callbacks.Add(callback); return new Binding(); }
    }
    private sealed class Binding : IDisposable { public void Dispose() { } }

    private static int SelectionGuardCheck()
    {
        var windowA = new IntPtr(0x1001);
        var windowB = new IntPtr(0x2002);
        if (SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: true, overlayActive: false, windowA, windowA, movedOrDoubleClicked: true))
            throw new Exception("Suppressed drag must not trigger auto selection translation.");
        if (SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: true, windowA, windowA, movedOrDoubleClicked: true))
            throw new Exception("Active overlay must suppress auto selection translation.");
        if (SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: false, windowA, windowB, movedOrDoubleClicked: true))
            throw new Exception("Mouse drag ending in a different foreground window must not trigger auto selection translation.");
        if (SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: false, IntPtr.Zero, windowA, movedOrDoubleClicked: true) ||
            SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: false, windowA, IntPtr.Zero, movedOrDoubleClicked: true))
            throw new Exception("Missing window handle must not trigger auto selection translation.");
        if (SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: false, windowA, windowA, movedOrDoubleClicked: false))
            throw new Exception("Click without drag or double-click must not trigger auto selection translation.");
        if (!SelectionTranslationModule.ShouldTriggerAutoSelection(wasSuppressed: false, overlayActive: false, windowA, windowA, movedOrDoubleClicked: true))
            throw new Exception("Valid same-window drag should trigger auto selection translation.");
        Console.WriteLine("PASS: ShouldTriggerAutoSelection rejects overlay drags and cross-window focus changes.");

        if (!SelectionTranslationModule.SameReadableFocus(null, [1, 2], hasNativeFocus: true) ||
            !SelectionTranslationModule.SameReadableFocus([1, 2], null, hasNativeFocus: true) ||
            !SelectionTranslationModule.SameReadableFocus([1, 2], [1, 2], hasNativeFocus: false) ||
            SelectionTranslationModule.SameReadableFocus([1, 2], [1, 3], hasNativeFocus: true) ||
            SelectionTranslationModule.SameReadableFocus([1, 2], null, hasNativeFocus: false))
            throw new Exception("Copy must allow a transient accessibility identity only with stable native focus, and reject a confirmed field change.");
        Console.WriteLine("PASS: Selection capture tolerates transient accessibility identities and rejects a different focused field.");

        var enteredCount = 0;
        Action onEntered = () => enteredCount++;
        SelectionTranslationGuard.OverlayEntered += onEntered;
        try
        {
            if (SelectionTranslationGuard.IsOverlayActive) throw new Exception("Overlay guard should start inactive.");
            using (var outer = SelectionTranslationGuard.EnterOverlay())
            {
                if (!SelectionTranslationGuard.IsOverlayActive || enteredCount != 1)
                    throw new Exception("First overlay entry did not activate guard or fire OverlayEntered.");
                var inner = SelectionTranslationGuard.EnterOverlay();
                if (!SelectionTranslationGuard.IsOverlayActive || enteredCount != 1)
                    throw new Exception("Nested overlay entry should keep guard active without re-firing OverlayEntered.");
                inner.Dispose();
                inner.Dispose();
                if (!SelectionTranslationGuard.IsOverlayActive)
                    throw new Exception("Double-disposing inner overlay scope exited outer scope.");
            }
            if (SelectionTranslationGuard.IsOverlayActive)
                throw new Exception("Disposing outer overlay scope did not clear overlay guard.");
        }
        finally { SelectionTranslationGuard.OverlayEntered -= onEntered; }
        Console.WriteLine("PASS: SelectionTranslationGuard tracks nested overlay scopes idempotently.");

        var app = new Application();
        var window = new Window
        {
            Title = "WinForge selection guard test",
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Opacity = 0
        };
        var exit = 1;
        window.Loaded += async (_, _) =>
        {
            DataObject? clipboardBefore = null;
            try
            {
                clipboardBefore = SelectionTranslationModule.SnapshotClipboard();

                const string oldClipboard = "WinForge old clipboard sentinel";
                const string simulatedCtrlC = "WinForge ctrl-c text";
                const string regionCaptureText = "WinForge internal capture sentinel";
                await SetClipboardTextAsync(oldClipboard);

                var originChecks = 0;
                Func<bool> concurrentInternalWriteOrigin = () =>
                {
                    if (++originChecks == 2)
                    {
                        SelectionTranslationGuard.NotifyInternalClipboardWrite();
                        SetClipboardTextSync(regionCaptureText);
                    }
                    return true;
                };

                try
                {
                    await SelectionTranslationModule.CopySelectionWithAction(
                        CancellationToken.None,
                        concurrentInternalWriteOrigin,
                        () => SetClipboardTextSync(simulatedCtrlC));
                    throw new Exception("CopySelection accepted an internal clipboard write.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("Pano değişti")) { }

                if (Clipboard.GetText() != regionCaptureText)
                    throw new Exception($"CopySelection overwrote internal clipboard write with '{Clipboard.GetText()}'.");

                await SetClipboardTextAsync(oldClipboard);
                var delayedWriteTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                delayedWriteTimer.Tick += (_, _) =>
                {
                    delayedWriteTimer.Stop();
                    SelectionTranslationGuard.NotifyInternalClipboardWrite();
                    SetClipboardTextSync(regionCaptureText);
                };
                delayedWriteTimer.Start();
                try
                {
                    await SelectionTranslationModule.CopySelectionWithAction(
                        CancellationToken.None,
                        () => true,
                        () => { });
                    throw new Exception("CopySelection accepted a delayed internal clipboard write.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("Pano değişti")) { }
                finally { delayedWriteTimer.Stop(); }

                if (Clipboard.GetText() != regionCaptureText)
                    throw new Exception($"CopySelection overwrote delayed internal clipboard write with '{Clipboard.GetText()}'.");

                Console.WriteLine("PASS: CopySelection does not restore old clipboard over a concurrent internal clipboard write.");
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally
            {
                try
                {
                    await RestoreClipboardAsync(clipboardBefore);
                }
                catch (Exception error) { Console.Error.WriteLine(error); exit = 1; }
                finally { window.Close(); }
            }
        };
        app.Run(window);
        return exit;

        static async Task SetClipboardTextAsync(string text)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { Clipboard.SetText(text); return; }
                catch (System.Runtime.InteropServices.ExternalException) { await Task.Delay(40); }
            }
            throw new Exception("Could not initialize test clipboard.");
        }

        static void SetClipboardTextSync(string text)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { Clipboard.SetText(text); return; }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(25); }
            }
            throw new Exception("Could not write simulated internal clipboard value.");
        }

        static async Task RestoreClipboardAsync(DataObject? saved)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (saved is null || saved.GetFormats(false).Length == 0) Clipboard.Clear();
                    else Clipboard.SetDataObject(saved, true);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException) { await Task.Delay(50); }
            }
            throw new Exception("Could not restore original clipboard.");
        }
    }
}

