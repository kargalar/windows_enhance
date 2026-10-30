using System.Windows;
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
    private readonly IFeatureUiSurface _uiSurface;
    private int _selectedIndex;
    private double _jpegQuality = AdvancedPasteArtifacts.DefaultJpegQuality;
    private bool _jpegVisible;
    private bool _canCopy;
    private bool _canPaste;
    private string _previewText = "";
    private string _statusText = "";
    private string _pasteLabel = "Yapıştır";
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
        _uiSurface = featureUi?.CreateSurface("advanced-paste")
            ?? throw new InvalidOperationException("The React advanced paste view is required.");
        _selectedIndex = snapshot.Image is not null && snapshot.Text.Length == 0
            ? Array.FindIndex(Actions, action => action.Action == AdvancedPasteAction.ImageToText)
            : 0;
        _uiSurface.MessageReceived += OnUiMessage;
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

        Content = _uiSurface.Element;

        Closed += (_, _) => _uiSurface.Dispose();

        RefreshPreview();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "paste.select":
                if (payload.TryGetProperty("index", out var selected) && selected.TryGetInt32(out var index) && index >= 0 && index < Actions.Length)
                {
                    _selectedIndex = index;
                    RefreshPreview();
                }
                break;
            case "paste.quality":
                if (payload.TryGetProperty("quality", out var quality) && quality.TryGetDouble(out var value))
                {
                    _jpegQuality = Math.Clamp(value, 10, 100);
                    RefreshPreview();
                }
                break;
            case "paste.copy": _copy(SelectedAction(), CurrentSnapshot(), _previewText); break;
            case "paste.paste":
                _paste(SelectedAction(), CurrentSnapshot(), _previewText);
                DialogResult = true;
                break;
            case "paste.cancel": DialogResult = false; break;
        }
    }

    private void PushUiState() => _uiSurface.SetState(new
    {
        options = Actions.Select(row => new { row.Name, action = row.Action.ToString() }).ToArray(),
        selectedIndex = _selectedIndex,
        jpegQuality = _jpegQuality,
        jpegVisible = _jpegVisible,
        sourceText = _snapshot.Text.Length > 4000 ? _snapshot.Text[..4000] + "…" : _snapshot.Text,
        sourceLength = _snapshot.Text.Length,
        hasImage = _snapshot.Image is not null,
        imageWidth = _snapshot.Image?.PixelWidth ?? 0,
        imageHeight = _snapshot.Image?.PixelHeight ?? 0,
        preview = _previewText,
        status = _statusText,
        canCopy = _canCopy,
        canPaste = _canPaste,
        pasteLabel = _pasteLabel
    });

    private AdvancedPasteAction SelectedAction() =>
        _selectedIndex >= 0 && _selectedIndex < Actions.Length
            ? Actions[_selectedIndex].Action
            : AdvancedPasteAction.PlainText;

    private ClipboardTextSnapshot CurrentSnapshot() =>
        _snapshot with { JpegQuality = (int)Math.Round(_jpegQuality) };

    private async void RefreshPreview()
    {
        var revision = ++_previewRevision;
        var action = SelectedAction();
        _jpegVisible = action == AdvancedPasteAction.ConvertImageToJpeg;
        var hasText = _snapshot.Text.Length > 0 || !string.IsNullOrWhiteSpace(_snapshot.Html);
        var requiresImage = action == AdvancedPasteAction.ImageToText || AdvancedPasteArtifacts.IsImageFileAction(action);
        _canCopy = false;
        _canPaste = false;
        _pasteLabel = AdvancedPasteArtifacts.IsFileAction(action) ? "Dosya yapıştır" : "Yapıştır";

        if (requiresImage && _snapshot.Image is null)
        {
            _previewText = "";
            _statusText = "Bu işlem için panoda görsel bulunmalı.";
            PushUiState();
            return;
        }
        if (!requiresImage && !hasText)
        {
            _previewText = "";
            _statusText = "Bu işlem için panoda metin veya HTML bulunmalı.";
            PushUiState();
            return;
        }

        _statusText = action == AdvancedPasteAction.ImageToText ? "Windows OCR çalışıyor…" : "Önizleme hazırlanıyor…";
        PushUiState();
        try
        {
            var result = action == AdvancedPasteAction.ImageToText
                ? await _recognizeImage(_snapshot.Image!)
                : AdvancedPasteTransforms.Transform(action, CurrentSnapshot());
            if (revision != _previewRevision) return;

            _previewText = result;
            if (action == AdvancedPasteAction.ImageToText && string.IsNullOrWhiteSpace(result))
            {
                _statusText = "Görselde metin bulunamadı.";
                PushUiState();
                return;
            }

            _statusText = AdvancedPasteArtifacts.IsFileAction(action)
                ? "Yerel dosya hazırlanıp panoya aktarılacak."
                : $"{result.Length:N0} karakter · yerel dönüşüm";
            _canCopy = result.Length > 0;
            _canPaste = result.Length > 0;
            PushUiState();
        }
        catch (Exception exception)
        {
            if (revision != _previewRevision) return;
            _previewText = "";
            _statusText = "Bu içerik seçilen biçime dönüştürülemedi: " + exception.Message;
            PushUiState();
        }
    }

}
