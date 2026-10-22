using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.AlwaysOnTop;

public sealed class BorderOverlay : Window
{
    private readonly IntPtr _target;
    private readonly Border _frame;
    private readonly DispatcherTimer _timer;

    public BorderOverlay(IntPtr target, Color color, double thickness)
    {
        _target = target;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _frame = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(thickness),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent
        };

        Content = _frame;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => Track();
    }

    public void SetStyle(Color color, double thickness)
    {
        _frame.BorderBrush = new SolidColorBrush(color);
        _frame.BorderThickness = new Thickness(thickness);
    }

    public void Start()
    {
        Track();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        Close();
    }

    private void Track()
    {
        if (!Native.IsWindow(_target))
        {
            Stop();
            return;
        }

        if (!Native.IsWindowVisible(_target) || Native.IsIconic(_target))
        {
            if (IsVisible) Hide();
            return;
        }

        var scale = Native.GetDpiForWindow(_target) / 96.0;
        if (scale <= 0) scale = 1.0;

        RECT rect;
        if (Native.DwmGetWindowAttribute(_target, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0)
            Native.GetWindowRect(_target, out rect);

        var width = (rect.Right - rect.Left) / scale;
        var height = (rect.Bottom - rect.Top) / scale;
        if (width < 4 || height < 4)
        {
            if (IsVisible) Hide();
            return;
        }

        Left = rect.Left / scale;
        Top = rect.Top / scale;
        Width = width;
        Height = height;

        if (!IsVisible) Show();
    }
}
