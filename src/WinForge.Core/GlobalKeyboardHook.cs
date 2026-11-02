using System.Runtime.InteropServices;

namespace WinForge.Core;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int DoublePressWindowMs = 450;

    private Native.LowLevelKeyboardProc? _callback;
    private IntPtr _hook;
    private bool _controlDown;
    private long _lastControlUp = -DoublePressWindowMs;

    public event Action? DoubleControlPressed;
    public event Action<uint, bool>? KeyStateChanged;
    public Action<Exception>? ErrorHandler { get; set; }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;

        _callback = Handle;
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("Klavye kancasÄ± kurulamadÄ±.");
    }

    private IntPtr Handle(int nCode, IntPtr wParam, IntPtr lParam)
    {
        using var timing = PerformanceTrace.Measure("input.keyboard.callback");
        if (nCode >= 0)
        {
            var message = wParam.ToInt64();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (HotkeyService.IsCapturing)
            {
                // Keep releases observable so recording/drag state can finish safely.
                if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP) NotifyKeyStateChanged(info.vkCode, isDown: false);
                _controlDown = false;
                _lastControlUp = -DoublePressWindowMs;
                return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
            }
            if (message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN) NotifyKeyStateChanged(info.vkCode, isDown: true);
            else if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP) NotifyKeyStateChanged(info.vkCode, isDown: false);

            if (info.vkCode is VkControl or VkLeftControl or VkRightControl)
            {
                if (message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
                {
                    _controlDown = true;
                }
                else if ((message is Native.WM_KEYUP or Native.WM_SYSKEYUP) && _controlDown)
                {
                    _controlDown = false;
                    var now = Environment.TickCount64;

                    if (now - _lastControlUp <= DoublePressWindowMs)
                    {
                        _lastControlUp = -DoublePressWindowMs;
                        try
                        {
                            if (DoubleControlPressed is { } observers)
                                foreach (Action observer in observers.GetInvocationList())
                                    try { observer(); } catch (Exception error) { ReportError(error); }
                        }
                        catch
                        {
                            // A hook callback must never take the message loop down.
                        }
                    }
                    else
                    {
                        _lastControlUp = now;
                    }
                }
            }
            else if (message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                // BaÅŸka bir tuÅŸa basÄ±ldÄ±ysa Ã¶nceki Ctrl dokunuÅŸu sayÄ±lmaz.
                _lastControlUp = -DoublePressWindowMs;
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    internal void NotifyKeyStateChanged(uint virtualKey, bool isDown)
    {
        if (KeyStateChanged is not { } observers) return;
        foreach (Action<uint, bool> observer in observers.GetInvocationList())
            try { observer(virtualKey, isDown); } catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error) { try { ErrorHandler?.Invoke(error); } catch { } }

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


