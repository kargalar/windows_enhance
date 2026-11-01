using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Capture;

internal sealed record CaptureShortcut(uint Modifiers, uint Key, string Mouse)
{
    internal static CaptureShortcut Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mouse = parts.LastOrDefault()?.ToLowerInvariant() switch
        {
            "xbutton1" or "mousexbutton1" => "mousex1",
            "xbutton2" or "mousexbutton2" => "mousex2",
            var value => value ?? ""
        };
        if (mouse is not ("mouseleft" or "mouseright" or "mousemiddle" or "mousex1" or "mousex2" or "wheelup" or "wheeldown"))
        {
            var keyboard = ModuleShortcut.Parse(text);
            return new(keyboard.Modifiers, keyboard.Key, "");
        }
        var (modifiers, _) = ModuleShortcut.Parse(string.Join('+', parts[..^1].Append("Space")));
        return new(modifiers, 0, mouse);
    }

    internal IDisposable Register(IHotkeyService hotkeys, Action callback) => Mouse.Length == 0
        ? hotkeys.Register(Modifiers, Key, callback)
        : new MouseBinding(this, callback);

    internal sealed class MouseBinding : IDisposable
    {
        private readonly CaptureShortcut shortcut;
        private readonly Action callback;
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        private readonly Native.LowLevelMouseProc hookCallback;
        private IntPtr hook;
        private int heldButton;
        private bool disposed;

        internal MouseBinding(CaptureShortcut shortcut, Action callback, bool installHook = true)
        {
            this.shortcut = shortcut;
            this.callback = callback;
            hookCallback = Handle;
            if (!installHook) return;
            hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, hookCallback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new InvalidOperationException("Yakalama için fare kısayolu başlatılamadı.");
        }

        private IntPtr Handle(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
                var modifiers = (Down(0x12) ? HotkeyModifiers.Alt : 0) |
                    (Down(0x11) ? HotkeyModifiers.Control : 0) |
                    (Down(0x10) ? HotkeyModifiers.Shift : 0) |
                    (Down(0x5B) || Down(0x5C) ? HotkeyModifiers.Win : 0);
                if (ProcessInput(message.ToInt32(), Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data), modifiers)) return new(1);
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        internal bool ProcessInput(int message, MSLLHOOKSTRUCT input, uint modifiers)
        {
            if (disposed || (input.flags & 1) != 0) return false;
            var xButton = (int)((input.mouseData >> 16) & 0xffff);
            var button = message switch
            {
                0x201 or 0x202 => 1, 0x204 or 0x205 => 2, 0x207 or 0x208 => 3,
                0x20B or 0x20C when xButton is 1 or 2 => 3 + xButton, _ => 0
            };
            if (message is 0x202 or 0x205 or 0x208 or 0x20C)
            {
                if (button == 0 || heldButton != button) return false;
                heldButton = 0;
                return true;
            }
            if (HotkeyService.IsCapturing || modifiers != shortcut.Modifiers) return false;
            var mouse = message switch
            {
                0x201 => "mouseleft", 0x204 => "mouseright", 0x207 => "mousemiddle",
                0x20B when xButton == 1 => "mousex1", 0x20B when xButton == 2 => "mousex2",
                0x20A when (short)(input.mouseData >> 16) > 0 => "wheelup",
                0x20A when (short)(input.mouseData >> 16) < 0 => "wheeldown", _ => ""
            };
            if (mouse != shortcut.Mouse) return false;
            if (button != 0 && heldButton == button) return true;
            heldButton = button;
            dispatcher.BeginInvoke(new Action(() => { if (!disposed && !HotkeyService.IsCapturing) callback(); }));
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            heldButton = 0;
        }
    }
}
