using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

public sealed record StitchResult(BitmapSource Image, double Confidence, bool Partial);
public static class ScrollStitcher
{
    public static (int Overlap, double Confidence) FindOverlap(BitmapSource previous, BitmapSource next, int? expectedOverlap = null)
    {
        if (previous.PixelWidth != next.PixelWidth || previous.PixelHeight != next.PixelHeight) return (0, 0);
        var width = previous.PixelWidth; var height = previous.PixelHeight; var stride = width * 4;
        var a = new byte[stride * height]; var b = new byte[a.Length];
        new FormatConvertedBitmap(previous, PixelFormats.Bgr32, null, 0).CopyPixels(a, stride, 0);
        new FormatConvertedBitmap(next, PixelFormats.Bgr32, null, 0).CopyPixels(b, stride, 0);
        var best = double.MaxValue; var overlap = 0;
        var firstCandidate = expectedOverlap is { } expected ? Math.Max(8, expected - 8) : Math.Max(8, height / 8);
        var lastCandidate = expectedOverlap is { } anticipated ? Math.Min(height - 3, anticipated + 8) : height - 3;
        for (var candidate = firstCandidate; candidate <= lastCandidate; candidate++)
        {
            double error = 0; var samples = 0; var pixelErrors = new List<double>();
            for (var y = 0; y < candidate; y += Math.Max(1, candidate / 24))
                for (var x = Math.Max(0, width / 12); x < width * 11 / 12; x += Math.Max(1, width / 60))
                {
                    var pixelError = 0d;
                    for (var channel = 0; channel < 3; channel++) pixelError += Math.Abs(a[(height - candidate + y) * stride + x * 4 + channel] - b[y * stride + x * 4 + channel]);
                    error += pixelError; pixelErrors.Add(pixelError / 3);
                    samples += 3;
                }
            var meanError = error / Math.Max(1, samples);
            pixelErrors.Sort(); var medianError = pixelErrors[pixelErrors.Count / 2];
            var score = (meanError + medianError) / 2;
            if (score < best) { best = score; overlap = candidate; }
        }
        return (overlap, Math.Clamp(1 - best / 100, 0, 1));
    }
    public static BitmapSource Join(BitmapSource accumulated, BitmapSource next, int overlap)
    {
        if (overlap < 1 || overlap >= next.PixelHeight || next.PixelWidth != accumulated.PixelWidth) throw new ArgumentException("Geçersiz birleştirme.");
        var height = accumulated.PixelHeight + next.PixelHeight - overlap;
        if ((long)height * next.PixelWidth > 30_000_000) throw new InvalidOperationException("Kayan yakalama 30 megapiksel sınırına ulaştı.");
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(accumulated, new Rect(0, 0, accumulated.PixelWidth, accumulated.PixelHeight));
            var crop = new CroppedBitmap(next, new Int32Rect(0, overlap, next.PixelWidth, next.PixelHeight - overlap));
            drawing.DrawImage(crop, new Rect(0, accumulated.PixelHeight, crop.PixelWidth, crop.PixelHeight));
        }
        var image = new RenderTargetBitmap(next.PixelWidth, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
}

internal static class ScrollingCapture
{
    public static async Task<StitchResult> RunAsync(CaptureArea area, CancellationToken token)
    {
        GetPhysicalCursorPos(out var cursor);
        try
        {
            var point = new WinForge.Core.POINT(area.X + area.Width / 2, area.Y + area.Height / 2);
            SetPhysicalCursorPos(point.X, point.Y);
            var target = WindowFromPoint(point);
            if (target != 0) WinForge.Core.Native.SetForegroundWindow(GetAncestor(target, 2));
            await Task.Delay(150, token);
            var previous = CaptureServices.Capture(area); var result = previous; var confidence = 1d;
            var scrollPattern = FindScrollPattern(point);
            for (var page = 0; page < 100; page++)
            {
                token.ThrowIfCancellationRequested();
                if (target == 0) return new(result, 0, true);
                var scrolled = false;
                var previousPercent = scrollPattern?.Current.VerticalScrollPercent;
                if (scrollPattern is not null)
                {
                    try
                    {
                        var currentPercent = scrollPattern.Current.VerticalScrollPercent;
                        var currentViewSize = scrollPattern.Current.VerticalViewSize;
                        var targetPercent = Math.Min(currentPercent + currentViewSize * 0.65, 100);
                        if (currentViewSize < 100 && targetPercent > currentPercent + 0.01)
                            scrollPattern.SetScrollPercent(ScrollPattern.NoScroll, targetPercent);
                    }
                    catch (ElementNotAvailableException) { scrollPattern = null; }
                    await Task.Delay(150, token);
                    if (scrollPattern is not null && previousPercent is { } scrollPositionBefore) scrolled = Math.Abs(scrollPattern.Current.VerticalScrollPercent - scrollPositionBefore) > 0.01;
                }
                if (!scrolled)
                {
                    SetPhysicalCursorPos(point.X + page % 2, point.Y);
                    SendWheel(-120);
                }
                await Task.Delay(200, token);
                var next = CaptureServices.Capture(area);
                if (Same(previous, next)) break;
                var match = ScrollStitcher.FindOverlap(previous, next); confidence = Math.Min(confidence, match.Confidence);
                if (match.Confidence < 0.85) return new(result, confidence, true);
                try { result = ScrollStitcher.Join(result, next, match.Overlap); }
                catch (InvalidOperationException) { return new(result, confidence, true); }
                previous = next;
                if (page == 99) return new(result, confidence, true);
            }
            return new(result, confidence, false);
        }
        finally { SetPhysicalCursorPos(cursor.X, cursor.Y); }
    }
    private static bool Same(BitmapSource a, BitmapSource b)
    {
        var stride = a.PixelWidth * 4; var first = new byte[stride * a.PixelHeight]; var second = new byte[first.Length];
        a.CopyPixels(first, stride, 0); b.CopyPixels(second, stride, 0); return first.AsSpan().SequenceEqual(second);
    }
    private static ScrollPattern? FindScrollPattern(WinForge.Core.POINT point)
    {
        try
        {
            var current = AutomationElement.FromPoint(new Point(point.X, point.Y));
            for (var depth = 0; current is not null && depth < 12; depth++, current = TreeWalker.ControlViewWalker.GetParent(current))
            {
                if (current.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern) && pattern is ScrollPattern scroll) return scroll;
            }
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return null;
    }
    [DllImport("user32.dll")] private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out WinForge.Core.POINT point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WinForge.Core.POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);
    private static void SendWheel(int delta) => mouse_event(0x0800, 0, 0, unchecked((uint)delta), 0);
}
