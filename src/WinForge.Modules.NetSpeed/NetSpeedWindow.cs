using System.Net.NetworkInformation;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.NetSpeed;

public sealed class NetSpeedWindow
{
    public const string AboveTaskbar = "Görev çubuğunun üstünde";
    public const string InsideTaskbar = "Görev çubuğunun içinde";
    public const string SideBySide = "Yan yana";
    public const string Stacked = "Alt alta";
    private const double SideBySideWidth = 220;
    private const double SideBySideHeight = 38;
    private const double StackedWidth = 132;
    private const double StackedHeight = 44;

    private readonly IFeatureUiWindow? _view;
    private readonly Timer _timer;
    private readonly DispatcherTimer _placementTimer;
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSample = DateTime.UtcNow;
    private int _trayGap = 10;
    private double _fontSize = 11;
    private bool _showDownload = true;
    private bool _showUpload = true;
    private string _unit = "Otomatik";
    private string _position = InsideTaskbar;
    private string _layout = SideBySide;
    private int _intervalMilliseconds = 1000;
    private int _running;
    private int _tickInProgress;

    private double OverlayWidth => IsStacked ? StackedWidth : SideBySideWidth;
    private double OverlayHeight => IsStacked ? StackedHeight : SideBySideHeight;
    private bool IsStacked => string.Equals(_layout, Stacked, StringComparison.CurrentCultureIgnoreCase);

    public NetSpeedWindow(IFeatureUiService? featureUi)
    {
        _view = featureUi?.Open("net-speed-indicator", "net-speed-overlay", "Ağ hızı",
            new FeatureUiWindowOptions(OverlayWidth, OverlayHeight, Topmost: true, ActivateOnShow: false,
                Transparent: true, ClickThrough: true, ShowOnOpen: false));
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        // Taskbar animations and Start-menu activation must not wait for the
        // network sampling interval (which can be as long as three seconds).
        // Run placement on the UI thread, independently of network statistics.
        _placementTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _placementTimer.Tick += OnPlacementTick;
        UpdateView(Format(0), Format(0));
    }

    public void ApplyOptions(double fontSize, bool showDownload, bool showUpload, int trayGap,
        string unit, int intervalMs, string position, string layout)
    {
        _fontSize = Math.Clamp(fontSize, 8, 24);
        _showDownload = showDownload;
        _showUpload = showUpload;
        _trayGap = Math.Clamp(trayGap, 0, 120);
        _unit = unit;
        _position = position;
        _layout = layout;

        _intervalMilliseconds = Math.Clamp(intervalMs, 250, 3000);
        if (Volatile.Read(ref _running) != 0)
            _timer.Change(_intervalMilliseconds, _intervalMilliseconds);

        Reposition();
        UpdateView(Format(0), Format(0));
    }

    private void Reposition()
    {
        if (_view is null) return;
        if (Volatile.Read(ref _running) == 0 || (!_showDownload && !_showUpload))
        {
            _view.Hide();
            return;
        }

        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Native.IsWindowVisible(taskbar) || !Native.GetWindowRect(taskbar, out var rect))
        {
            _view.Hide();
            return;
        }

        var scale = Native.GetDpiForWindow(taskbar) / 96.0;
        if (scale <= 0) scale = 1.0;

        var display = DesktopDisplays.ForWindow(taskbar);
        var bounds = display.Bounds;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Auto-hide may leave a one-pixel edge outside the monitor. Full virtual
        // desktop coordinates (including negative origins) are valid.
        if (width < 8 || height < 8 || rect.Bottom <= bounds.Top + 2 || rect.Top >= bounds.Bottom - 2 ||
            rect.Right <= bounds.Left + 2 || rect.Left >= bounds.Right - 2)
        {
            _view.Hide();
            return;
        }

        var tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var trayRect = rect;
        if (tray != IntPtr.Zero) Native.GetWindowRect(tray, out trayRect);

