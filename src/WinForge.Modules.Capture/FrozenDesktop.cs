using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.Modules.Capture;

/// <summary>The original desktop pixels shared by the selection overlay and the saved crop.</summary>
internal sealed class FrozenDesktop(IReadOnlyList<(CaptureArea Area, BitmapSource Image)> frames)
{
    public static FrozenDesktop Capture(IReadOnlyList<DesktopDisplay> displays) => new(displays
        .Select(display => CaptureServices.Area(display.Bounds))
        .Select(area => (area, CaptureServices.Capture(area))).ToArray());

    public BitmapSource ForDisplay(DesktopDisplay display) => frames.First(frame => frame.Area == CaptureServices.Area(display.Bounds)).Image;

    public BitmapSource Crop(CaptureArea area)
    {
        if (!area.IsValid) throw new ArgumentException("Invalid capture area.", nameof(area));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, area.Width, area.Height));
            foreach (var frame in frames)
                drawing.DrawImage(frame.Image, new Rect(frame.Area.X - area.X, frame.Area.Y - area.Y, frame.Area.Width, frame.Area.Height));
        }
        var image = new RenderTargetBitmap(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); return image;
    }
}
