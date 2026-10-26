using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.WindowControl;

internal static partial class WindowControlNative
{
    internal const int WindowStyleIndex = -16;
    internal const long ResizableStyle = 0x00040000;
    internal const long CaptionStyle = 0x00C00000;
    internal const long BorderStyles = CaptionStyle | ResizableStyle;
    internal const long LayeredStyle = 0x00080000;
    internal const int MouseMove = 0x0200, XButtonUp = 0x020C, HorizontalWheel = 0x020E;
    internal const int SystemCommand = 0x0112, GetMinMaxInfo = 0x0024;
    internal const uint NoZOrder = 0x0004, FrameChanged = 0x0020, AsyncWindowPosition = 0x4000;
    internal const int Restore = 9, Maximize = 3, Minimize = 6;
    internal const uint MenuReturnCommand = 0x0100, MenuRightButton = 0x0002;
    internal const int DoubleClickWidthMetric = 36, DoubleClickHeightMetric = 37;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public POINT MinimumPosition, MaximumPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, ref MinMaxInfo info, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] internal static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);
}
