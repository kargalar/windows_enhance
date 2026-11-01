using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

public enum DrawingTool { Freehand, Rectangle, Ellipse, Line, Arrow, Text, Step, Highlight, Blur, Pixelate, Redact, Crop, Select }

public static class CaptureImageEffects
{
    public static BitmapSource ApplyPreset(BitmapSource source, string preset)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var stride = image.PixelWidth * 4;
        var data = new byte[stride * image.PixelHeight]; image.CopyPixels(data, stride, 0);
        for (var index = 0; index < data.Length; index += 4)
        {
            var blue = data[index]; var green = data[index + 1]; var red = data[index + 2];
            (data[index + 2], data[index + 1], data[index]) = preset switch
            {
                "grayscale" => Gray(red, green, blue),
                "sepia" => ((byte)Math.Clamp((int)(red * .393 + green * .769 + blue * .189), 0, 255), (byte)Math.Clamp((int)(red * .349 + green * .686 + blue * .168), 0, 255), (byte)Math.Clamp((int)(red * .272 + green * .534 + blue * .131), 0, 255)),
                "invert" => ((byte)(255 - red), (byte)(255 - green), (byte)(255 - blue)),
                _ => (red, green, blue)
            };
        }
        var output = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, data, stride); output.Freeze(); return output;
    }

    private static (byte Red, byte Green, byte Blue) Gray(byte red, byte green, byte blue)
    {
        var gray = (byte)Math.Clamp((int)(red * .2126 + green * .7152 + blue * .0722), 0, 255);
        return (gray, gray, gray);
    }

    public static BitmapSource Apply(BitmapSource source, Int32Rect area, bool blur)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var stride = image.PixelWidth * 4;
        var data = new byte[stride * image.PixelHeight]; image.CopyPixels(data, stride, 0); var original = (byte[])data.Clone();
        var size = blur ? 9 : 14;
        for (var y = area.Y; y < area.Y + area.Height; y += blur ? 1 : size)
            for (var x = area.X; x < area.X + area.Width; x += blur ? 1 : size)
            {
                var left = blur ? Math.Max(area.X, x - size / 2) : x; var top = blur ? Math.Max(area.Y, y - size / 2) : y;
                var right = Math.Min(area.X + area.Width, blur ? x + size / 2 + 1 : x + size); var bottom = Math.Min(area.Y + area.Height, blur ? y + size / 2 + 1 : y + size);
                var sum = new int[3]; var count = (right - left) * (bottom - top);
                for (var sy = top; sy < bottom; sy++) for (var sx = left; sx < right; sx++) for (var c = 0; c < 3; c++) sum[c] += original[sy * stride + sx * 4 + c];
                for (var dy = y; dy < (blur ? y + 1 : bottom); dy++) for (var dx = x; dx < (blur ? x + 1 : right); dx++)
                { var index = dy * stride + dx * 4; for (var c = 0; c < 3; c++) data[index + c] = (byte)(sum[c] / count); data[index + 3] = 255; }
            }
        var output = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, data, stride); output.Freeze(); return output;
    }
}

internal sealed class CaptureEditor
{
    private readonly HistoryStore _store;
    private readonly IFeatureUiService? _featureUi;
    private readonly TaskCompletionSource<BitmapSource?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IFeatureUiWindow? _window;
    private readonly string _image;
    private readonly int _initialTool;
    private readonly bool _workflowCompletion;
    private string _status = string.Empty;

