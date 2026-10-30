using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.ClipboardLine;

internal static class LinkThumbnail
{
    private const int MinimumWidth = 160;
    private const int MaximumPixels = 20_000_000;
    private const byte BlackChannelLimit = 20;
    private const byte OpaqueAlphaLimit = 245;

    internal static BitmapSource ForPreview(BitmapSource image, Uri link)
    {
        if (!IsYouTubeUrl(link) || image.PixelWidth < MinimumWidth || image.PixelHeight < 1 ||
            (long)image.PixelWidth * image.PixelHeight > MaximumPixels ||
            Math.Abs((double)image.PixelWidth / image.PixelHeight - 4d / 3) >= .03)
            return image;

        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var targetHeight = (int)Math.Round(width * 9d / 16);
        var padding = (height - targetHeight) / 2;
        if (padding <= 2 || height - targetHeight < 4) return image;

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        var step = Math.Max(1, width / 128);

        bool IsOpaqueBlack(int x, int y)
        {
            var offset = (y * width + x) * 4;
            return pixels[offset] < BlackChannelLimit && pixels[offset + 1] < BlackChannelLimit &&
                   pixels[offset + 2] < BlackChannelLimit && pixels[offset + 3] > OpaqueAlphaLimit;
        }

        for (var y = 0; y < padding - 2; y++)
        for (var x = 0; x < width; x += step)
            if (!IsOpaqueBlack(x, y) || !IsOpaqueBlack(x, height - 1 - y)) return image;

        var centerHasContent = false;
        for (var x = 0; x < width; x += step)
            if (!IsOpaqueBlack(x, height / 2)) { centerHasContent = true; break; }
        if (!centerHasContent) return image;

        var cropped = new CroppedBitmap(image, new Int32Rect(0, padding, width, targetHeight));
        cropped.Freeze();
        return cropped;
    }

    private static bool IsYouTubeUrl(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https")) return false;
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant() is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be";
    }
}
