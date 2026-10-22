using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.NetSpeed;

public sealed class NetSpeedWindow : Window
{
    public const string AboveTaskbar = "Görev çubuğunun üstünde";
    public const string InsideTaskbar = "Görev çubuğunun içinde";

    private readonly TextBlock _download;
    private readonly TextBlock _upload;
    private readonly StackPanel _panel;
    private readonly Border _gap;
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly DispatcherTimer _timer;
    private int _trayGap = 10;
    private string _unit = "Otomatik";
    private string _position = AboveTaskbar;

    public NetSpeedWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _download = new TextBlock
        {
            TextAlignment = TextAlignment.Right,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(76, 194, 255))
        };

        _upload = new TextBlock
        {
            TextAlignment = TextAlignment.Right,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 208, 220))
        };

        _panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _gap = new Border { Width = 10 };
        _panel.Children.Add(_download);
        _panel.Children.Add(_gap);
        _panel.Children.Add(_upload);

        Content = new Border { Child = _panel, Padding = new Thickness(2, 0, 2, 0) };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += (_, _) => Tick();
    }

    public void ApplyOptions(double fontSize, bool showDownload, bool showUpload, int trayGap,
        string unit, int intervalMs, string position)
    {
        _download.FontSize = fontSize;
        _upload.FontSize = fontSize;
        _download.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        _upload.Visibility = showUpload ? Visibility.Visible : Visibility.Collapsed;
        _gap.Visibility = showDownload && showUpload ? Visibility.Visible : Visibility.Collapsed;
        _panel.Visibility = showDownload || showUpload ? Visibility.Visible : Visibility.Collapsed;
        _trayGap = trayGap;
        _unit = unit;
        _position = position;

        var interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 250, 3000));
        if (_timer.Interval != interval) _timer.Interval = interval;

        Reposition();
    }

    private void Reposition()
    {
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            if (IsVisible) Hide();
            return;
        }

        Native.GetWindowRect(taskbar, out var rect);
        var scale = Native.GetDpiForWindow(taskbar) / 96.0;
        if (scale <= 0) scale = 1.0;

        var screenWidth = Native.GetSystemMetrics(0);
        var screenHeight = Native.GetSystemMetrics(1);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width < 8 || height < 8 ||
            rect.Left < 0 || rect.Top < 0 || rect.Right > screenWidth || rect.Bottom > screenHeight)
        {
            // Auto-hide slides the taskbar off-screen; hide the indicator with it.
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible) Show();
        UpdateLayout();

        var ownWidth = ActualWidth;
        var ownHeight = ActualHeight;
        if (ownWidth <= 0 || ownHeight <= 0) return;

        var tray = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var trayRect = rect;
        if (tray != IntPtr.Zero) Native.GetWindowRect(tray, out trayRect);

        double left;
        double top;
        var inside = string.Equals(_position, InsideTaskbar, StringComparison.CurrentCultureIgnoreCase);
        var screenCenterX = screenWidth / 2.0;
        var screenCenterY = screenHeight / 2.0;

        if (width >= height)
        {
            left = trayRect.Left / scale - ownWidth - _trayGap;

            if (inside)
            {
                top = rect.Top / scale + (height / scale - ownHeight) / 2;
            }
            else if ((rect.Top + rect.Bottom) / 2.0 > screenCenterY)
            {
                top = rect.Top / scale - ownHeight - 2;
            }
            else
            {
                top = rect.Bottom / scale + 2;
            }
        }
        else
        {
            left = (rect.Left + rect.Right) / 2.0 < screenCenterX
                ? rect.Right / scale + 2
                : rect.Left / scale - ownWidth - 2;

            top = inside
                ? rect.Top / scale + (height / scale - ownHeight) / 2
                : trayRect.Top / scale - ownHeight - _trayGap;
        }

        Left = Math.Max(0, left);
        Top = Math.Max(0, top);

        var handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Start()
    {
        Sample();
        Show();
        _lastSample = DateTime.UtcNow;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Close();
    }

    private void Tick()
    {
        var (received, sent) = ReadTotals();
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.2, (now - _lastSample).TotalSeconds);

        var down = Math.Max(0, received - _lastReceived) / seconds;
        var up = Math.Max(0, sent - _lastSent) / seconds;

        _lastReceived = received;
        _lastSent = sent;
        _lastSample = now;

        _download.Text = "↓ " + Format(down);
        _upload.Text = "↑ " + Format(up);
        Reposition();
    }

    private void Sample()
    {
        (_lastReceived, _lastSent) = ReadTotals();
    }

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
                if (network.OperationalStatus != OperationalStatus.Up) continue;
                if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

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
