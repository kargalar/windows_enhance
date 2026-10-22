using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.NetSpeed;

public sealed class NetSpeedWindow : Window
{
    private readonly TextBlock _download;
    private readonly TextBlock _upload;
    private readonly StackPanel _panel;
    private long _lastReceived;
    private long _lastSent;
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly DispatcherTimer _timer;
    private int _rightOffset = 190;
    private int _bottomOffset = 6;

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

        _panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _panel.Children.Add(_download);
        _panel.Children.Add(_upload);

        Content = new Border
        {
            Child = _panel,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.65,
                Color = Colors.Black
            }
        };

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

    public void ApplyOptions(double fontSize, bool showDownload, bool showUpload, int rightOffset, int gap)
    {
        _download.FontSize = fontSize;
        _upload.FontSize = fontSize;
        _download.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        _rightOffset = rightOffset;
        _bottomOffset = gap;
        _upload.Visibility = showUpload ? Visibility.Visible : Visibility.Collapsed;
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

        if (width < 8 || height < 8)
        {
            if (IsVisible) Hide();
            return;
        }

        if (!IsVisible) Show();
        UpdateLayout();

        var ownWidth = ActualWidth;
        var ownHeight = ActualHeight;
        if (ownWidth <= 0 || ownHeight <= 0) return;

        // Auto-hide slides the taskbar off-screen; clamp to the screen edge so the
        // indicator stays visible right above where the taskbar would pop up.
        var edgeLeft = Math.Min(rect.Left, screenWidth);
        var edgeRight = Math.Min(rect.Right, screenWidth);
        var edgeTop = Math.Min(rect.Top, screenHeight);
        var edgeBottom = Math.Min(rect.Bottom, screenHeight);

        double left;
        double top;

        if (width >= height)
        {
            left = edgeRight / scale - ownWidth - _rightOffset;
            top = edgeTop > screenHeight / 2
                ? edgeTop / scale - ownHeight - _bottomOffset
                : edgeBottom / scale + _bottomOffset;
        }
        else
        {
            left = edgeLeft > screenWidth / 2
                ? edgeLeft / scale - ownWidth - _bottomOffset
                : edgeRight / scale + _bottomOffset;
            top = edgeBottom / scale - ownHeight - _rightOffset;
        }

        Left = left;
        Top = top;
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

    private static string Format(double bytesPerSecond)
    {
        var mb = bytesPerSecond / 1024d / 1024d;
        return string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{mb:0.00} MB/s");
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
