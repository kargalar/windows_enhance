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
    private readonly DispatcherTimer _timer;

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
            FontSize = 12.5,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560
        };

        Content = new Border
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

    private void ShowMessage(string message)
    {
        _text.Text = message;

        if (!IsVisible) Show();
        UpdateLayout();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 72;

        _timer.Stop();
        _timer.Start();
    }
}
