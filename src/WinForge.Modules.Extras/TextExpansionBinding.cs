using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

// Keeps only the current token, never a history of typed text.
internal sealed class TextExpansionBuffer
{
    private string _token = "";
    private bool _overflow;
    internal void Reset() { _token = ""; _overflow = false; }
    internal string? Push(char character)
    {
        if (character == ' ')
        {
            var token = _overflow ? null : _token;
            Reset();
            return token;
        }
        if (char.IsWhiteSpace(character) || char.IsControl(character)) { Reset(); return null; }
        if (_token.Length == 64) _overflow = true;
        if (!_overflow) _token += character;
        return null;
    }
}

internal sealed class TextExpansionBinding : IDisposable
{
    private readonly Dictionary<string, ShortcutActionRule> _rules;
    private readonly TextExpansionBuffer _buffer = new();
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly GlobalMouseHook _mouse = new();
    private readonly Action<string> _notify;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr _hook, _target, _focus;
    private bool _sending, _spaceHeld;

    internal static void Validate(string abbreviation)
    {
        if (string.IsNullOrEmpty(abbreviation) || abbreviation.Length > 64 || abbreviation.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || char.IsSurrogate(c)))
            throw new FormatException("Kısaltma 1–64 karakter olmalı; boşluk içermemelidir.");
    }

    internal TextExpansionBinding(IEnumerable<ShortcutActionRule> rules, Action<string> notify)
    {
        _rules = rules.ToDictionary(r => r.Shortcut, StringComparer.Ordinal);
        _notify = notify;
        _callback = Handle;
        _mouse.EventFilter = _ => { _buffer.Reset(); return false; };
        try
        {
            _mouse.Install();
            _hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new InvalidOperationException("Metin genişletme başlatılamadı.");
        }
        catch { Dispose(); throw; }
    }

    private IntPtr Handle(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0)
        {
            var input = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(pointer);
            if ((input.flags & 0x10) != 0)
            {
                if (!_sending) _buffer.Reset();
            }
            else if (input.vkCode == 0x20 && _spaceHeld)
            {
                if (message.ToInt32() is Native.WM_KEYUP or Native.WM_SYSKEYUP) _spaceHeld = false;
                return new(1);
            }
            else if (message.ToInt32() is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                try { if (ProcessKey(input)) return new(1); }
                catch (Exception ex)
                {
                    _buffer.Reset();
                    _dispatcher.BeginInvoke(new Action(() => _notify(ex.Message)));
                }
            }
        }
        return Native.CallNextHookEx(_hook, code, message, pointer);
    }

    private bool ProcessKey(KBDLLHOOKSTRUCT input)
    {
        var target = Native.GetForegroundWindow();
        var thread = Native.GetWindowThreadProcessId(target, out _);
        var gui = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref gui)) { _buffer.Reset(); return false; }
        if (target != _target || gui.Focus != _focus) _buffer.Reset();
        _target = target; _focus = gui.Focus;
        var modifiers = ActionInput.Modifiers;
        if (HotkeyService.IsCapturing || (modifiers & 11) != 0) { _buffer.Reset(); return false; }
        if (input.vkCode is 0x10 or 0xA0 or 0xA1 or 0x14) return false;
        // Editing/navigation invalidate the token; never guess what remains before the caret.
        if (input.vkCode is < 0x20 or >= 0x21 and <= 0x2F) { _buffer.Reset(); return false; }
        var state = new byte[256];
        if ((modifiers & 4) != 0) state[0x10] = 0x80;
        state[0x14] = (byte)(GetKeyState(0x14) & 1);
        var text = new StringBuilder(8);
        var count = ToUnicodeEx(input.vkCode, input.scanCode, state, text, text.Capacity, 4, GetKeyboardLayout(thread));
        if (count != 1) { _buffer.Reset(); return false; }
        var token = _buffer.Push(text[0]);
        if (token is null || !_rules.TryGetValue(token, out var rule)) return false;
        var replacement = ShortcutSecret.Unprotect(rule.Value) + " ";
        if (target != Native.GetForegroundWindow()) return false;
        var events = Enumerable.Range(0, token.Length).SelectMany(_ => new[] { ActionInput.Key(8), ActionInput.Key(8, 2) })
            .Concat(replacement.SelectMany(c => new[] { ActionInput.Key(0, 4, c), ActionInput.Key(0, 6, c) })).ToArray();
        _sending = true;
        try { ActionInput.Send(events); _spaceHeld = true; }
        finally { _sending = false; }
        return true;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _mouse.Dispose(); _buffer.Reset();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder text, int capacity, uint flags, IntPtr layout);
}
