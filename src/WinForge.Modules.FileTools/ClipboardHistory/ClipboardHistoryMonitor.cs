using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForge.ClipboardLine;
using WinForge.Core;
using WinForge.History;

namespace WinForge.FileTools.ClipboardHistory;

internal sealed record ClipboardHistorySnapshot(long CopiedAt, string? Text, byte[]? Png, BitmapSource? Bitmap);

internal static class ClipboardHistoryImage
{
    internal static ClipboardHistoryImageData? Prepare(ClipboardHistorySnapshot snapshot)
    {
        var bitmap = snapshot.Png is { } png ? Decode(png) : snapshot.Bitmap;
        if (bitmap is null) return null;
        var scale = Math.Min(1, Math.Min(192d / bitmap.PixelWidth, 120d / bitmap.PixelHeight));
        BitmapSource thumbnail = scale < 1 ? new TransformedBitmap(bitmap, new ScaleTransform(scale, scale)) : bitmap;
        return new(snapshot.Png ?? Encode(bitmap), Encode(thumbnail), bitmap.PixelWidth, bitmap.PixelHeight);
    }

    internal static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var bitmap = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

internal sealed class ClipboardHistoryMonitor : IDisposable
{
    internal const string PngFormat = "PNG";
    internal const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";
    internal const int ClipboardBusy = unchecked((int)0x800401D0);
    private const int ClipboardUpdateMessage = 0x031D;

    private readonly HwndSource _source;
    private readonly DispatcherTimer _retry;
    private readonly ILogger _log;
    private readonly Action<ClipboardHistorySnapshot> _capture;
    private uint _lastSequence;
    private int _attempts;

    internal ClipboardHistoryMonitor(ILogger log, Action<ClipboardHistorySnapshot> capture)
    {
        _log = log;
        _capture = capture;
        _source = new HwndSource(new HwndSourceParameters("WinForge Clipboard History") { ParentWindow = new IntPtr(-3) });
        _retry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _retry.Tick += Retry;
        _source.AddHook(WindowProcedure);
        if (!AddClipboardFormatListener(_source.Handle))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            _source.Dispose();
            throw error;
        }
        _lastSequence = FocusedTextOutput.GetClipboardSequenceNumber();
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClipboardUpdateMessage)
        {
            _attempts = 0;
            ReadClipboard();
        }
        return IntPtr.Zero;
    }

    private void Retry(object? sender, EventArgs args) => ReadClipboard();

    private void ReadClipboard()
    {
        _retry.Stop();
        var sequence = FocusedTextOutput.GetClipboardSequenceNumber();
        if (sequence == _lastSequence) return;
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data is null || ClipboardCapture.IsPrivate(data))
            {
                _lastSequence = sequence;
                return;
            }

            var text = data.GetData(DataFormats.UnicodeText) as string;
            var png = data.GetDataPresent(PngFormat, autoConvert: false)
                ? data.GetData(PngFormat, autoConvert: false) switch
                {
                    MemoryStream stream => stream.ToArray(),
                    byte[] bytes => bytes,
                    _ => null
                }
                : null;
            BitmapSource? bitmap = null;
            if (png is null && data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource image)
            {
                if (image.CanFreeze) image.Freeze();
                else
                {
                    image = new WriteableBitmap(image);
                    image.Freeze();
                }
                bitmap = image;
            }
            if (FocusedTextOutput.GetClipboardSequenceNumber() != sequence)
            {
                _retry.Start();
                return;
            }
            _lastSequence = sequence;
            if (!string.IsNullOrEmpty(text) || png is not null || bitmap is not null)
                _capture(new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), string.IsNullOrEmpty(text) ? null : text, png, bitmap));
        }
        catch (ExternalException error) when (error.HResult == ClipboardBusy && ++_attempts < 6)
        {
            _retry.Start();
        }
        catch (Exception error)
        {
            _lastSequence = sequence;
            _log.Error("Pano içeriği okunamadı.", error);
        }
    }

    public void Dispose()
    {
        _retry.Stop();
        _retry.Tick -= Retry;
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
