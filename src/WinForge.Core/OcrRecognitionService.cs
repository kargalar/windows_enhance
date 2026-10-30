using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace WinForge.Core;

/// <summary>Recognizes local images with Windows OCR, within the installed engine's size limit.</summary>
public static class OcrRecognitionService
{
    private const long MaximumImagePixels = 30_000_000;

    public static Task<string> RecognizeAsync(BitmapSource source) => RecognizeAsync(source, CancellationToken.None);

    public static async Task<string> RecognizeAsync(BitmapSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 ||
            (long)source.PixelWidth * source.PixelHeight > MaximumImagePixels)
            throw new InvalidOperationException("Görsel OCR için 30 megapiksel sınırını aşıyor.");

        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                     ?? throw new InvalidOperationException("Windows OCR altyapısı veya uygun dil paketi bulunamadı.");
        var scale = Math.Min(1d, (double)OcrEngine.MaxImageDimension / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource image = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        if (image.Format != PixelFormats.Bgra32)
            image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        var stride = checked(image.PixelWidth * 4);
        var pixels = new byte[checked(stride * image.PixelHeight)];
        image.CopyPixels(pixels, stride, 0);
        var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
        // GDI screen captures leave alpha undefined; OCR must use the visible RGB values.
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8,
            image.PixelWidth, image.PixelHeight, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        return string.Join(Environment.NewLine,
            result.Lines.Select(line => line.Text).Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