        double left;
        double top;
        var inside = string.Equals(_position, InsideTaskbar, StringComparison.CurrentCultureIgnoreCase);
        var screenCenterX = (bounds.Left + bounds.Right) / 2.0;
        var screenCenterY = (bounds.Top + bounds.Bottom) / 2.0;
        if (width >= height)
        {
            left = trayRect.Left / scale - OverlayWidth - _trayGap;
            if (inside)
                top = rect.Top / scale + (height / scale - OverlayHeight) / 2;
            else if ((rect.Top + rect.Bottom) / 2.0 > screenCenterY)
                top = rect.Top / scale - OverlayHeight - 2;
            else
                top = rect.Bottom / scale + 2;
        }
        else
        {
            left = inside
                ? rect.Left / scale + (width / scale - OverlayWidth) / 2
                : (rect.Left + rect.Right) / 2.0 < screenCenterX
                    ? rect.Right / scale + 2
                    : rect.Left / scale - OverlayWidth - 2;
            top = inside
                ? rect.Top / scale + (height / scale - OverlayHeight) / 2
                : trayRect.Top / scale - OverlayHeight - _trayGap;
        }

        _view.SetBounds(Math.Clamp(left, bounds.Left / scale, Math.Max(bounds.Left / scale, bounds.Right / scale - OverlayWidth)),
            Math.Clamp(top, bounds.Top / scale, Math.Max(bounds.Top / scale, bounds.Bottom / scale - OverlayHeight)), OverlayWidth, OverlayHeight);
        _view.Show();
    }

    private void OnPlacementTick(object? sender, EventArgs args) => Reposition();

    public void Start()
    {
        Sample();
        _lastSample = DateTime.UtcNow;
        Volatile.Write(ref _running, 1);
        _timer.Change(_intervalMilliseconds, _intervalMilliseconds);
        _placementTimer.Start();
        Reposition();
    }

    public void Stop()
    {
        Volatile.Write(ref _running, 0);
        _placementTimer.Stop();
        _placementTimer.Tick -= OnPlacementTick;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _view?.Close();
        _timer.Dispose();
    }

    private void Tick()
    {
        if (Volatile.Read(ref _running) == 0 || Interlocked.Exchange(ref _tickInProgress, 1) != 0) return;
        try
        {
            TickCore();
        }
        finally
        {
            Volatile.Write(ref _tickInProgress, 0);
        }
    }

    private void TickCore()
    {
        var (received, sent) = ReadTotals();
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.2, (now - _lastSample).TotalSeconds);
        var down = Math.Max(0, received - _lastReceived) / seconds;
        var up = Math.Max(0, sent - _lastSent) / seconds;

        _lastReceived = received;
        _lastSent = sent;
        _lastSample = now;
        UpdateView(Format(down), Format(up));
    }

    private void UpdateView(string download, string upload) => _view?.SetState(new
    {
        download,
        upload,
        fontSize = _fontSize,
        showDownload = _showDownload,
        showUpload = _showUpload,
        layout = _layout
    });

    private void Sample() => (_lastReceived, _lastSent) = ReadTotals();

    private string Format(double bytesPerSecond)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        if (string.Equals(_unit, "Mbit/s", StringComparison.CurrentCultureIgnoreCase))
            return string.Create(culture, $"{bytesPerSecond * 8d / 1_000_000d:0.00} Mbit/s");
        if (string.Equals(_unit, "MB/s", StringComparison.CurrentCultureIgnoreCase))
            return string.Create(culture, $"{bytesPerSecond / 1024d / 1024d:0.00} MB/s");
        if (bytesPerSecond >= 1024d * 1024d) return string.Create(culture, $"{bytesPerSecond / 1024d / 1024d:0.00} MB/s");
        if (bytesPerSecond >= 1024d) return string.Create(culture, $"{bytesPerSecond / 1024d:0.0} KB/s");
        return string.Create(culture, $"{bytesPerSecond:0} B/s");
    }

    private static (long Received, long Sent) ReadTotals()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = network.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch
        {
            // Network statistics are best effort.
        }
        return (received, sent);
    }
}
