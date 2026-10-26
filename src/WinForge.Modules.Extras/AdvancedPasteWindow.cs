using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Extras;

internal sealed class AdvancedPasteWindow : Window
{
    private static readonly (string Name, AdvancedPasteAction Action)[] Actions =
    [
        ("Düz metin", AdvancedPasteAction.PlainText),
        ("Markdown", AdvancedPasteAction.Markdown),
        ("JSON", AdvancedPasteAction.Json),
        ("Görselden metin (OCR)", AdvancedPasteAction.ImageToText),
        ("Metin dosyası", AdvancedPasteAction.PasteAsTextFile),
        ("HTML dosyası", AdvancedPasteAction.PasteAsHtmlFile),
        ("PNG dosyası", AdvancedPasteAction.PasteAsPngFile),
        ("JPEG'e dönüştür", AdvancedPasteAction.ConvertImageToJpeg),
        ("BMP'ye dönüştür", AdvancedPasteAction.ConvertImageToBmp),
        ("GIF'e dönüştür", AdvancedPasteAction.ConvertImageToGif)
    ];

    private readonly ClipboardTextSnapshot _snapshot;
    private readonly Func<BitmapSource, Task<string>> _recognizeImage;
    private readonly Action<AdvancedPasteAction, ClipboardTextSnapshot, string> _copy;
    private readonly Action<AdvancedPasteAction, ClipboardTextSnapshot, string> _paste;
    private readonly ComboBox _actionBox;
    private readonly TextBox _preview;
    private readonly TextBlock _status;
    private readonly StackPanel _jpegOptions;
    private readonly Slider _jpegQuality;
    private readonly TextBlock _jpegQualityLabel;
    private Button? _copyButton;
    private Button? _pasteButton;
    private int _previewRevision;

