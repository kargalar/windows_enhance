using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public sealed class SelectionOverlay : Window
{
    private readonly Canvas canvas = new();
    private readonly Rectangle selection = new()
    {
        Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(35, 50, 160, 255)),
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };
    private readonly GlobalKeyboardHook escape = new();
    private Point? start;
    public Rect? Result { get; private set; }

    public SelectionOverlay(IFeatureUiService? featureUi)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(32, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        canvas.Background = Brushes.Transparent;
        canvas.Children.Add(selection);
        var hint = new TextBlock { Text = "Alanı seçmek için sürükle · Esc ile iptal",
            Foreground = Brushes.White, Background = Brushes.Black, Padding = new Thickness(12),
            FontSize = 16, IsHitTestVisible = false };
        canvas.Children.Add(hint);
        Content = canvas;
        SourceInitialized += (_, _) =>
        {
            // Virtual-screen metrics are physical pixels; avoid mixed-DPI WPF placement errors.
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, Native.HWND_TOPMOST,
                Native.GetSystemMetrics(76), Native.GetSystemMetrics(77),
                Native.GetSystemMetrics(78), Native.GetSystemMetrics(79), 0);
            Native.GetCursorPos(out var cursor);
            var point = PointFromScreen(new Point(cursor.X, cursor.Y));
            Canvas.SetLeft(hint, Math.Max(8, point.X - 100));
            Canvas.SetTop(hint, Math.Max(8, point.Y - 50));
        };
        Loaded += (_, _) => { Activate(); Focus(); Keyboard.Focus(this); escape.Install(); };
        escape.KeyStateChanged += (key, down) =>
        {
            if (key == 0x1B && down) Dispatcher.BeginInvoke(new Action(Close));
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        canvas.MouseRightButtonDown += (_, _) => Close();
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas);
            hint.Visibility = Visibility.Collapsed;
            canvas.CaptureMouse();
            Draw(start.Value);
        };
        canvas.MouseMove += (_, e) => { if (start.HasValue) Draw(e.GetPosition(canvas)); };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } origin) return;
            var end = e.GetPosition(canvas);
            canvas.ReleaseMouseCapture();
            start = null;
            if (Math.Abs(end.X - origin.X) < 6 || Math.Abs(end.Y - origin.Y) < 6) return;
            var first = PointToScreen(origin);
            var last = PointToScreen(end);
            Result = new Rect(first, last);
            Close();
        };
        Closed += (_, _) => { escape.Dispose(); canvas.ReleaseMouseCapture(); };
    }

    private void Draw(Point end)
    {
        if (start is not { } origin) return;
        Canvas.SetLeft(selection, Math.Min(origin.X, end.X));
        Canvas.SetTop(selection, Math.Min(origin.Y, end.Y));
        selection.Width = Math.Abs(end.X - origin.X);
        selection.Height = Math.Abs(end.Y - origin.Y);
        selection.Visibility = Visibility.Visible;
    }
}
