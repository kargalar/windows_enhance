using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Capture;

/// <summary>Mutes active render endpoints for a recording and restores only endpoints this scope changed.</summary>
internal sealed class PlaybackMuteScope : IDisposable
{
    private List<IAudioEndpointVolume>? changedEndpoints = [];

    private PlaybackMuteScope() { }

    public static PlaybackMuteScope Begin()
    {
        var scope = new PlaybackMuteScope();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.EnumAudioEndpoints(0, 1, out collection) < 0) return scope;
            if (collection.GetCount(out var count) < 0) return scope;
            for (uint index = 0; index < count; index++) scope.TryMute(collection, index);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException or UnauthorizedAccessException) { }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
        return scope;
    }

    private void TryMute(IMMDeviceCollection collection, uint index)
    {
        IMMDevice? device = null;
        object? instance = null;
        IAudioEndpointVolume? volume = null;
        var retain = false;
        try
        {
            if (collection.Item(index, out device) < 0) return;
            var id = IidAudioEndpointVolume;
            if (device.Activate(ref id, 1, IntPtr.Zero, out instance) < 0 || instance is not IAudioEndpointVolume endpoint) return;
            volume = endpoint;
            if (volume.GetMute(out var wasMuted) < 0 || wasMuted) return;
            var context = VolumeService.DictationPlaybackMuteContext;
            if (volume.SetMute(true, ref context) >= 0)
            {
                changedEndpoints!.Add(volume);
                retain = true;
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException or UnauthorizedAccessException) { }
        finally
        {
            if (!retain) Release(volume);
            if (!ReferenceEquals(instance, volume)) Release(instance);
            Release(device);
        }
    }

    public void Dispose()
    {
        var endpoints = Interlocked.Exchange(ref changedEndpoints, null);
        if (endpoints is null) return;
        foreach (var endpoint in endpoints)
        {
            try { var context = VolumeService.DictationPlaybackMuteContext; endpoint.SetMute(false, ref context); }
            catch (Exception error) when (error is COMException or InvalidOperationException) { }
            finally { Release(endpoint); }
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static readonly Guid IidAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
