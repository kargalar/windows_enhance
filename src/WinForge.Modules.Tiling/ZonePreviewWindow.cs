using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WinForge.Core;

namespace WinForge.Modules.Tiling;

internal sealed class ZonePreviewWindow : Window
{
    private readonly Canvas _canvas = new();

    public ZonePreviewWindow()
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
    }

    public void ShowZones(RECT workArea, IReadOnlyList<RECT> zones, IReadOnlyCollection<int> selectedZones, double scale,
        RECT? mergedBounds = null)
    {
        _canvas.Children.Clear();
        Width = (workArea.Right - workArea.Left) / scale;
        Height = (workArea.Bottom - workArea.Top) / scale;

        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            var isSelected = selectedZones.Contains(index);
            var border = new Border
            {
                Width = (zone.Right - zone.Left) / scale,
                Height = (zone.Bottom - zone.Top) / scale,
                Background = new SolidColorBrush(isSelected
                    ? Color.FromArgb(100, 76, 194, 255)
                    : Color.FromArgb(48, 30, 38, 52)),
                BorderBrush = new SolidColorBrush(isSelected
                    ? Color.FromArgb(240, 76, 194, 255)
                    : Color.FromArgb(190, 220, 230, 245)),
                BorderThickness = new Thickness(isSelected ? 3 : 2),
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 28,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White
                }
            };

            Canvas.SetLeft(border, (zone.Left - workArea.Left) / scale);
            Canvas.SetTop(border, (zone.Top - workArea.Top) / scale);
            _canvas.Children.Add(border);
        }

        if (mergedBounds is { } span)
        {
            var outline = new Border
            {
                Width = (span.Right - span.Left) / scale,
                Height = (span.Bottom - span.Top) / scale,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 129, 220, 255)),
                BorderThickness = new Thickness(4),
                CornerRadius = new CornerRadius(12)
            };
            Canvas.SetLeft(outline, (span.Left - workArea.Left) / scale);
            Canvas.SetTop(outline, (span.Top - workArea.Top) / scale);
            _canvas.Children.Add(outline);
        }

        if (!IsVisible) Show();

        var handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, workArea.Left, workArea.Top,
            workArea.Right - workArea.Left, workArea.Bottom - workArea.Top,
            Native.SWP_NOACTIVATE);
    }

    public void ClosePreview()
    {
        if (IsVisible) Hide();
    }
}
