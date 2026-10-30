using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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

internal sealed class CaptureEditor : Window
{
    private const long UndoBudgetBytes = 128L * 1024 * 1024;
    private static readonly string[] ToolLabels = ["Serbest çizim", "Dikdörtgen", "Elips", "Çizgi", "Ok", "Metin", "Numaralı adım", "Vurgula", "Bulanıklaştır", "Pikselleştir", "Opak kapat", "Kırp", "Seç / taşı"];
    private readonly Canvas canvas = new();
    private readonly ComboBox tools = new() { ItemsSource = ToolLabels, SelectedIndex = 0, Width = 150, Margin = new(4) };
    private readonly TextBox text = new() { Width = 150, Text = "Metin", Margin = new(4), ToolTip = "Metin içeriği" };
    private readonly ComboBox colors = new() { ItemsSource = new[] { "Kırmızı", "Mavi", "Yeşil", "Sarı", "Siyah", "Beyaz" }, SelectedIndex = 0, Margin = new(4) };
    private readonly Stack<BitmapSource> undo = [], redo = [];
    private readonly TextBlock status = new() { Margin = new(6) };
    private readonly HistoryStore store;
    private BitmapSource current;
    public BitmapSource? CompletedImage { get; private set; }
    private Point? origin;
    private Shape? mark;
    private FrameworkElement? selectedMark;
    private Matrix selectedBaseTransform;
    private Rect selectedBounds;
    private bool resizingMark;
    private BitmapSource? beforeAction;
    private int step = 1;
    public CaptureEditor(HistoryStore store, BitmapSource image, DrawingTool? initialTool = null, bool workflowCompletion = false)
    {
        this.store = store; current = image; Title = "Ekran görüntüsünü düzenle"; Width = 1180; Height = 800; MinWidth = 850; CaptureUi.Theme(this);
        if (initialTool is { } selectedTool) tools.SelectedIndex = (int)selectedTool;
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(tools);
        toolbar.Children.Add(CaptureUi.Button("Metin yaz", () => SelectTool(DrawingTool.Text)));
        toolbar.Children.Add(CaptureUi.Button("Şekil çiz", () => SelectTool(DrawingTool.Rectangle)));
        toolbar.Children.Add(CaptureUi.Button("Serbest çiz", () => SelectTool(DrawingTool.Freehand)));
        toolbar.Children.Add(CaptureUi.Button("Seç / taşı", () => SelectTool(DrawingTool.Select)));
        toolbar.Children.Add(CaptureUi.Button("Alanı seç / kırp", () => SelectTool(DrawingTool.Crop)));
        toolbar.Children.Add(colors); toolbar.Children.Add(text);
        toolbar.Children.Add(CaptureUi.Button("Geri al", () => Restore(undo, redo))); toolbar.Children.Add(CaptureUi.Button("İleri al", () => Restore(redo, undo)));
        toolbar.Children.Add(CaptureUi.Button("Seçileni sil", DeleteSelected));
        toolbar.Children.Add(CaptureUi.Button("Öne al", () => ReorderSelected(true)));
        toolbar.Children.Add(CaptureUi.Button("Arkaya al", () => ReorderSelected(false)));
        toolbar.Children.Add(CaptureUi.Button("Kopyala", () => Guard(() => Clipboard.SetImage(Snapshot()))));
        toolbar.Children.Add(CaptureUi.Button("Kaydet", Save));
        toolbar.Children.Add(CaptureUi.Button("Geçmişe kaydet", () => Guard(() => { var media = CaptureServices.Save(store, Snapshot()); store.Add("capture", DateTimeOffset.UtcNow, title: "Düzenlenmiş görüntü", media: media); status.Text = "Geçmişe kaydedildi."; })));
        toolbar.Children.Add(CaptureUi.Button("Sabitle", () => new ImagePreviewWindow(Snapshot(), true).Show()));
        if (workflowCompletion) toolbar.Children.Add(CaptureUi.Button("Düzenlemeyi tamamla", CompleteEditing));
        var effects = new ContextMenu();
        foreach (var (preset, label) in new[] { ("grayscale", "Siyah-beyaz"), ("sepia", "Sepya"), ("invert", "Renkleri ters çevir") })
        {
            var item = new MenuItem { Header = label }; item.Click += (_, _) => ApplyEffect(preset); effects.Items.Add(item);
        }
        var effectButton = CaptureUi.Button("Görsel efektleri", () => effects.IsOpen = true); effectButton.ContextMenu = effects; toolbar.Children.Add(effectButton);
        toolbar.Children.Add(CaptureUi.Button("OCR", async () =>
        {
            try { var result = await OcrRecognitionService.RecognizeAsync(Snapshot()); Clipboard.SetText(result); status.Text = "OCR metni panoya kopyalandı."; }
            catch (Exception error) { status.Text = error.Message; }
        }));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var view = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        root.Children.Add(new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        ResetCanvas(); canvas.MouseLeftButtonDown += Begin; canvas.MouseMove += Move; canvas.MouseLeftButtonUp += End;
        KeyDown += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) Restore(undo, redo); };
    }
    private void SelectTool(DrawingTool tool) => tools.SelectedIndex = (int)tool;
    private Brush Ink => colors.SelectedIndex switch { 1 => Brushes.DodgerBlue, 2 => Brushes.LimeGreen, 3 => Brushes.Gold, 4 => Brushes.Black, 5 => Brushes.White, _ => Brushes.Red };
    private void ResetCanvas()
    {
        canvas.Children.Clear(); canvas.Width = current.PixelWidth; canvas.Height = current.PixelHeight;
        canvas.Children.Add(new Image { Source = current, Width = canvas.Width, Height = canvas.Height });
    }
    internal BitmapSource Snapshot()
    {
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); bitmap.Freeze(); return bitmap;
    }
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(canvas);
        var tool = (DrawingTool)tools.SelectedIndex;
        if (tool == DrawingTool.Select)
        {
            selectedMark = canvas.InputHitTest(point) as FrameworkElement;
            if (selectedMark is null or Image) return;
            selectedBounds = selectedMark.TransformToAncestor(canvas).TransformBounds(new Rect(selectedMark.RenderSize));
            selectedBaseTransform = selectedMark.RenderTransform.Value;
            resizingMark = Math.Abs(point.X - selectedBounds.Right) <= 12 && Math.Abs(point.Y - selectedBounds.Bottom) <= 12;
            origin = point; beforeAction = Snapshot(); canvas.CaptureMouse(); return;
        }
        selectedMark = null; origin = point; beforeAction = Snapshot(); canvas.CaptureMouse();
        mark = tool switch
        {
            DrawingTool.Freehand => new Polyline { Stroke = Ink, StrokeThickness = 4 },
            DrawingTool.Ellipse => new Ellipse { Stroke = Ink, StrokeThickness = 4 },
            DrawingTool.Line or DrawingTool.Arrow => new Line { Stroke = Ink, StrokeThickness = 4 },
            _ => new Rectangle { Stroke = Ink, StrokeThickness = 3, Fill = tool == DrawingTool.Redact ? Brushes.Black : tool == DrawingTool.Highlight ? Brushes.Yellow : Brushes.Transparent, Opacity = tool == DrawingTool.Highlight ? 0.4 : 1 }
        };
        canvas.Children.Add(mark); Move(sender, e);
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (origin is not { } start) return;
        var point = e.GetPosition(canvas); point.X = Math.Clamp(point.X, 0, canvas.Width); point.Y = Math.Clamp(point.Y, 0, canvas.Height);
        if (selectedMark is { } selected)
        {
            if (resizingMark && selected is Shape or TextBlock)
            {
                selected.Width = Math.Max(8, selectedBounds.Width + point.X - start.X);
                selected.Height = Math.Max(8, selectedBounds.Height + point.Y - start.Y);
            }
            else
            {
                var transform = selectedBaseTransform; transform.Translate(point.X - start.X, point.Y - start.Y);
                selected.RenderTransform = new MatrixTransform(transform);
            }
            return;
        }
        if (mark is null) return;
        if (mark is Polyline line) { line.Points.Add(point); return; }
        if (mark is Line segment) { segment.X1 = start.X; segment.Y1 = start.Y; segment.X2 = point.X; segment.Y2 = point.Y; return; }
        Canvas.SetLeft(mark, Math.Min(start.X, point.X)); Canvas.SetTop(mark, Math.Min(start.Y, point.Y)); mark.Width = Math.Abs(start.X - point.X); mark.Height = Math.Abs(start.Y - point.Y);
    }
    private async void End(object sender, MouseButtonEventArgs e)
    {
        if (origin is not { } start) return;
        var end = e.GetPosition(canvas); origin = null; canvas.ReleaseMouseCapture(); var tool = (DrawingTool)tools.SelectedIndex;
        var before = beforeAction ?? current; beforeAction = null;
        if (tool == DrawingTool.Select)
        {
            if (selectedMark is not null && (Math.Abs(end.X - start.X) > 1 || Math.Abs(end.Y - start.Y) > 1))
            { undo.Push(before); redo.Clear(); TrimUndo(); }
            resizingMark = false; return;
        }
        if (mark is null) return;
        try
        {
            var x = (int)Math.Clamp(Math.Min(start.X, end.X), 0, current.PixelWidth - 1); var y = (int)Math.Clamp(Math.Min(start.Y, end.Y), 0, current.PixelHeight - 1);
            var area = new Int32Rect(x, y, Math.Clamp((int)Math.Abs(end.X - start.X), 1, current.PixelWidth - x), Math.Clamp((int)Math.Abs(end.Y - start.Y), 1, current.PixelHeight - y));
            if (tool is DrawingTool.Text or DrawingTool.Step)
            {
                canvas.Children.Remove(mark); var label = new TextBlock { Text = tool == DrawingTool.Step ? (step++).ToString() : text.Text, Foreground = Ink, FontSize = tool == DrawingTool.Step ? 32 : 24, FontWeight = FontWeights.Bold, Background = tool == DrawingTool.Step ? Brushes.White : Brushes.Transparent };
                Canvas.SetLeft(label, start.X); Canvas.SetTop(label, start.Y); canvas.Children.Add(label);
            }
            if (tool == DrawingTool.Arrow)
            {
                var vector = start - end; vector.Normalize(); var normal = new Vector(-vector.Y, vector.X);
                if (!double.IsNaN(vector.X)) canvas.Children.Add(new Polyline { Stroke = Ink, StrokeThickness = 4, Points = [end + vector * 18 + normal * 9, end, end + vector * 18 - normal * 9] });
            }
            if (tool is DrawingTool.Blur or DrawingTool.Pixelate or DrawingTool.Crop)
            {
                canvas.Children.Remove(mark);
                var flattened = Snapshot();
                current = tool == DrawingTool.Crop ? new CroppedBitmap(flattened, area) : await Task.Run(() => CaptureImageEffects.Apply(flattened, area, tool == DrawingTool.Blur));
                current.Freeze();
            }
            undo.Push(before); redo.Clear();
            if (tool is DrawingTool.Blur or DrawingTool.Pixelate or DrawingTool.Crop) ResetCanvas();
            TrimUndo();
        }
        catch (Exception error) { current = before; ResetCanvas(); status.Text = error.Message; }
        finally { mark = null; }
    }
    private void TrimUndo()
    {
        var budget = 0L; var retained = undo.TakeWhile(frame => (budget += (long)frame.PixelWidth * frame.PixelHeight * 4) <= UndoBudgetBytes).Take(20).ToArray();
        if (retained.Length == 0 && undo.TryPeek(out var latest)) retained = [latest];
        undo.Clear(); foreach (var frame in retained.Reverse()) undo.Push(frame);
    }
    private void Restore(Stack<BitmapSource> source, Stack<BitmapSource> destination)
    { if (source.TryPop(out var image)) { destination.Push(Snapshot()); current = image; ResetCanvas(); } }
    private void ApplyEffect(string preset)
    {
        try { var before = Snapshot(); var output = CaptureImageEffects.ApplyPreset(before, preset); undo.Push(before); redo.Clear(); current = output; ResetCanvas(); TrimUndo(); status.Text = "Görsel efekti uygulandı; geri al ile kaldırabilirsiniz."; }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void CompleteEditing()
    {
        CompletedImage = Snapshot();
        DialogResult = true;
    }
    private void DeleteSelected()
    {
        if (selectedMark is not { } selected || !canvas.Children.Contains(selected)) return;
        undo.Push(Snapshot()); redo.Clear(); canvas.Children.Remove(selected); selectedMark = null; TrimUndo();
    }
    private void ReorderSelected(bool toFront)
    {
        if (selectedMark is not { } selected || !canvas.Children.Contains(selected)) return;
        undo.Push(Snapshot()); redo.Clear(); canvas.Children.Remove(selected);
        if (toFront) canvas.Children.Add(selected); else canvas.Children.Insert(Math.Min(1, canvas.Children.Count), selected);
        TrimUndo();
    }
    private void Save()
    {
        var dialog = new SaveFileDialog { Filter = "PNG görüntüsü|*.png|JPEG görüntüsü|*.jpg", FileName = "WinForge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() =>
        {
            BitmapEncoder encoder = dialog.FilterIndex == 2 ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Snapshot()));
            using var output = File.Create(dialog.FileName); encoder.Save(output); status.Text = "Görüntü kaydedildi.";
        });
    }
    private void Guard(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
}

internal sealed class ImagePreviewWindow : Window
{
    public ImagePreviewWindow(BitmapSource image, bool pinned)
    {
        Title = pinned ? "Sabit görüntü · Esc ile kapat" : "Görsel önizlemesi";
        Width = Math.Min(pinned ? 700 : 1100, SystemParameters.WorkArea.Width - 60); Height = Math.Min(pinned ? 500 : 750, SystemParameters.WorkArea.Height - 60);
        Topmost = pinned; Content = new Image { Source = image, Stretch = Stretch.Uniform }; CaptureUi.Theme(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
