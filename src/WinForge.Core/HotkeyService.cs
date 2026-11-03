using System.Collections.Concurrent;
using System.Windows.Interop;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace WinForge.Core;

public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HwndMessage = -3;
    private static int _captureScopes;
    public static bool IsCapturing => Volatile.Read(ref _captureScopes) > 0;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly Dictionary<int, (uint Modifiers, uint Key)> _shortcuts = new();
    private readonly Dictionary<int, string> _owners = new();
    private readonly HashSet<int> _registered = new();
    private readonly HashSet<int> _fallback = new();
    private readonly ConcurrentDictionary<uint, byte> _heldFallbackKeys = new();
    private volatile FallbackBinding[] _fallbackBindings = [];
    private sealed record FallbackBinding(int Id, uint Modifiers, uint Key);
    private Native.LowLevelKeyboardProc? _keyboardCallback;
    private KeyboardHookThread? _keyboardHook;
    private int _suspensions;
    private volatile bool _disposed;
    private int _nextId = 0x0F00;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("WinForgeHotkeys")
        {
            ParentWindow = new IntPtr(HwndMessage),
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || _suspensions != 0) return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (_actions.TryGetValue(id, out var callback))
        {
            handled = true;
            try
            {
                callback();
            }
            catch
            {
                // A hotkey callback must not take the message loop down.
            }
        }

        return IntPtr.Zero;
    }

    public IDisposable Register(uint modifiers, uint virtualKey, Action callback) =>
        Register(modifiers, virtualKey, callback, "WinForge");

    public IDisposable Register(uint modifiers, uint virtualKey, Action callback, string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        modifiers &= ~HotkeyModifiers.NoRepeat;
        foreach (var (existingId, shortcut) in _shortcuts)
            if (shortcut == (modifiers, virtualKey))
                throw new InvalidOperationException($"{FormatShortcut(modifiers, virtualKey)} kısayolu WinForge içinde kullanılıyor: {_owners[existingId]}.");
        var id = ++_nextId;
        if (_suspensions == 0) RegisterNativeOrFallback(id, modifiers, virtualKey);

        _shortcuts[id] = (modifiers, virtualKey);
        _owners[id] = owner;
        _actions[id] = callback;
        PublishFallbackBindings();
        return new Registration(this, id);
    }

    /// <summary>Release native keys while a shortcut field is being edited.</summary>
    public IDisposable Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _captureScopes);
        if (_suspensions++ == 0)
        {
            foreach (var id in _registered) Native.UnregisterHotKey(_source.Handle, id);
            _registered.Clear();
            _fallback.Clear();
            PublishFallbackBindings();
            _heldFallbackKeys.Clear();
        }
        return new Suspension(this);
    }

    private void Resume()
    {
        if (_disposed) return;
        Interlocked.Decrement(ref _captureScopes);
        if (--_suspensions != 0) return;
        var failures = new List<string>();
        foreach (var (id, shortcut) in _shortcuts)
        {
            try { RegisterNativeOrFallback(id, shortcut.Modifiers, shortcut.Key); }
            catch (InvalidOperationException error) { failures.Add($"{_owners[id]}: {error.Message}"); }
        }
        PublishFallbackBindings();
        if (failures.Count > 0) throw new InvalidOperationException("Kısayollar geri açılamadı: " + string.Join(", ", failures));
    }

    private void Unregister(int id)
    {
        _actions.Remove(id);
        _shortcuts.Remove(id);
        _owners.Remove(id);
        _fallback.Remove(id);
        PublishFallbackBindings();
        if (_registered.Remove(id)) Native.UnregisterHotKey(_source.Handle, id);
    }

    private static InvalidOperationException RegistrationError(uint modifiers, uint key, int error)
    {
        var shortcut = FormatShortcut(modifiers, key);
        return new InvalidOperationException($"{shortcut} kısayolu kaydedilemedi: {new Win32Exception(error).Message} (Windows hata kodu: {error}).");
    }

    private void RegisterNativeOrFallback(int id, uint modifiers, uint key)
    {
        if (Native.RegisterHotKey(_source.Handle, id, modifiers | HotkeyModifiers.NoRepeat, key))
        {
            _registered.Add(id);
            return;
        }
        var error = Marshal.GetLastWin32Error();
        if (error != 1409) throw RegistrationError(modifiers, key, error);
        if (_keyboardHook is null)
        {
            _keyboardCallback = HandleFallback;
            _keyboardHook = new KeyboardHookThread(_keyboardCallback);
        }
        _fallback.Add(id);
    }

    private IntPtr HandleFallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var input = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
            // Ignore generated input, including keys sent by our own shortcut actions.
            if ((input.flags & 0x10) == 0)
            {
                uint modifiers = 0;
                if (Down(0x11)) modifiers |= HotkeyModifiers.Control;
                if (Down(0x12)) modifiers |= HotkeyModifiers.Alt;
                if (Down(0x10)) modifiers |= HotkeyModifiers.Shift;
                if (Down(0x5B) || Down(0x5C)) modifiers |= HotkeyModifiers.Win;
                if (ProcessFallback(message.ToInt32(), input.vkCode, modifiers)) return new IntPtr(1);
            }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void PublishFallbackBindings() =>
        _fallbackBindings = _fallback.Select(id => new FallbackBinding(id, _shortcuts[id].Modifiers, _shortcuts[id].Key)).ToArray();

    private static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

    internal bool ProcessFallback(int message, uint key, uint modifiers)
    {
        if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP)
            return _heldFallbackKeys.TryRemove(key, out _);
        if (message is not (Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)) return false;
        if (_heldFallbackKeys.ContainsKey(key)) return true;
        if (_disposed || Volatile.Read(ref _suspensions) != 0 || IsCapturing) return false;
        foreach (var binding in _fallbackBindings)
        {
            if ((binding.Modifiers, binding.Key) != (modifiers, key)) continue;
            var id = binding.Id;
            _heldFallbackKeys.TryAdd(key, 0);
            // Keep the native hook fast and do not let an action dispose it mid-callback.
            _source.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || _suspensions != 0 || IsCapturing || !_fallback.Contains(id)) return;
                if (_actions.TryGetValue(id, out var callback))
                    try { callback(); } catch { /* Match WM_HOTKEY callback isolation. */ }
            }));
            return true;
        }
        return false;
    }

    private static string FormatShortcut(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        var name = KeyInterop.KeyFromVirtualKey((int)key).ToString();
        parts.Add(name == "None" ? $"0x{key:X2}" : name);
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Add(ref _captureScopes, -_suspensions);
        _suspensions = 0;
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _keyboardCallback = null;
        _heldFallbackKeys.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private sealed class Suspension(HotkeyService owner) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            owner.Resume();
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly HotkeyService _owner;
        private readonly int _id;

        public Registration(HotkeyService owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose() => _owner.Unregister(_id);
    }
}
