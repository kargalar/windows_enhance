using System.ComponentModel;
using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal sealed class NativeWindowFixture : IDisposable
{
    private const int ClassAlreadyExists = 1410;
    private readonly string _className;
    private readonly bool _topmost;
    private bool _ownsClass;
    internal IntPtr Handle { get; private set; }

    internal NativeWindowFixture(string className, string title, RECT bounds, uint style, uint extendedStyle, IntPtr owner = default)
    {
        _className = className;
        _topmost = (extendedStyle & 8) != 0;
        var definition = new WindowClass
        {
            Procedure = NativeLibrary.GetExport(Native.GetModuleHandle("user32.dll"), "DefWindowProcW"),
            Instance = Native.GetModuleHandle(null), ClassName = className
        };
        _ownsClass = RegisterClass(ref definition) != 0;
        if (!_ownsClass && Marshal.GetLastWin32Error() != ClassAlreadyExists) throw new Win32Exception(Marshal.GetLastWin32Error());
        Handle = CreateWindowEx(extendedStyle | 0x00080000, className, title, style, bounds.Left, bounds.Top,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, owner, IntPtr.Zero, definition.Instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero) { Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (!SetLayeredWindowAttributes(Handle, 0, 0, 2)) { Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }

    internal void Show(bool behindOtherWindows = false)
    {
        ShowWindow(Handle, 4);
        if (_topmost) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        if (behindOtherWindows) Native.SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
        if (_ownsClass) { UnregisterClass(_className, Native.GetModuleHandle(null)); _ownsClass = false; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public IntPtr Procedure;
        public int ClassBytes;
        public int WindowBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? Menu;
        public string ClassName;
    }
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass definition);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
}