    public CaptureEditor(HistoryStore store, BitmapSource image, DrawingTool? initialTool = null, bool workflowCompletion = false,
        IFeatureUiService? featureUi = null)
    {
        _store = store;
        _featureUi = featureUi;
        _initialTool = (int)(initialTool ?? DrawingTool.Freehand);
        _workflowCompletion = workflowCompletion;
        _image = ToDataUri(image);
        Completion = _completion.Task;
        if (featureUi is null)
        {
            _completion.TrySetResult(null);
            return;
        }

        _window = featureUi.Open("capture-editor-" + Guid.NewGuid().ToString("N"), "capture-editor", "Ekran görüntüsünü düzenle",
            new FeatureUiWindowOptions(1180, 800, Resizable: true, ShowInTaskbar: true));
        _window.MessageReceived += OnMessage;
        _window.ClosedByHost += () => _completion.TrySetResult(null);
        _window.SetState(new { image = _image, initialTool = _initialTool, workflowCompletion = _workflowCompletion, status = _status });
    }

    public Task<BitmapSource?> Completion { get; }
    public void Show() => _window?.Show();
    public void Close()
    {
        _completion.TrySetResult(null);
        _window?.Close();
    }

    private async void OnMessage(string command, System.Text.Json.JsonElement payload)
    {
        try
        {
            switch (command)
            {
                case "capture-editor.copy":
                    Clipboard.SetImage(ReadImage(payload));
                    SetStatus("Görsel panoya kopyalandı.");
                    break;
                case "capture-editor.save-history":
                    var media = CaptureServices.Save(_store, ReadImage(payload));
                    _store.Add("capture", DateTimeOffset.UtcNow, title: "Düzenlenmiş görüntü", media: media);
                    SetStatus("Geçmişe kaydedildi.");
                    break;
                case "capture-editor.save-file":
                    SaveFile(ReadImage(payload));
                    break;
                case "capture-editor.pin":
                    new ImagePreviewWindow(ReadImage(payload), true, _featureUi);
                    break;
                case "capture-editor.ocr":
                    var text = await OcrRecognitionService.RecognizeAsync(ReadImage(payload));
                    Clipboard.SetText(text);
                    SetStatus("OCR metni panoya kopyalandı.");
                    break;
                case "capture-editor.complete":
                    _completion.TrySetResult(ReadImage(payload));
                    _window?.Close();
                    break;
                case "capture-editor.close":
                    _completion.TrySetResult(null);
                    _window?.Close();
                    break;
            }
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private void SetStatus(string status)
    {
        _status = status;
        _window?.SetState(new { image = _image, initialTool = _initialTool, workflowCompletion = _workflowCompletion, status = _status });
    }

    private static BitmapSource ReadImage(System.Text.Json.JsonElement payload)
    {
        var value = payload.GetProperty("image").GetString() ?? throw new InvalidDataException("Düzenlenmiş görüntü alınamadı.");
        var separator = value.IndexOf(',');
        if (separator < 0) throw new InvalidDataException("Görüntü biçimi geçersiz.");
        using var stream = new MemoryStream(Convert.FromBase64String(value[(separator + 1)..]));
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        image.Freeze();
        return image;
    }

    private static string ToDataUri(BitmapSource image)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private void SaveFile(BitmapSource image)
    {
        var dialog = new SaveFileDialog { Filter = "PNG görüntüsü|*.png|JPEG görüntüsü|*.jpg", FileName = "WinForge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") };
        if (dialog.ShowDialog() != true) return;
        BitmapEncoder encoder = dialog.FilterIndex == 2 ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(dialog.FileName);
        encoder.Save(output);
        SetStatus("Görüntü kaydedildi.");
    }
}
internal sealed class ImagePreviewWindow
{
    public ImagePreviewWindow(BitmapSource image, bool pinned, IFeatureUiService? featureUi)
    {
        if (featureUi is null) return;
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        var data = "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        var window = featureUi.Open("capture-image-preview-" + Guid.NewGuid().ToString("N"), "capture-image-preview",
            pinned ? "Sabit görüntü" : "Görsel önizlemesi",
            new FeatureUiWindowOptions(pinned ? 700 : 1100, pinned ? 500 : 750, Topmost: pinned, Resizable: true, ShowInTaskbar: !pinned));
        window.SetState(new { image = data, pinned });
        window.MessageReceived += (command, _) => { if (command == "capture-image-preview.close") window.Close(); };
    }
}
