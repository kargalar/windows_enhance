using WinForge.Core;

namespace WinForge.Modules.TextGrabber;

public static class ScreenCapture
{
    public static (byte[] Pixels, int Width, int Height) Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Geçersiz yakalama alanı.");

        var screen = Native.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("Ekran yakalanamadı.");

        IntPtr memory = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;

        try
        {
            memory = Native.CreateCompatibleDC(screen);
            bitmap = Native.CreateCompatibleBitmap(screen, width, height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Görüntü tamponu oluşturulamadı.");

            Native.SelectObject(memory, bitmap);

            if (!Native.BitBlt(memory, 0, 0, width, height, screen, x, y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException("Ekran görüntüsü alınamadı.");

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

            var pixels = new byte[width * height * 4];
            var lines = Native.GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);
            if (lines == 0) throw new InvalidOperationException("Pikseller okunamadı.");

            return (pixels, width, height);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
            if (memory != IntPtr.Zero) Native.DeleteDC(memory);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
