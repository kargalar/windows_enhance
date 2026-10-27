using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brushes = System.Windows.Media.Brushes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.App;

public sealed class OsdWindow : Window
{
    private static OsdWindow? _instance;

    private readonly TextBlock _text;
    private readonly Border _container;
    private readonly DispatcherTimer _timer;
    private const double DefaultFontSize = 12.5;

    private OsdWindow()
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

        _text = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(233, 237, 244)),
            FontSize = DefaultFontSize,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560
        };

        _container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(244, 19, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 50, 64)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 11, 18, 11),
            Child = _text,
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 2,
                Opacity = 0.45,
                Color = Colors.Black
            }
        };
        Content = _container;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    public static void Toast(string message)
    {
        var app = Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _instance ??= new OsdWindow();
                _instance.ShowMessage(message);
            }
            catch
            {
                // A notification must never break the caller.
            }
        }));
    }

    public static void ShowBrightness(int percent, string deviceName)
    {
        var app = Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _instance ??= new OsdWindow();
                _instance.ShowBrightnessMessage(Math.Clamp(percent, 0, 100), deviceName);
            }
            catch
            {
                // A notification must never break the caller.
            }
        }));
    }

    private void ShowMessage(string message)
    {
        _container.Padding = new Thickness(18, 11, 18, 11);
        _container.CornerRadius = new CornerRadius(10);
        _text.FontSize = DefaultFontSize;
        _text.Text = message;

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 72;

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1400 + message.Length * 28, 1800, 6000));
        _timer.Stop();
        _timer.Start();
    }

    private void ShowBrightnessMessage(int percent, string deviceName)
    {
        _container.Padding = new Thickness(14, 8, 14, 8);
        _container.CornerRadius = new CornerRadius(8);
        _text.FontSize = 14;
        _text.Text = $"☼  {percent}%";

        if (!IsVisible) Show();
        UpdateLayout();

        var area = DesktopDisplays.Enumerate().FirstOrDefault(display =>
            display.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))?.WorkArea;
        if (area is null)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
            Top = workArea.Bottom - ActualHeight - 56;
        }
        else
        {
            var workArea = area.Value;
            Left = workArea.Left + ((workArea.Right - workArea.Left) - ActualWidth) / 2;
            Top = area.Value.Bottom - ActualHeight - 56;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.Stop();
        _timer.Start();
    }
}
