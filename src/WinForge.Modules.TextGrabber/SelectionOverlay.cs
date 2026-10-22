using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public sealed class SelectionOverlay : Window
{
    private readonly Canvas _canvas;
    private readonly Border _selection;
    private readonly Border _hint;
    private Point? _origin;
    private bool _finished;

    public Rect? Result { get; private set; }

    public SelectionOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(64, 4, 8, 16));
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
            Background = new SolidColorBrush(Color.FromArgb(32, 76, 194, 255)),
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
                Text = "Alanı seçmek için sürükle   ·   ESC ile iptal",
                Foreground = Brushes.White,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI")
            }
        };

        _canvas = new Canvas();
        _canvas.Children.Add(_selection);
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
        KeyDown += OnKeyDown;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _origin = e.GetPosition(_canvas);
        _hint.Visibility = Visibility.Collapsed;

        Canvas.SetLeft(_selection, _origin.Value.X);
        Canvas.SetTop(_selection, _origin.Value.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        _selection.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_origin is null) return;

        var current = e.GetPosition(_canvas);
        var x = Math.Min(_origin.Value.X, current.X);
        var y = Math.Min(_origin.Value.Y, current.Y);
        var w = Math.Abs(current.X - _origin.Value.X);
        var h = Math.Abs(current.Y - _origin.Value.Y);

        Canvas.SetLeft(_selection, x);
        Canvas.SetTop(_selection, y);
        _selection.Width = w;
        _selection.Height = h;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_origin is null) return;

        ReleaseMouseCapture();
        var local = new Rect(
            Canvas.GetLeft(_selection),
            Canvas.GetTop(_selection),
            _selection.Width,
            _selection.Height);

        if (local.Width < 6 || local.Height < 6)
        {
            _origin = null;
            _selection.Visibility = Visibility.Collapsed;
            _hint.Visibility = Visibility.Visible;
            return;
        }

        Result = ToScreenPixels(local);
        _finished = true;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        _finished = true;
        Result = null;
        Close();
    }

    private Rect ToScreenPixels(Rect local)
    {
        var handle = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(handle, out var windowRect);

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

        var topLeft = transform.Transform(local.TopLeft);
        var bottomRight = transform.Transform(local.BottomRight);

        return new Rect(
            windowRect.Left + topLeft.X,
            windowRect.Top + topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_finished) Result = null;
        base.OnClosed(e);
    }
}
