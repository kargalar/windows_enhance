using WinForge.Core;
using System.IO;

internal static class InputObserverChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var hook = new GlobalKeyboardHook();
        var received = new List<(uint Key, bool Down)>();
        var errors = 0;
        hook.KeyStateChanged += (_, _) => throw new InvalidOperationException("Fixture failure");
        hook.KeyStateChanged += (key, down) => received.Add((key, down));
        hook.ErrorHandler = _ => { errors++; throw new IOException("Fixture reporting failure"); };
        hook.NotifyKeyStateChanged(0x41, true);
        hook.NotifyKeyStateChanged(0x41, false);
        check(received.SequenceEqual(new[] { (0x41u, true), (0x41u, false) }), "A failing keyboard subscriber cannot hide key-down or key-up from later subscribers");
        check(errors == 2 && !hook.IsInstalled, "Keyboard error reporting is isolated and observer checks never install a global hook");
    }
}
