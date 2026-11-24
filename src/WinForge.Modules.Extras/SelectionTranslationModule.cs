using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Previews selected text and copies or pastes a translation using a shortcut.</summary>
public sealed class SelectionTranslationModule : ITweakModule
{
    private const string ShortcutSource = "tr";
    private const string DefaultShortcutTarget = "en";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly string[] TargetLanguages = ["tr", "en", "de", "fr", "es", "it", "pt", "pt-BR", "ru", "uk", "ar", "fa", "zh-CN", "zh-TW", "ja", "ko", "hi", "nl", "pl", "el", "sv", "da", "no", "fi"];
    private static readonly IReadOnlyDictionary<string, string> LanguageLabels = new Dictionary<string, string> {
        ["auto"] = "Otomatik algıla", ["tr"] = "Türkçe", ["en"] = "İngilizce", ["de"] = "Almanca", ["fr"] = "Fransızca",
        ["es"] = "İspanyolca", ["it"] = "İtalyanca", ["pt"] = "Portekizce", ["pt-BR"] = "Portekizce (Brezilya)",
        ["ru"] = "Rusça", ["uk"] = "Ukraynaca", ["ar"] = "Arapça", ["fa"] = "Farsça", ["zh-CN"] = "Çince (Basitleştirilmiş)",
        ["zh-TW"] = "Çince (Geleneksel)", ["ja"] = "Japonca", ["ko"] = "Korece", ["hi"] = "Hintçe", ["nl"] = "Felemenkçe",
        ["pl"] = "Lehçe", ["el"] = "Yunanca", ["sv"] = "İsveççe", ["da"] = "Danca", ["no"] = "Norveççe", ["fi"] = "Fince"
    };
    private readonly HttpClient _client;
    private IModuleContext _context = null!;
    private IDisposable? _shortcutBinding;
    private CancellationTokenSource? _operation;
    private Task _translation = Task.CompletedTask;
    private bool _fromShortcut;
    private Window? _resultWindow;
    private System.Windows.Threading.DispatcherTimer? _selectionTimer;
    private bool _mouseDown;
    private CursorPoint _mouseStart;
    private long _lastClick;
    private bool _doubleClick;

