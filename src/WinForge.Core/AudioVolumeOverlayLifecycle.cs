namespace WinForge.Core;

/// <summary>Coordinates the volume indicator and its media companion on the UI thread.</summary>
public static class AudioVolumeOverlayLifecycle
{
    public static bool IsVisible { get; private set; }
    public static event Action? Shown;
    public static event Action? Hidden;
    public static event Action? Interaction;
    public static event Action? DismissRequested;
    public static event Func<bool>? KeepOpenRequested;

    public static bool ShouldKeepOpen => KeepOpenRequested?.GetInvocationList()
        .Cast<Func<bool>>().Any(keepOpen => keepOpen()) == true;

    public static void NotifyShown()
    {
        IsVisible = true;
        Shown?.Invoke();
    }

    public static void NotifyHidden()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Hidden?.Invoke();
    }

    public static void NotifyInteraction()
    {
        if (IsVisible) Interaction?.Invoke();
    }

    public static void RequestDismiss()
    {
        if (IsVisible) DismissRequested?.Invoke();
    }
}
