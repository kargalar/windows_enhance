using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Waits for activation keys to be released while preserving the intended foreground target.</summary>
internal static class PasteOperation
{
    private const int MaximumReleaseAttempts = 100;
    private static readonly TimeSpan ReleasePollInterval = TimeSpan.FromMilliseconds(20);
    private static readonly int[] ActivationKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0x56];

    internal static Task<bool> ExecuteAsync(IntPtr target, Action paste, CancellationToken cancellationToken) =>
        ExecuteAsync(target, Native.GetForegroundWindow,
            () => ActivationKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0), paste, cancellationToken, Task.Delay);

    internal static async Task<bool> ExecuteAsync(IntPtr target, Func<IntPtr> foregroundWindow,
        Func<bool> modifiersPressed, Action paste, CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        if (target == IntPtr.Zero) return false;
        for (var attempt = 0; attempt < MaximumReleaseAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (foregroundWindow() != target) return false;
            if (!modifiersPressed())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (foregroundWindow() != target) return false;
                paste();
                return true;
            }
            await delay(ReleasePollInterval, cancellationToken);
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
