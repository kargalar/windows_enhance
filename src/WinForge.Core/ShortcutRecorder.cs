using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WinForge.Core;

/// <summary>Records physical input before Explorer or Windows can execute it.</summary>
public sealed class ShortcutRecorder : IDisposable
{
    private readonly Native.LowLevelKeyboardProc _keyboardCallback;
    private readonly Native.LowLevelMouseProc _mouseCallback;
    private readonly Action<string> _recorded;
    private readonly HashSet<uint> _down = [];
    private IntPtr _keyboard, _mouse;
    private bool _completed;
    public ShortcutRecorder(Action<string> recorded) : this(recorded, true) { }
    internal ShortcutRecorder(Action<string> recorded, bool installHooks)
    {
        _recorded = recorded;
        if (installHooks) foreach (var key in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
            if ((Native.GetAsyncKeyState((int)key) & 0x8000) != 0) _down.Add(key);
        _keyboardCallback = Keyboard;
        _mouseCallback = Mouse;
        if (!installHooks) return;
        _keyboard = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _keyboardCallback, Native.GetModuleHandle(null), 0);
        _mouse = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseCallback, Native.GetModuleHandle(null), 0);
        if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("Kısayol yakalama başlatılamadı."); }
    }
    private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return Native.CallNextHookEx(_keyboard, code, message, data);
        var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data).vkCode;
        var down = message.ToInt64() is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        if (down) _down.Add(key); else _down.Remove(key);
        if (down && key is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C))
            Complete(key == 0x1B ? "" : KeyInterop.KeyFromVirtualKey((int)key).ToString());
        return new IntPtr(1);
    }
    private IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return Native.CallNextHookEx(_mouse, code, message, data);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
        var key = message.ToInt32() switch
        {
            0x20B => ((info.mouseData >> 16) & 0xffff) == 1 ? "MouseX1" : "MouseX2",
            0x20A => (short)(info.mouseData >> 16) > 0 ? "WheelUp" : "WheelDown", _ => null
        };
        var consume = key is not null && !_completed;
        if (key is not null) Complete(key);
        // Keep movement and cancellation controls usable after the first chord.
        return consume ? new IntPtr(1) :
            message.ToInt32() is 0x20B or 0x20C or 0x20A ? new IntPtr(1) : Native.CallNextHookEx(_mouse, code, message, data);
    }
    private void Complete(string key)
    {
        if (_completed) return;
        _completed = true;
        if (key.Length == 0) { _recorded(""); return; }
        var parts = new List<string>();
        if (_down.Overlaps([0x11u, 0xA2u, 0xA3u])) parts.Add("Ctrl");
        if (_down.Overlaps([0x12u, 0xA4u, 0xA5u])) parts.Add("Alt");
        if (_down.Overlaps([0x10u, 0xA0u, 0xA1u])) parts.Add("Shift");
        if (_down.Overlaps([0x5Bu, 0x5Cu])) parts.Add("Win");
        parts.Add(key); _recorded(string.Join("+", parts));
    }
    public void Dispose()
    {
        if (_keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboard);
        if (_mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouse);
        _keyboard = _mouse = IntPtr.Zero;
    }
}
