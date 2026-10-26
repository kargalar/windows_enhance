using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
            MessageBox.Show(exception.Message, "Dosya önizlenemedi", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(exception.Message, "Dosya önizlenemedi", MessageBoxButton.OK, MessageBoxImage.Error);
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
        var preview = new PeekPreviewWindow(path, document);
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
    private MediaElement? _mediaElement;
    private Slider? _mediaTimeline;
    private TextBlock? _mediaPosition;
    private TextBlock? _mediaStatus;
    private Button? _playButton;
    private DispatcherTimer? _playbackTimer;
    private bool _mediaOpened;

    public PeekPreviewWindow(string path, PeekPreviewDocument document)
    {
        Title = Path.GetFileName(path) + " · Önizleme";
        Width = 900;
        Height = 650;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(Color.FromRgb(24, 27, 33));
        Foreground = Brushes.White;

        var layout = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 20, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = path, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path });
        header.Children.Add(new TextBlock { Text = document.Summary, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var openButton = new Button { Content = "Varsayılan uygulamada aç", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        openButton.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "Dosya açılamadı", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        DockPanel.SetDock(openButton, Dock.Bottom);
        layout.Children.Add(openButton);

        if (document.Truncated)
        {
            var notice = new TextBlock { Text = "Dosya sınırlandırılmış boyutta gösteriliyor.", Foreground = Brushes.Gold, Margin = new Thickness(0, 0, 0, 8) };
            var body = new DockPanel();
            DockPanel.SetDock(notice, Dock.Top);
            body.Children.Add(notice);
            body.Children.Add(CreateContent(document));
            layout.Children.Add(body);
        }
        else
        {
            layout.Children.Add(CreateContent(document));
        }

        Content = layout;
        Closed += (_, _) =>
        {
            _playbackTimer?.Stop();
            _mediaElement?.Close();
        };
    }

    private FrameworkElement CreateContent(PeekPreviewDocument document)
    {
        if (document.Image is not null)
            return new Border
            {
                Background = Brushes.Black,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Image { Source = document.Image, Stretch = Stretch.Uniform, MaxWidth = 840, MaxHeight = 490, HorizontalAlignment = HorizontalAlignment.Center }
                }
            };

        if (document.Kind is "audio" or "video") return CreateMediaContent(document);

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBox
            {
                Text = document.Text,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = document.Kind == "text" ? TextWrapping.NoWrap : TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(17, 19, 23)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12)
            }
        };
    }

    private FrameworkElement CreateMediaContent(PeekPreviewDocument document)
    {
        var mediaLayout = new DockPanel { LastChildFill = false };
        if (document.Kind == "video")
        {
            _mediaElement = new MediaElement
            {
                Source = new Uri(document.MediaPath!, UriKind.Absolute),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                ScrubbingEnabled = true,
                Volume = 0.7,
                MinHeight = 180
            };
            DockPanel.SetDock(_mediaElement, Dock.Top);
            mediaLayout.Children.Add(_mediaElement);
        }
        else
        {
            var audioVisual = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 19, 23)),
                Child = new TextBlock
                {
                    Text = "♫  Ses önizlemesi",
                    FontSize = 28,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            DockPanel.SetDock(audioVisual, Dock.Top);
            mediaLayout.Children.Add(audioVisual);
            _mediaElement = new MediaElement
            {
                Source = new Uri(document.MediaPath!, UriKind.Absolute),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                ScrubbingEnabled = true,
                Volume = 0.7,
                Visibility = Visibility.Collapsed
            };
            mediaLayout.Children.Add(_mediaElement);
        }

        var controls = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var timelineRow = new DockPanel();
        _mediaPosition = new TextBlock { Text = "0:00 / --:--", MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_mediaPosition, Dock.Right);
        timelineRow.Children.Add(_mediaPosition);
        _mediaTimeline = new Slider { Minimum = 0, Maximum = 1, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _mediaTimeline.ValueChanged += (_, _) =>
        {
            if (_mediaOpened && _mediaElement is not null &&
                (_mediaTimeline.IsMouseCaptureWithin || _mediaTimeline.IsKeyboardFocusWithin))
                _mediaElement.Position = TimeSpan.FromSeconds(_mediaTimeline.Value);
        };
        timelineRow.Children.Add(_mediaTimeline);
        controls.Children.Add(timelineRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _playButton = new Button { Content = "▶ Oynat", Padding = new Thickness(12, 6, 12, 6), MinWidth = 92 };
        _playButton.Click += (_, _) => TogglePlayback();
        buttons.Children.Add(_playButton);
        var stop = new Button { Content = "■ Durdur", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 12, 0) };
        stop.Click += (_, _) => StopPlayback();
        buttons.Children.Add(stop);
        buttons.Children.Add(new TextBlock { Text = "Ses", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = 70, Width = 110, VerticalAlignment = VerticalAlignment.Center };
        volume.ValueChanged += (_, _) => { if (_mediaElement is not null) _mediaElement.Volume = volume.Value / 100; };
        buttons.Children.Add(volume);
        controls.Children.Add(buttons);

        _mediaStatus = new TextBlock { Text = "Oynatmak için düğmeye basın.", Opacity = 0.75, Margin = new Thickness(0, 5, 0, 0) };
        controls.Children.Add(_mediaStatus);
        DockPanel.SetDock(controls, Dock.Bottom);
        mediaLayout.Children.Add(controls);

        _mediaElement!.MediaOpened += (_, _) =>
        {
            _mediaOpened = true;
            var duration = _mediaElement.NaturalDuration.HasTimeSpan ? _mediaElement.NaturalDuration.TimeSpan : TimeSpan.Zero;
            _mediaTimeline.Maximum = Math.Max(1, duration.TotalSeconds);
            _mediaTimeline.IsEnabled = duration > TimeSpan.Zero;
            _mediaStatus.Text = duration > TimeSpan.Zero ? "Hazır · ses dosyalarında oynatma kullanıcı başlatana kadar duraklatılmıştır." : "Hazır";
            UpdateMediaPosition();
        };
        _mediaElement.MediaFailed += (_, args) =>
        {
            _mediaStatus!.Text = "Bu dosyanın codec'i Windows'ta bulunamadı veya dosya bozuk.";
            _playButton!.IsEnabled = false;
            _mediaTimeline!.IsEnabled = false;
        };
        _mediaElement.MediaEnded += (_, _) =>
        {
            _playButton!.Content = "▶ Yeniden oynat";
            _mediaStatus!.Text = "Tamamlandı";
        };
        // Manual MediaElement instances remain closed until a playback operation is requested.
        // Pause opens and buffers the source without playing sound, enabling the first Play click.
        _mediaElement.Loaded += (_, _) => _mediaElement.Pause();
        _playbackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _playbackTimer.Tick += (_, _) => UpdateMediaPosition();
        _playbackTimer.Start();
        return mediaLayout;
    }

    private void TogglePlayback()
    {
        if (!_mediaOpened || _mediaElement is null) return;
        var hasDuration = _mediaElement.NaturalDuration.HasTimeSpan;
        if (_mediaElement.CanPause && (!hasDuration || _mediaElement.Position < _mediaElement.NaturalDuration.TimeSpan))
        {
            if (_playButton?.Content?.ToString()?.Contains("Duraklat", StringComparison.Ordinal) == true)
            {
                _mediaElement.Pause();
                _playButton.Content = "▶ Devam";
                _mediaStatus!.Text = "Duraklatıldı";
            }
            else
            {
                _mediaElement.Play();
                _playButton!.Content = "Ⅱ Duraklat";
                _mediaStatus!.Text = "Oynatılıyor";
            }
        }
        else
        {
            _mediaElement.Position = TimeSpan.Zero;
            _mediaElement.Play();
            _playButton!.Content = "Ⅱ Duraklat";
        }
    }

    private void StopPlayback()
    {
        if (!_mediaOpened || _mediaElement is null) return;
        _mediaElement.Stop();
        _playButton!.Content = "▶ Oynat";
        _mediaStatus!.Text = "Durduruldu";
        UpdateMediaPosition();
    }

    private void UpdateMediaPosition()
    {
        if (!_mediaOpened || _mediaElement is null || _mediaTimeline is null || _mediaPosition is null) return;
        var position = _mediaElement.Position;
        if (!_mediaTimeline.IsMouseCaptureWithin && !_mediaTimeline.IsKeyboardFocusWithin)
            _mediaTimeline.Value = Math.Clamp(position.TotalSeconds, _mediaTimeline.Minimum, _mediaTimeline.Maximum);
        var duration = _mediaElement.NaturalDuration.HasTimeSpan ? _mediaElement.NaturalDuration.TimeSpan : TimeSpan.Zero;
        _mediaPosition.Text = $"{FormatTime(position)} / {(duration == TimeSpan.Zero ? "--:--" : FormatTime(duration))}";
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
