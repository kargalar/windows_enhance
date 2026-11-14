using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForge.ClipboardLine;
using WinForge.Core;

internal static class OcrChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge-OcrChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            LocalOcr.RegisterWithCore();

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

            var cases = new (string Name, BitmapSource Image, string Expected)[]
            {
                ("small", RenderGdiStyle("Dosya boyutu 14,8 MB", 180, 22, 12, Color.FromRgb(255, 255, 255), Color.FromRgb(20, 20, 20), 2, 2), "Dosya boyutu 14,8 MB"),
                ("dark", RenderGdiStyle("WinForge karanlık tema testi 2026", 420, 48, 16, Color.FromRgb(30, 30, 30), Color.FromRgb(204, 204, 204), 8, 10), "WinForge karanlık tema testi 2026"),
                ("button", RenderGdiStyle("Ayarları Kaydet", 190, 40, 15, Color.FromRgb(0, 120, 212), Color.FromRgb(255, 255, 255), 16, 8), "Ayarları Kaydet"),
                ("iso", RenderGdiStyle("Error 404", 160, 44, 18, Color.FromRgb(46, 125, 50), Color.FromRgb(255, 72, 66), 14, 8), "Error 404"),
                ("tr", RenderGdiStyle("İstanbul'da güneşli hava · Çalışma klasörü", 520, 50, 18, Color.FromRgb(255, 255, 255), Color.FromRgb(16, 16, 16), 12, 10), "İstanbul'da güneşli hava · Çalışma klasörü"),
                ("ip", RenderGdiStyle("192.168.1.42", 120, 26, 13, Color.FromRgb(245, 245, 245), Color.FromRgb(20, 20, 20), 6, 4), "192.168.1.42"),
            };

            foreach (var (name, img, expected) in cases)
            {
                var bal = await OcrRecognitionService.RecognizeAsync(img, OcrRecognitionMode.Balanced);
                var acc = await OcrRecognitionService.RecognizeAsync(img, OcrRecognitionMode.Accurate);
                Console.WriteLine($"[{name}] expected='{expected}' | balanced='{bal}' | accurate='{acc}'");
            }

            // Small tight UI text (12px Segoe UI, 2px margin, GDI zero alpha)
            var small = cases[0].Image;
            var smallText = await OcrRecognitionService.RecognizeAsync(small, OcrRecognitionMode.Accurate);
            check(smallText.Contains("Dosya", StringComparison.OrdinalIgnoreCase) && smallText.Contains("14", StringComparison.Ordinal),
                $"Accurate OCR reads tight small UI text (got: '{smallText}').");

            // Dark-mode low-contrast text (#CCCCCC on #1E1E1E)
            var dark = cases[1].Image;
            var darkText = await OcrRecognitionService.RecognizeAsync(dark, OcrRecognitionMode.Accurate);
            check(darkText.Contains("WinForge", StringComparison.OrdinalIgnoreCase) && darkText.Contains("2026", StringComparison.Ordinal) &&
                  darkText.Contains("karanlık", StringComparison.OrdinalIgnoreCase),
                $"Accurate OCR reads dark-mode Turkish text with diacritics (got: '{darkText}').");

            // Colored button (white text on #0078D4 blue)
            var button = RenderGdiStyle("Ayarları Kaydet", 190, 40, 15, Color.FromRgb(0, 120, 212), Color.FromRgb(255, 255, 255), 16, 8);
            var buttonText = await OcrRecognitionService.RecognizeAsync(button, OcrRecognitionMode.Accurate);
            check(buttonText.Contains("Ayarları", StringComparison.OrdinalIgnoreCase) && buttonText.Contains("Kaydet", StringComparison.OrdinalIgnoreCase),
                $"Accurate OCR reads white Turkish text on colored button (got: '{buttonText}').");

            // Isoluminant colored text (bright red on medium green)
            var iso = RenderGdiStyle("Error 404", 160, 44, 18, Color.FromRgb(46, 125, 50), Color.FromRgb(255, 72, 66), 14, 8);
            var isoText = await OcrRecognitionService.RecognizeAsync(iso, OcrRecognitionMode.Accurate);
            check(isoText.Contains("Error", StringComparison.OrdinalIgnoreCase) && isoText.Contains("404", StringComparison.Ordinal),
                $"Accurate OCR reads isoluminant colored text via color-distance pass (got: '{isoText}').");

            // Turkish diacritics preservation
            var tr = RenderGdiStyle("İstanbul'da güneşli hava · Çalışma klasörü", 520, 50, 18, Color.FromRgb(255, 255, 255), Color.FromRgb(16, 16, 16), 12, 10);
            var trText = await OcrRecognitionService.RecognizeAsync(tr, OcrRecognitionMode.Accurate);
            check(trText.Contains("güneşli", StringComparison.OrdinalIgnoreCase) && trText.Contains("Çalışma", StringComparison.OrdinalIgnoreCase),
                $"Accurate OCR preserves Turkish diacritics (got: '{trText}').");

            // Very small numeric/IP snippet
            var ip = RenderGdiStyle("192.168.1.42", 120, 26, 13, Color.FromRgb(245, 245, 245), Color.FromRgb(20, 20, 20), 6, 4);
            var ipText = await OcrRecognitionService.RecognizeAsync(ip, OcrRecognitionMode.Accurate);
            check(ipText.Contains("192.168.1.42", StringComparison.Ordinal),
                $"Accurate OCR reads small IP snippet accurately (got: '{ipText}').");
        }
        finally
        {
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar); var resolved = Path.GetFullPath(root);
            if (Path.GetDirectoryName(resolved) == parent && Path.GetFileName(resolved).StartsWith("WinForge-OcrChecks-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    private static BitmapSource RenderGdiStyle(string text, int width, int height, double fontSize, Color bg, Color fg, double x, double y)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, width, height));
            drawing.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, new SolidColorBrush(fg), 1), new Point(x, y));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 0; // Simulate GDI BitBlt zero alpha
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        result.Freeze();
        return result;
    }

    private static string Fold(string value) => string.Concat(value.Normalize(NormalizationForm.FormD).Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
}
