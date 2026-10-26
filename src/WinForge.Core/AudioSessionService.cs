using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Reads and adjusts audio levels for sessions on the default render device.</summary>
public static class AudioSessionService
{
    private const int RenderFlow = 0;
    private const int ConsoleRole = 0;
    private const int InProcessServer = 1;
    private const int ActiveState = 1;
    private static readonly Guid SessionManager2Id = new("bfa971f1-4d5e-40bb-935e-967039bfbee4");

    public sealed record Session(string InstanceId, uint ProcessId, string Application, int Volume, bool IsMuted);

    public static IReadOnlyList<Session> GetActiveSessions()
    {
        var result = new List<Session>();
        WithSessionEnumerator((enumerator) =>
        {
            Check(enumerator.GetCount(out var count));
            for (var index = 0; index < count; index++)
            {
                IAudioSessionControl? control = null;
                IAudioSessionControl2? details = null;
                ISimpleAudioVolume? volume = null;
                try
                {
                    Check(enumerator.GetSession(index, out control));
                    details = (IAudioSessionControl2)control;
                    Check(details.GetState(out var state));
                    if (state != ActiveState) continue;
                    Check(details.GetProcessId(out var processId));
                    Check(details.GetSessionInstanceIdentifier(out var instanceId));
                    Check(control.GetDisplayName(out var displayName));
                    volume = (ISimpleAudioVolume)control;
                    Check(volume.GetMasterVolume(out var scalar));
                    Check(volume.GetMute(out var muted));
                    result.Add(new Session(instanceId, processId, ResolveApplication(displayName, processId),
                        (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100), muted));
                }
                catch (COMException) { }
                finally
                {
                    ReleaseDistinct(volume, details, control);
                }
            }
        });
        return result;
    }

    public static bool SetVolume(string instanceId, int percent) => UpdateSession(instanceId, volume =>
    {
        var context = Guid.Empty;
        Check(volume.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref context));
        if (percent > 0) Check(volume.SetMute(false, ref context));
    });

    public static bool SetMuted(string instanceId, bool muted) => UpdateSession(instanceId, volume =>
    {
        var context = Guid.Empty;
        Check(volume.SetMute(muted, ref context));
    });

    private static bool UpdateSession(string instanceId, Action<ISimpleAudioVolume> update)
    {
        var found = false;
        try
        {
            WithSessionEnumerator(enumerator =>
            {
                Check(enumerator.GetCount(out var count));
                for (var index = 0; index < count; index++)
                {
                    IAudioSessionControl? control = null;
                    IAudioSessionControl2? details = null;
                    ISimpleAudioVolume? volume = null;
                    try
                    {
                        Check(enumerator.GetSession(index, out control));
                        details = (IAudioSessionControl2)control;
                        Check(details.GetSessionInstanceIdentifier(out var currentId));
                        if (!string.Equals(instanceId, currentId, StringComparison.Ordinal)) continue;
                        volume = (ISimpleAudioVolume)control;
                        update(volume);
                        found = true;
                        break;
                    }
                    finally
                    {
                        ReleaseDistinct(volume, details, control);
                    }
                }
            });
        }
        catch (COMException) { return false; }
        catch (InvalidOperationException) { return false; }
        return found;
    }

    private static void WithSessionEnumerator(Action<IAudioSessionEnumerator> action)
    {
        IMMDeviceEnumerator? devices = null;
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            devices = (IMMDeviceEnumerator)CoreAudioDeviceFactory.CreateEnumerator();
            Check(devices.GetDefaultAudioEndpoint(RenderFlow, ConsoleRole, out device));
            var iid = SessionManager2Id;
            Check(device.Activate(ref iid, InProcessServer, IntPtr.Zero, out var instance));
            manager = (IAudioSessionManager2)instance;
            Check(manager.GetSessionEnumerator(out sessions));
            action(sessions);
        }
        finally
        {
            Release(sessions);
            Release(manager);
            Release(device);
            Release(devices);
        }
    }

    private static string ResolveApplication(string? displayName, uint processId)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch { return $"Uygulama ({processId})"; }
    }

    private static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static void ReleaseDistinct(params object?[] values)
    {
        var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var value in values)
            if (value is not null && released.Add(value)) Release(value);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("bfa971f1-4d5e-40bb-935e-967039bfbee4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification(IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl control);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
