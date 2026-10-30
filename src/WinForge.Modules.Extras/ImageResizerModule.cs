using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForge.Core;
using System.Text.Json;

namespace WinForge.Modules.Extras;

public sealed class ImageResizerModule : ITweakModule
{
    private readonly SettingDescriptor[] _settings;

    public ImageResizerModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Görsel boyutlandırmayı aç",
                Kind = SettingKind.Action,
                Description = "Birden fazla görseli yeniden boyutlandırın veya PNG/JPEG biçimine dönüştürün; EXIF/IPTC metadatasını korumayı seçin ve işlemi iptal edin.",
                Execute = Open
            }
        ];
    }
    private bool _isActive;
    private IFeatureUiService? _featureUi;

    public string Id => "image-resizer";
    public string Name => "Görsel Boyutlandırıcı";
    public string Category => "Gezgin";
    public string Description => "İlerleme ve iptal destekli toplu görsel boyutlandırma, EXIF/IPTC koruması, en-boy oranını koruyan presetler ve PNG/JPEG dönüştürme.";
    public string? Warning => "Sonuçlar özgün görsellerin yanına _resized ekiyle yazılır; var olan dosyaların üzerine yazılmaz.";
    public bool IsActive => _isActive;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _featureUi = (context as IFeatureUiContext)?.FeatureUi;
    public void Activate() => _isActive = true;
    public void Deactivate() => _isActive = false;
    public void Apply() { }

    private void Open()
    {
        if (_featureUi is null) throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
        _ = new ImageResizerWindow(_featureUi);
    }
}

internal sealed class ImageResizerWindow
{
    private readonly IFeatureUiWindow _window;
    private readonly List<ImageResizeItem> _items = [];
    private CancellationTokenSource? _resizeCancellation;
    private string _preset = "medium";
    private string _customSize = "1280";
    private string _format = "source";
    private string _quality = "90";
    private bool _preserveMetadata = true;
    private string _status = "Görselleri ekleyerek başlayın.";
    private bool _processing;
    private int _progress;
    private int _progressMaximum;

    internal ImageResizerWindow(IFeatureUiService featureUi)
    {
        _window = featureUi.Open("image-resizer-" + Guid.NewGuid().ToString("N"), "image-resizer", "Görsel Boyutlandırıcı",
            new FeatureUiWindowOptions(980, 680, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        PushState();
    }

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "image-resizer.choose": ChooseImages(); break;
            case "image-resizer.remove": Remove(payload); break;
            case "image-resizer.options": UpdateOptions(payload); break;
            case "image-resizer.run": _ = ResizeImagesAsync(); break;
            case "image-resizer.cancel": _resizeCancellation?.Cancel(); break;
            case "image-resizer.close": _resizeCancellation?.Cancel(); _window.Close(); break;
        }
    }

