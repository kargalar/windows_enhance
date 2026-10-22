using System.Runtime.InteropServices;

namespace WinForge.Core;

public static class VolumeService
{
    private static readonly Guid IidAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static int Current()
    {
        var volume = GetVolume();
        volume.GetMasterVolumeLevelScalar(out var scalar);
        return (int)Math.Round(scalar * 100);
    }

    public static int Step(int percent)
    {
        var volume = GetVolume();
        volume.GetMasterVolumeLevelScalar(out var current);

        var next = Math.Clamp(current + percent / 100f, 0f, 1f);
        var context = Guid.Empty;
        volume.SetMasterVolumeLevelScalar(next, ref context);
        if (next > 0f) volume.SetMute(false, ref context);

        return (int)Math.Round(next * 100);
    }

    private static IAudioEndpointVolume GetVolume()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        var hr = enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out var device);
        if (hr != 0) throw new InvalidOperationException($"Ses aygıtı alınamadı (0x{hr:X8}).");

        var iid = IidAudioEndpointVolume;
        hr = device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var instance);
        if (hr != 0) throw new InvalidOperationException($"Ses denetimi açılamadı (0x{hr:X8}).");

        return (IAudioEndpointVolume)instance;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
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
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
