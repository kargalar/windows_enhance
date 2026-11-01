using System.Runtime.InteropServices;
using System.Windows;

namespace WinForge.Core;

public static class CursorPanelPlacement
{
    public static Rect WorkArea
    {
        get
        {
            Native.GetCursorPos(out var point);
            var display = DesktopDisplays.ForPoint(point);
            var scale = GetDpiForMonitor(display.Handle, 0, out var x, out _) == 0 && x > 0 ? x / 96d : 1;
            var area = display.WorkArea;
            return new Rect(area.Left / scale, area.Top / scale,
                (area.Right - area.Left) / scale, (area.Bottom - area.Top) / scale);
        }
    }

    public static void Center(Window window)
    {
        var area = WorkArea;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = area.Left + Math.Max(0, (area.Width - window.Width) / 2);
        window.Top = area.Top + Math.Max(0, (area.Height - window.Height) / 2);
    }

    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