    public SelectionTranslationModule() : this(SharedClient) { }
    internal SelectionTranslationModule(HttpClient client) => _client = client;
    public string Id => "selection-translation";
    public string Name => "Seçili Metni Çevir";
    public string Category => "Verimlilik";
    public string Description => "Seçili metni önizler. Kısayol Türkçe seçimin çevirisini panoya kopyalar ve düzenlenebilir alandaki seçimin yerine yapıştırır.";
    public string? Warning => "Seçilen metin seçtiğiniz çeviri hizmetine gönderilir. Varsayılan Google bağlantısı resmî API değildir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } = [
        new() { Key = "source", Label = "Önizleme kaynak dili", Kind = SettingKind.Text, Default = "auto", Options = ["auto", .. TargetLanguages], OptionLabels = LanguageLabels, Description = "Bir dil seçin veya desteklenen başka bir dil kodu yazın. auto kaynak dili algılar." },
        new() { Key = "target", Label = "Önizleme hedef dili", Kind = SettingKind.Text, Default = "tr", Options = TargetLanguages, OptionLabels = LanguageLabels, Description = "Bir dil seçin veya desteklenen başka bir dil kodu yazın." },
        new() { Key = "provider", Label = "Çeviri sağlayıcısı", Kind = SettingKind.Choice, Default = "google", Options = ["google", "libretranslate", "mymemory"], OptionLabels = new Dictionary<string, string> { ["google"] = "Google (resmî olmayan)", ["libretranslate"] = "LibreTranslate", ["mymemory"] = "MyMemory" }, Description = "MyMemory kota uygular; otomatik dil algılama belgelenmemiştir ve değişebilir. Çeviri kalitesi değişebilir." },
        new() { Key = "libre-endpoint", Label = "LibreTranslate sunucu adresi", Kind = SettingKind.Text, Default = "http://localhost:5000", Description = "LibreTranslate seçildiğinde kullanılır. API anahtarı gerekiyorsa WINFORGE_LIBRETRANSLATE_API_KEY ortam değişkenini ayarlayın." },
        new() { Key = "translation-target", Label = "Türkçe seçimlerin hedef dili", Kind = SettingKind.Text, Default = DefaultShortcutTarget, Options = TargetLanguages.Where(language => language != ShortcutSource).ToArray(), OptionLabels = LanguageLabels, Description = "Kısayol Türkçe metni bu dile çevirir." },
        new() { Key = "replace-shortcut", Label = "Çeviriyi kopyalama ve yapıştırma kısayolu", Kind = SettingKind.Text, Default = "Ctrl+Alt+Y", Description = "Çeviri panoya kopyalanır. Düzenlenebilir bir alanda seçimin yerine de yapıştırılır." }
    ];
    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() { IsActive = true; try { Bind(); StartSelectionMonitor(); } catch { Deactivate(); throw; } }
    public void Deactivate() { IsActive = false; _selectionTimer?.Stop(); _selectionTimer = null; _operation?.Cancel(); _shortcutBinding?.Dispose(); _shortcutBinding = null; _resultWindow?.Close(); _resultWindow = null; }
    public void Apply() { if (!IsActive) return; _operation?.Cancel(); _shortcutBinding?.Dispose(); _shortcutBinding = null; Bind(); }
    private void Bind()
    {
        var shortcut = AutomationCommands.ParseShortcut(_context.Settings.GetString("replace-shortcut", "Ctrl+Alt+Y"));
        var source = SelectionTranslationService.Language(_context.Settings.GetString("source", "auto"), true);
        var target = SelectionTranslationService.Language(_context.Settings.GetString("target", "tr"));
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Kaynak ve hedef dil farklı olmalı.");
        var shortcutTarget = SelectionTranslationService.Language(_context.Settings.GetString("translation-target", DefaultShortcutTarget));
        if (ShortcutSource.Equals(shortcutTarget, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Kısayol hedef dili Türkçe dışında bir dil olmalı.");
        var provider = _context.Settings.GetString("provider", "google");
        if (provider is not ("google" or "libretranslate" or "mymemory")) throw new FormatException("Bilinmeyen çeviri sağlayıcısı.");
        if (provider == "libretranslate") SelectionTranslationService.LibreEndpoint(_context.Settings.GetString("libre-endpoint", "http://localhost:5000"));
        if (IsClipboardShortcut(shortcut)) throw new FormatException("Ctrl+C ve Ctrl+V kısayol olarak atanamaz.");
        _shortcutBinding = _context.Hotkeys.Register(shortcut.Modifiers, (uint)shortcut.Key, () => RequestTranslation(fromShortcut: true));
    }
    private static bool IsClipboardShortcut(AutomationShortcut key) => key.Modifiers == HotkeyModifiers.Control && key.Key is 0x43 or 0x56;
    private void StartSelectionMonitor()
    {
        _selectionTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _selectionTimer.Tick += (_, _) =>
        {
            var down = (GetAsyncKeyState(1) & 0x8000) != 0;
            GetCursorPos(out var point);
            if (down && !_mouseDown)
            {
                _mouseStart = point;
                var now = Environment.TickCount64;
                _doubleClick = now - _lastClick < 500;
                _lastClick = now;
                if (_resultWindow is not null)
                {
                    var source = System.Windows.PresentationSource.FromVisual(_resultWindow)?.CompositionTarget;
                    var pos = source?.TransformFromDevice.Transform(new Point(point.X, point.Y)) ?? new Point(point.X, point.Y);
                    if (pos.X < _resultWindow.Left || pos.X >= _resultWindow.Left + _resultWindow.ActualWidth || pos.Y < _resultWindow.Top || pos.Y >= _resultWindow.Top + _resultWindow.ActualHeight)
                    { _resultWindow.Close(); if (!_fromShortcut) _operation?.Cancel(); }
                }
                else if (!_fromShortcut) _operation?.Cancel();
            }
            if (!down && _mouseDown && (Math.Abs(point.X - _mouseStart.X) > 5 || Math.Abs(point.Y - _mouseStart.Y) > 5 || _doubleClick))
                RequestTranslation(fromShortcut: false);
            _mouseDown = down;
        };
        _selectionTimer.Start();
    }
    private void RequestTranslation(bool fromShortcut)
    {
        if (!IsActive || (_operation is not null && (!fromShortcut || _fromShortcut))) return;
        _operation?.Cancel();
        _fromShortcut = fromShortcut;
        var cts = new CancellationTokenSource();
        _operation = cts;
        // Let a canceled preview finish restoring its temporary clipboard data first.
        _translation = TranslateAsync(fromShortcut, GetForegroundWindow(), _translation, cts);
    }

    private async Task TranslateAsync(bool fromShortcut, IntPtr window, Task previous, CancellationTokenSource cts)
    {
        using var operation = cts;
        try
        {
            var focusWindow = FocusedTextOutput.FocusedWindow(window);
            var focused = AutomationElement.FocusedElement;
            bool SameOrigin() => window != IntPtr.Zero && GetForegroundWindow() == window &&
                FocusedTextOutput.FocusedWindow(window) == focusWindow && SameFocus(focused);
            await previous;
            cts.Token.ThrowIfCancellationRequested();
            var source = fromShortcut ? ShortcutSource : _context.Settings.GetString("source", "auto");
            var target = fromShortcut ? _context.Settings.GetString("translation-target", DefaultShortcutTarget) : _context.Settings.GetString("target", "tr");
            var provider = _context.Settings.GetString("provider", "google");
            await WaitForModifiers(cts.Token);
            if (!SameOrigin()) throw new InvalidOperationException("Etkin pencere veya odak değişti.");
            if (focused?.Current.IsPassword == true || IsNativePassword(window)) throw new InvalidOperationException("Parola alanları çevrilmez.");
            var resultAnchor = fromShortcut ? default : SelectionAnchor(focused);
            var selection = fromShortcut ? ReadTextSelection(focused) : null;
            var editableRange = selection is not null && IsEditable(focused!, selection) ? selection.Clone() : null;
            var nativeRange = fromShortcut && selection is null && focused is not null ? ReadNativeEditorSelection(focused) : null;

            // Ask for one UTF-16 unit beyond the byte limit. The service can then
            // reject an oversized selection without materializing it in full.
            var selected = selection is not null ? selection.GetText(SelectionTranslationService.MaxInputBytes + 1)
                : await CopySelection(cts.Token, SameOrigin);
            if (string.IsNullOrWhiteSpace(selected)) throw new InvalidOperationException("Seçili metin kopyalanamadı.");
            bool SameTarget() => SameOrigin() && !IsNativePassword(window) && (nativeRange is not null
                ? ReadNativeEditorSelection(focused!) == nativeRange
                : editableRange is not null && SameSelection(focused, editableRange, selected));
            var canPaste = fromShortcut && SameTarget();
            var endpoint = _context.Settings.GetString("libre-endpoint", "http://localhost:5000");
            var key = provider == "libretranslate" ? Environment.GetEnvironmentVariable("WINFORGE_LIBRETRANSLATE_API_KEY") ?? "" : "";
            var translated = await new SelectionTranslationService(_client).TranslateAsync(selected, source, target, cts.Token, provider, endpoint, key);
            cts.Token.ThrowIfCancellationRequested();
            if (!fromShortcut)
            {
                ShowResult(translated, resultAnchor);
                return;
            }

            // Native offsets alone cannot detect edits that leave the range unchanged.
            if (canPaste && nativeRange is not null)
            {
                try
                {
                    await WaitForModifiers(cts.Token);
                    canPaste = SameTarget() && await CopySelection(cts.Token, SameTarget) == selected;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ExternalException) { canPaste = false; }
            }

            cts.Token.ThrowIfCancellationRequested();
            Clipboard.SetText(translated);
            var sequence = GetClipboardSequenceNumber();
            if (!canPaste || !SameTarget()) return;
            await WaitForModifiers(cts.Token);
            if (GetClipboardSequenceNumber() != sequence || Clipboard.GetText() != translated || !SameTarget()) return;
            if (GetClipboardSequenceNumber() == sequence && !ModifiersDown() && GetForegroundWindow() == window)
                Chord(0x56);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (OperationCanceledException ex)
        {
            _context.Log.Error("Çeviri hizmeti zaman aşımına uğradı.", ex);
            _context.Notify("Çeviri hizmeti zaman aşımına uğradı; bağlantıyı kontrol edip tekrar deneyin.");
        }
        catch (Exception ex) { if (fromShortcut) { _context.Log.Error("Seçili metin çevirisi başarısız.", ex); _context.Notify("Çeviri: " + ex.Message); } }
        finally { if (_operation == cts) _operation = null; }
    }
    private sealed record NativeEditorSelection(IntPtr Handle, long Start, long End);
    private static NativeEditorSelection? ReadNativeEditorSelection(AutomationElement element)
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.Focus == IntPtr.Zero ||
            element.Current.NativeWindowHandle != info.Focus.ToInt64() || !IsWindowEnabled(info.Focus)) return null;
        var name = new System.Text.StringBuilder(128);
        if (GetClassName(info.Focus, name, name.Capacity) == 0) return null;
        var className = name.ToString();
        if (className.Equals("Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            // Scintilla messages here carry only integer arguments/results;
            // never pass local pointers into another process. Reject multiple,
            // rectangular and virtual-space selections to avoid multi-paste.
            if (!TryScintilla(info.Focus, 2140 /* SCI_GETREADONLY */, out var readOnly) || readOnly != 0 ||
                !TryScintilla(info.Focus, 2570 /* SCI_GETSELECTIONS */, out var count) || count != 1 ||
                !TryScintilla(info.Focus, 2423 /* SCI_GETSELECTIONMODE */, out var mode) || mode != 0 ||
                !TryScintilla(info.Focus, 2581 /* SCI_GETSELECTIONNCARETVIRTUALSPACE */, out var caretSpace) || caretSpace != 0 ||
                !TryScintilla(info.Focus, 2583 /* SCI_GETSELECTIONNANCHORVIRTUALSPACE */, out var anchorSpace) || anchorSpace != 0 ||
                !TryScintilla(info.Focus, 2143 /* SCI_GETSELECTIONSTART */, out var startPosition) ||
                !TryScintilla(info.Focus, 2145 /* SCI_GETSELECTIONEND */, out var endPosition) ||
                endPosition <= startPosition || endPosition - startPosition > SelectionTranslationService.MaxInputBytes) return null;
            return new NativeEditorSelection(info.Focus, startPosition, endPosition);
        }
        if (!(className.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase) || className.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase))) return null;
        var style = unchecked((uint)GetWindowLong(info.Focus, -16));
        if (style == 0 || (style & (0x20u | 0x800u)) != 0 ||
            SendMessageTimeout(info.Focus, 0xD2, IntPtr.Zero, IntPtr.Zero, 3, 200, out var passwordChar) == IntPtr.Zero || passwordChar != UIntPtr.Zero ||
            SendMessageSelection(info.Focus, 0xB0 /* EM_GETSEL */, out var start, out var end, 3, 200, out _) == IntPtr.Zero) return null;
        // EM_GETSEL's pointer outputs carry full 32-bit offsets, unlike its
        // packed return value, which cannot represent positions beyond 65535.
        if (end <= start || end > int.MaxValue || end - start > SelectionTranslationService.MaxInputBytes) return null;
        return new NativeEditorSelection(info.Focus, start, end);
    }
    private static bool TryScintilla(IntPtr hwnd, uint message, out long value)
    {
        value = 0;
        if (SendMessageTimeout(hwnd, message, IntPtr.Zero, IntPtr.Zero, 3, 200, out var result) == IntPtr.Zero || result.ToUInt64() > long.MaxValue) return false;
        value = (long)result.ToUInt64();
        return true;
    }
    private static bool IsNativePassword(IntPtr window)
    {
        var handle = FocusedTextOutput.FocusedWindow(window);
        var name = new System.Text.StringBuilder(32);
        if (handle == IntPtr.Zero || GetClassName(handle, name, name.Capacity) == 0 ||
            name.ToString().ToUpperInvariant() is not ("EDIT" or "RICHEDIT" or "RICHEDIT20A" or "RICHEDIT20W" or "RICHEDIT50W")) return false;
        const uint passwordStyle = 0x20;
        return (unchecked((uint)GetWindowLong(handle, -16)) & passwordStyle) != 0 ||
            SendMessageTimeout(handle, 0xD2 /* EM_GETPASSWORDCHAR */, IntPtr.Zero, IntPtr.Zero, 3, 200, out var passwordChar) == IntPtr.Zero ||
            passwordChar != UIntPtr.Zero;
    }
    private static TextPatternRange? ReadTextSelection(AutomationElement? element)
    {
        if (element is null || !element.TryGetCurrentPattern(TextPattern.Pattern, out var text)) return null;
        var ranges = ((TextPattern)text).GetSelection();
        return ranges.Length == 1 && !string.IsNullOrEmpty(ranges[0].GetText(1)) ? ranges[0] : null;
    }
    private static bool IsEditable(AutomationElement element, TextPatternRange selection)
    {
        if (!element.Current.IsEnabled || element.Current.IsPassword) return false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            return !((ValuePattern)value).Current.IsReadOnly;

        // WPF RichTextBox exposes TextPattern and its range-level read-only
        // attribute, but not ValuePattern. Avoid accepting arbitrary document
        // providers, where an unspecified attribute can default to false.
        if (element.Current.ClassName == "RichTextBox")
            return selection.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is bool readOnly && !readOnly;

        // The built-in Win32 EDIT provider exposes TextPattern but may not
        // expose ValuePattern (notably multiline EDIT). Restrict the fallback
        // to the actual focused HWND and the built-in class; arbitrary custom
        // controls cannot establish editability merely by exposing text.
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info) || info.Focus == IntPtr.Zero ||
            element.Current.NativeWindowHandle != info.Focus.ToInt64() || !IsWindowEnabled(info.Focus)) return false;
        var name = new System.Text.StringBuilder(128);
        if (GetClassName(info.Focus, name, name.Capacity) == 0 || !name.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase)) return false;
        var style = unchecked((uint)GetWindowLong(info.Focus, -16));
        if (style == 0 || (style & (0x20u /* ES_PASSWORD */ | 0x800u /* ES_READONLY */)) != 0) return false;
        // Some edit controls set password masking dynamically rather than at
        // creation. Fail closed when the control is hung or the query fails.
        return SendMessageTimeout(info.Focus, 0xD2 /* EM_GETPASSWORDCHAR */, IntPtr.Zero, IntPtr.Zero, 3, 200, out var passwordChar) != IntPtr.Zero && passwordChar == UIntPtr.Zero;
    }
    private static bool SameSelection(AutomationElement? original, TextPatternRange? range, string expectedText)
    {
        try
        {
            var current = AutomationElement.FocusedElement;
            if (original is null || current is null || range is null || !Automation.Compare(original, current)) return false;
            var now = ReadTextSelection(current);
            return now is not null && IsEditable(current, now) && now.GetText(SelectionTranslationService.MaxInputBytes + 1) == expectedText &&
                   range.CompareEndpoints(TextPatternRangeEndpoint.Start, now, TextPatternRangeEndpoint.Start) == 0 &&
                   range.CompareEndpoints(TextPatternRangeEndpoint.End, now, TextPatternRangeEndpoint.End) == 0;
        }
        catch { return false; }
    }
    private static bool SameFocus(AutomationElement? original)
    {
        if (original is null) return true;
        try
        {
            var current = AutomationElement.FocusedElement;
            return current is not null && Automation.Compare(original, current);
        }
        catch { return false; }
    }
    private static async Task WaitForModifiers(CancellationToken token)
    {
        for (var i = 0; i < 100; i++) { token.ThrowIfCancellationRequested(); if (!ModifiersDown()) return; await Task.Delay(20, token); }
        throw new InvalidOperationException("Kısayol tuşlarını bırakın.");
    }
    private static bool ModifiersDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
    private static async Task<string?> CopySelection(CancellationToken token, Func<bool> sameOrigin)
    {
        var saved = CaptureClipboard(out var before);
        token.ThrowIfCancellationRequested();
        if (!sameOrigin()) throw new InvalidOperationException("Pencere veya odak değişti; metin alınmadı.");
        if (ModifiersDown()) throw new InvalidOperationException("Kopyalama için değiştirici tuşları bırakın.");
        if (GetClipboardSequenceNumber() != before) throw new InvalidOperationException("Pano değişti; metin alınmadı.");
        Chord(0x43);
        uint captured = before;
        try
        {
            // Once Ctrl+C is sent, wait briefly even on cancellation so its
            // delayed clipboard update can still be restored safely.
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(20);
                captured = GetClipboardSequenceNumber();
                if (captured == before) continue;
                token.ThrowIfCancellationRequested();
                if (!sameOrigin()) throw new InvalidOperationException("Pencere veya odak değişti; metin alınmadı.");
                if (IsCopiedEmptyVsCodeSelection()) throw new InvalidOperationException("Metin seçilmedi; editörün otomatik kopyaladığı satır çevrilmedi.");
                var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                if (GetClipboardSequenceNumber() != captured) throw new InvalidOperationException("Pano değişti; metin alınmadı.");
                return text;
            }
            token.ThrowIfCancellationRequested();
            return null;
        }
        finally { if (captured != before && sameOrigin() && GetClipboardSequenceNumber() == captured) RestoreClipboard(saved); }
    }
    private static bool IsCopiedEmptyVsCodeSelection()
    {
        var data = Clipboard.GetDataObject();
        if (data is null || !data.GetDataPresent(ChromiumClipboardData.Format, false)) return false;
        var value = data.GetData(ChromiumClipboardData.Format, false);
        if (value is byte[] bytes) return ChromiumClipboardData.IsVsCodeEmptySelection(bytes);
        if (value is not System.IO.Stream stream || !stream.CanSeek || stream.Length > ChromiumClipboardData.MaximumBytes) return false;
        stream.Position = 0;
        using var copy = new System.IO.MemoryStream((int)stream.Length);
        stream.CopyTo(copy);
        return ChromiumClipboardData.IsVsCodeEmptySelection(copy.ToArray());
    }
    internal static DataObject? SnapshotClipboard() => CaptureClipboard(out _);
    private static DataObject? CaptureClipboard(out uint sequence)
    {
        sequence = GetClipboardSequenceNumber();
        var current = Clipboard.GetDataObject();
        if (current is null)
        {
            if (GetClipboardSequenceNumber() != sequence) throw new InvalidOperationException("Pano değişti; yeniden deneyin.");
            return null;
        }
        var snapshot = new DataObject();
        foreach (var format in current.GetFormats(false))
        {
            // Windows advertises marker formats (for example
            // EnterpriseDataProtectionId) that have no managed payload.
            var value = current.GetData(format, false);
            if (value is null) continue;
            if (value is System.IO.Stream stream)
            {
                var copy = new System.IO.MemoryStream();
                if (stream.CanSeek) stream.Position = 0;
                stream.CopyTo(copy);
                copy.Position = 0;
                value = copy;
            }
            snapshot.SetData(format, value, false);
        }
        if (GetClipboardSequenceNumber() != sequence) throw new InvalidOperationException("Pano değişti; yeniden deneyin.");
        return snapshot;
    }
    private static void RestoreClipboard(DataObject? saved)
    {
        if (saved is null || saved.GetFormats(false).Length == 0) Clipboard.Clear();
        else Clipboard.SetDataObject(saved, true);
    }
    private static void Chord(ushort key)
    {
        static KeyboardInputSender.INPUT Input(ushort code, uint flags) => new() { Type = 1, Union = new KeyboardInputSender.InputUnion { Keyboard = new KeyboardInputSender.KEYBDINPUT { VirtualKey = code, Flags = flags } } };
        KeyboardInputSender.INPUT[] inputs = [Input(0x11, 0), Input(key, 0), Input(key, 2), Input(0x11, 2)];
        if (KeyboardInputSender.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyboardInputSender.INPUT>()) == inputs.Length) return;
        KeyboardInputSender.INPUT[] release = [Input(key, 2), Input(0x11, 2)];
        KeyboardInputSender.SendInput((uint)release.Length, release, Marshal.SizeOf<KeyboardInputSender.INPUT>());
        throw new InvalidOperationException("Kopyalama veya yapıştırma tuşları gönderilemedi.");
    }
    private static Point SelectionAnchor(AutomationElement? focused)
    {
        try
        {
            var element = focused;
            for (var i = 0; element is not null && i < 8; i++, element = TreeWalker.ControlViewWalker.GetParent(element))
            {
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) continue;
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges.Length == 0) continue;
                var rectangles = ranges[0].GetBoundingRectangles();
                if (rectangles.Length > 0 && !rectangles[0].IsEmpty)
                    return new Point(rectangles[0].Left, rectangles[0].Top);
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        GetCursorPos(out var cursor);
        return new Point(cursor.X, cursor.Y);
    }

    private void ShowResult(string text, Point anchor)
    {
        _resultWindow?.Close();
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 120, Foreground = System.Windows.Media.Brushes.White };
        var button = new Button { Content = label, Padding = new Thickness(12, 8, 12, 8), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)), BorderThickness = new Thickness(0), ToolTip = "Çevirinin tamamını kopyala", Cursor = System.Windows.Input.Cursors.Hand };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, button.Padding);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var window = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, SizeToContent = SizeToContent.WidthAndHeight, MaxWidth = 360, Background = System.Windows.Media.Brushes.Transparent, AllowsTransparency = true, Content = button };
        button.Click += (_, _) => { try { Clipboard.SetText(text); window.Close(); } catch (System.Runtime.InteropServices.ExternalException) { _context.Notify("Çeviri panoya kopyalanamadı."); } };
        _resultWindow = window;
        window.Closed += (_, _) => { if (_resultWindow == window) _resultWindow = null; };
        window.Show();
        var transform = System.Windows.PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var point = transform.Transform(anchor);
        window.Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(point.X, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - window.ActualWidth));
        window.Top = Math.Max(SystemParameters.VirtualScreenTop, point.Y - window.ActualHeight - 6);
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageSelection(IntPtr hwnd, uint message, out uint start, out uint end, uint flags, uint timeout, out UIntPtr result);
}
