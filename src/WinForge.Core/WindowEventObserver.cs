using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Owns a bounded Windows accessibility-event subscription on its installing message-loop thread.</summary>
public sealed class WindowEventObserver : IDisposable
{
    public readonly record struct WindowEvent(uint Kind, IntPtr Window);
    public const uint Foreground = 0x0003;
    public const uint ObjectDestroy = 0x8001;
    public const uint ObjectShow = 0x8002;
    public const uint ObjectLocationChange = 0x800B;
    private readonly WinEventCallback _callback;
    private IntPtr _hook;

    public WindowEventObserver(uint firstEvent, uint lastEvent, Action<WindowEvent> onWindow, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(onWindow);
        ArgumentNullException.ThrowIfNull(onError);
        _callback = (_, kind, window, objectId, childId, _, _) =>
        {
            if (window == IntPtr.Zero || objectId != 0 || childId != 0) return;
            try { onWindow(new(kind, window)); }
            catch (Exception exception) { try { onError(exception); } catch { } }
        };
        _hook = SetWinEventHook(firstEvent, lastEvent, IntPtr.Zero, _callback, 0, 0, 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero && !UnhookWinEvent(hook)) throw new Win32Exception(Marshal.GetLastWin32Error());
        GC.KeepAlive(_callback);
    }

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
