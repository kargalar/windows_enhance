namespace WinForge.Core;

/// <summary>Coordinates capture/selection overlays and internal clipboard writes with mouse-selection listeners.</summary>
public static class SelectionTranslationGuard
{
    private static int _overlayDepth;
    private static long _clipboardWriteVersion;

    public static bool IsOverlayActive => Volatile.Read(ref _overlayDepth) > 0;
    public static long ClipboardWriteVersion => Volatile.Read(ref _clipboardWriteVersion);

    public static event Action? OverlayEntered;
    public static event Action? InternalClipboardWritten;

    public static IDisposable EnterOverlay()
    {
        if (Interlocked.Increment(ref _overlayDepth) == 1)
            OverlayEntered?.Invoke();
        return new OverlayScope();
    }

    public static void NotifyInternalClipboardWrite()
    {
        Interlocked.Increment(ref _clipboardWriteVersion);
        InternalClipboardWritten?.Invoke();
    }

    private sealed class OverlayScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _overlayDepth);
        }
    }
}
