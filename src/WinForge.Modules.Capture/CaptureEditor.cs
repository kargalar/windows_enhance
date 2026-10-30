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

public enum DrawingTool { Freehand, Rectangle, Ellipse, Line, Arrow, Text, Step, Highlight, Blur, Pixelate, Redact, Crop }

public static class CaptureImageEffects
{
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
    private static readonly string[] ToolLabels = ["Serbest çizim", "Dikdörtgen", "Elips", "Çizgi", "Ok", "Metin", "Numaralı adım", "Vurgula", "Bulanıklaştır", "Pikselleştir", "Opak kapat", "Kırp"];
    private readonly Canvas canvas = new();
    private readonly ComboBox tools = new() { ItemsSource = ToolLabels, SelectedIndex = 0, Width = 150, Margin = new(4) };
    private readonly TextBox text = new() { Width = 150, Text = "Metin", Margin = new(4), ToolTip = "Metin içeriği" };
    private readonly ComboBox colors = new() { ItemsSource = new[] { "Kırmızı", "Mavi", "Yeşil", "Sarı", "Siyah", "Beyaz" }, SelectedIndex = 0, Margin = new(4) };
    private readonly Stack<BitmapSource> undo = [], redo = [];
    private readonly TextBlock status = new() { Margin = new(6) };
    private readonly HistoryStore store;
    private BitmapSource current;
    private Point? origin;
    private Shape? mark;
    private int step = 1;
    public CaptureEditor(HistoryStore store, BitmapSource image)
    {
        this.store = store; current = image; Title = "Ekran görüntüsünü düzenle"; Width = 1180; Height = 800; MinWidth = 850; CaptureUi.Theme(this);
        var root = new DockPanel { Margin = new(12) }; Content = root;
        var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(tools); toolbar.Children.Add(colors); toolbar.Children.Add(text);
        toolbar.Children.Add(CaptureUi.Button("Geri al", () => Restore(undo, redo))); toolbar.Children.Add(CaptureUi.Button("İleri al", () => Restore(redo, undo)));
        toolbar.Children.Add(CaptureUi.Button("Kopyala", () => Guard(() => Clipboard.SetImage(Snapshot()))));
        toolbar.Children.Add(CaptureUi.Button("Kaydet", Save));
        toolbar.Children.Add(CaptureUi.Button("Geçmişe kaydet", () => Guard(() => { var media = CaptureServices.Save(store, Snapshot()); store.Add("capture", DateTimeOffset.UtcNow, title: "Düzenlenmiş görüntü", media: media); status.Text = "Geçmişe kaydedildi."; })));
        toolbar.Children.Add(CaptureUi.Button("Sabitle", () => new ImagePreviewWindow(Snapshot(), true).Show()));
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
        origin = e.GetPosition(canvas); canvas.CaptureMouse();
        var tool = (DrawingTool)tools.SelectedIndex;
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
        if (origin is not { } start || mark is null) return;
        var point = e.GetPosition(canvas); point.X = Math.Clamp(point.X, 0, canvas.Width); point.Y = Math.Clamp(point.Y, 0, canvas.Height);
        if (mark is Polyline line) { line.Points.Add(point); return; }
        if (mark is Line segment) { segment.X1 = start.X; segment.Y1 = start.Y; segment.X2 = point.X; segment.Y2 = point.Y; return; }
        Canvas.SetLeft(mark, Math.Min(start.X, point.X)); Canvas.SetTop(mark, Math.Min(start.Y, point.Y)); mark.Width = Math.Abs(start.X - point.X); mark.Height = Math.Abs(start.Y - point.Y);
    }
    private async void End(object sender, MouseButtonEventArgs e)
    {
        if (origin is not { } start || mark is null) return;
        var end = e.GetPosition(canvas); origin = null; canvas.ReleaseMouseCapture(); var tool = (DrawingTool)tools.SelectedIndex;
        var before = current;
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
                current = tool == DrawingTool.Crop ? new CroppedBitmap(current, area) : await Task.Run(() => CaptureImageEffects.Apply(current, area, tool == DrawingTool.Blur));
                current.Freeze();
            }
            else current = Snapshot();
            undo.Push(before); redo.Clear(); ResetCanvas();
            var budget = 0L; var retained = undo.TakeWhile(frame => (budget += (long)frame.PixelWidth * frame.PixelHeight * 4) <= UndoBudgetBytes).Take(20).ToArray();
            if (retained.Length == 0) retained = [before];
            undo.Clear(); foreach (var frame in retained.Reverse()) undo.Push(frame);
        }
        catch (Exception error) { current = before; ResetCanvas(); status.Text = error.Message; }
        finally { mark = null; }
    }
    private void Restore(Stack<BitmapSource> source, Stack<BitmapSource> destination)
    { if (source.TryPop(out var image)) { destination.Push(current); current = image; ResetCanvas(); } }
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
