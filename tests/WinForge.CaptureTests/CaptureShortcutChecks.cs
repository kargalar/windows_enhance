using System.Windows.Threading;
using WinForge.Core;
using WinForge.Modules.Capture;

internal static class CaptureShortcutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var shortcut = CaptureShortcut.Parse("Alt+XButton1");
        check(shortcut == new CaptureShortcut(HotkeyModifiers.Alt, 0, "mousex1"), "Region capture accepts Alt+XButton1");
        check(shortcut == CaptureShortcut.Parse("Alt+MouseX1") && shortcut == CaptureShortcut.Parse("alt+MouseXButton1"), "Recorded and manual side-button aliases match");
        check(CaptureShortcut.Parse("Win+Alt+S") is { Key: 0x53, Modifiers: 9, Mouse: "" }, "Default keyboard capture shortcut remains supported");
        var calls = 0;
        using var binding = new CaptureShortcut.MouseBinding(shortcut, () => calls++, installHook: false);
        var x1 = new MSLLHOOKSTRUCT { mouseData = 1u << 16 };
        check(!binding.ProcessInput(0x20B, x1, 0) && !binding.ProcessInput(0x20B, x1, HotkeyModifiers.Alt | HotkeyModifiers.Shift), "Capture shortcut requires exactly the configured modifiers");
        check(!binding.ProcessInput(0x20B, new() { mouseData = 2u << 16 }, 1), "Other side button does not trigger capture");
        check(!binding.ProcessInput(0x20B, new() { mouseData = 3u << 16 }, 1), "Invalid side button is ignored");
        check(!binding.ProcessInput(0x20B, new() { mouseData = 1u << 16, flags = 1 }, 1), "Injected mouse input does not trigger capture");
        using (var hotkeys = new HotkeyService())
        using (hotkeys.Suspend())
            check(!binding.ProcessInput(0x20B, x1, 1), "Recording a shortcut suspends region capture");
        check(binding.ProcessInput(0x20B, x1, 1) && binding.ProcessInput(0x20B, x1, 1), "Matching mouse press is consumed without repeating capture");
        check(calls == 0, "Capture callback is deferred out of the mouse hook");
        Pump();
        check(calls == 1, "Alt+XButton1 dispatches region capture once");
        check(binding.ProcessInput(0x20C, x1, 0), "Matching mouse release is consumed even after Alt is released");
        check(!binding.ProcessInput(0x20C, x1, 0), "Unmatched mouse release passes through");
        binding.ProcessInput(0x20B, x1, 1);
        binding.Dispose(); binding.Dispose(); Pump();
        check(calls == 1 && !binding.ProcessInput(0x20B, x1, 1), "Disposing capture shortcut cancels pending callbacks and releases input");

        using var native = shortcut.Register(new UnusedHotkeys(), () => { });
        check(true, "Mouse capture shortcut installs a native hook without RegisterHotKey");
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class UnusedHotkeys : IHotkeyService
    {
        public IDisposable Register(uint modifiers, uint virtualKey, Action callback) => throw new InvalidOperationException("Mouse input cannot use RegisterHotKey.");
    }
}
