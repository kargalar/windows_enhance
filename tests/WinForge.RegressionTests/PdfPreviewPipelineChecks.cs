using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class PdfPreviewPipelineChecks
{
    private const string ValidStatus = "{\"width\":2,\"height\":3,\"pageCount\":1,\"backend\":\"warp\"}";

    internal static void Run(string directory, Action<bool, string> check)
    {
        var output = Path.Combine(directory, "pdf-pipeline-fixture.png");
        var pixels = Enumerable.Repeat((byte)255, 24).ToArray();
        var source = BitmapSource.Create(2, 3, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = File.Create(output)) encoder.Save(stream);
        var decoded = CorePdfPreviewService.DecodePreview(output, ValidStatus, 1400);
        check(decoded.Image.PixelWidth == 2 && decoded.Image.PixelHeight == 3 && decoded.Image.IsFrozen && decoded.PageCount == 1,
            "Own PDF pipeline decodes verified WARP metadata into a frozen bounded preview image");
        check(Rejects<InvalidDataException>(() => CorePdfPreviewService.DecodePreview(output,
            ValidStatus.Replace("\"width\":2", "\"width\":3", StringComparison.Ordinal), 1400)),
            "PDF preview rejects native dimensions that disagree with the PNG header before image allocation");
        check(Rejects<InvalidDataException>(() => CorePdfPreviewService.DecodePreview(output,
            ValidStatus.Replace("\"width\":2", "\"width\":4097", StringComparison.Ordinal), 1400)),
            "PDF preview rejects oversized native dimensions before image allocation");
        check(Rejects<InvalidDataException>(() => CorePdfPreviewService.DecodePreview(output,
                ValidStatus.Replace("warp", "hardware", StringComparison.Ordinal), 1400)) &&
            Rejects<InvalidDataException>(() => CorePdfPreviewService.DecodePreview(output,
                ValidStatus.Replace("\"pageCount\":1", "\"pageCount\":0", StringComparison.Ordinal), 1400)),
            "PDF preview rejects stale hardware-backend metadata and invalid page counts");
        check(Rejects<ArgumentOutOfRangeException>(() => CorePdfPreviewService.RenderFirstPageAsync(output, 63).GetAwaiter().GetResult()) &&
            Rejects<ArgumentOutOfRangeException>(() => CorePdfPreviewService.RenderFirstPageAsync(output, 4097).GetAwaiter().GetResult()),
            "PDF preview bounds requested native dimensions before launching a helper");
        check(Rejects<FileNotFoundException>(() => CorePdfPreviewService.RenderFirstPageAsync(output + ".missing").GetAwaiter().GetResult()),
            "PDF preview rejects absent input without launching a native helper");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        check(Rejects<OperationCanceledException>(() => CorePdfPreviewService.RenderFirstPageAsync(output, cancellation: cancellation.Token).GetAwaiter().GetResult()),
            "An already cancelled PDF preview never creates a native helper or temporary output");
        var bytes = File.ReadAllBytes(output);
        bytes[0] = 0;
        File.WriteAllBytes(output, bytes);
        check(Rejects<InvalidDataException>(() => CorePdfPreviewService.DecodePreview(output, ValidStatus, 1400)),
            "PDF preview rejects a damaged PNG signature before calling an image decoder");
        File.Delete(output);
        var readback = new byte[24];
        new FormatConvertedBitmap(decoded.Image, PixelFormats.Bgra32, null, 0).CopyPixels(readback, 8, 0);
        check(readback.SequenceEqual(pixels), "Frozen PDF previews remain readable after their operation's temporary PNG is deleted");
    }

    private static bool Rejects<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }
}
