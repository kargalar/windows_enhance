using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.Modules.Capture;

/// <summary>A short preview that reveals the captured file in Explorer.</summary>
internal sealed class CapturePreviewWindow : Window
{
    public CapturePreviewWindow(BitmapSource image, Action? reveal = null)
    {
        Native.GetCursorPos(out var cursor);
        var display = DesktopDisplays.ForPoint(cursor);
        var work = display.WorkArea;
        var scale = GetDpiForMonitor(display.Handle, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
        var margin = (int)Math.Round(16 * scale);
        var maxWidth = Math.Max(1, Math.Min(320 * scale, work.Right - work.Left - 2 * margin));
        var maxHeight = Math.Max(1, Math.Min(220 * scale, work.Bottom - work.Top - 2 * margin));
        var ratio = Math.Min(maxWidth / (image.PixelWidth + 12d), maxHeight / (image.PixelHeight + 12d));
        var width = (int)Math.Ceiling((image.PixelWidth + 12d) * ratio);
        var height = (int)Math.Ceiling((image.PixelHeight + 12d) * ratio);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        Cursor = System.Windows.Input.Cursors.Hand;
        ToolTip = "Dosyayı klasöründe göster";
        MouseLeftButtonUp += (_, _) => { reveal?.Invoke(); Close(); };
        Topmost = true;
        Opacity = 0;
        Width = width / scale;
        Height = height / scale;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 28, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5),
            Child = new Image { Source = image, Stretch = Stretch.Uniform }
        };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE,
                new IntPtr(style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW));
            Native.SetWindowPos(handle, Native.HWND_TOPMOST,
                work.Right - margin - width, work.Bottom - margin - height, width, height, Native.SWP_NOACTIVATE);
        };
        Loaded += (_, _) =>
        {
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2800))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3200)))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        };
        Closed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Content = null;
        };
    }

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
