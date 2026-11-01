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
        var data = CaptureClipboard.CreateData(image, directory);
        check(data.ContainsImage() && data.ContainsFileDropList(), "Capture supports both image paste and file paste");
        var path = data.GetFileDropList().Cast<string>().Single();
        var saved = new BitmapImage();
        saved.BeginInit(); saved.CacheOption = BitmapCacheOption.OnLoad; saved.UriSource = new Uri(path); saved.EndInit();
        var decoded = new FormatConvertedBitmap(saved, PixelFormats.Bgr24, null, 0);
        var actual = new byte[6]; decoded.CopyPixels(actual, 6, 0);
        check(actual.SequenceEqual(pixels), "Clipboard PNG preserves captured pixels");
        var repeated = CaptureClipboard.CreateData(image, directory);
        check(repeated.GetFileDropList()[0] == path && Directory.GetFiles(directory).Length == 1,
            "Repeated copies reuse the same PNG without accumulating duplicate files");
    }
}
