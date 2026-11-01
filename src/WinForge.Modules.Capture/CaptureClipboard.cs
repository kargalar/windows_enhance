using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

internal static class CaptureClipboard
{
    internal static DataObject CreateData(BitmapSource image, string directory)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Ekran-" + Convert.ToHexString(SHA256.HashData(bytes)) + ".png");
        // Keep clipboard files independent of history retention and editor changes.
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        var data = new DataObject();
        data.SetImage(image);
        data.SetFileDropList(new StringCollection { path });
        return data;
    }

    internal static async Task CopyAsync(BitmapSource image, CancellationToken token)
    {
        var data = CreateData(image, Path.Combine(Path.GetTempPath(), "WinForge", "CaptureClipboard"));
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
