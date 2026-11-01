using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.Extras.KeyboardInputSender;

namespace WinForge.Modules.Extras;

internal sealed record ActionShortcut(uint Modifiers, int Key, string Mouse)
{
    internal static ActionShortcut Parse(string text)
    {
        var parts = text.Trim().Split('+', StringSplitOptions.TrimEntries);
        var mouse = parts[^1].ToLowerInvariant() switch { "xbutton1" or "mousexbutton1" => "mousex1", "xbutton2" or "mousexbutton2" => "mousex2", var name => name };
        if (mouse is not ("mouseleft" or "mouseright" or "mousemiddle" or "mousex1" or "mousex2" or "wheelup" or "wheeldown"))
        {
            var key = AutomationCommands.ParseShortcut(text);
            return new(key.Modifiers, key.Key, "");
        }
        var parsed = AutomationCommands.ParseShortcut(string.Join('+', parts[..^1].Append("Space")));
        if (parsed.Modifiers == 0 && mouse is not ("mousex1" or "mousex2")) throw new FormatException("Fare kısayoluna Ctrl, Alt, Shift veya Win ekleyin.");
        return new(parsed.Modifiers, 0, mouse);
    }
}

internal static class ActionInput
{
    internal static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    internal static uint Modifiers => (Down(0x11) ? 2u : 0) | (Down(0x12) ? 1u : 0) | (Down(0x10) ? 4u : 0) | (Down(0x5B) || Down(0x5C) ? 8u : 0);
    internal static INPUT Key(ushort key, uint flags = 0, ushort scan = 0) => new() { Type = 1, Union = new InputUnion { Keyboard = new() { VirtualKey = key, ScanCode = scan, Flags = flags } } };
    internal static void Send(INPUT[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length) return;
        var releases = inputs.Where(i => (i.Union.Keyboard.Flags & 2) != 0).ToArray();
        SendInput((uint)releases.Length, releases, Marshal.SizeOf<INPUT>());
        throw new InvalidOperationException("Girdi gönderilemedi. Hedef uygulama yönetici olarak çalışıyorsa WinForge'u da yönetici olarak çalıştırın.");
    }
    internal static void Chord(params ushort[] keys) => Send(keys.Select(k => Key(k, k is >= 0x21 and <= 0x28 ? 1u : 0)).Concat(keys.Reverse().Select(k => Key(k, k is >= 0x21 and <= 0x28 ? 3u : 2u))).ToArray());
    internal static INPUT[] DesktopChord(ushort arrow, IReadOnlyCollection<ushort> held)
    {
        // Keep the physical Windows key down so repeated clicks can switch desktops.
        ushort[] unwanted = held.Where(k => k is 0xA0 or 0xA1 or 0xA4 or 0xA5).ToArray();
        ushort[] required = [.. held.Any(k => k is 0xA2 or 0xA3) ? Array.Empty<ushort>() : new ushort[] { 0xA2 },
            .. held.Any(k => k is 0x5B or 0x5C) ? Array.Empty<ushort>() : new ushort[] { 0x5B }];
        return [.. unwanted.Select(k => Key(k, 2)), .. required.Select(k => Key(k)),
            Key(arrow, 1), Key(arrow, 3), .. required.Reverse().Select(k => Key(k, 2)),
            .. unwanted.Select(k => Key(k))];
    }
    internal static void SwitchDesktop(ushort arrow) => Send(DesktopChord(arrow,
        new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }.Where(k => Down(k)).ToArray()));
    internal static void Text(string text) => Send(text.SelectMany(c => new[] { Key(0, 4, c), Key(0, 6, c) }).ToArray());
}

/// <summary>One mouse hook for all action modules; matching presses and releases are consumed together.</summary>
internal static class ActionMouseBindings
{
    private static readonly Dictionary<ActionShortcut, Action> Bindings = new();
    private static readonly HashSet<int> Held = new();
    private static Native.LowLevelMouseProc? _callback;
    private static IntPtr _hook;
    internal static IDisposable Register(ActionShortcut shortcut, Action action)
    {
        if (Bindings.ContainsKey(shortcut)) throw new InvalidOperationException("Fare kısayolu başka bir modülde kullanılıyor.");
        if (_hook == IntPtr.Zero)
        {
            _callback = Handle;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) throw new InvalidOperationException("Fare kısayolları başlatılamadı.");
        }
        Bindings.Add(shortcut, action);
        return new Lease(shortcut);
    }
    private sealed class Lease(ActionShortcut shortcut) : IDisposable
    {
        public void Dispose() { Bindings.Remove(shortcut); Cleanup(); }
    }
    private static void Cleanup()
    {
        if (Bindings.Count != 0 || Held.Count != 0 || _hook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; _callback = null;
    }
    private static IntPtr Handle(int code, IntPtr message, IntPtr pointer)
    {
        if (code >= 0 && ProcessInput(message.ToInt32(), Marshal.PtrToStructure<MSLLHOOKSTRUCT>(pointer), ActionInput.Modifiers)) return new(1);
        return Native.CallNextHookEx(_hook, code, message, pointer);
    }
    internal static bool ProcessInput(int msg, MSLLHOOKSTRUCT info, uint modifiers)
    {
        if ((info.flags & 1) == 0)
        {
                int button = msg is 0x20B or 0x20C ? 3 + (int)(info.mouseData >> 16) : msg is 0x201 or 0x202 ? 1 : msg is 0x204 or 0x205 ? 2 : msg is 0x207 or 0x208 ? 3 : 0;
                if (msg is 0x202 or 0x205 or 0x208 or 0x20C && Held.Remove(button)) { Cleanup(); return true; }
                if (HotkeyService.IsCapturing) return false;
                var mouse = msg switch { 0x201 => "mouseleft", 0x204 => "mouseright", 0x207 => "mousemiddle", 0x20B => button == 4 ? "mousex1" : "mousex2", 0x20A => (short)(info.mouseData >> 16) > 0 ? "wheelup" : "wheeldown", _ => "" };
                if (mouse.Length > 0 && Bindings.TryGetValue(new(modifiers, 0, mouse), out var action))
                {
                    if (button != 0) Held.Add(button);
                    Dispatcher.CurrentDispatcher.BeginInvoke(action);
                    return true;
                }
        }
        return false;
    }
}
