namespace WinForge.Modules.TextGrabber;

public static class ScreenCapture
{
    public static (byte[] Pixels, int Width, int Height) Capture(int x, int y, int width, int height) =>
        WinForge.Core.DesktopCapture.Capture(x, y, width, height);
}
