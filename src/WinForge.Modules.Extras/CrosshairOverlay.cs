using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class CrosshairOverlay : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private readonly Canvas _canvas = new();
    private readonly DispatcherTimer _timer;
    private readonly List<Line> _lines = new();
    private Color _color = Colors.White;
    private int _opacity = 75;
    private int _centerRadius = 20;
    private int _thickness = 5;
    private int _borderSize = 2;
    private int _fixedLength;
    private POINT _cursor;
    private RECT _monitor;
    private double _scale = 1;

    public CrosshairOverlay()
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
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += (_, _) => UpdateCursor();
    }

    public void Start(Color color, int opacity, int centerRadius, int thickness, int borderSize, int fixedLength)
    {
        _color = color;
        _opacity = Math.Clamp(opacity, 10, 100);
        _centerRadius = Math.Clamp(centerRadius, 0, 100);
        _thickness = Math.Clamp(thickness, 1, 20);
        _borderSize = Math.Clamp(borderSize, 0, 10);
        _fixedLength = Math.Clamp(fixedLength, 0, 4000);
        if (!IsVisible) Show();
        UpdateCursor();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        if (IsVisible) Hide();
    }

    private void UpdateCursor()
    {
        if (!Native.GetCursorPos(out _cursor)) return;
        var monitor = MonitorFromPoint(_cursor, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        _monitor = info.Monitor;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var dpi = Native.GetDpiForWindow(handle);
        _scale = dpi == 0 ? 1 : dpi / 96.0;
        Width = (_monitor.Right - _monitor.Left) / _scale;
        Height = (_monitor.Bottom - _monitor.Top) / _scale;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, _monitor.Left, _monitor.Top,
            _monitor.Right - _monitor.Left, _monitor.Bottom - _monitor.Top, SwpNoActivate);
        DrawCrosshairs();
    }

    private void DrawCrosshairs()
    {
        _canvas.Children.Clear();
        _lines.Clear();

        var width = Width;
        var height = Height;
        var centerX = (_cursor.X - _monitor.Left) / _scale;
        var centerY = (_cursor.Y - _monitor.Top) / _scale;
        var radius = _centerRadius;
        var halfLength = _fixedLength == 0 ? double.PositiveInfinity : _fixedLength / 2.0;
        var horizontalStart = Math.Max(0, centerX - halfLength);
        var horizontalEnd = Math.Min(width, centerX + halfLength);
        var verticalStart = Math.Max(0, centerY - halfLength);
        var verticalEnd = Math.Min(height, centerY + halfLength);

        AddSegment(horizontalStart, centerY, Math.Max(horizontalStart, centerX - radius), centerY);
        AddSegment(Math.Min(horizontalEnd, centerX + radius), centerY, horizontalEnd, centerY);
        AddSegment(centerX, verticalStart, centerX, Math.Max(verticalStart, centerY - radius));
        AddSegment(centerX, Math.Min(verticalEnd, centerY + radius), centerX, verticalEnd);

        foreach (var line in _lines)
        {
            _canvas.Children.Add(line);
            Panel.SetZIndex(line, 0);
        }

        if (IsVisible)
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, Native.HWND_TOPMOST, _monitor.Left, _monitor.Top,
                _monitor.Right - _monitor.Left, _monitor.Bottom - _monitor.Top, SwpNoActivate);
        }
    }

    private void AddSegment(double x1, double y1, double x2, double y2)
    {
        if (x2 <= x1 && y2 <= y1) return;
        var borderColor = Color.FromArgb((byte)_opacity, 0, 0, 0);
        if (_borderSize > 0)
            _lines.Add(CreateLine(x1, y1, x2, y2, borderColor, _thickness + _borderSize * 2));

        var color = Color.FromArgb((byte)Math.Round(255 * _opacity / 100.0), _color.R, _color.G, _color.B);
        _lines.Add(CreateLine(x1, y1, x2, y2, color, _thickness));
    }

    private static Line CreateLine(double x1, double y1, double x2, double y2, Color color, int thickness) => new()
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = new SolidColorBrush(color),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Square,
        StrokeEndLineCap = PenLineCap.Square,
        IsHitTestVisible = false
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
