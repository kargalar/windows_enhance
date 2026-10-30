using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class PeekModule : ITweakModule, IAppCommandModule
{
    private const uint VkP = 0x50;
    private readonly ExplorerFileVerb _explorerVerb = new("WinForge.PeekPreview", "WinForge ile önizle", "--peek \"%1\"");
    private readonly SettingDescriptor[] _settings;
    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private bool _isActive;
    private bool _opening;

    public PeekModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Dosya önizle",
                Kind = SettingKind.Action,
                Description = "Metin ve görselleri açmadan önizleyin.",
                Execute = OpenFile
            },
            new SettingDescriptor
            {
                Key = "hotkey-enabled",
                Label = "Win+Alt+P kısayolu etkin",
                Kind = SettingKind.Toggle,
                Default = true
            },
            new SettingDescriptor
            {
                Key = "text-limit-kb",
                Label = "Metin önizleme sınırı (KB)",
                Kind = SettingKind.Number,
                Default = 1024,
                Min = 64,
                Max = 4096,
                Description = "Dosyanın başından en fazla bu kadar veri belleğe alınır."
            },
            new SettingDescriptor
            {
                Key = "image-limit-mb",
                Label = "Görsel dosya sınırı (MB)",
                Kind = SettingKind.Number,
                Default = 50,
                Min = 1,
                Max = 100,
                Description = "Büyük görseller için önizleme yerine güvenli sınır uyarısı gösterilir."
            },
            new SettingDescriptor
            {
                Key = "pdf-limit-mb",
                Label = "PDF önizleme sınırı (MB)",
                Kind = SettingKind.Number,
                Default = 100,
                Min = 1,
                Max = 500,
                Description = "Yalnızca ilk sayfa, uzun kenarı en fazla 1400 piksel olacak şekilde çizilir; daha büyük PDF dosyaları açılmaz."
            }
        ];
    }

    public string Id => "peek-preview";
    public string Name => "Dosya Önizleme";
    public string Category => "Gezgin";
    public string Description => "Win+Alt+P ile metin, görsel ve PDF dosyalarını açmadan görüntüler; ses ve videoları yerleşik denetimlerle oynatır.";
    public string? Warning => "PDF önizlemesi Windows PDF işleyicisini, medya oynatımı ise Windows'ta yüklü ses/video codec'lerini kullanır.";
    public bool IsActive => _isActive;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _isActive = true;
        RegisterHotkey();
        _explorerVerb.Install();
    }

    public void Deactivate()
    {
        _isActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        _explorerVerb.Uninstall();
    }

    public void Apply()
    {
        if (!_isActive) return;
        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;
        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Alt, VkP, OpenFile);
        context.Log.Info("Dosya önizleme kısayolu kaydedildi (Win+Alt+P).");
    }

    private async void OpenFile()
    {
        if (_opening) return;
        _opening = true;
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Önizlenecek dosyayı seçin",
                CheckFileExists = true,
                CheckPathExists = true
            };
            if (dialog.ShowDialog() != true) return;
            await ShowPreviewAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Dosya önizlemesi açılamadı.", exception);
            if ((_context as IFeatureUiContext)?.FeatureUi is { } featureUi)
                await featureUi.ShowMessageAsync("Dosya önizlenemedi", exception.Message, "Kapat");
        }
        finally
        {
            _opening = false;
        }
    }

    public bool HandleCommand(string command, IReadOnlyList<string> arguments)
    {
        if (!string.Equals(command, "--peek", StringComparison.OrdinalIgnoreCase)) return false;
        if (arguments.Count != 1) throw new ArgumentException("Peek komutu tek dosya yolu bekliyor.");
        PreviewPath(arguments[0]);
        return true;
    }

    private async void PreviewPath(string path)
    {
        if (_opening) return;
        _opening = true;
        try
        {
            await ShowPreviewAsync(path);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Dosya önizlemesi açılamadı.", exception);
            if ((_context as IFeatureUiContext)?.FeatureUi is { } featureUi)
                await featureUi.ShowMessageAsync("Dosya önizlenemedi", exception.Message, "Kapat");
        }
        finally
        {
            _opening = false;
        }
    }

    private async Task ShowPreviewAsync(string path)
    {
        var context = _context;
        var textLimit = Math.Clamp(context?.Settings.GetInt("text-limit-kb", 1024) ?? 1024, 64, 4096);
        var imageLimit = Math.Clamp(context?.Settings.GetInt("image-limit-mb", 50) ?? 50, 1, 100);
        var pdfLimit = Math.Clamp(context?.Settings.GetInt("pdf-limit-mb", 100) ?? 100, 1, 500);
        var document = await PeekPreviewLoader.LoadAsync(path, textLimit * 1024,
            imageLimit * 1024L * 1024L, pdfLimit * 1024L * 1024L);
        var preview = new PeekPreviewWindow(path, document, (context as IFeatureUiContext)?.FeatureUi);
        preview.ShowDialog();
    }
}

internal sealed record PeekPreviewDocument(string Kind, string Summary, string Text, ImageSource? Image, bool Truncated,
    string? MediaPath = null, int PageCount = 0);

