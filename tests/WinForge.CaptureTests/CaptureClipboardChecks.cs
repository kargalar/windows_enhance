using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;
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

        // Region capture crops a FrozenDesktop via RenderTargetBitmap (Pbgra32).
        var framePixels = new byte[4 * 4 * 4];
        for (var i = 0; i < framePixels.Length; i += 4)
        {
            framePixels[i] = 40; framePixels[i + 1] = 180; framePixels[i + 2] = 90; framePixels[i + 3] = 255;
        }
        var frame = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr32, null, framePixels, 16);
        frame.Freeze();
        var desktop = new FrozenDesktop([(new CaptureArea(0, 0, 4, 4), frame)]);
        var cropped = desktop.Crop(new CaptureArea(1, 1, 2, 2));
        var cropFile = new CaptureImageFile("Code", new DateTimeOffset(2026, 11, 24, 13, 24, 20, TimeSpan.Zero));
        var cropData = CaptureClipboard.CreateData(cropped, directory, cropFile);
        var cropPath = cropData.GetFileDropList().Cast<string>().Single();
        var cropSaved = new BitmapImage();
        cropSaved.BeginInit(); cropSaved.CacheOption = BitmapCacheOption.OnLoad; cropSaved.UriSource = new Uri(cropPath); cropSaved.EndInit();
        var cropActual = new byte[2 * 2 * 4];
        new FormatConvertedBitmap(cropSaved, PixelFormats.Bgra32, null, 0).CopyPixels(cropActual, 8, 0);
        check(cropData.ContainsImage() && cropData.GetDataPresent(CaptureClipboard.PngFormat, autoConvert: false) &&
            cropSaved.PixelWidth == 2 && cropSaved.PixelHeight == 2 &&
            cropActual[0] == 40 && cropActual[1] == 180 && cropActual[2] == 90,
            "FrozenDesktop cropped region capture preserves image, PNG, file drop, and pixels");

        var initialVersion = SelectionTranslationGuard.ClipboardWriteVersion;
        check(!SelectionTranslationGuard.IsOverlayActive, "Selection translation overlay guard starts inactive");
        using (var outer = SelectionTranslationGuard.EnterOverlay())
        {
            using var inner = SelectionTranslationGuard.EnterOverlay();
            check(SelectionTranslationGuard.IsOverlayActive, "Overlay guard stays active for nested scopes");
            inner.Dispose();
            inner.Dispose();
            check(SelectionTranslationGuard.IsOverlayActive, "Overlay guard stays active until outer scope disposes");
        }
        check(!SelectionTranslationGuard.IsOverlayActive, "Overlay guard resets after all scopes dispose");
        SelectionTranslationGuard.NotifyInternalClipboardWrite();
        check(SelectionTranslationGuard.ClipboardWriteVersion == initialVersion + 1,
            "Internal clipboard write notification increments the guard version");
    }
}
