using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Tiling;

internal sealed class ZonePreviewWindow : Window
{
    private readonly IFeatureUiSurface uiSurface;

    public ZonePreviewWindow(IFeatureUiService? featureUi)
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
        uiSurface = featureUi?.CreateSurface("tiling-zone-preview")
            ?? throw new InvalidOperationException("Döşeme alanı önizlemesi için React arayüzü gerekli.");
        Content = uiSurface.Element;
        Closed += (_, _) => uiSurface.Dispose();

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
            style |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(style));
        };
    }

    public void ShowZones(RECT workArea, IReadOnlyList<RECT> zones, IReadOnlyCollection<int> selectedZones, double scale,
        RECT? mergedBounds = null, bool showNumbers = true, string activeColor = "#4CC2FF",
        string inactiveColor = "#6B6B6B", int borderThickness = 2)
    {
        Width = (workArea.Right - workArea.Left) / scale;
        Height = (workArea.Bottom - workArea.Top) / scale;
        var selected = selectedZones.ToHashSet();
        uiSurface.SetState(new
        {
            zones = zones.Select((zone, index) => new
            {
                index,
                x = (zone.Left - workArea.Left) / scale,
                y = (zone.Top - workArea.Top) / scale,
                width = (zone.Right - zone.Left) / scale,
                height = (zone.Bottom - zone.Top) / scale,
                active = selected.Contains(index)
            }).ToArray(),
            width = Width,
            height = Height,
            showNumbers,
            activeColor,
            inactiveColor,
            borderThickness = Math.Clamp(borderThickness, 1, 12),
            merged = mergedBounds is { } span ? new
            {
                x = (span.Left - workArea.Left) / scale,
                y = (span.Top - workArea.Top) / scale,
                width = (span.Right - span.Left) / scale,
                height = (span.Bottom - span.Top) / scale
            } : null
        });

        if (!IsVisible) Show();
        var handle = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(handle, Native.HWND_TOPMOST, workArea.Left, workArea.Top,
            workArea.Right - workArea.Left, workArea.Bottom - workArea.Top, Native.SWP_NOACTIVATE);
    }

    public void ClosePreview()
    {
        if (IsVisible) Hide();
    }
}
