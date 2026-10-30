using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public sealed class TextGrabberModule : ITweakModule
{
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private bool _busy;

    public string Id => "text-grabber";
    public string Name => "Ekrandan Metin Al";
    public string Category => "Verimlilik";
    public string Description => "Alt+Q ile ekranda bir bölge seç, içindeki yazıyı OCR ile doğrudan panoya kopyala.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Alt+Q kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        RegisterHotkey();
    }

    public void Deactivate()
    {
        IsActive = false;
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

        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Alt, 0x51 /* Q */, () => _ = GrabAsync());
        context.Log.Info("Ekrandan Metin Al kısayolu kaydedildi (Alt+Q).");
    }

    private async Task GrabAsync()
    {
        var context = _context;
        if (context is null || _busy) return;

        _busy = true;
        try
        {
            var overlay = new SelectionOverlay((_context as IFeatureUiContext)?.FeatureUi);
            overlay.ShowDialog();

            if (overlay.Result is not { } rect) return;

            var (pixels, width, height) = ScreenCapture.Capture(
                (int)Math.Round(rect.X),
                (int)Math.Round(rect.Y),
                (int)Math.Round(rect.Width),
                (int)Math.Round(rect.Height));

            var text = await RecognizeAsync(pixels, width, height).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(text))
            {
                context.Notify("Metin bulunamadı");
                return;
            }

            if (!TryCopyToClipboard(text))
            {
                context.Notify("Panoya kopyalanamadı");
                return;
            }

            context.Notify(Preview(text));
            context.Log.Info($"OCR: {width}x{height} bölge, {text.Length} karakter.");
        }
        catch (Exception ex)
        {
            context.Log.Error("Metin yakalama başarısız.", ex);
            context.Notify("Metin alınamadı: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private static string Preview(string text)
    {
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        return flat.Length <= 160 ? flat : flat[..160].TrimEnd() + "…";
    }

    private static Task<string> RecognizeAsync(byte[] bgra, int width, int height) =>
        OcrRecognitionService.RecognizeAsync(BitmapSource.Create(width, height, 96, 96,
            PixelFormats.Bgra32, null, bgra, checked(width * 4)));

    private static bool TryCopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }
}
