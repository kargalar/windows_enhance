using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Modules.Capture;

internal static class CaptureClipboardChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var pixels = new byte[] { 0, 0, 255, 255, 0, 0 };
        var image = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null, pixels, 6);
        image.Freeze();
        var directory = Path.Combine(root, "clipboard");
        var file = new CaptureImageFile("Firefox", new DateTimeOffset(2026, 11, 18, 14, 15, 16, TimeSpan.Zero));
        var data = CaptureClipboard.CreateData(image, directory, file);
        check(data.ContainsImage() && data.ContainsFileDropList(), "Capture supports both image paste and file paste");
        var path = data.GetFileDropList().Cast<string>().Single();
        var saved = new BitmapImage();
        saved.BeginInit(); saved.CacheOption = BitmapCacheOption.OnLoad; saved.UriSource = new Uri(path); saved.EndInit();
        var decoded = new FormatConvertedBitmap(saved, PixelFormats.Bgr24, null, 0);
        var actual = new byte[6]; decoded.CopyPixels(actual, 6, 0);
        check(actual.SequenceEqual(pixels), "Clipboard PNG preserves captured pixels");
        using var clipboardPng = (MemoryStream)data.GetData(CaptureClipboard.PngFormat, autoConvert: false)!;
        check(clipboardPng.ToArray().SequenceEqual(File.ReadAllBytes(path)),
            "Clipboard exposes the original PNG bytes directly to image paste targets");
        var repeated = CaptureClipboard.CreateData(image, directory, file);
        check(repeated.GetFileDropList()[0] == path && Directory.GetFiles(directory).Length == 1 &&
            Path.GetFileName(path).StartsWith("Firefox - 2026-11-18", StringComparison.Ordinal),
            "Repeated copies of one capture reuse the same application-and-date PNG");

        var imageOnly = CaptureClipboard.CreateData(image, path, file);
        check(imageOnly.ContainsImage() && imageOnly.GetDataPresent(CaptureClipboard.PngFormat, autoConvert: false) &&
            !imageOnly.ContainsFileDropList(), "Image paste still works when the clipboard file cannot be written");
    }
}
