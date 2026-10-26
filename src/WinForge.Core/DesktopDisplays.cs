using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinForge.Core;

public sealed record DesktopDisplay(IntPtr Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary);

public static class DesktopDisplays
{
    public static IReadOnlyList<DesktopDisplay> Enumerate()
    {
        var result = new List<DesktopDisplay>();
        MonitorCallback callback = (monitor, _, _, _) => { result.Add(Read(monitor)); return true; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    public static DesktopDisplay ForWindow(IntPtr window) => Read(MonitorFromWindow(window, 2));
    public static DesktopDisplay ForPoint(POINT point) => Read(MonitorFromPoint(point, 2));

    private static DesktopDisplay Read(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(monitor, info.Device, info.Bounds, info.WorkArea, (info.Flags & 1) != 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public RECT Bounds;
        public RECT WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rectangle, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
}