    private void ChooseImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Boyutlandırılacak görselleri seçin",
            Filter = "Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.wdp;*.jxr|Tüm dosyalar|*.*",
            Multiselect = true, CheckFileExists = true, CheckPathExists = true
        };
        if (dialog.ShowDialog() != true) return;
        var known = _items.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in dialog.FileNames)
        {
            if (!known.Add(path)) continue;
            try
            {
                var size = ImageResizeProcessor.ReadSize(path);
                _items.Add(new ImageResizeItem(path, size.Width, size.Height));
            }
            catch (Exception exception) { _items.Add(new ImageResizeItem(path, 0, 0, "Okunamadı: " + exception.Message)); }
        }
        RefreshTargets();
    }

    private void Remove(JsonElement payload)
    {
        if (_processing || !payload.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out var index)) return;
        if (index >= 0 && index < _items.Count) _items.RemoveAt(index);
        RefreshTargets();
    }

    private void UpdateOptions(JsonElement payload)
    {
        if (_processing) return;
        if (payload.TryGetProperty("preset", out var preset)) _preset = preset.GetString() ?? _preset;
        if (payload.TryGetProperty("customSize", out var custom)) _customSize = custom.GetString() ?? _customSize;
        if (payload.TryGetProperty("format", out var format)) _format = format.GetString() ?? _format;
        if (payload.TryGetProperty("quality", out var quality)) _quality = quality.GetString() ?? _quality;
        if (payload.TryGetProperty("preserveMetadata", out var metadata)) _preserveMetadata = metadata.ValueKind == JsonValueKind.True;
        RefreshTargets();
    }

    private void RefreshTargets(bool updateStatus = true)
    {
        var maxDimension = GetMaxDimension();
        foreach (var item in _items)
        {
            if (item.Width <= 0 || item.Height <= 0) continue;
            var size = ImageResizeProcessor.CalculateSize(item.Width, item.Height, maxDimension);
            var extension = ImageResizeProcessor.OutputExtension(item.Path, _format);
            item.TargetName = Path.GetFileNameWithoutExtension(item.Path) + "_resized" + extension;
            item.TargetSize = $"{size.Width} × {size.Height}";
            var target = Path.Combine(Path.GetDirectoryName(item.Path)!, item.TargetName);
            item.Status = item.Status == "Tamamlandı" ? item.Status : File.Exists(target) ? "Çakışma: çıktı var" : "Hazır";
        }
        if (updateStatus)
            _status = $"{_items.Count} görsel · {_items.Count(item => item.Status == "Hazır")} hazır · {_items.Count(item => item.Status.StartsWith("Çakışma", StringComparison.Ordinal))} çakışma";
        PushState();
    }

    private int GetMaxDimension() => _preset switch
    {
        "small" => 854,
        "medium" => 1280,
        "large" => 1920,
        "custom" when int.TryParse(_customSize, out var custom) && custom is >= 1 and <= ImageResizeProcessor.MaximumDimension => custom,
        "source" => 0,
        _ => -1
    };

    private async Task ResizeImagesAsync()
    {
        if (_resizeCancellation is not null || !int.TryParse(_quality, out var quality) || quality is < 1 or > 100) return;
        var maxDimension = GetMaxDimension();
        if (maxDimension < 0) return;
        var workItems = _items.Where(item => item.Status == "Hazır").ToArray();
        if (workItems.Length == 0) return;
        var jobs = workItems.Select(item => (Item: item, OutputPath: Path.Combine(Path.GetDirectoryName(item.Path)!, item.TargetName))).ToArray();
        var cancellation = new CancellationTokenSource();
        _resizeCancellation = cancellation;
        _processing = true;
        _progress = 0;
        _progressMaximum = workItems.Length;
        _status = $"0 / {workItems.Length} görsel işlendi…";
        PushState();
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        IProgress<ImageResizeProgress> progress = new Progress<ImageResizeProgress>(update =>
        {
            update.Item.Status = update.Status;
            _progress = update.Completed;
            _status = $"{update.Completed} / {update.Total} işlendi · {update.Item.FileName}";
            PushState();
        });
        try
        {
            await Task.Run(() =>
            {
                for (var index = 0; index < workItems.Length; index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var (item, outputPath) = jobs[index];
                    var status = "Tamamlandı";
                    try
                    {
                        ImageResizeProcessor.ResizeWithOptions(item.Path, outputPath, maxDimension, _format,
                            quality, _preserveMetadata, cancellation.Token);
                        Interlocked.Increment(ref succeeded);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                    catch (Exception exception) { status = "Başarısız: " + exception.Message; Interlocked.Increment(ref failed); }
                    progress.Report(new ImageResizeProgress(Interlocked.Increment(ref completed), workItems.Length, item, status));
                }
            }, cancellation.Token);
            _status = $"{succeeded} görsel oluşturuldu · {failed} başarısız; özgün dosyalar korundu.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _status = $"İşlem iptal edildi · {succeeded} tamamlandı · {failed} başarısız · {workItems.Length - completed} beklemede.";
        }
        finally
        {
            _resizeCancellation = null;
            cancellation.Dispose();
            _processing = false;
            RefreshTargets(updateStatus: false);
        }
    }

    private void PushState() => _window.SetState(new
    {
        preset = _preset, customSize = _customSize, format = _format, quality = _quality,
        preserveMetadata = _preserveMetadata, processing = _processing, progress = _progress,
        progressMaximum = _progressMaximum,
        canResize = !_processing && _items.Any(item => item.Status == "Hazır") && GetMaxDimension() >= 0 &&
            int.TryParse(_quality, out var quality) && quality is >= 1 and <= 100,
        status = _status,
        rows = _items.Select((item, index) => new { index, item.FileName, item.SourceSize, item.TargetName, item.TargetSize, item.Status }).ToArray()
    });

    private sealed class ImageResizeItem(string path, int width, int height, string status = "Hazır")
    {
        public string Path { get; } = path;
        public string FileName => System.IO.Path.GetFileName(Path);
        public int Width { get; } = width;
        public int Height { get; } = height;
        public string SourceSize => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";
        public string TargetName { get; set; } = "";
        public string TargetSize { get; set; } = "";
        public string Status { get; set; } = status;
    }

    private sealed record ImageResizeProgress(int Completed, int Total, ImageResizeItem Item, string Status);
}
internal static class ImageResizeProcessor
{
    public const int MaximumDimension = 16_384;
    private const long MaximumPixelCount = 100_000_000;

