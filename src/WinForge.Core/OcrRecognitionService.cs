using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace WinForge.Core;

public enum OcrRecognitionMode
{
    Balanced,
    Accurate
}

public sealed record SupplementaryOcrResult(string Text, float Confidence);

public delegate Task<SupplementaryOcrResult> SupplementaryOcrHandler(
    byte[] grayscalePixels,
    int width,
    int height,
    bool singleLineHint,
    CancellationToken cancellationToken);

/// <summary>Recognizes local images with preprocessing, Windows OCR, and an optional supplementary engine.</summary>
public static class OcrRecognitionService
{
    private const long MaximumImagePixels = 30_000_000;
    private const int QuietZonePadding = 24;
    private static readonly char[] TurkishSpecificCharacters = ['ç', 'ğ', 'ı', 'İ', 'ö', 'ş', 'ü', 'Ç', 'Ğ', 'Ö', 'Ş', 'Ü'];
    private static SupplementaryOcrHandler? _supplementaryEngine;

    public static void RegisterSupplementaryEngine(SupplementaryOcrHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _supplementaryEngine, handler);
    }

    public static Task<string> RecognizeAsync(BitmapSource source) =>
        RecognizeAsync(source, OcrRecognitionMode.Balanced, CancellationToken.None);

    public static Task<string> RecognizeAsync(BitmapSource source, CancellationToken cancellationToken) =>
        RecognizeAsync(source, OcrRecognitionMode.Balanced, cancellationToken);

    public static Task<string> RecognizeAsync(BitmapSource source, OcrRecognitionMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 ||
            (long)source.PixelWidth * source.PixelHeight > MaximumImagePixels)
            throw new InvalidOperationException("Görsel OCR için 30 megapiksel sınırını aşıyor.");

        var (bgra, width, height) = ExtractBgraPixels(source);
        return RecognizeBgraAsync(bgra, width, height, mode, cancellationToken);
    }

    public static Task<string> RecognizeBgraAsync(
        byte[] bgraPixels,
        int width,
        int height,
        OcrRecognitionMode mode = OcrRecognitionMode.Accurate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (width <= 0 || height <= 0 || (long)width * height > MaximumImagePixels)
            throw new InvalidOperationException("Görsel OCR için 30 megapiksel sınırını aşıyor.");
        var expectedLength = checked(width * height * 4);
        if (bgraPixels.Length < expectedLength)
            throw new ArgumentException("Piksel tamponu görsel boyutundan küçük.", nameof(bgraPixels));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => RecognizeCoreAsync(bgraPixels, width, height, mode, cancellationToken), cancellationToken);
    }

    private static async Task<string> RecognizeCoreAsync(
        byte[] bgra,
        int width,
        int height,
        OcrRecognitionMode mode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (winEngine, winHasTurkish) = ResolveWindowsOcrEngine();
        var supplementary = Volatile.Read(ref _supplementaryEngine);
        if (winEngine is null && supplementary is null)
            throw new InvalidOperationException("Windows OCR altyapısı veya uygun dil paketi bulunamadı.");

        var singleLineHint = height <= 48 || (height <= 72 && width >= height * 4);
        var gray = ToGrayscale(bgra, width, height);
        var (standardPixels, stdW, stdH, isFlat) = PrepareLuminancePass(gray, width, height);

        if (isFlat)
            return string.Empty;

        var primary = await RunEnginesOnGrayscaleAsync(
            standardPixels, stdW, stdH, singleLineHint, mode, winEngine, winHasTurkish, supplementary, cancellationToken)
            .ConfigureAwait(false);

        if (mode == OcrRecognitionMode.Accurate && ShouldTrySecondaryPasses(primary))
        {
            var (distPixels, distW, distH, distFlat) = PrepareColorDistancePass(bgra, width, height);
            if (!distFlat)
            {
                var secondary = await RunEnginesOnGrayscaleAsync(
                    distPixels, distW, distH, singleLineHint, mode, winEngine, winHasTurkish, supplementary, cancellationToken)
                    .ConfigureAwait(false);
                primary = PickHigherScoring(primary, secondary, winHasTurkish);
            }

            if (ShouldTrySecondaryPasses(primary))
            {
                var adaptive = PrepareAdaptiveThresholdPass(standardPixels, stdW, stdH);
                var tertiary = await RunEnginesOnGrayscaleAsync(
                    adaptive, stdW, stdH, singleLineHint, mode, winEngine, winHasTurkish, supplementary, cancellationToken)
                    .ConfigureAwait(false);
                primary = PickHigherScoring(primary, tertiary, winHasTurkish);
            }
        }

        return primary.Text;
    }

    private static async Task<OcrCandidate> RunEnginesOnGrayscaleAsync(
        byte[] gray,
        int width,
        int height,
        bool singleLineHint,
        OcrRecognitionMode mode,
        OcrEngine? winEngine,
        bool winHasTurkish,
        SupplementaryOcrHandler? supplementary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mode == OcrRecognitionMode.Accurate && winEngine is not null && supplementary is not null)
        {
            var winTask = RecognizeWithWindowsOcrAsync(winEngine, gray, width, height, cancellationToken);
            var tessTask = SafeRunSupplementaryAsync(supplementary, gray, width, height, singleLineHint, cancellationToken);
            await Task.WhenAll(winTask, tessTask).ConfigureAwait(false);

            var winCandidate = new OcrCandidate(winTask.Result, 0.85f, IsWindows: true);
            var tessResult = tessTask.Result;
            var tessCandidate = new OcrCandidate(tessResult.Text, tessResult.Confidence, IsWindows: false);
            if (Environment.GetEnvironmentVariable("WINFORGE_OCR_DEBUG") == "1")
                Console.WriteLine($"  WIN='{winCandidate.Text}' | TESS='{tessCandidate.Text}' (conf={tessCandidate.Confidence:F2})");
            return PickHigherScoring(winCandidate, tessCandidate, winHasTurkish);
        }

        if (winEngine is not null)
        {
            var winText = await RecognizeWithWindowsOcrAsync(winEngine, gray, width, height, cancellationToken).ConfigureAwait(false);
            var winCandidate = new OcrCandidate(winText, 0.85f, IsWindows: true);
            if (!string.IsNullOrWhiteSpace(winText) || supplementary is null)
                return winCandidate;
        }

        if (supplementary is not null)
        {
            var tessResult = await SafeRunSupplementaryAsync(supplementary, gray, width, height, singleLineHint, cancellationToken).ConfigureAwait(false);
            return new OcrCandidate(tessResult.Text, tessResult.Confidence, IsWindows: false);
        }

        return OcrCandidate.Empty;
    }

    private static async Task<SupplementaryOcrResult> SafeRunSupplementaryAsync(
        SupplementaryOcrHandler handler,
        byte[] gray,
        int width,
        int height,
        bool singleLineHint,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler(gray, width, height, singleLineHint, cancellationToken).ConfigureAwait(false);
            return new SupplementaryOcrResult(NormalizeOutput(result.Text), result.Confidence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SupplementaryOcrResult(string.Empty, 0f);
        }
    }

    private static async Task<string> RecognizeWithWindowsOcrAsync(
        OcrEngine engine,
        byte[] gray,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var bgra = new byte[checked(width * height * 4)];
        for (var i = 0; i < gray.Length; i++)
        {
            var lum = gray[i];
            var dst = i * 4;
            bgra[dst] = lum;
            bgra[dst + 1] = lum;
            bgra[dst + 2] = lum;
            bgra[dst + 3] = 255;
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8,
            width, height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        return NormalizeOutput(string.Join(Environment.NewLine,
            result.Lines.Select(line => line.Text).Where(line => !string.IsNullOrWhiteSpace(line))));
    }

    private static (OcrEngine? Engine, bool HasTurkish) ResolveWindowsOcrEngine()
    {
        var turkish = new Language("tr-TR");
        var turkishAvailable = OcrEngine.IsLanguageSupported(turkish);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null && turkishAvailable && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase))
            engine = OcrEngine.TryCreateFromLanguage(turkish);
        engine ??= OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        if (engine is null)
        {
            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine is not null) break;
            }
        }

        var hasTurkish = engine?.RecognizerLanguage.LanguageTag.StartsWith("tr", StringComparison.OrdinalIgnoreCase) == true;
        return (engine, hasTurkish);
    }

    private static (byte[] Bgra, int Width, int Height) ExtractBgraPixels(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];

        var format = source.Format;
        if (format == PixelFormats.Bgra32 || format == PixelFormats.Bgr32 || format == PixelFormats.Pbgra32)
        {
            source.CopyPixels(pixels, stride, 0);
            if (format != PixelFormats.Bgr32)
                FlattenAlphaOverWhiteIfMeaningful(pixels, premultiplied: format == PixelFormats.Pbgra32);
            return (pixels, width, height);
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, stride, 0);
        FlattenAlphaOverWhiteIfMeaningful(pixels, premultiplied: false);
        return (pixels, width, height);
    }

    private static void FlattenAlphaOverWhiteIfMeaningful(byte[] bgra, bool premultiplied)
    {
        var hasNonZero = false;
        var hasTransparent = false;
        for (var i = 3; i < bgra.Length; i += 4)
        {
            var a = bgra[i];
            if (a > 0) hasNonZero = true;
            if (a < 255) hasTransparent = true;
            if (hasNonZero && hasTransparent) break;
        }

        // GDI BitBlt leaves alpha=0 across the whole buffer; preserve raw RGB values as opaque.
        if (!hasNonZero || !hasTransparent) return;

        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255) continue;
            var invA = 255 - a;
            if (premultiplied)
            {
                bgra[i] = (byte)Math.Min(255, bgra[i] + invA);
                bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] + invA);
                bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] + invA);
            }
            else
            {
                bgra[i] = (byte)((bgra[i] * a + 255 * invA + 127) / 255);
                bgra[i + 1] = (byte)((bgra[i + 1] * a + 255 * invA + 127) / 255);
                bgra[i + 2] = (byte)((bgra[i + 2] * a + 255 * invA + 127) / 255);
            }
            bgra[i + 3] = 255;
        }
    }

    private static byte[] ToGrayscale(byte[] bgra, int width, int height)
    {
        var count = checked(width * height);
        var gray = new byte[count];
        for (var i = 0; i < count; i++)
        {
            var src = i * 4;
            gray[i] = (byte)((29 * bgra[src] + 150 * bgra[src + 1] + 77 * bgra[src + 2] + 128) >> 8);
        }
        return gray;
    }

    private static (byte[] Pixels, int Width, int Height, bool IsFlat) PrepareLuminancePass(byte[] gray, int width, int height)
    {
        var work = new byte[gray.Length];
        Buffer.BlockCopy(gray, 0, work, 0, gray.Length);

        var histogram = BuildHistogram(work);
        var minVal = FirstNonZeroBin(histogram);
        var maxVal = LastNonZeroBin(histogram);
        if (maxVal - minVal < 8)
            return (work, width, height, true);

        var overallMedian = PercentileFromHistogram(histogram, work.Length, 0.50);
        var borderMedian = EstimateBorderMedian(work, width, height);
        var isDark = overallMedian < 128 || (borderMedian < 96 && overallMedian < 144);

        if (isDark)
        {
            for (var i = 0; i < work.Length; i++)
                work[i] = (byte)(255 - work[i]);
            histogram = BuildHistogram(work);
        }

        // Stretch dynamic range so faint gray text on light/inverted backgrounds becomes crisp black on white.
        var pDark = PercentileFromHistogram(histogram, work.Length, 0.002);
        var pLight = PercentileFromHistogram(histogram, work.Length, 0.85);
        if (pLight - pDark >= 20)
            ApplyContrastStretch(work, pDark, pLight);

        return UpscaleSharpenAndPad(work, width, height);
    }

    private static (byte[] Pixels, int Width, int Height, bool IsFlat) PrepareColorDistancePass(byte[] bgra, int width, int height)
    {
        var (bgR, bgG, bgB) = EstimateBorderMedianRgb(bgra, width, height);
        var count = checked(width * height);
        var distGray = new byte[count];
        var maxDist = 0;

        for (var i = 0; i < count; i++)
        {
            var src = i * 4;
            var db = bgra[src] - bgB;
            var dg = bgra[src + 1] - bgG;
            var dr = bgra[src + 2] - bgR;
            var d = (int)Math.Sqrt(dr * dr + dg * dg + db * db);
            if (d > maxDist) maxDist = d;
            // Background (distance 0) becomes white (255); colored foreground text becomes dark.
            distGray[i] = (byte)(255 - Math.Min(255, d));
        }

        if (maxDist < 18)
            return (distGray, width, height, true);

        var histogram = BuildHistogram(distGray);
        var pDark = PercentileFromHistogram(histogram, count, 0.002);
        var pLight = PercentileFromHistogram(histogram, count, 0.85);
        if (pLight - pDark >= 16)
            ApplyContrastStretch(distGray, pDark, pLight);

        return UpscaleSharpenAndPad(distGray, width, height);
    }

    private static byte[] PrepareAdaptiveThresholdPass(byte[] gray, int width, int height)
    {
        // Integral-image local mean binarization handles gradients and textured backgrounds.
        var integral = new long[checked((width + 1) * (height + 1))];
        var iw = width + 1;
        for (var y = 0; y < height; y++)
        {
            long rowSum = 0;
            var srcRow = y * width;
            var dstRow = (y + 1) * iw;
            var prevRow = y * iw;
            for (var x = 0; x < width; x++)
            {
                rowSum += gray[srcRow + x];
                integral[dstRow + x + 1] = integral[prevRow + x + 1] + rowSum;
            }
        }

        var radius = Math.Clamp(Math.Min(width, height) / 12, 10, 22);
        var output = new byte[gray.Length];
        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - radius);
            var y1 = Math.Min(height - 1, y + radius);
            var rowOffset = y * width;
            var intTop = y0 * iw;
            var intBottom = (y1 + 1) * iw;
            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - radius);
                var x1 = Math.Min(width - 1, x + radius);
                var area = (x1 - x0 + 1) * (y1 - y0 + 1);
                var sum = integral[intBottom + x1 + 1] - integral[intTop + x1 + 1]
                        - integral[intBottom + x0] + integral[intTop + x0];
                var mean = sum / area;
                output[rowOffset + x] = gray[rowOffset + x] < mean - 10 ? (byte)0 : (byte)255;
            }
        }
        return output;
    }

    private static (byte[] Pixels, int Width, int Height, bool IsFlat) UpscaleSharpenAndPad(byte[] gray, int width, int height)
    {
        var maxDim = Math.Max(width, height);
        var maxAllowed = OcrEngine.MaxImageDimension - 2 * QuietZonePadding;
        double scale;
        if (maxDim > maxAllowed)
            scale = (double)maxAllowed / maxDim;
        else if (height < 64 || width < 180)
            scale = Math.Min(3.0, (double)maxAllowed / maxDim);
        else if (maxDim < 1400 && (long)width * height < 1_500_000)
            scale = Math.Min(2.0, (double)maxAllowed / maxDim);
        else
            scale = 1.0;

        byte[] scaled;
        int scaledW;
        int scaledH;
        if (Math.Abs(scale - 1.0) > 0.01)
        {
            scaledW = Math.Max(1, (int)Math.Round(width * scale));
            scaledH = Math.Max(1, (int)Math.Round(height * scale));
            scaled = scale > 1.0
                ? ResizeBicubic(gray, width, height, scaledW, scaledH)
                : ResizeBilinear(gray, width, height, scaledW, scaledH);
            if (scale >= 1.5)
                scaled = UnsharpMask(scaled, scaledW, scaledH);
        }
        else
        {
            scaled = gray;
            scaledW = width;
            scaledH = height;
        }

        var paddedW = scaledW + 2 * QuietZonePadding;
        var paddedH = scaledH + 2 * QuietZonePadding;
        var padded = new byte[checked(paddedW * paddedH)];
        Array.Fill(padded, (byte)255);
        for (var y = 0; y < scaledH; y++)
            Buffer.BlockCopy(scaled, y * scaledW, padded, (y + QuietZonePadding) * paddedW + QuietZonePadding, scaledW);

        return (padded, paddedW, paddedH, false);
    }

    private static byte[] ResizeBilinear(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[checked(dstW * dstH)];
        var xRatio = (double)srcW / dstW;
        var yRatio = (double)srcH / dstH;
        for (var y = 0; y < dstH; y++)
        {
            var sy = Math.Clamp((y + 0.5) * yRatio - 0.5, 0, srcH - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(srcH - 1, y0 + 1);
            var fy = sy - y0;
            var row0 = y0 * srcW;
            var row1 = y1 * srcW;
            var dstRow = y * dstW;
            for (var x = 0; x < dstW; x++)
            {
                var sx = Math.Clamp((x + 0.5) * xRatio - 0.5, 0, srcW - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(srcW - 1, x0 + 1);
                var fx = sx - x0;
                var top = src[row0 + x0] + (src[row0 + x1] - src[row0 + x0]) * fx;
                var bottom = src[row1 + x0] + (src[row1 + x1] - src[row1 + x0]) * fx;
                dst[dstRow + x] = (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
            }
        }
        return dst;
    }

    private static byte[] ResizeBicubic(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[checked(dstW * dstH)];
        var xRatio = (double)srcW / dstW;
        var yRatio = (double)srcH / dstH;

        var sxArr = new int[dstW * 4];
        var wxArr = new double[dstW * 4];
        for (var x = 0; x < dstW; x++)
        {
            var sx = (x + 0.5) * xRatio - 0.5;
            var ix = (int)Math.Floor(sx);
            var fx = sx - ix;
            for (var k = -1; k <= 2; k++)
            {
                sxArr[x * 4 + (k + 1)] = Math.Clamp(ix + k, 0, srcW - 1);
                wxArr[x * 4 + (k + 1)] = CatmullRomWeight(k - fx);
            }
        }

        for (var y = 0; y < dstH; y++)
        {
            var sy = (y + 0.5) * yRatio - 0.5;
            var iy = (int)Math.Floor(sy);
            var fy = sy - iy;
            var sy0 = Math.Clamp(iy - 1, 0, srcH - 1) * srcW;
            var sy1 = Math.Clamp(iy, 0, srcH - 1) * srcW;
            var sy2 = Math.Clamp(iy + 1, 0, srcH - 1) * srcW;
            var sy3 = Math.Clamp(iy + 2, 0, srcH - 1) * srcW;
            var wy0 = CatmullRomWeight(-1 - fy);
            var wy1 = CatmullRomWeight(-fy);
            var wy2 = CatmullRomWeight(1 - fy);
            var wy3 = CatmullRomWeight(2 - fy);
            var dstRow = y * dstW;

            for (var x = 0; x < dstW; x++)
            {
                var baseIdx = x * 4;
                var ix0 = sxArr[baseIdx];
                var ix1 = sxArr[baseIdx + 1];
                var ix2 = sxArr[baseIdx + 2];
                var ix3 = sxArr[baseIdx + 3];
                var wx0 = wxArr[baseIdx];
                var wx1 = wxArr[baseIdx + 1];
                var wx2 = wxArr[baseIdx + 2];
                var wx3 = wxArr[baseIdx + 3];

                var r0 = src[sy0 + ix0] * wx0 + src[sy0 + ix1] * wx1 + src[sy0 + ix2] * wx2 + src[sy0 + ix3] * wx3;
                var r1 = src[sy1 + ix0] * wx0 + src[sy1 + ix1] * wx1 + src[sy1 + ix2] * wx2 + src[sy1 + ix3] * wx3;
                var r2 = src[sy2 + ix0] * wx0 + src[sy2 + ix1] * wx1 + src[sy2 + ix2] * wx2 + src[sy2 + ix3] * wx3;
                var r3 = src[sy3 + ix0] * wx0 + src[sy3 + ix1] * wx1 + src[sy3 + ix2] * wx2 + src[sy3 + ix3] * wx3;

                var val = r0 * wy0 + r1 * wy1 + r2 * wy2 + r3 * wy3;
                dst[dstRow + x] = (byte)Math.Clamp((int)Math.Round(val), 0, 255);
            }
        }
        return dst;
    }

    private static double CatmullRomWeight(double t)
    {
        var x = Math.Abs(t);
        if (x < 1.0) return (1.5 * x - 2.5) * x * x + 1.0;
        if (x < 2.0) return ((-0.5 * x + 2.5) * x - 4.0) * x + 2.0;
        return 0.0;
    }

    private static byte[] UnsharpMask(byte[] gray, int width, int height)
    {
        if (width < 3 || height < 3) return gray;
        var result = new byte[gray.Length];
        Buffer.BlockCopy(gray, 0, result, 0, gray.Length);
        for (var y = 1; y < height - 1; y++)
        {
            var row = y * width;
            var prev = row - width;
            var next = row + width;
            for (var x = 1; x < width - 1; x++)
            {
                var idx = row + x;
                var center = gray[idx];
                var blur = (gray[prev + x] + gray[next + x] + gray[idx - 1] + gray[idx + 1] + center * 2) / 6;
                var sharpened = center + ((center - blur) * 45) / 100;
                result[idx] = (byte)Math.Clamp(sharpened, 0, 255);
            }
        }
        return result;
    }

    private static int[] BuildHistogram(byte[] gray)
    {
        var histogram = new int[256];
        for (var i = 0; i < gray.Length; i++)
            histogram[gray[i]]++;
        return histogram;
    }

    private static int FirstNonZeroBin(int[] histogram)
    {
        for (var i = 0; i < 256; i++)
            if (histogram[i] > 0) return i;
        return 255;
    }

    private static int LastNonZeroBin(int[] histogram)
    {
        for (var i = 255; i >= 0; i--)
            if (histogram[i] > 0) return i;
        return 0;
    }

    private static int PercentileFromHistogram(int[] histogram, int totalCount, double fraction)
    {
        var target = (long)Math.Clamp(Math.Round(totalCount * fraction), 0, totalCount - 1);
        long cumulative = 0;
        for (var i = 0; i < 256; i++)
        {
            cumulative += histogram[i];
            if (cumulative > target) return i;
        }
        return 255;
    }

    private static int EstimateBorderMedian(byte[] gray, int width, int height)
    {
        var histogram = new int[256];
        var count = 0;
        for (var x = 0; x < width; x++)
        {
            histogram[gray[x]]++;
            histogram[gray[(height - 1) * width + x]]++;
            count += 2;
        }
        for (var y = 1; y < height - 1; y++)
        {
            histogram[gray[y * width]]++;
            histogram[gray[y * width + width - 1]]++;
            count += 2;
        }
        return count == 0 ? 255 : PercentileFromHistogram(histogram, count, 0.50);
    }

    private static (byte R, byte G, byte B) EstimateBorderMedianRgb(byte[] bgra, int width, int height)
    {
        var histR = new int[256];
        var histG = new int[256];
        var histB = new int[256];
        var count = 0;

        void Sample(int pixelIndex)
        {
            var offset = pixelIndex * 4;
            histB[bgra[offset]]++;
            histG[bgra[offset + 1]]++;
            histR[bgra[offset + 2]]++;
            count++;
        }

        for (var x = 0; x < width; x++)
        {
            Sample(x);
            if (height > 1) Sample((height - 1) * width + x);
        }
        for (var y = 1; y < height - 1; y++)
        {
            Sample(y * width);
            if (width > 1) Sample(y * width + width - 1);
        }

        if (count == 0) return (255, 255, 255);
        return (
            (byte)PercentileFromHistogram(histR, count, 0.50),
            (byte)PercentileFromHistogram(histG, count, 0.50),
            (byte)PercentileFromHistogram(histB, count, 0.50));
    }

    private static void ApplyContrastStretch(byte[] gray, int low, int high)
    {
        var range = high - low;
        if (range <= 0) return;
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            if (i <= low) lut[i] = 0;
            else if (i >= high) lut[i] = 255;
            else lut[i] = (byte)(((i - low) * 255 + range / 2) / range);
        }
        for (var i = 0; i < gray.Length; i++)
            gray[i] = lut[gray[i]];
    }

    private static bool ShouldTrySecondaryPasses(OcrCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Text)) return true;
        var clean = AlphanumericCount(candidate.Text);
        if (clean == 0) return true;
        return !candidate.IsWindows && candidate.Confidence < 0.40f && clean < 4;
    }

    private static OcrCandidate PickHigherScoring(OcrCandidate first, OcrCandidate second, bool winHasTurkish)
    {
        var firstEmpty = string.IsNullOrWhiteSpace(first.Text);
        var secondEmpty = string.IsNullOrWhiteSpace(second.Text);
        if (firstEmpty && secondEmpty) return OcrCandidate.Empty;
        if (firstEmpty) return second;
        if (secondEmpty) return first;

        // If one is Windows OCR without Turkish support and the other is Tesseract (eng+tur):
        if (first.IsWindows != second.IsWindows)
        {
            var win = first.IsWindows ? first : second;
            var tess = first.IsWindows ? second : first;

            if (!winHasTurkish && ContainsTurkishDiacritics(tess.Text) && tess.Confidence >= 0.45f)
            {
                // Confirm Tesseract actually read coherent text rather than noise.
                var similarity = WordOverlapRatio(FoldDiacritics(win.Text), FoldDiacritics(tess.Text));
                if (similarity >= 0.45 || ScoreTextQuality(tess.Text, tess.Confidence) >= ScoreTextQuality(win.Text, win.Confidence) * 0.90)
                    return tess;
            }

            // If Tesseract is confident and has cleaner/richer output, prefer it; otherwise compare quality.
            var winScore = ScoreTextQuality(win.Text, win.Confidence);
            var tessScore = ScoreTextQuality(tess.Text, tess.Confidence);
            if (tess.Confidence < 0.50f && winScore >= tessScore * 0.70)
                return win;
            return tessScore > winScore * 1.08 ? tess : win;
        }

        return ScoreTextQuality(second.Text, second.Confidence) > ScoreTextQuality(first.Text, first.Confidence)
            ? second
            : first;
    }

    private static double ScoreTextQuality(string text, float confidence)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var alnum = 0;
        var normalPunct = 0;
        var junk = 0;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) alnum++;
            else if (char.IsWhiteSpace(ch)) continue;
            else if (".,:;!?-_/'\"()[]{}@#$%&*+=<>\\|".Contains(ch))
            {
                if (ch is '|' or '~' or '^') junk++;
                else normalPunct++;
            }
            else junk++;
        }

        if (alnum == 0) return 0;
        var totalVisible = alnum + normalPunct + junk;
        var cleanliness = (double)(alnum + normalPunct * 0.5) / Math.Max(1, totalVisible);
        var lengthFactor = Math.Min(alnum, 200) * 0.25 + Math.Sqrt(alnum) * 2.0;
        var confidenceFactor = 0.65 + 0.45 * Math.Clamp(confidence, 0f, 1f);
        return Math.Max(0, (lengthFactor * cleanliness - junk * 1.5) * confidenceFactor);
    }

    private static int AlphanumericCount(string text)
    {
        var count = 0;
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch)) count++;
        return count;
    }

    private static bool ContainsTurkishDiacritics(string text) =>
        text.IndexOfAny(TurkishSpecificCharacters) >= 0;

    private static string FoldDiacritics(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(ch == 'ı' ? 'i' : ch == 'İ' ? 'I' : char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    private static double WordOverlapRatio(string a, string b)
    {
        var tokensA = a.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var tokensB = b.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokensA.Length == 0 || tokensB.Length == 0) return 0;
        var setA = new HashSet<string>(tokensA, StringComparer.OrdinalIgnoreCase);
        var matches = tokensB.Count(token => setA.Contains(token));
        return (double)matches / Math.Max(setA.Count, tokensB.Length);
    }

    private static string NormalizeOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .SkipWhile(string.IsNullOrWhiteSpace)
            .ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines);
    }

    private readonly record struct OcrCandidate(string Text, float Confidence, bool IsWindows)
    {
        public static readonly OcrCandidate Empty = new(string.Empty, 0f, false);
    }
}
