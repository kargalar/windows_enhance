using System.Windows.Interop;

namespace WinForge.Core;

public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HwndMessage = -3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
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
        if (msg != WmHotkey) return IntPtr.Zero;

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
        var id = ++_nextId;
        if (!Native.RegisterHotKey(_source.Handle, id, modifiers | HotkeyModifiers.NoRepeat, virtualKey))
            throw new InvalidOperationException("Kısayol başka bir uygulama tarafından kullanılıyor.");

        _actions[id] = callback;
        return new Registration(this, id);
    }

    private void Unregister(int id)
    {
        if (_actions.Remove(id)) Native.UnregisterHotKey(_source.Handle, id);
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
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
