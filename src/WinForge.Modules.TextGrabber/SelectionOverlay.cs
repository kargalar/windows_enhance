using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public sealed class SelectionOverlay : Window
{
    private const int MinimumSelectionPixels = 4;
    private const uint SwpShowWindow = 0x0040;
    private readonly Canvas _canvas = new();
    private readonly GlobalKeyboardHook _escape = new();
    private readonly List<MonitorSurface> _surfaces = [];
    private IReadOnlyList<FrozenFrame> _frames = [];
    private POINT? _dragOrigin;
    private IDisposable? _translationGuard;
    private bool _closed;

    public Rect? Result { get; private set; }
    public (byte[] Pixels, int Width, int Height)? CapturedPixels { get; private set; }

    public SelectionOverlay(IFeatureUiService? featureUi)
    {
        _ = featureUi;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Opacity = 0;
        Width = 0;
        Height = 0;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Cancel();
        };

        _escape.KeyStateChanged += (key, down) =>
        {
            if (key == 0x1B && down && !_closed)
                Dispatcher.BeginInvoke(new Action(Cancel));
        };

        Loaded += (_, _) => InitializeSurfaces();
        Closed += (_, _) => CleanupSurfaces();
    }

    private void InitializeSurfaces()
    {
        if (_closed) return;
        _translationGuard ??= SelectionTranslationGuard.EnterOverlay();

        // If an owner window (such as ClipboardShelf) just hid itself, let DWM finish removing it first.
        if (Owner is not null)
            _ = Native.DwmFlush();

        _frames = CaptureAllDisplays();
        _escape.Install();

        Native.GetCursorPos(out var initialCursor);
        foreach (var frame in _frames)
        {
            var surface = new MonitorSurface(this, frame);
            _surfaces.Add(surface);
            surface.Show();
        }

        if (_surfaces.Count > 0)
        {
            var active = _surfaces.FirstOrDefault(s => s.ContainsPhysicalPoint(initialCursor)) ?? _surfaces[0];
            active.Activate();
            active.Focus();
        }
    }

    private static IReadOnlyList<FrozenFrame> CaptureAllDisplays()
    {
        var displays = DesktopDisplays.Enumerate();
        if (displays.Count == 0)
        {
            var vx = Native.GetSystemMetrics(76);
            var vy = Native.GetSystemMetrics(77);
            var vw = Native.GetSystemMetrics(78);
            var vh = Native.GetSystemMetrics(79);
            var fallbackRect = new RECT { Left = vx, Top = vy, Right = vx + vw, Bottom = vy + vh };
            return [CaptureFrame(fallbackRect)];
        }

        var frames = new List<FrozenFrame>(displays.Count);
        foreach (var display in displays)
        {
            var w = display.Bounds.Right - display.Bounds.Left;
            var h = display.Bounds.Bottom - display.Bounds.Top;
            if (w <= 0 || h <= 0) continue;
            frames.Add(CaptureFrame(display.Bounds));
        }
        return frames;
    }

    private static FrozenFrame CaptureFrame(RECT bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var (pixels, w, h) = DesktopCapture.Capture(bounds.Left, bounds.Top, width, height);
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, checked(w * 4));
        image.Freeze();
        return new FrozenFrame(bounds, pixels, w, h, image);
    }

    internal void BeginDrag(POINT origin)
    {
        _dragOrigin = ClampToCapturedBounds(origin);
        UpdateDrag(_dragOrigin.Value);
    }

    internal void UpdateDrag(POINT current)
    {
        if (_dragOrigin is not { } origin) return;
        var clamped = ClampToCapturedBounds(current);
        var left = Math.Min(origin.X, clamped.X);
        var top = Math.Min(origin.Y, clamped.Y);
        var right = Math.Max(origin.X, clamped.X);
        var bottom = Math.Max(origin.Y, clamped.Y);
        foreach (var surface in _surfaces)
            surface.UpdateSelection(left, top, right, bottom, clamped);
    }

    internal void CompleteDrag(POINT end)
    {
        if (_dragOrigin is not { } origin) return;
        _dragOrigin = null;
        var clamped = ClampToCapturedBounds(end);
        var left = Math.Min(origin.X, clamped.X);
        var top = Math.Min(origin.Y, clamped.Y);
        var right = Math.Max(origin.X, clamped.X);
        var bottom = Math.Max(origin.Y, clamped.Y);
        var width = right - left;
        var height = bottom - top;

        if (width < MinimumSelectionPixels || height < MinimumSelectionPixels)
        {
            foreach (var surface in _surfaces)
                surface.ClearSelection();
            return;
        }

        Result = new Rect(left, top, width, height);
        CapturedPixels = CropFromFrozenFrames(left, top, width, height);
        Close();
    }

    internal void Cancel()
    {
        if (_closed) return;
        Result = null;
        CapturedPixels = null;
        Close();
    }

    private POINT ClampToCapturedBounds(POINT point)
    {
        if (_frames.Count == 0) return point;
        var minX = _frames.Min(f => f.Bounds.Left);
        var minY = _frames.Min(f => f.Bounds.Top);
        var maxX = _frames.Max(f => f.Bounds.Right);
        var maxY = _frames.Max(f => f.Bounds.Bottom);
        return new POINT(Math.Clamp(point.X, minX, maxX), Math.Clamp(point.Y, minY, maxY));
    }

    private (byte[] Pixels, int Width, int Height) CropFromFrozenFrames(int left, int top, int width, int height)
    {
        var output = new byte[checked(width * height * 4)];
        var dstStride = checked(width * 4);

        foreach (var frame in _frames)
        {
            var ix0 = Math.Max(left, frame.Bounds.Left);
            var iy0 = Math.Max(top, frame.Bounds.Top);
            var ix1 = Math.Min(left + width, frame.Bounds.Right);
            var iy1 = Math.Min(top + height, frame.Bounds.Bottom);
            if (ix1 <= ix0 || iy1 <= iy0) continue;

            var copyWidthBytes = (ix1 - ix0) * 4;
            var srcStride = frame.Width * 4;
            var srcX = ix0 - frame.Bounds.Left;
            var dstX = ix0 - left;

            for (var y = iy0; y < iy1; y++)
            {
                var srcOffset = (y - frame.Bounds.Top) * srcStride + srcX * 4;
                var dstOffset = (y - top) * dstStride + dstX * 4;
                Buffer.BlockCopy(frame.Pixels, srcOffset, output, dstOffset, copyWidthBytes);
            }
        }

        return (output, width, height);
    }

    private void CleanupSurfaces()
    {
        _closed = true;
        _translationGuard?.Dispose();
        _translationGuard = null;
        _escape.Dispose();
        foreach (var surface in _surfaces)
        {
            try { surface.Close(); }
            catch { }
        }
        _surfaces.Clear();
    }

    private sealed record FrozenFrame(RECT Bounds, byte[] Pixels, int Width, int Height, BitmapSource Image);

    private sealed class MonitorSurface : Window
    {
        private readonly SelectionOverlay _owner;
        private readonly FrozenFrame _frame;
        private readonly Canvas _surfaceCanvas = new() { Background = Brushes.Transparent };
        private readonly System.Windows.Shapes.Path _dimMask = new()
        {
            Fill = new SolidColorBrush(Color.FromArgb(92, 0, 0, 0)),
            IsHitTestVisible = false
        };
        private readonly Rectangle _selectionBorder = new()
        {
            Stroke = Brushes.DeepSkyBlue,
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        private readonly Border _sizeBadge = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3, 7, 3),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        private readonly TextBlock _sizeText = new()
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        private Matrix _fromDevice = Matrix.Identity;
        private bool _draggingHere;

        public MonitorSurface(SelectionOverlay owner, FrozenFrame frame)
        {
            _owner = owner;
            _frame = frame;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Cursor = Cursors.Cross;
            Focusable = true;

            var backdrop = new Image
            {
                Source = frame.Image,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            _surfaceCanvas.Children.Add(backdrop);
            _surfaceCanvas.Children.Add(_dimMask);
            _surfaceCanvas.Children.Add(_selectionBorder);
            _sizeBadge.Child = _sizeText;
            _surfaceCanvas.Children.Add(_sizeBadge);

            Content = _surfaceCanvas;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST,
                    _frame.Bounds.Left, _frame.Bounds.Top, _frame.Width, _frame.Height, SwpShowWindow);
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget is not null)
                    _fromDevice = source.CompositionTarget.TransformFromDevice;

                var dipW = _frame.Width * _fromDevice.M11;
                var dipH = _frame.Height * _fromDevice.M22;
                backdrop.Width = dipW;
                backdrop.Height = dipH;
                ResetDimMask(dipW, dipH);

            };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                _owner.Cancel();
            };

            MouseRightButtonDown += (_, e) =>
            {
                e.Handled = true;
                _owner.Cancel();
            };

            MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _draggingHere = true;
                CaptureMouse();
                Native.GetCursorPos(out var pt);
                _owner.BeginDrag(pt);
            };

            MouseMove += (_, _) =>
            {
                if (!_draggingHere) return;
                Native.GetCursorPos(out var pt);
                _owner.UpdateDrag(pt);
            };

            MouseLeftButtonUp += (_, e) =>
            {
                if (!_draggingHere) return;
                e.Handled = true;
                _draggingHere = false;
                ReleaseMouseCapture();
                Native.GetCursorPos(out var pt);
                _owner.CompleteDrag(pt);
            };

            Closed += (_, _) =>
            {
                if (_draggingHere) ReleaseMouseCapture();
                _draggingHere = false;
            };
        }

        public bool ContainsPhysicalPoint(POINT point) =>
            point.X >= _frame.Bounds.Left && point.X < _frame.Bounds.Right &&
            point.Y >= _frame.Bounds.Top && point.Y < _frame.Bounds.Bottom;

        public void ClearSelection()
        {
            _selectionBorder.Visibility = Visibility.Collapsed;
            _sizeBadge.Visibility = Visibility.Collapsed;
            ResetDimMask(_frame.Width * _fromDevice.M11, _frame.Height * _fromDevice.M22);
        }

        public void UpdateSelection(int left, int top, int right, int bottom, POINT cursor)
        {
            var dipW = _frame.Width * _fromDevice.M11;
            var dipH = _frame.Height * _fromDevice.M22;

            var ix0 = Math.Max(left, _frame.Bounds.Left);
            var iy0 = Math.Max(top, _frame.Bounds.Top);
            var ix1 = Math.Min(right, _frame.Bounds.Right);
            var iy1 = Math.Min(bottom, _frame.Bounds.Bottom);

            if (ix1 <= ix0 || iy1 <= iy0)
            {
                _selectionBorder.Visibility = Visibility.Collapsed;
                _sizeBadge.Visibility = Visibility.Collapsed;
                ResetDimMask(dipW, dipH);
                return;
            }

            var x = (ix0 - _frame.Bounds.Left) * _fromDevice.M11;
            var y = (iy0 - _frame.Bounds.Top) * _fromDevice.M22;
            var w = (ix1 - ix0) * _fromDevice.M11;
            var h = (iy1 - iy0) * _fromDevice.M22;

            _dimMask.Data = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, dipW, dipH)),
                new RectangleGeometry(new Rect(x, y, w, h)));

            Canvas.SetLeft(_selectionBorder, x);
            Canvas.SetTop(_selectionBorder, y);
            _selectionBorder.Width = w;
            _selectionBorder.Height = h;
            _selectionBorder.Visibility = Visibility.Visible;

            if (ContainsPhysicalPoint(cursor))
            {
                _sizeText.Text = $"{right - left} × {bottom - top} px";
                var badgeX = Math.Clamp((cursor.X - _frame.Bounds.Left) * _fromDevice.M11 + 14, 8, Math.Max(8, dipW - 130));
                var badgeY = Math.Clamp((cursor.Y - _frame.Bounds.Top) * _fromDevice.M22 + 18, 8, Math.Max(8, dipH - 36));
                Canvas.SetLeft(_sizeBadge, badgeX);
                Canvas.SetTop(_sizeBadge, badgeY);
                _sizeBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _sizeBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void ResetDimMask(double dipW, double dipH)
        {
            _dimMask.Data = new RectangleGeometry(new Rect(0, 0, Math.Max(1, dipW), Math.Max(1, dipH)));
        }
    }
}