    public static (int Width, int Height) ReadSize(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0) throw new InvalidDataException("Görsel boyutları geçersiz.");
        if (frame.PixelWidth > MaximumDimension || frame.PixelHeight > MaximumDimension ||
            (long)frame.PixelWidth * frame.PixelHeight > MaximumPixelCount)
            throw new InvalidDataException("Görsel desteklenen boyut veya piksel sınırını aşıyor.");
        return (frame.PixelWidth, frame.PixelHeight);
    }

    public static (int Width, int Height) CalculateSize(int width, int height, int maxDimension)
    {
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(width), "Kaynak görsel boyutu desteklenen sınırın dışında.");
        if (maxDimension < 0 || maxDimension > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        if (maxDimension == 0) return (width, height);

        var scale = Math.Min(1d, maxDimension / (double)Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    public static string OutputExtension(string sourcePath, string format) => format switch
    {
        "PNG" => ".png",
        "JPEG" => ".jpg",
        _ => Path.GetExtension(sourcePath).ToLowerInvariant()
    };

    public static void Resize(string sourcePath, string targetPath, int maxDimension, string format, int quality)
        => ResizeWithOptions(sourcePath, targetPath, maxDimension, format, quality, preserveMetadata: true, CancellationToken.None);

    public static void ResizeWithOptions(string sourcePath, string targetPath, int maxDimension, string format, int quality,
        bool preserveMetadata, CancellationToken cancellationToken)
    {
        if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        cancellationToken.ThrowIfCancellationRequested();
        var outputExtension = OutputExtension(sourcePath, format);
        var (width, height) = ReadSize(sourcePath);
        var targetSize = CalculateSize(width, height, maxDimension);
        if ((long)targetSize.Width * targetSize.Height > MaximumPixelCount)
            throw new InvalidDataException("Çıktı görseli güvenli piksel sınırını aşıyor.");

        BitmapSource source;
        BitmapMetadata? metadata = null;
        using (var stream = File.OpenRead(sourcePath))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            source = frame;
            if (preserveMetadata && CodecName(Path.GetExtension(sourcePath)) == CodecName(outputExtension) &&
                frame.Metadata is BitmapMetadata sourceMetadata)
            {
                metadata = sourceMetadata.Clone();
                if (metadata.CanFreeze) metadata.Freeze();
            }
        }

        if (targetSize.Width != source.PixelWidth || targetSize.Height != source.PixelHeight)
        {
            var transformed = new TransformedBitmap(source, new ScaleTransform(
                targetSize.Width / (double)source.PixelWidth,
                targetSize.Height / (double)source.PixelHeight));
            transformed.Freeze();
            source = transformed;
        }

        if (outputExtension is ".jpg" or ".jpeg")
            source = FlattenOnWhite(source);

        var encoder = CreateEncoder(outputExtension, quality);
        encoder.Frames.Add(metadata is null ? BitmapFrame.Create(source) : BitmapFrame.Create(source, null, metadata, null));

        var temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".winforge.tmp";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                encoder.Save(output);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string CodecName(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "jpeg",
        ".tif" or ".tiff" => "tiff",
        ".wdp" or ".jxr" => "wdp",
        var value => value
    };

    private static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }
        var flattened = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        flattened.Render(visual);
        flattened.Freeze();
        return flattened;
    }

    private static BitmapEncoder CreateEncoder(string extension, int quality) => extension switch
    {
        ".png" => new PngBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        ".gif" => new GifBitmapEncoder(),
        ".tif" or ".tiff" => new TiffBitmapEncoder(),
        ".wdp" or ".jxr" => new WmpBitmapEncoder { QualityLevel = (byte)quality },
        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
        _ => throw new NotSupportedException($"Bu çıktı biçimi desteklenmiyor: {extension}")
    };
}


