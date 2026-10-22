using System.Runtime.InteropServices;

namespace WinForge.Core;

public sealed class GlobalMouseHook : IDisposable
{
    private Native.LowLevelMouseProc? _callback;
    private IntPtr _hook;

    public event Action<POINT>? MiddleButtonUp;
    public event Action<POINT, int>? WheelMoved;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;

        _callback = Handle;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Fare kancası kurulamadı.");
    }

    private IntPtr Handle(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt64() & 0xffff;
            if (message is Native.WM_MBUTTONUP or Native.WM_MOUSEWHEEL)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (message == Native.WM_MBUTTONUP)
                {
                    MiddleButtonUp?.Invoke(info.pt);
                }
                else
                {
                    var delta = (short)((info.mouseData >> 16) & 0xffff);
                    WheelMoved?.Invoke(info.pt, delta);
                }
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _callback = null;
    }
}
