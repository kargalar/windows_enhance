using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class CursorRingOverlay : Window
{
    private readonly Ellipse _ring;
    private readonly DropShadowEffect _glow;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();

    private POINT _point;
    private int _size = 120;
    private int _duration = 800;
    private bool _followCursor;

    public CursorRingOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _glow = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.75 };

        _ring = new Ellipse
        {
            StrokeThickness = 3,
            Margin = new Thickness(6),
            Effect = _glow,
            IsHitTestVisible = false
        };

        Content = _ring;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Show(POINT point, Color color, int size, int durationMs, bool followCursor)
    {
        _point = point;
        _size = size;
        _duration = durationMs;
        _followCursor = followCursor;

        var brush = new SolidColorBrush(color);
        _ring.Stroke = brush;
        _ring.Fill = new SolidColorBrush(Color.FromArgb(26, color.R, color.G, color.B));
        _glow.Color = color;
        _ring.Opacity = 1;

        Width = size;
        Height = size;

        if (!IsVisible) Show();
        MoveToCursor();

        _clock.Restart();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Close();
    }

    private void Tick()
    {
        if (_followCursor)
        {
            Native.GetCursorPos(out var current);
            _point = current;
            MoveToCursor();
        }

        var elapsed = _clock.ElapsedMilliseconds;
        if (elapsed >= _duration)
        {
            _timer.Stop();
            Hide();
            return;
        }

        var fade = _duration - elapsed;
        if (fade < 500) _ring.Opacity = Math.Max(0, fade / 500.0);
    }

    private void MoveToCursor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var scale = Native.GetDpiForWindow(handle) / 96.0;
        if (scale <= 0) scale = 1.0;

        var pixels = Math.Max(8, (int)Math.Round(_size * scale));
        Native.SetWindowPos(handle, Native.HWND_TOPMOST,
            _point.X - pixels / 2, _point.Y - pixels / 2, pixels, pixels,
            Native.SWP_NOACTIVATE);
    }
}
