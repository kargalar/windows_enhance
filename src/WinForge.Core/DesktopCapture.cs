using WinForge.Core;

namespace WinForge.Core;

public static class DesktopCapture
{
    private const long MaximumCapturePixels = 30_000_000;

    public static (byte[] Pixels, int Width, int Height) Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Geçersiz yakalama alanı.");
        if ((long)width * height > MaximumCapturePixels)
            throw new ArgumentException("Yakalama alanı 30 megapiksel sınırını aşıyor.");

        var screen = Native.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("Ekran yakalanamadı.");

        IntPtr memory = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;

        try
        {
            memory = Native.CreateCompatibleDC(screen);
            bitmap = Native.CreateCompatibleBitmap(screen, width, height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Görüntü tamponu oluşturulamadı.");

            previousBitmap = Native.SelectObject(memory, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                throw new InvalidOperationException("Görüntü tamponu DC'ye seçilemedi.");

            if (!Native.BitBlt(memory, 0, 0, width, height, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException("Ekran görüntüsü alınamadı.");

            // GetDIBits requires that the bitmap is not selected into a device context.
            Native.SelectObject(memory, previousBitmap);
            previousBitmap = IntPtr.Zero;

            var info = new Native.BITMAPINFO
            {
                header = new Native.BITMAPINFOHEADER
                {
                    size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                    width = width,
                    height = -height,
                    planes = 1,
                    bitCount = 32,
                    compression = 0
                },
                colors = new uint[256]
            };

            var pixels = new byte[checked(width * height * 4)];
            var lines = Native.GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);
            if (lines != height) throw new InvalidOperationException("Piksellerin tamamı okunamadı.");

            return (pixels, width, height);
        }
        finally
        {
            if (memory != IntPtr.Zero && previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                Native.SelectObject(memory, previousBitmap);
            if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
            if (memory != IntPtr.Zero) Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
