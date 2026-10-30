using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.ClipboardLine;

internal static class OcrChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-OcrChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1100, 240));
                var font = new Typeface("Segoe UI");
                drawing.DrawText(new FormattedText("ISTANBUL TÜRKÇE MERHABA", CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight, font, 58, Brushes.Black, 1), new Point(15, 15));
                drawing.DrawText(new FormattedText("THE QUICK BROWN FOX", CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, font, 58, Brushes.Black, 1), new Point(15, 115));
            }
            var bitmap = new RenderTargetBitmap(1100, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var input = Path.Combine(root, "recognition.png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(input)) encoder.Save(stream);
            var recognized = Fold(await LocalOcr.RecognizeAsync(input, root, CancellationToken.None));
            check(recognized.Contains("istanbul") && recognized.Contains("turkce") && recognized.Contains("merhaba"), "Packaged Turkish model recognizes a rendered Turkish fixture.");
            check(recognized.Contains("quick") && recognized.Contains("brown") && recognized.Contains("fox"), "Packaged English model recognizes a rendered English fixture.");
            var library = new ClipboardLibrary(Path.Combine(root, "library")); var item = library.AddMedia(input);
            library.Update([item.Id], value => value with { OcrText = recognized }, undo: false, touch: false);
            check(new ClipboardLibrary(library.DirectoryPath).Search("turkce quick").Single().Id == item.Id, "Real OCR text remains searchable after library restart.");
        }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar); var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("WinForge-OcrChecks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD).Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
}
