using System.Windows.Threading;
using WinForge.Core;

internal static class KeyboardHookThreadChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var ui = Dispatcher.CurrentDispatcher;
        var uiThread = Environment.CurrentManagedThreadId;
        using var hook = new KeyboardHookThread((code, message, data) =>
            Native.CallNextHookEx(IntPtr.Zero, code, message, data));
        var uiCallbackRan = false;
        var operation = hook.Dispatcher.InvokeAsync(() =>
        {
            ui.BeginInvoke(new Action(() => uiCallbackRan = true));
            return Environment.CurrentManagedThreadId;
        });
        // Deliberately do not pump the UI. The hook message loop must still run.
        check(operation.Task.Wait(TimeSpan.FromSeconds(3)), "Keyboard message loop remains responsive while UI is blocked");
        check(operation.Task.Result != uiThread && !uiCallbackRan, "Keyboard work never waits for queued UI notifications");
        hook.Dispose();
        hook.Dispose();
        check(hook.Completion.Wait(TimeSpan.FromSeconds(3)), "Keyboard hook unhooks and stops after idempotent disposal");
        var frame = new DispatcherFrame();
        ui.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        check(uiCallbackRan, "Deferred UI notifications resume when the UI is available");
    }
}
