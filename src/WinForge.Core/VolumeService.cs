using System.Runtime.InteropServices;

namespace WinForge.Core;

public static class VolumeService
{
    private static readonly Guid IidAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid WinForgeVolumeChangeContext = new("67C789EC-48D1-4B1C-A056-B84F832D7A1B");

    public sealed record EndpointVolumeChange(int VolumePercent, bool IsMuted, Guid EventContext);

    public static bool IsWinForgeChange(EndpointVolumeChange change) =>
        change.EventContext == WinForgeVolumeChangeContext;

    /// <summary>Observes software-visible volume and mute changes on the current default render endpoint.</summary>
    public static IDisposable WatchDefaultEndpoint(Action<EndpointVolumeChange> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        var endpoint = GetVolume();
        var watcher = new EndpointVolumeWatcher(endpoint, onChanged);
        try { watcher.Register(); return watcher; }
        catch { watcher.Dispose(); throw; }
    }

    public static int Current()
    {
        using var endpoint = GetVolume();
        var volume = endpoint.Volume;
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var scalar));
        return (int)Math.Round(scalar * 100);
    }

    public static int Step(int percent) => StepCore(percent, Guid.Empty);

    public static int StepFromFlyout(int percent) => StepCore(percent, WinForgeVolumeChangeContext);

    public static int SetFromFlyout(int percent)
    {
        using var endpoint = GetVolume();
        var volume = endpoint.Volume;
        var next = Math.Clamp(percent, 0, 100) / 100f;
        var context = WinForgeVolumeChangeContext;
        Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(next, ref context));
        if (next > 0f) Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var actual));
        return (int)Math.Round(actual * 100);
    }

    private static int StepCore(int percent, Guid eventContext)
    {
        using var endpoint = GetVolume();
        var volume = endpoint.Volume;
        Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var current));

        var next = Math.Clamp(current + percent / 100f, 0f, 1f);
        var context = eventContext;
        Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(next, ref context));
        if (next > 0f) Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));

        return (int)Math.Round(next * 100);
    }

    public static bool IsMuted()
    {
        using var endpoint = GetVolume();
        var volume = endpoint.Volume;
        Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
        return muted;
    }

    public static void SetMuted(bool muted) => SetMutedCore(muted, Guid.Empty);

    public static void SetMutedFromFlyout(bool muted) => SetMutedCore(muted, WinForgeVolumeChangeContext);

    private static void SetMutedCore(bool muted, Guid eventContext)
    {
        using var endpoint = GetVolume();
        var volume = endpoint.Volume;
        var context = eventContext;
        Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context));
    }

    private static AudioEndpointLease GetVolume()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? instance = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)CoreAudioDeviceFactory.CreateEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out device));
            var iid = IidAudioEndpointVolume;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out instance));
            if (instance is not IAudioEndpointVolume volume) throw new InvalidOperationException("Ses denetimi açılamadı.");
            var endpoint = new AudioEndpointLease(volume);
            instance = null; // The lease owns exactly this activation's RCW reference.
            return endpoint;
        }
        finally
        {
            ReleaseOwnedReference(instance);
            ReleaseOwnedReference(device);
            ReleaseOwnedReference(enumerator);
        }
    }

    private static void ReleaseOwnedReference(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
    }

    private sealed class AudioEndpointLease(IAudioEndpointVolume volume) : IDisposable
    {
        private IAudioEndpointVolume? _volume = volume;
        internal IAudioEndpointVolume Volume => _volume ?? throw new ObjectDisposedException(nameof(AudioEndpointLease));
        public void Dispose() => ReleaseOwnedReference(Interlocked.Exchange(ref _volume, null));
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComVisible(true)]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr notificationData);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioVolumeNotificationData
    {
        public Guid EventContext;
        public int Muted;
        public float MasterVolume;
        public uint ChannelCount;
    }

    private sealed class EndpointVolumeWatcher(AudioEndpointLease endpoint, Action<EndpointVolumeChange> onChanged) :
        IAudioEndpointVolumeCallback, IDisposable
    {
        private bool _registered;
        private int _disposed;

        public void Register()
        {
            var result = endpoint.Volume.RegisterControlChangeNotify(this);
            if (result != 0) Marshal.ThrowExceptionForHR(result);
            _registered = true;
        }

        public int OnNotify(IntPtr notificationData)
        {
            if (notificationData == IntPtr.Zero || Volatile.Read(ref _disposed) != 0) return 0;
            try
            {
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notificationData);
                onChanged(new EndpointVolumeChange(
                    (int)Math.Round(Math.Clamp(data.MasterVolume, 0f, 1f) * 100),
                    data.Muted != 0,
                    data.EventContext));
            }
            catch
            {
                // Never allow exceptions to escape through the native audio callback.
            }
            return 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (!_registered) return;
                _registered = false;
                var result = endpoint.Volume.UnregisterControlChangeNotify(this);
                if (result != 0) Marshal.ThrowExceptionForHR(result);
            }
            finally { endpoint.Dispose(); }
        }
    }
}
