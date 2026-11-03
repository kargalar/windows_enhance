using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinForge.Core;

public sealed class GlobalKeyboardHook : IDisposable
{
    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int DoublePressWindowMs = 450;

    private KeyboardHookThread? _hook;
    private int _generation;
    private bool _controlDown;
    private long _lastControlUp = -DoublePressWindowMs;

    public event Action? DoubleControlPressed;
    public event Action<uint, bool>? KeyStateChanged;
    public Action<Exception>? ErrorHandler { get; set; }

    public bool IsInstalled => _hook is not null;

    public void Install()
    {
        if (_hook is not null) return;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var generation = ++_generation;
        _hook = new KeyboardHookThread((code, message, pointer) =>
        {
            if (code >= 0)
            {
                // The native pointer is valid only for the duration of this callback.
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(pointer);
                var timestamp = Environment.TickCount64;
                var capturing = HotkeyService.IsCapturing;
                dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (generation == _generation && IsInstalled)
                        ProcessKey(message.ToInt64(), info, timestamp, capturing);
                }));
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, pointer);
        });
    }

    private void ProcessKey(long message, KBDLLHOOKSTRUCT info, long timestamp, bool capturing)
    {
        if (capturing)
        {
            // Keep releases observable so recording/drag state can finish safely.
            if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP) NotifyKeyStateChanged(info.vkCode, isDown: false);
            _controlDown = false;
            _lastControlUp = -DoublePressWindowMs;
            return;
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
                var now = timestamp;

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

    internal void NotifyKeyStateChanged(uint virtualKey, bool isDown)
    {
        if (KeyStateChanged is not { } observers) return;
        foreach (Action<uint, bool> observer in observers.GetInvocationList())
            try { observer(virtualKey, isDown); } catch (Exception error) { ReportError(error); }
    }

    private void ReportError(Exception error) { try { ErrorHandler?.Invoke(error); } catch { } }

    public void Dispose()
    {
        ++_generation;
        _hook?.Dispose();
        _hook = null;
        _controlDown = false;
        _lastControlUp = -DoublePressWindowMs;
    }
}
