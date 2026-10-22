using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinForge.Modules.Extras;

public sealed class RulerOverlay : Window
{
    private readonly Canvas _canvas;
    private readonly Border _selection;
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private readonly Border _hint;
    private readonly string _unit;
    private Point? _origin;
    private bool _finished;

    public string? Result { get; private set; }

    public RulerOverlay(string unit)
    {
        _unit = unit;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(48, 4, 8, 16));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Focusable = true;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _selection = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(24, 76, 194, 255)),
            Visibility = Visibility.Collapsed
        };

        _labelText = new TextBlock
        {
            Text = "0 x 0",
            Foreground = Brushes.White,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
        };

        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 18, 21, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Child = _labelText,
            Visibility = Visibility.Collapsed
        };

        _hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 18, 21, 28)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 194, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 9, 16, 9),
            Child = new TextBlock
            {
                Text = "Ölçmek için sürükle   ·   ESC ile iptal",
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
            }
        };

        _canvas = new Canvas();
        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_label);
        _canvas.Children.Add(_hint);
        Content = _canvas;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);

            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_hint, Math.Max(0, (Width - _hint.DesiredSize.Width) / 2));
            Canvas.SetTop(_hint, 48);
        };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseRightButtonDown += (_, _) => Cancel();
        KeyDown += OnKeyDown;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _origin = e.GetPosition(_canvas);
        _hint.Visibility = Visibility.Collapsed;
        _selection.Visibility = Visibility.Visible;
        _label.Visibility = Visibility.Visible;

        Canvas.SetLeft(_selection, _origin.Value.X);
        Canvas.SetTop(_selection, _origin.Value.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_origin is null) return;

        var current = e.GetPosition(_canvas);
        var x = Math.Min(_origin.Value.X, current.X);
        var y = Math.Min(_origin.Value.Y, current.Y);
        var width = Math.Abs(current.X - _origin.Value.X);
        var height = Math.Abs(current.Y - _origin.Value.Y);

        Canvas.SetLeft(_selection, x);
        Canvas.SetTop(_selection, y);
        _selection.Width = width;
        _selection.Height = height;

        var (pixelWidth, pixelHeight) = ToPixels(width, height);
        _labelText.Text = Describe(pixelWidth, pixelHeight);

        Canvas.SetLeft(_label, x);
        Canvas.SetTop(_label, Math.Max(0, y - 30));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_origin is null) return;

        ReleaseMouseCapture();

        var width = _selection.Width;
        var height = _selection.Height;
        if (width < 4 || height < 4)
        {
            _origin = null;
            _selection.Visibility = Visibility.Collapsed;
            _label.Visibility = Visibility.Collapsed;
            _hint.Visibility = Visibility.Visible;
            return;
        }

        var (pixelWidth, pixelHeight) = ToPixels(width, height);
        Result = Describe(pixelWidth, pixelHeight);
        _finished = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancel();
    }

    private void Cancel()
    {
        _finished = true;
        Result = null;
        Close();
    }

    private (int Width, int Height) ToPixels(double width, double height)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return ((int)Math.Round(width * dpi.DpiScaleX), (int)Math.Round(height * dpi.DpiScaleY));
    }

    private string Describe(int width, int height)
    {
        if (string.Equals(_unit, "Santimetre", StringComparison.CurrentCultureIgnoreCase))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var scale = dpi.DpiScaleX * 96d;
            return $"{width / scale * 2.54:0.0} x {height / scale * 2.54:0.0} cm";
        }

        if (string.Equals(_unit, "İnç", StringComparison.CurrentCultureIgnoreCase))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var scale = dpi.DpiScaleX * 96d;
            return $"{width / scale:0.0} x {height / scale:0.0} in";
        }

        return $"{width} x {height} px";
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_finished) Result = null;
        base.OnClosed(e);
    }
}
