using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

public sealed record StitchResult(BitmapSource Image, double Confidence, bool Partial);
public static class ScrollStitcher
{
    public static (int Overlap, double Confidence) FindOverlap(BitmapSource previous, BitmapSource next)
    {
        if (previous.PixelWidth != next.PixelWidth || previous.PixelHeight != next.PixelHeight) return (0, 0);
        var width = previous.PixelWidth; var height = previous.PixelHeight; var stride = width * 4;
        var a = new byte[stride * height]; var b = new byte[a.Length];
        new FormatConvertedBitmap(previous, PixelFormats.Bgr32, null, 0).CopyPixels(a, stride, 0);
        new FormatConvertedBitmap(next, PixelFormats.Bgr32, null, 0).CopyPixels(b, stride, 0);
        var best = double.MaxValue; var overlap = 0;
        for (var candidate = Math.Max(8, height / 8); candidate < height - 3; candidate++)
        {
            double error = 0; var samples = 0;
            for (var y = 0; y < candidate; y += Math.Max(1, candidate / 12))
                for (var x = Math.Max(0, width / 12); x < width * 11 / 12; x += Math.Max(1, width / 40))
                {
                    for (var channel = 0; channel < 3; channel++) error += Math.Abs(a[(height - candidate + y) * stride + x * 4 + channel] - b[y * stride + x * 4 + channel]);
                    samples += 3;
                }
            error /= Math.Max(1, samples);
            if (error < best) { best = error; overlap = candidate; }
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
            var coordinates = (nint)((point.X & 0xffff) | (point.Y << 16));
            for (var page = 0; page < 100; page++)
            {
                token.ThrowIfCancellationRequested();
                if (target == 0 || !PostMessage(target, 0x020A, (nint)(-360 << 16), coordinates)) return new(result, 0, true);
                await Task.Delay(350, token);
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
    [DllImport("user32.dll")] private static extern bool SetPhysicalCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out WinForge.Core.POINT point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(WinForge.Core.POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
