using System.Runtime.InteropServices;

namespace WinForge.Core;

/// <summary>Sends shell notifications without waiting for other applications on the UI thread.</summary>
public static class SettingsChangeBroadcaster
{
    private const uint RecipientTimeoutMilliseconds = 1000;
    private static readonly object Gate = new();
    private static readonly HashSet<string> PendingAreas = new(StringComparer.Ordinal);
    private static Task _worker = Task.CompletedTask;

    public static void Broadcast(params string[] areas) => _ = BroadcastAsync(areas);

    public static Task BroadcastAsync(params string[] areas)
    {
        lock (Gate)
        {
            foreach (var area in areas) PendingAreas.Add(area);
            if (_worker.IsCompleted) _worker = Task.Run(SendPending);
            return _worker;
        }
    }

    private static void SendPending()
    {
        while (true)
        {
            string[] areas;
            lock (Gate)
            {
                if (PendingAreas.Count == 0)
                {
                    _worker = Task.CompletedTask;
                    return;
                }
                areas = PendingAreas.ToArray();
                PendingAreas.Clear();
            }

            foreach (var area in areas)
                SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, area,
                    Native.SMTO_ABORTIFHUNG, RecipientTimeoutMilliseconds, out _);
        }
    }

    // Keep the marshalled string alive until the synchronous send finishes on the worker.
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam,
        string area, uint flags, uint timeout, out IntPtr result);
}
