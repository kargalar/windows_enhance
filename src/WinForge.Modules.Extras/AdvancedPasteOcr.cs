using System.Windows.Media.Imaging;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal static class AdvancedPasteOcr
{
    public static Task<string> RecognizeAsync(BitmapSource source) => OcrRecognitionService.RecognizeAsync(source);
}
