using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Extras;

internal static class AdvancedPasteArtifacts
{
    private const string FilePrefix = "winforge-advanced-paste-";
    internal const int DefaultJpegQuality = 90;
    private static readonly TimeSpan FileLifetime = TimeSpan.FromDays(30);

    public static string CreateFile(AdvancedPasteAction action, ClipboardTextSnapshot snapshot, string text,
        string? outputFolder = null)
    {
        var folder = outputFolder ?? Path.Combine(WinForge.Core.AppData.TemporaryDirectory, "AdvancedPaste");
        Directory.CreateDirectory(folder);
        CleanupExpiredFiles(folder);

        var extension = action switch
        {
            AdvancedPasteAction.PasteAsHtmlFile => ".html",
            AdvancedPasteAction.PasteAsPngFile => ".png",
            AdvancedPasteAction.ConvertImageToJpeg => ".jpg",
            AdvancedPasteAction.ConvertImageToBmp => ".bmp",
            AdvancedPasteAction.ConvertImageToGif => ".gif",
            _ => ".txt"
        };
        var path = Path.Combine(folder, FilePrefix + Guid.NewGuid().ToString("N") + extension);

        if (IsImageFileAction(action))
        {
            var image = snapshot.Image ?? throw new InvalidOperationException("Panoda görsel yok.");
            var encodedImage = action == AdvancedPasteAction.ConvertImageToJpeg ? CompositeOnWhite(image) : image;
            var encoder = CreateImageEncoder(action, snapshot.JpegQuality);
            encoder.Frames.Add(BitmapFrame.Create(encodedImage));
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        else
        {
            var content = action == AdvancedPasteAction.PasteAsHtmlFile
                ? AdvancedPasteTransforms.ToHtmlDocument(snapshot.Html, snapshot.Text)
                : text;
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return path;
    }

    public static bool IsImageFileAction(AdvancedPasteAction action) => action is
        AdvancedPasteAction.PasteAsPngFile or AdvancedPasteAction.ConvertImageToJpeg or
        AdvancedPasteAction.ConvertImageToBmp or AdvancedPasteAction.ConvertImageToGif;

    public static bool IsFileAction(AdvancedPasteAction action) => action is
        AdvancedPasteAction.PasteAsTextFile or AdvancedPasteAction.PasteAsHtmlFile || IsImageFileAction(action);

    private static BitmapEncoder CreateImageEncoder(AdvancedPasteAction action, int jpegQuality) => action switch
    {
        AdvancedPasteAction.PasteAsPngFile => new PngBitmapEncoder(),
        AdvancedPasteAction.ConvertImageToBmp => new BmpBitmapEncoder(),
        AdvancedPasteAction.ConvertImageToGif => new GifBitmapEncoder(),
        AdvancedPasteAction.ConvertImageToJpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 10, 100) },
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static BitmapSource CompositeOnWhite(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        }
        var rendered = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    public static StringCollection CreateFileDropList(string path)
    {
        var list = new StringCollection();
        list.Add(path);
        return list;
    }

    private static void CleanupExpiredFiles(string folder)
    {
        var cutoff = DateTime.UtcNow - FileLifetime;
        foreach (var path in Directory.EnumerateFiles(folder, FilePrefix + "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
