using WinForge.Core;
using System.Windows.Threading;

internal static class HotkeyConflictChecks
{
    internal static int Run()
    {
        using var hotkeys = new HotkeyService();
        var media = new ModuleHotkeyService(hotkeys, "Medya Kısayolları");
        var windows = new ModuleHotkeyService(hotkeys, "Pencere Kısayolları");
        const uint modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;
        uint key = 0;
        IDisposable? binding = null;
        for (uint candidate = 0x82; candidate <= 0x87 && binding is null; candidate++)
        {
            try
            {
                binding = media.Register(modifiers, candidate, () => { }, "Oynat/duraklat");
                key = candidate;
            }
            catch (InvalidOperationException) { }
        }
        if (binding is null) throw new Exception("No free key for native hotkey conflict checks.");
        using (binding)
        {
            var conflict = Failure(() => windows.Register(modifiers, key, () => { }));
            Require(conflict.Contains("Medya Kısayolları → Oynat/duraklat") && conflict.Contains("Ctrl+Alt+Shift+F"),
                "Internal conflict includes owning module, rule and key.");
            Require(Failure(() => windows.Register(modifiers | HotkeyModifiers.NoRepeat, key, () => { })) == conflict,
                "NoRepeat does not hide the internal owner.");
            using (hotkeys.Suspend())
                Require(Failure(() => windows.Register(modifiers, key, () => { })) == conflict,
                    "Capture suspension retains ownership information.");
            using var otherApp = new HotkeyService();
            var triggered = 0;
            using var external = otherApp.Register(modifiers, key, () => triggered++);
            Require(otherApp.ProcessFallback(Native.WM_KEYDOWN, key, modifiers), "External conflict uses a consuming keyboard fallback.");
            Require(otherApp.ProcessFallback(Native.WM_KEYDOWN, key, modifiers), "Held fallback key repeats are consumed.");
            Drain();
            Require(triggered == 1, "Fallback executes once per physical press.");
            Require(otherApp.ProcessFallback(Native.WM_KEYUP, key, 0), "Release is consumed even after modifiers are released.");
            Require(!otherApp.ProcessFallback(Native.WM_KEYDOWN, key, 0), "Unrelated chords pass through.");
            using (otherApp.Suspend())
                Require(!otherApp.ProcessFallback(Native.WM_KEYDOWN, key, modifiers), "Recording suspends fallback capture.");
            Require(otherApp.ProcessFallback(Native.WM_SYSKEYDOWN, key, modifiers), "Resume restores fallback while another app still owns the key.");
            Drain();
            Require(triggered == 2, "Resumed fallback executes.");
            otherApp.ProcessFallback(Native.WM_SYSKEYUP, key, 0);
            Require(Failure(() => otherApp.Register(modifiers, key, () => { })).Contains("WinForge içinde"),
                "Fallback registrations still reject internal duplicates.");
            external.Dispose();
            Require(!otherApp.ProcessFallback(Native.WM_KEYDOWN, key, modifiers), "Disposed fallback stops intercepting.");
        }
        using var replacement = windows.Register(modifiers, key, () => { });
        Require(Failure(() => media.Register(modifiers, key, () => { })).Contains("Pencere Kısayolları"),
            "Disposing a registration clears its previous owner.");
        using var desktop = hotkeys.Register(HotkeyModifiers.Alt | HotkeyModifiers.Win, 0x44, () => { });
        return 15;
    }

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static string Failure(Func<IDisposable> register)
    {
        try { using var unexpected = register(); }
        catch (InvalidOperationException error) { return error.Message; }
        throw new Exception("Expected a hotkey conflict.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
