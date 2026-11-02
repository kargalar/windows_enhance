using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForge.Core;
using WinForge.Modules.Capture;

internal static class CaptureAnnotationChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var area = new CaptureArea(-300, 200, 240, 160);
        var pixels = Enumerable.Repeat((byte)255, area.Width * area.Height * 4).ToArray();
        var image = BitmapSource.Create(area.Width, area.Height, 144, 144, PixelFormats.Bgra32, null, pixels, area.Width * 4); image.Freeze();
        var marks = new CaptureAnnotations { Tool = DrawingTool.Freehand, Color = Colors.Red, Width = 6 };
        check(ReferenceEquals(marks.Apply(image, area), image), "An unmarked capture preserves the original image");
        Mark(new(-280, 220), new(-220, 220));
        var composite = marks.Apply(image, area);
        check(Pixel(composite, 40, 20) is { R: > 240, G: < 20, B: < 20 }, "Pre-capture pen strokes are burned into the final pixels at negative desktop origins");
        check(composite.PixelWidth == 240 && composite.PixelHeight == 160 && composite.DpiX == 96,
            "Composition uses physical pixels without stretching a high-DPI source");
        check(Pixel(composite, 5, 5) == Colors.White, "Pixels outside marks stay unchanged");
        marks.Undo(); check(!marks.HasMarks, "Undo removes the previous pre-capture mark");
        marks.Begin(new(-280, 220)); marks.Move(new(-220, 220)); marks.Cancel();
        check(!marks.HasMarks, "Losing pointer capture cancels an unfinished stroke");
        foreach (var (tool, x, y) in new[] { (DrawingTool.Rectangle, 30, 20), (DrawingTool.Ellipse, 50, 20), (DrawingTool.Arrow, 50, 40) })
        {
            marks.Clear(); marks.Tool = tool; Mark(new(-280, 220), new(-220, 260));
            check(Pixel(marks.Apply(image, area), x, y).G < 100, tool + " is included in the exported screenshot");
        }
        marks.Clear(); marks.Tool = DrawingTool.Line; Mark(new(-280, 220), new(-220, 220));
        check(Pixel(marks.Apply(image, area), 40, 20).G < 20, "Geometry lines are included in exported screenshots");
        marks.Clear(); marks.Text = "Test"; marks.AddText(new(-280, 220));
        var textImage = marks.Apply(image, area);
        check(Enumerable.Range(20, 40).Any(y => Enumerable.Range(20, 80).Any(x => Pixel(textImage, x, y).G < 200)), "Text annotations render into the captured image");
        marks.Undo(); check(!marks.HasMarks, "Text annotations can be undone");
        marks.Text = " "; marks.AddText(new(-280, 220)); check(!marks.HasMarks, "Empty text does not create an annotation");
        marks.Clear(); marks.Tool = DrawingTool.Highlight; Mark(new(-280, 250), new(-180, 250));
        var highlighted = Pixel(marks.Apply(image, area), 50, 50);
        check(highlighted.R > 240 && highlighted.G is > 100 and < 230, "Highlight stays translucent over the captured content");
        marks.Clear(); marks.Tool = DrawingTool.Freehand; Mark(new(-500, 250), new(-400, 250));
        check(Pixel(marks.Apply(image, area), 10, 50) == Colors.White, "Marks outside the selected region are clipped");
        marks.Clear(); Mark(new(-280, 220), new(-220, 220));
        var artifact = Path.Combine(root, "annotated.png"); Save(marks.Apply(image, area), artifact);
        var saved = CaptureServices.Load(artifact);
        check(Pixel(saved, 40, 20).G < 20, "PNG encoding preserves the pre-capture annotations");
        marks.Clear(); check(!marks.HasMarks, "Clear removes every annotation");
        CheckPicker(check);

        void Mark(Point start, Point end) { marks.Begin(start); marks.Move(end); marks.Complete(); }
    }

    private static void CheckPicker(Action<bool, string> check)
    {
        if (Application.Current is null)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/WinForge.CaptureTests;component/TestTheme.xaml", UriKind.Relative) });
        }
        CheckFrozenDesktop(check);
        var quickPicker = new RegionPicker(quickCapture: true);
        quickPicker.Show(); Pump();
        var quickSelection = new CaptureArea(10, 10, 100, 100);
        typeof(RegionPicker).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(quickPicker, [quickSelection]);
        check(quickPicker.Result == quickSelection && !quickPicker.IsVisible, "Quick capture finishes as soon as an area is selected");
        var picker = new RegionPicker(quickCapture: false, allowDrawing: true);
        picker.Show(); Pump();
        try
        {
            var surface = Application.Current!.Windows.Cast<Window>().First(window => window.GetType().Name == "RegionSurface");
            var canvas = (Canvas)surface.Content;
            var buttons = Descendants(canvas).OfType<Button>().ToArray();
            Button Tool(string name) => buttons.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == name);
            var primaryTools = buttons.Where(button => button.Content is System.Windows.Shapes.Path).ToArray();
            check(primaryTools.Length == 4, "The toolbar starts with exactly four icon tools");
            check(picker.Annotations!.Tool == DrawingTool.Select && primaryTools.Skip(1).All(button => System.Windows.Automation.AutomationProperties.GetItemStatus(button) == ""),
                "A capture starts in area selection with every drawing tool inactive");
            var options = Descendants(canvas).OfType<TextBox>().Single();
            check(!options.IsVisible, "Drawing options stay hidden until a drawing tool is chosen");
            Tool("Metin").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(options.IsVisible && picker.Annotations.Tool == DrawingTool.Text, "Text tool opens its text entry options");
            options.Text = "Screenshot note";
            check(picker.Annotations.Text == "Screenshot note", "Text entry updates the annotation text");
            Tool("Geometri").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var shapes = Descendants(canvas).OfType<ComboBox>().Single(box => box.SelectedValuePath == "Tag");
            shapes.SelectedValue = DrawingTool.Ellipse;
            check(picker.Annotations.Tool == DrawingTool.Ellipse && !options.IsVisible, "Geometry chooses a shape and hides text options");
            Tool("Kalem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(picker.Annotations.Tool == DrawingTool.Freehand, "The overlay switches from region selection to drawing");
            primaryTools[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(picker.Annotations.Tool == DrawingTool.Select && !shapes.IsVisible, "The first icon returns to area selection and hides drawing options");
            Tool("Kalem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var display = DesktopDisplays.ForWindow(new System.Windows.Interop.WindowInteropHelper(surface).Handle);
            var x = display.Bounds.Left; var y = display.Bounds.Top;
            picker.Annotations.Begin(new(x + 130, y + 230)); picker.Annotations.Move(new(x + 450, y + 340)); picker.Annotations.Complete();
            picker.Annotations.Tool = DrawingTool.Arrow;
            picker.Annotations.Begin(new(x + 480, y + 240)); picker.Annotations.Move(new(x + 600, y + 360)); picker.Annotations.Complete();
            var selection = new CaptureArea(x + 80, y + 180, 640, 300);
            typeof(RegionPicker).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, [selection]);
            typeof(RegionPicker).GetMethod("RefreshAnnotations", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null);
            // Render the overlay itself over a synthetic background; no desktop content is recorded.
            canvas.Background = Brushes.SlateGray;
            surface.UpdateLayout();
            var visual = new DrawingVisual();
            var width = Math.Min(1100, canvas.ActualWidth); var height = canvas.ActualHeight * width / canvas.ActualWidth;
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(canvas), null, new Rect(0, 0, width, height));
            var preview = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); preview.Render(visual);
            Directory.CreateDirectory("artifacts/capture-verification"); Save(preview, "artifacts/capture-verification/pre-capture-tools.png");
            buttons.Single(button => Equals(button.Content, "Yakala ↵")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(picker.Result == selection && picker.Annotations.HasMarks, "Confirming the selection retains its annotations after the overlays close");
        }
        finally { picker.Close(); }
        picker = new RegionPicker(quickCapture: false, allowDrawing: true); picker.Show(); Pump();
        var cancelSurface = Application.Current.Windows.Cast<Window>().First(window => window.GetType().Name == "RegionSurface");
        Descendants((DependencyObject)cancelSurface.Content).OfType<Button>().Single(button => Equals(button.Content, "İptal")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(picker.Result is null && !picker.Annotations!.HasMarks, "Cancelling discards the capture and a new session starts without previous marks");
    }

    private static void CheckFrozenDesktop(Action<bool, string> check)
    {
        BitmapSource Solid(Color color)
        {
            var pixels = new byte[80 * 60 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
            var source = BitmapSource.Create(80, 60, 144, 144, PixelFormats.Bgra32, null, pixels, 80 * 4); source.Freeze(); return source;
        }
        var snapshot = new FrozenDesktop(new[] { (new CaptureArea(-80, 0, 80, 60), Solid(Colors.Red)), (new CaptureArea(0, 0, 80, 60), Solid(Colors.Blue)) });
        var crop = snapshot.Crop(new(-20, 10, 40, 30));
        check(crop.PixelWidth == 40 && crop.PixelHeight == 30 && Pixel(crop, 5, 5) == Colors.Red && Pixel(crop, 35, 5) == Colors.Blue,
            "Frozen crops span monitors at negative origins using physical pixels at mixed DPI");
        var display = DesktopDisplays.Enumerate().First();
        var fixture = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            ResizeMode = ResizeMode.NoResize, Background = Brushes.Magenta, Topmost = true, Width = 180, Height = 160 };
        var picker = new RegionPicker(quickCapture: false, allowDrawing: true);
        try
        {
            fixture.Show();
            Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(fixture).Handle, Native.HWND_TOPMOST,
                display.Bounds.Left + 100, display.Bounds.Top + 150, 180, 160, 0x0040);
            fixture.UpdateLayout(); Pump();
            picker.Show(); Pump();
            var area = new CaptureArea(display.Bounds.Left + 130, display.Bounds.Top + 180, 80, 60);
            fixture.Background = Brushes.Lime; fixture.UpdateLayout(); Pump();
            var surface = Application.Current!.Windows.Cast<Window>().First(window => window.GetType().Name == "RegionSurface");
            check(((Canvas)surface.Content).Background is DrawingBrush,
                "The selection overlay displays an opaque frozen desktop backdrop");
            check(Pixel(picker.Snapshot!.Crop(area), 40, 30) == Colors.Magenta,
                "Changing the underlying window during selection preserves the opening frame");
            picker.Close(); Pump();
            check(Pixel(CaptureServices.Capture(area), 40, 30) == Colors.Lime,
                "The live desktop changed while the saved screenshot kept the original frame");
            check(Pixel(picker.Snapshot.Crop(area), 40, 30) == Colors.Magenta,
                "The opening frame remains available after closing the selection overlays");
        }
        finally { picker.Close(); fixture.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
