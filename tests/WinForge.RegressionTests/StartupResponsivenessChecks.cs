using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class StartupResponsivenessChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string area = "WinForge.Tests.AyarDeğişikliği";
        using var ready = new ManualResetEventSlim();
        using var received = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Dispatcher? listenerDispatcher = null;
        var notifications = 0;
        var listener = new Thread(() =>
        {
            listenerDispatcher = Dispatcher.CurrentDispatcher;
            using var source = new HwndSource(new HwndSourceParameters("WinForge notification fixture"));
            source.AddHook((IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == Native.WM_SETTINGCHANGE && Marshal.PtrToStringUni(lParam) == area)
                {
                    Interlocked.Increment(ref notifications);
                    received.Set();
                    release.Wait(TimeSpan.FromSeconds(20));
                }
                return IntPtr.Zero;
            });
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true };
        listener.SetApartmentState(ApartmentState.STA);
        listener.Start();
        try
        {
            check(ready.Wait(TimeSpan.FromSeconds(5)), "Notification fixture starts on an independent UI thread");
            var send = SettingsChangeBroadcaster.BroadcastAsync(area);
            PumpUntil(() => received.IsSet || send.IsCompleted, TimeSpan.FromSeconds(15));
            check(received.IsSet, "Shell notifications preserve Unicode setting names");
            check(!send.IsCompleted, "A slow notification recipient does not block the caller");

            var uiResponded = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => uiResponded = true), DispatcherPriority.Background);
            PumpUntil(() => uiResponded, TimeSpan.FromSeconds(2));
            check(uiResponded, "The UI dispatcher processes input while a shell recipient is waiting");
            var queued = SettingsChangeBroadcaster.BroadcastAsync(area, area);
            check(ReferenceEquals(send, queued), "Repeated notifications share a single background worker");
            release.Set();
            PumpUntil(() => send.IsCompleted, TimeSpan.FromSeconds(15));
            send.GetAwaiter().GetResult();
            check(notifications == 2, "Duplicate pending notifications coalesce without losing a later change");
        }
        finally
        {
            release.Set();
            listenerDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            listener.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < timeout)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
                (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        if (!condition()) throw new TimeoutException("Startup responsiveness check timed out.");
    }
}