internal static class PeekPreviewLoader
{
    private const long MaximumImagePixels = 30_000_000;
    private const int MaximumTextCharacters = 100_000;
    private const int MaximumPdfRenderDimension = 1400;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".wdp", ".jxr"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        ".ini", ".config", ".html", ".htm", ".css", ".js", ".ts", ".tsx", ".jsx", ".cs", ".xaml",
        ".c", ".h", ".cpp", ".hpp", ".py", ".ps1", ".bat", ".cmd", ".sh", ".sql", ".toml", ".properties"
    };
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".wma", ".m4a", ".aac", ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mpeg", ".mpg"
    };

    public static async Task<PeekPreviewDocument> LoadAsync(string path, int maxTextBytes, long maxImageBytes,
        long maxPdfBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxPdfBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPdfBytes));

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Dosya bulunamadı.", fullPath);
        if (info.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (info.Length > maxPdfBytes)
                return new PeekPreviewDocument("limit", $"PDF {FormatSize(info.Length)}; PDF sınırı {FormatSize(maxPdfBytes)}.", "", null, false);
            return await LoadPdfPageAsync(fullPath, info.Length);
        }

        if (MediaExtensions.Contains(info.Extension))
        {
            var isAudio = info.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                          info.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                          info.Extension.Equals(".wma", StringComparison.OrdinalIgnoreCase) ||
                          info.Extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                          info.Extension.Equals(".aac", StringComparison.OrdinalIgnoreCase);
            return new PeekPreviewDocument(isAudio ? "audio" : "video",
                $"{info.Extension.ToUpperInvariant()} · {FormatSize(info.Length)} · Codec desteğine bağlıdır.",
                "", null, false, fullPath);
        }

        return Load(fullPath, maxTextBytes, maxImageBytes);
    }

    public static PeekPreviewDocument Load(string path, int maxTextBytes, long maxImageBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxTextBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxTextBytes));
        if (maxImageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxImageBytes));

        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Dosya bulunamadı.", fullPath);
        var extension = info.Extension;

        if (ImageExtensions.Contains(extension))
        {
            if (info.Length > maxImageBytes)
                return new PeekPreviewDocument("limit", $"Dosya {FormatSize(info.Length)}; görsel sınırı {FormatSize(maxImageBytes)}.", "", null, false);

            using var stream = File.OpenRead(fullPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                (long)frame.PixelWidth * frame.PixelHeight > MaximumImagePixels)
                return new PeekPreviewDocument("limit", "Görsel, 30 megapiksel önizleme sınırını aşıyor.", "", null, false);

            frame.Freeze();
            return new PeekPreviewDocument("image", $"{frame.PixelWidth} × {frame.PixelHeight} · {FormatSize(info.Length)}", "", frame, false);
        }

        if (TextExtensions.Contains(extension))
        {
            var bytes = ReadBounded(fullPath, maxTextBytes, out var truncated);
            using var memory = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (text.Length > MaximumTextCharacters)
            {
                text = text[..MaximumTextCharacters];
                truncated = true;
            }
            return new PeekPreviewDocument("text", $"{FormatSize(info.Length)} · UTF BOM algılama etkin", text, null, truncated);
        }

        return new PeekPreviewDocument("unsupported", $"{extension.ToUpperInvariant()} · {FormatSize(info.Length)}", "Bu dosya türü için yerleşik önizleme yok.", null, false);
    }

    private static byte[] ReadBounded(string path, int limit, out bool truncated)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[limit + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0) break;
            total += read;
        }
        truncated = total > limit;
        return buffer.AsSpan(0, Math.Min(total, limit)).ToArray();
    }

    private static async Task<PeekPreviewDocument> LoadPdfPageAsync(string path, long fileSize)
    {
        var preview = await CorePdfPreviewService.RenderFirstPageAsync(path, MaximumPdfRenderDimension);
        return new PeekPreviewDocument("pdf", $"PDF · {preview.PageCount} sayfa · ilk sayfa · {FormatSize(fileSize)}",
            "", preview.Image, false, PageCount: preview.PageCount);
    }

    private static string FormatSize(long size) => size < 1024 * 1024
        ? $"{size / 1024d:0.#} KB"
        : $"{size / (1024d * 1024):0.#} MB";
}

internal sealed class PeekPreviewWindow : Window
{
    private readonly string path;
    private readonly PeekPreviewDocument document;
    private readonly IFeatureUiAssetSurface uiSurface;
    private string status = "";

    public PeekPreviewWindow(string path, PeekPreviewDocument document, IFeatureUiService? featureUi)
    {
        this.path = path;
        this.document = document;
        Title = Path.GetFileName(path) + " · Preview";
        Width = 900;
        Height = 650;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        uiSurface = featureUi?.CreateSurface("peek-preview") as IFeatureUiAssetSurface
            ?? throw new InvalidOperationException("Hızlı önizleme için React varlık yüzeyi gerekli.");
        Content = uiSurface.Element;
        uiSurface.MessageReceived += OnUiMessage;
        Closed += (_, _) =>
        {
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
        };
        PushState();
    }

    private void OnUiMessage(string command, System.Text.Json.JsonElement payload)
    {
        switch (command)
        {
            case "peek.close":
                Close();
                break;
            case "peek.open":
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception exception) { status = exception.Message; PushState(); }
                break;
        }
    }

    private void PushState()
    {
        var image = document.Image is BitmapSource bitmap ? ToDataUri(bitmap) : "";
        var media = document.MediaPath is { } mediaPath
            ? uiSurface.MapLocalAsset(mediaPath).AbsoluteUri
            : "";
        uiSurface.SetState(new
        {
            file = Path.GetFileName(path),
            path,
            kind = document.Kind,
            summary = document.Summary,
            text = document.Text,
            image,
            media,
            truncated = document.Truncated,
            pageCount = document.PageCount,
            status
        });
    }

    private static string ToDataUri(BitmapSource image)
    {
        const int maximumPreviewDimension = 2200;
        var scale = Math.Min(1d, maximumPreviewDimension / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale < 1)
        {
            var resized = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            resized.Freeze();
            image = resized;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
