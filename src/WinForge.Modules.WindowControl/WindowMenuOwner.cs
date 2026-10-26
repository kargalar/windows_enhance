using System.ComponentModel;
using System.Runtime.InteropServices;
using WinForge.Core;
using static WinForge.Modules.WindowControl.WindowControlNative;

namespace WinForge.Modules.WindowControl;

/// <summary>A real top-level window procedure owns and activates native action menus.</summary>
internal static class WindowMenuOwner
{
    internal const string ClassName = "WinForge.NativeWindowMenuOwner";
    internal const string Title = "WinForge.WindowMenu";
    private const uint PopupStyle = 0x90000000; // WS_POPUP | WS_VISIBLE; no taskbar entry.
    private const int ClassAlreadyExists = 1410;
    private static readonly WindowProcedure Procedure = DefWindowProc;
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    internal static IntPtr Create(POINT point)
    {
        lock (RegistrationLock)
        {
            if (!_registered)
            {
                var definition = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                    Instance = Native.GetModuleHandle(null), ClassName = ClassName
                };
                if (RegisterClassEx(ref definition) == 0 && Marshal.GetLastWin32Error() != ClassAlreadyExists)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Pencere eylem menüsü sınıfı oluşturulamadı.");
                _registered = true;
            }
        }
        var owner = CreatePopupOwner(Native.WS_EX_TOOLWINDOW, ClassName, Title, PopupStyle,
            point.X, point.Y, 0, 0, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (owner == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Pencere eylem menüsü oluşturulamadı.");
        return owner;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, nuint wParam, nint lParam);
}
