using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal static class ForegroundApplication
{
    internal static string Normalize(string value) => value.Trim().Trim('"');
    internal static void Validate(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length > 1024 || normalized.IndexOfAny(['\r', '\n', '*', '?']) >= 0)
            throw new FormatException("Geçerli bir işlem adı veya exe yolu girin.");
    }
    internal static bool Matches(string configured, string name, string? path = null)
    {
        configured = Normalize(configured);
        if (configured.Length == 0) return true;
        if (configured.Contains('\\') || configured.Contains('/'))
            return string.Equals(Environment.ExpandEnvironmentVariables(configured), path, StringComparison.OrdinalIgnoreCase);
        return string.Equals(System.IO.Path.GetFileNameWithoutExtension(configured), System.IO.Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase);
    }
    internal static bool IsFocused(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return true;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var needsPath = configured.Contains('\\') || configured.Contains('/');
            return Matches(configured, process.ProcessName, needsPath ? process.MainModule?.FileName : null);
        }
        catch { return false; }
    }
}

// Unlike RegisterHotKey, this leaves the original keystroke untouched outside the target app.
internal sealed class ForegroundShortcutBinding : IDisposable
{
    private readonly AutomationShortcut _shortcut;
    private readonly string _process;
    private readonly Action _action;
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr _hook;
    private bool _held;
    internal ForegroundShortcutBinding(AutomationShortcut shortcut, string process, Action action)
    {
        _shortcut = shortcut; _process = process; _action = action; _callback = Handle;
        _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Uygulamaya özel kısayol başlatılamadı.");
    }
    private IntPtr Handle(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(pointer);
            if (info.vkCode == _shortcut.Key && ProcessInput(message.ToInt32(), info, Modifiers(), ForegroundApplication.IsFocused(_process))) return new(1);
        }
        return Native.CallNextHookEx(_hook, code, message, pointer);
    }
    internal bool ProcessInput(int message, KBDLLHOOKSTRUCT info, uint modifiers, bool focused)
    {
        if (_hook == IntPtr.Zero || (info.flags & 0x10) != 0 || info.vkCode != _shortcut.Key) return false;
        if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP && _held) { _held = false; return true; }
        if (message is not (Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)) return false;
        if (_held) return true;
        if (HotkeyService.IsCapturing || modifiers != _shortcut.Modifiers || !focused) return false;
        _held = true;
        _dispatcher.BeginInvoke(new Action(() => { if (_hook != IntPtr.Zero && ForegroundApplication.IsFocused(_process)) _action(); }));
        return true;
    }
    private static uint Modifiers() => (Down(0x11) ? 2u : 0) | (Down(0x12) ? 1u : 0) | (Down(0x10) ? 4u : 0) | (Down(0x5B) || Down(0x5C) ? 8u : 0);
    private static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose() { if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
}
