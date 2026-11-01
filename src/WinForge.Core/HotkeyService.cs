using System.Windows.Interop;

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
    private readonly HashSet<int> _registered = new();
    private int _suspensions;
    private bool _disposed;
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

    public IDisposable Register(uint modifiers, uint virtualKey, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_shortcuts.Values.Contains((modifiers, virtualKey)))
            throw new InvalidOperationException("Kısayol başka bir modülde kullanılıyor.");
        var id = ++_nextId;
        if (_suspensions == 0 && !Native.RegisterHotKey(_source.Handle, id, modifiers | HotkeyModifiers.NoRepeat, virtualKey))
            throw new InvalidOperationException("Kısayol başka bir uygulama tarafından kullanılıyor.");

        _shortcuts[id] = (modifiers, virtualKey);
        if (_suspensions == 0) _registered.Add(id);
        _actions[id] = callback;
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
            if (Native.RegisterHotKey(_source.Handle, id, shortcut.Modifiers | HotkeyModifiers.NoRepeat, shortcut.Key))
                _registered.Add(id);
            else failures.Add($"{shortcut.Modifiers:X}+{shortcut.Key:X}");
        }
        if (failures.Count > 0) throw new InvalidOperationException("Kısayollar geri açılamadı: " + string.Join(", ", failures));
    }

    private void Unregister(int id)
    {
        _actions.Remove(id);
        _shortcuts.Remove(id);
        if (_registered.Remove(id)) Native.UnregisterHotKey(_source.Handle, id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Add(ref _captureScopes, -_suspensions);
        _suspensions = 0;
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
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
