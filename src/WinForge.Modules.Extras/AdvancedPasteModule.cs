using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class AdvancedPasteModule : ITweakModule
{
    private const byte VkV = 0x56;
    private const int PreviewCharacterLimit = 100_000;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private bool _opening;
    private AdvancedPasteWindow? _window;
    private CancellationTokenSource? _pendingPaste;
    private readonly SettingDescriptor[] _settings;

    public AdvancedPasteModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Gelişmiş Yapıştır penceresini aç",
                Kind = SettingKind.Action,
                Description = "Panodaki metni dönüştürün; görsellere yerel OCR uygulayın veya PNG/JPEG/BMP/GIF biçimine dönüştürün.",
                Execute = Open
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Shift+V kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true,
                Description = "Dönüştürme önizleme penceresini açar. Dönüşümler cihazda yerel olarak çalışır."
            }
        ];
    }

    public string Id => "advanced-paste";
    public string Name => "Gelişmiş Yapıştır";
    public string Category => "Sistem";
    public string Description => "Panodaki metin ve görselleri düz metin, Markdown, JSON, yerel OCR veya PNG/JPEG/BMP/GIF dosyalarına dönüştürüp yapıştırır.";
    public string? Warning => "OCR ve PNG/JPEG/BMP/GIF görsel dönüşümleri cihazda yerel olarak çalışır. JPEG dönüşümünde saydam alanlar beyazla birleştirilir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        try
        {
            RegisterHotkey();
        }
        catch
        {
            IsActive = false;
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        CancelPendingPaste();
        _window?.Close();
        _window = null;
        _hotkey?.Dispose();
        _hotkey = null;
    }

    public void Apply()
    {
        if (!IsActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;
        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Shift, VkV, Open);
    }

    private void Open()
    {
        if (_opening || !IsActive) return;
        var target = Native.GetForegroundWindow();
        CancelPendingPaste();
        _opening = true;
        try
        {
            var snapshot = ReadClipboard();
            if (snapshot is null)
            {
                _context?.Notify("Panoda dönüştürülebilecek metin veya HTML yok.");
                return;
            }

            Action? paste = null;
            _window = new AdvancedPasteWindow(snapshot, RecognizeImageAsync, CopyToClipboard,
                (action, contents, result) => paste = () => _ = PasteIntoTargetAsync(target, action, contents, result));
            if (_window.ShowDialog() == true && IsActive) paste?.Invoke();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Gelişmiş Yapıştır açılamadı.", exception);
            _context?.Notify("Gelişmiş Yapıştır açılamadı: " + exception.Message);
        }
        finally
        {
            _opening = false;
            _window = null;
        }
    }

    internal static ClipboardTextSnapshot? ReadClipboard()
    {
        var data = Clipboard.GetDataObject();
        if (data is null) return null;

        var text = data.GetDataPresent(DataFormats.UnicodeText)
            ? data.GetData(DataFormats.UnicodeText) as string ?? ""
            : data.GetDataPresent(DataFormats.Text) ? data.GetData(DataFormats.Text) as string ?? "" : "";
        var html = data.GetDataPresent(DataFormats.Html) ? data.GetData(DataFormats.Html) as string : null;
        BitmapSource? image = null;
        if (data.GetDataPresent(DataFormats.Bitmap))
        {
            image = Clipboard.GetImage();
            image?.Freeze();
        }
        if (text.Length > PreviewCharacterLimit) text = text[..PreviewCharacterLimit];
        if (html is { Length: > PreviewCharacterLimit }) html = html[..PreviewCharacterLimit];
        if (text.Length == 0 && string.IsNullOrWhiteSpace(html) && image is null) return null;
        if (text.Length == 0 && html is not null) text = AdvancedPasteTransforms.HtmlToText(html);
        return new ClipboardTextSnapshot(text, html, image);
    }

    private static Task<string> RecognizeImageAsync(BitmapSource image) => AdvancedPasteOcr.RecognizeAsync(image);

    private void CopyToClipboard(AdvancedPasteAction action, ClipboardTextSnapshot snapshot, string result)
    {
        try
        {
            SetClipboard(action, snapshot, result);
            _context?.Notify(AdvancedPasteArtifacts.IsFileAction(action)
                ? "Dosya panoya kopyalandı"
                : action == AdvancedPasteAction.Json ? "JSON panoya kopyalandı" : "Dönüştürülmüş metin panoya kopyalandı");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Dönüştürülmüş metin panoya kopyalanamadı.", exception);
            _context?.Notify("Panoya yazılamadı: " + exception.Message);
        }
    }

    private async Task PasteIntoTargetAsync(IntPtr target, AdvancedPasteAction action, ClipboardTextSnapshot snapshot, string result)
    {
        if (!IsActive || !Native.IsWindow(target) ||
            (Native.GetForegroundWindow() != target && !SetForegroundWindow(target)))
        {
            _context?.Notify("Yapıştırılacak pencere etkinleştirilemedi.");
            return;
        }
        CancelPendingPaste();
        var cancellation = new CancellationTokenSource();
        _pendingPaste = cancellation;
        try
        {
            var completed = await PasteOperation.ExecuteAsync(target, () =>
            {
                SetClipboard(action, snapshot, result);
                KeyboardInputSender.PasteShortcut();
            }, cancellation.Token);
            if (!completed)
            {
                _context?.Notify("Yapıştırma iptal edildi.");
                return;
            }
            _context?.Notify(action switch
            {
                AdvancedPasteAction.Markdown => "Markdown yapıştırılıyor",
                AdvancedPasteAction.Json => "JSON yapıştırılıyor",
                AdvancedPasteAction.ImageToText => "Görselden çıkarılan metin yapıştırılıyor",
                _ when AdvancedPasteArtifacts.IsFileAction(action) => "Dosya yapıştırılıyor",
                _ => "Dönüştürülmüş metin yapıştırılıyor"
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _context?.Log.Error("Dönüştürülmüş metin yapıştırılamadı.", exception);
            _context?.Notify("Panoya yazılamadı: " + exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_pendingPaste, cancellation)) _pendingPaste = null;
            cancellation.Dispose();
        }
    }

    private static void SetClipboard(AdvancedPasteAction action, ClipboardTextSnapshot snapshot, string result)
    {
        if (AdvancedPasteArtifacts.IsFileAction(action))
        {
            var file = AdvancedPasteArtifacts.CreateFile(action, snapshot, result);
            Clipboard.SetFileDropList(AdvancedPasteArtifacts.CreateFileDropList(file));
            return;
        }

        if (string.IsNullOrEmpty(result)) throw new InvalidOperationException("Yapıştırılacak içerik boş.");
        Clipboard.SetText(result, TextDataFormat.UnicodeText);
    }

    private void CancelPendingPaste()
    {
        var pending = Interlocked.Exchange(ref _pendingPaste, null);
        pending?.Cancel();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
