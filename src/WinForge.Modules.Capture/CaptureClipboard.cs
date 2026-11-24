using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

internal static class CaptureClipboard
{
    internal const string PngFormat = "PNG";

    internal static DataObject CreateData(BitmapSource image, string directory, CaptureImageFile file)
    {
        var png = CaptureImageFile.EncodePng(image);
        var data = new DataObject();
        data.SetData(PngFormat, new MemoryStream(png, writable: false), autoConvert: false);
        data.SetImage(image);
        try
        {
            // Keep clipboard files independent of history retention and editor changes.
            var path = file.Export(directory, png);
            data.SetFileDropList(new StringCollection { path });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Image paste is still available if the temporary file cannot be written.
        }
        return data;
    }

    internal static async Task CopyAsync(BitmapSource image, CaptureImageFile file, CancellationToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinForge", "CaptureClipboard");
        var data = image.IsFrozen
            ? await Task.Run(() => CreateData(image, directory, file), token)
            : CreateData(image, directory, file);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { Clipboard.SetDataObject(data, true); return; }
            catch (ExternalException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 9)
            {
                await Task.Delay(50, token);
            }
        }
    }
}
