using WinForge.Core;

namespace WinForge.Modules.WindowControl;

/// <summary>Keeps endpoint I/O replaceable so verification never changes the user's master audio.</summary>
internal sealed class MasterAudioEndpoint(Func<bool> readMuted, Action<bool> setMuted, Action<int>? stepVolume = null)
{
    internal static MasterAudioEndpoint Windows { get; } = new(VolumeService.IsMuted, VolumeService.SetMuted);
    internal void ToggleMute() => setMuted(!readMuted());
    internal void StepVolume(int direction, bool fine) => (stepVolume ?? ChangeVolume)(direction * (fine ? 10 : 2));
    private static void ChangeVolume(int amount) => VolumeService.Step(amount);
}
