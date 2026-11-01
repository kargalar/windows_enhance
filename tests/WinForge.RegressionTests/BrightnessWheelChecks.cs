using System.Reflection;
using System.IO;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class BrightnessWheelChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var extras = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "modules", "WinForge.Modules.Extras.dll"));
        var handler = extras.GetType("WinForge.Modules.Extras.DisplayBrightnessModule")!
            .GetMethod("HandleBrightnessWheel", BindingFlags.Static | BindingFlags.NonPublic)!;
        var modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        var activeModifiers = modifiers;
        var changes = new List<int>();
        object dim = (modifiers, 0u, -1);
        object brighten = (modifiers, 0u, 1);
        using var hook = new GlobalMouseHook
        {
            EventFilter = input => (bool)handler.Invoke(null,
                [input, activeModifiers, dim, brighten, (Action<int>)changes.Add])!
        };
        var wheel = new GlobalMouseHook.Input(Native.WM_MOUSEWHEEL, new(10, 20), 120, 0);
        check(hook.ProcessInput(wheel) && hook.ProcessInput(wheel with { WheelDelta = -120 }) &&
            changes.SequenceEqual(new[] { 1, -1 }),
            "Brightness wheel shortcuts adjust once and consume both directions before browser zoom");
        check(hook.ProcessInput(wheel with { WheelDelta = 30 }) && changes.Count == 3,
            "High-resolution brightness wheel events are consumed");
        foreach (var otherModifiers in new[] { 0u, HotkeyModifiers.Control, modifiers | HotkeyModifiers.Shift })
        {
            activeModifiers = otherModifiers;
            check(!hook.ProcessInput(wheel) && changes.Count == 3,
                "Unmatched modifiers preserve normal scrolling and Ctrl-wheel zoom");
        }
        activeModifiers = modifiers;
        check(!hook.ProcessInput(wheel with { WheelDelta = 0 }) &&
            !hook.ProcessInput(wheel with { Message = Native.WM_LBUTTONDOWN }) && changes.Count == 3,
            "Brightness shortcuts leave zero-delta and other mouse input alone");
        brighten = (modifiers, 0x7Bu, 0);
        check(!hook.ProcessInput(wheel) && changes.Count == 3,
            "A direction assigned to a keyboard shortcut does not consume the wheel");
        using var hotkeys = new HotkeyService();
        using (hotkeys.Suspend())
        {
            check(!hook.ProcessInput(wheel with { WheelDelta = -120 }) && changes.Count == 3,
                "Shortcut recording bypasses brightness actions and consumption");
        }
    }
}