    public AdvancedPasteWindow(ClipboardTextSnapshot snapshot,
        Func<BitmapSource, Task<string>> recognizeImage,
        Action<AdvancedPasteAction, ClipboardTextSnapshot, string> copy,
        Action<AdvancedPasteAction, ClipboardTextSnapshot, string> paste)
    {
        _snapshot = snapshot;
        _recognizeImage = recognizeImage;
        _copy = copy;
        _paste = paste;
        Title = "WinForge · Gelişmiş Yapıştır";
        Width = 720;
        Height = 560;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(25, 29, 38));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var root = new DockPanel { Margin = new Thickness(20) };
        var title = new TextBlock
        {
            Text = "Panodaki içeriği dönüştür",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(177, 187, 202)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        _copyButton = CreateButton("Panoya kopyala");
        _pasteButton = CreateButton("Yapıştır");
        _pasteButton.IsDefault = true;
        var cancelButton = CreateButton("Kapat");
        cancelButton.IsCancel = true;
        footer.Children.Add(_status);
        footer.Children.Add(_copyButton);
        footer.Children.Add(_pasteButton);
        footer.Children.Add(cancelButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        content.Children.Add(new TextBlock
        {
            Text = "Biçim",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 198, 212)),
            FontSize = 12
        });
        _actionBox = new ComboBox
        {
            ItemsSource = Actions.Select(action => action.Name).ToArray(),
            SelectedIndex = snapshot.Image is not null && snapshot.Text.Length == 0
                ? Array.FindIndex(Actions, action => action.Action == AdvancedPasteAction.ImageToText)
                : 0,
            Margin = new Thickness(0, 6, 0, 12),
            Padding = new Thickness(8, 5, 8, 5),
            Foreground = Brushes.Black,
            Background = Brushes.White
        };
        Grid.SetRow(_actionBox, 1);
        content.Children.Add(_actionBox);

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas, Cascadia Mono"),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(18, 21, 28)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 73, 90))
        };
        _copyButton.Click += (_, _) => _copy(SelectedAction(), CurrentSnapshot(), _preview.Text);
        _pasteButton.Click += (_, _) =>
        {
            _paste(SelectedAction(), CurrentSnapshot(), _preview.Text);
            DialogResult = true;
        };

        _jpegQualityLabel = new TextBlock
        {
            Text = $"JPEG kalite · {AdvancedPasteArtifacts.DefaultJpegQuality}%",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 198, 212)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        _jpegQuality = new Slider
        {
            Minimum = 10,
            Maximum = 100,
            Value = AdvancedPasteArtifacts.DefaultJpegQuality,
            TickFrequency = 5,
            IsSnapToTickEnabled = true
        };
        _jpegOptions = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        _jpegOptions.Children.Add(_jpegQualityLabel);
        _jpegOptions.Children.Add(_jpegQuality);
        Grid.SetRow(_jpegOptions, 2);
        content.Children.Add(_jpegOptions);

        Grid.SetRow(_preview, 3);
        content.Children.Add(_preview);

        var sourceText = snapshot.Text.Length > 4000 ? snapshot.Text[..4000] + "\n…" : snapshot.Text;
        if (sourceText.Length == 0 && snapshot.Image is { } image)
            sourceText = $"Görsel panoda · {image.PixelWidth} × {image.PixelHeight} piksel";
        var source = new TextBlock
        {
            Text = $"Girdi önizlemesi · {snapshot.Text.Length:N0} karakter\n{sourceText}",
            Foreground = new SolidColorBrush(Color.FromRgb(150, 160, 176)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 92,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(source, 4);
        content.Children.Add(source);
        root.Children.Add(content);
        Content = root;

        _actionBox.SelectionChanged += (_, _) => RefreshPreview();
        _jpegQuality.ValueChanged += (_, _) => RefreshPreview();

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) DialogResult = false;
        };
        RefreshPreview();
    }

    private AdvancedPasteAction SelectedAction() =>
        _actionBox.SelectedIndex >= 0 && _actionBox.SelectedIndex < Actions.Length
            ? Actions[_actionBox.SelectedIndex].Action
            : AdvancedPasteAction.PlainText;

    private ClipboardTextSnapshot CurrentSnapshot() =>
        _snapshot with { JpegQuality = (int)Math.Round(_jpegQuality.Value) };

    private async void RefreshPreview()
    {
        var revision = ++_previewRevision;
        var action = SelectedAction();
        _jpegOptions.Visibility = action == AdvancedPasteAction.ConvertImageToJpeg ? Visibility.Visible : Visibility.Collapsed;
        _jpegQualityLabel.Text = $"JPEG kalite · {(int)Math.Round(_jpegQuality.Value)}%";
        var hasText = _snapshot.Text.Length > 0 || !string.IsNullOrWhiteSpace(_snapshot.Html);
        var requiresImage = action == AdvancedPasteAction.ImageToText || AdvancedPasteArtifacts.IsImageFileAction(action);
        _copyButton!.IsEnabled = false;
        _pasteButton!.IsEnabled = false;
        _pasteButton.Content = AdvancedPasteArtifacts.IsFileAction(action)
            ? "Dosya yapıştır"
            : "Yapıştır";

        if (requiresImage && _snapshot.Image is null)
        {
            _preview.Text = "";
            _status.Text = "Bu işlem için panoda görsel bulunmalı.";
            return;
        }
        if (!requiresImage && !hasText)
        {
            _preview.Text = "";
            _status.Text = "Bu işlem için panoda metin veya HTML bulunmalı.";
            return;
        }

        _status.Text = action == AdvancedPasteAction.ImageToText ? "Windows OCR çalışıyor…" : "Önizleme hazırlanıyor…";
        try
        {
            var result = action == AdvancedPasteAction.ImageToText
                ? await _recognizeImage(_snapshot.Image!)
                : AdvancedPasteTransforms.Transform(action, CurrentSnapshot());
            if (revision != _previewRevision) return;

            _preview.Text = result;
            if (action == AdvancedPasteAction.ImageToText && string.IsNullOrWhiteSpace(result))
            {
                _status.Text = "Görselde metin bulunamadı.";
                return;
            }

            _status.Text = AdvancedPasteArtifacts.IsFileAction(action)
                ? "Yerel dosya hazırlanıp panoya aktarılacak."
                : $"{result.Length:N0} karakter · yerel dönüşüm";
            _copyButton.IsEnabled = result.Length > 0;
            _pasteButton.IsEnabled = result.Length > 0;
        }
        catch (Exception exception)
        {
            if (revision != _previewRevision) return;
            _preview.Text = "";
            _status.Text = "Bu içerik seçilen biçime dönüştürülemedi: " + exception.Message;
        }
    }

    private static Button CreateButton(string label) => new()
    {
        Content = label,
        Padding = new Thickness(12, 7, 12, 7),
        Margin = new Thickness(4, 0, 0, 0),
        MinWidth = 90
    };
}
