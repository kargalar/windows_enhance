using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using WinForge.Core;

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
    private readonly IFeatureUiSurface? _uiSurface;
    private Button? _copyButton;
    private Button? _pasteButton;
    private int _previewRevision;

    public AdvancedPasteWindow(ClipboardTextSnapshot snapshot,
        Func<BitmapSource, Task<string>> recognizeImage,
        Action<AdvancedPasteAction, ClipboardTextSnapshot, string> copy,
        Action<AdvancedPasteAction, ClipboardTextSnapshot, string> paste,
        IFeatureUiService? featureUi = null)
    {
        _snapshot = snapshot;
        _recognizeImage = recognizeImage;
        _copy = copy;
        _paste = paste;
        _uiSurface = featureUi?.CreateSurface("advanced-paste");
        if (_uiSurface is not null) _uiSurface.MessageReceived += OnUiMessage;
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
        Content = _uiSurface?.Element ?? root;

        _actionBox.SelectionChanged += (_, _) => RefreshPreview();
        _jpegQuality.ValueChanged += (_, _) => RefreshPreview();
        Closed += (_, _) => _uiSurface?.Dispose();

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) DialogResult = false;
        };
        RefreshPreview();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "paste.select":
                if (payload.TryGetProperty("index", out var selected) && selected.TryGetInt32(out var index) && index >= 0 && index < Actions.Length)
                    _actionBox.SelectedIndex = index;
                break;
            case "paste.quality":
                if (payload.TryGetProperty("quality", out var quality) && quality.TryGetDouble(out var value))
                    _jpegQuality.Value = Math.Clamp(value, _jpegQuality.Minimum, _jpegQuality.Maximum);
                break;
            case "paste.copy": _copy(SelectedAction(), CurrentSnapshot(), _preview.Text); break;
            case "paste.paste":
                _paste(SelectedAction(), CurrentSnapshot(), _preview.Text);
                DialogResult = true;
                break;
            case "paste.cancel": DialogResult = false; break;
        }
    }

    private void PushUiState() => _uiSurface?.SetState(new
    {
        options = Actions.Select(row => new { row.Name, action = row.Action.ToString() }).ToArray(),
        selectedIndex = _actionBox.SelectedIndex,
        jpegQuality = _jpegQuality.Value,
        jpegVisible = _jpegOptions.Visibility == Visibility.Visible,
        sourceText = _snapshot.Text.Length > 4000 ? _snapshot.Text[..4000] + "…" : _snapshot.Text,
        sourceLength = _snapshot.Text.Length,
        hasImage = _snapshot.Image is not null,
        imageWidth = _snapshot.Image?.PixelWidth ?? 0,
        imageHeight = _snapshot.Image?.PixelHeight ?? 0,
        preview = _preview.Text,
        status = _status.Text,
        canCopy = _copyButton?.IsEnabled == true,
        canPaste = _pasteButton?.IsEnabled == true,
        pasteLabel = _pasteButton?.Content?.ToString() ?? "Yapıştır"
    });

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
            PushUiState();
            return;
        }
        if (!requiresImage && !hasText)
        {
            _preview.Text = "";
            _status.Text = "Bu işlem için panoda metin veya HTML bulunmalı.";
            PushUiState();
            return;
        }

        _status.Text = action == AdvancedPasteAction.ImageToText ? "Windows OCR çalışıyor…" : "Önizleme hazırlanıyor…";
        PushUiState();
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
                PushUiState();
                return;
            }

            _status.Text = AdvancedPasteArtifacts.IsFileAction(action)
                ? "Yerel dosya hazırlanıp panoya aktarılacak."
                : $"{result.Length:N0} karakter · yerel dönüşüm";
            _copyButton.IsEnabled = result.Length > 0;
            _pasteButton.IsEnabled = result.Length > 0;
            PushUiState();
        }
        catch (Exception exception)
        {
            if (revision != _previewRevision) return;
            _preview.Text = "";
            _status.Text = "Bu içerik seçilen biçime dönüştürülemedi: " + exception.Message;
            PushUiState();
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
