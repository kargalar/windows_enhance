using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ImageResizerModule : ITweakModule
{
    private readonly SettingDescriptor[] _settings =
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

    private bool _isActive;

    public string Id => "image-resizer";
    public string Name => "Görsel Boyutlandırıcı";
    public string Category => "Gezgin";
    public string Description => "İlerleme ve iptal destekli toplu görsel boyutlandırma, EXIF/IPTC koruması, en-boy oranını koruyan presetler ve PNG/JPEG dönüştürme.";
    public string? Warning => "Sonuçlar özgün görsellerin yanına _resized ekiyle yazılır; var olan dosyaların üzerine yazılmaz.";
    public bool IsActive => _isActive;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) { }
    public void Activate() => _isActive = true;
    public void Deactivate() => _isActive = false;
    public void Apply() { }

    private static void Open() => new ImageResizerWindow().ShowDialog();
}

internal sealed class ImageResizerWindow : Window
{
    private readonly ObservableCollection<ImageResizeItem> _items = new();
    private readonly ComboBox _preset = new() { MinWidth = 150 };
    private readonly TextBox _customSize = new() { Text = "1280", Width = 78, IsEnabled = false };
    private readonly ComboBox _format = new() { MinWidth = 130 };
    private readonly TextBox _quality = new() { Text = "90", Width = 55 };
    private readonly CheckBox _preserveMetadata = new() { Content = "EXIF/IPTC metadatasını koru", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly DataGrid _grid;
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _resize = new() { Content = "Boyutlandır", MinWidth = 110, IsEnabled = false };
    private readonly Button _choose = new() { Content = "Görselleri seç…", Padding = new Thickness(10, 6, 10, 6) };
    private readonly Button _remove = new() { Content = "Seçileni kaldır", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
    private readonly Button _cancel = new() { Content = "İptal", MinWidth = 72, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ProgressBar _progress = new() { Height = 8, Minimum = 0, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 12, 0) };
    private CancellationTokenSource? _resizeCancellation;

    public ImageResizerWindow()
    {
        Title = "Görsel Boyutlandırıcı";
        Width = 820;
        Height = 550;
        MinWidth = 640;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _preset.ItemsSource = new[] { "Küçük (854 px)", "Orta (1280 px)", "Büyük (1920 px)", "Özel", "Kaynağı koru" };
        _preset.SelectedIndex = 1;
        _format.ItemsSource = new[] { "Kaynak biçimi", "PNG", "JPEG" };
        _format.SelectedIndex = 0;

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            ItemsSource = _items,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Dosya", Binding = new System.Windows.Data.Binding(nameof(ImageResizeItem.FileName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Özgün boyut", Binding = new System.Windows.Data.Binding(nameof(ImageResizeItem.SourceSize)), Width = 110 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Çıktı adı", Binding = new System.Windows.Data.Binding(nameof(ImageResizeItem.TargetName)), Width = 160 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Boyut", Binding = new System.Windows.Data.Binding(nameof(ImageResizeItem.TargetSize)), Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Durum", Binding = new System.Windows.Data.Binding(nameof(ImageResizeItem.Status)), Width = 150 });

        _choose.Click += (_, _) => ChooseImages();
        _remove.Click += (_, _) => RemoveSelected();
        _preset.SelectionChanged += (_, _) => RefreshTargets();
        _customSize.TextChanged += (_, _) => RefreshTargets();
        _format.SelectionChanged += (_, _) => RefreshTargets();
        _quality.TextChanged += (_, _) => RefreshTargets();
        _resize.Click += async (_, _) => await ResizeImagesAsync();
        _cancel.Click += (_, _) => _resizeCancellation?.Cancel();
        Closed += (_, _) => _resizeCancellation?.Cancel();

        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fileButtons = new StackPanel { Orientation = Orientation.Horizontal };
        fileButtons.Children.Add(_choose);
        fileButtons.Children.Add(_remove);
        Grid.SetRow(fileButtons, 0);
        layout.Children.Add(fileButtons);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        options.Children.Add(new TextBlock { Text = "Boyut", VerticalAlignment = VerticalAlignment.Center });
        options.Children.Add(_preset);
        options.Children.Add(_customSize);
        options.Children.Add(new TextBlock { Text = "Biçim", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        options.Children.Add(_format);
        options.Children.Add(new TextBlock { Text = "JPEG kalitesi", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
        options.Children.Add(_quality);
        options.Children.Add(_preserveMetadata);
        Grid.SetRow(options, 1);
        layout.Children.Add(options);

        Grid.SetRow(_grid, 2);
        layout.Children.Add(_grid);

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_cancel);
        right.Children.Add(_resize);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);
        var progressPanel = new StackPanel();
        progressPanel.Children.Add(_progress);
        progressPanel.Children.Add(_status);
        footer.Children.Add(progressPanel);
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);
        Content = layout;

        UpdateControlState();
    }

    private void ChooseImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Boyutlandırılacak görselleri seçin",
            Filter = "Görseller|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.wdp;*.jxr|Tüm dosyalar|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var known = _items.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in dialog.FileNames)
        {
            if (!known.Add(path)) continue;
            try
            {
                var size = ImageResizeProcessor.ReadSize(path);
                _items.Add(new ImageResizeItem(path, size.Width, size.Height));
            }
            catch (Exception exception)
            {
                _items.Add(new ImageResizeItem(path, 0, 0) { Status = "Okunamadı: " + exception.Message });
            }
        }
        RefreshTargets();
    }

    private void RemoveSelected()
    {
        foreach (var item in _grid.SelectedItems.Cast<ImageResizeItem>().ToArray())
            _items.Remove(item);
        RefreshTargets();
    }

    private void RefreshTargets()
    {
        var maxDimension = GetMaxDimension();
        var format = _format.SelectedItem as string ?? "Kaynak biçimi";
        foreach (var item in _items)
        {
            if (item.Width <= 0 || item.Height <= 0) continue;
            var size = ImageResizeProcessor.CalculateSize(item.Width, item.Height, maxDimension);
            var extension = ImageResizeProcessor.OutputExtension(item.Path, format);
            item.TargetName = Path.GetFileNameWithoutExtension(item.Path) + "_resized" + extension;
            item.TargetSize = $"{size.Width} × {size.Height}";
            var target = Path.Combine(Path.GetDirectoryName(item.Path)!, item.TargetName);
            item.Status = File.Exists(target) ? "Çakışma: çıktı var" : "Hazır";
        }
        UpdateControlState();
    }

    private int GetMaxDimension()
    {
        return _preset.SelectedIndex switch
        {
            0 => 854,
            1 => 1280,
            2 => 1920,
            3 when int.TryParse(_customSize.Text, out var custom) && custom is >= 1 and <= ImageResizeProcessor.MaximumDimension => custom,
            4 => 0,
            _ => -1
        };
    }

    private void UpdateControlState()
    {
        _customSize.IsEnabled = _preset.SelectedIndex == 3;
        var maxDimension = GetMaxDimension();
        var qualityValid = int.TryParse(_quality.Text, out var quality) && quality is >= 1 and <= 100;
        var ready = _items.Any(item => item.Width > 0 && item.Height > 0 && item.Status == "Hazır");
        _resize.IsEnabled = ready && maxDimension >= 0 && qualityValid;
        _status.Text = $"{_items.Count} görsel · {_items.Count(item => item.Status == "Hazır")} hazır · {_items.Count(item => item.Status.StartsWith("Çakışma", StringComparison.Ordinal))} çakışma";
    }

    private async Task ResizeImagesAsync()
    {
        if (_resizeCancellation is not null || !int.TryParse(_quality.Text, out var quality) || quality is < 1 or > 100) return;
        var maxDimension = GetMaxDimension();
        if (maxDimension < 0) return;

        var workItems = _items.Where(item => item.Status == "Hazır").ToArray();
        if (workItems.Length == 0) return;
        var jobs = workItems.Select(item =>
            (Item: item, OutputPath: Path.Combine(Path.GetDirectoryName(item.Path)!, item.TargetName))).ToArray();
        var outputFormat = _format.SelectedItem as string ?? "Kaynak biçimi";
        var preserveMetadata = _preserveMetadata.IsChecked == true;
        var cancellation = new CancellationTokenSource();
        _resizeCancellation = cancellation;
        _progress.Minimum = 0;
        _progress.Maximum = workItems.Length;
        _progress.Value = 0;
        _progress.Visibility = Visibility.Visible;
        SetProcessing(true);
        _status.Text = $"0 / {workItems.Length} görsel işlendi…";
        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        IProgress<ImageResizeProgress> progress = new Progress<ImageResizeProgress>(update =>
        {
            update.Item.Status = update.Status;
            _progress.Value = update.Completed;
            _status.Text = $"{update.Completed} / {update.Total} işlendi · {update.Item.FileName}";
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
                        ImageResizeProcessor.ResizeWithOptions(item.Path, outputPath, maxDimension,
                            outputFormat, quality, preserveMetadata, cancellation.Token);
                        Interlocked.Increment(ref succeeded);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        status = "Başarısız: " + exception.Message;
                        Interlocked.Increment(ref failed);
                    }

                    var done = Interlocked.Increment(ref completed);
                    progress.Report(new ImageResizeProgress(done, workItems.Length, item, status));
                }
            }, cancellation.Token);
            _progress.Value = completed;
            _status.Text = $"{succeeded} görsel oluşturuldu · {failed} başarısız; özgün dosyalar korundu.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _progress.Value = completed;
            _status.Text = $"İşlem iptal edildi · {succeeded} tamamlandı · {failed} başarısız · {workItems.Length - completed} beklemede.";
        }
        finally
        {
            _resizeCancellation = null;
            cancellation.Dispose();
            _progress.Visibility = Visibility.Collapsed;
            SetProcessing(false);
        }
    }

    private void SetProcessing(bool isProcessing)
    {
        _choose.IsEnabled = !isProcessing;
        _remove.IsEnabled = !isProcessing;
        _preset.IsEnabled = !isProcessing;
        _customSize.IsEnabled = !isProcessing && _preset.SelectedIndex == 3;
        _format.IsEnabled = !isProcessing;
        _quality.IsEnabled = !isProcessing;
        _preserveMetadata.IsEnabled = !isProcessing;
        _cancel.IsEnabled = isProcessing;
        _resize.IsEnabled = !isProcessing && _items.Any(item => item.Status == "Hazır") &&
                            GetMaxDimension() >= 0 && int.TryParse(_quality.Text, out var quality) && quality is >= 1 and <= 100;
    }

    private sealed record ImageResizeProgress(int Completed, int Total, ImageResizeItem Item, string Status);
}

internal sealed class ImageResizeItem(string path, int width, int height) : INotifyPropertyChanged
{
    private string _targetName = "";
    private string _targetSize = "";
    private string _status = "Hazır";

    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public int Width { get; } = width;
    public int Height { get; } = height;
    public string SourceSize => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";
    public string TargetName { get => _targetName; set { if (_targetName != value) { _targetName = value; OnPropertyChanged(); } } }
    public string TargetSize { get => _targetSize; set { if (_targetSize != value) { _targetSize = value; OnPropertyChanged(); } } }
    public string Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); } } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
