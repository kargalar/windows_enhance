using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WinForge.Core;

/// <summary>
/// Owns a low-level hook on a dedicated message loop. Callbacks must stay short
/// and must never synchronously wait for the UI dispatcher.
/// </summary>
public sealed class KeyboardHookThread : IDisposable
{
    private readonly Thread _thread;
    private readonly Native.LowLevelKeyboardProc _callback;
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    internal Dispatcher Dispatcher => _ready.Task.GetAwaiter().GetResult();
    internal Task Completion => _stopped.Task;

    public KeyboardHookThread(Native.LowLevelKeyboardProc callback)
    {
        _callback = (code, message, data) =>
        {
            if (Volatile.Read(ref _disposed) == 0)
                try { return callback(code, message, data); } catch { }
            return Native.CallNextHookEx(IntPtr.Zero, code, message, data);
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "WinForge keyboard hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = Dispatcher; // Propagate installation failures to the caller.
    }

    private void Run()
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            hook = Native.SetWindowsHookExKeyboard(Native.WH_KEYBOARD_LL, _callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _ready.TrySetResult(dispatcher);
            System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception error) { _ready.TrySetException(error); }
        finally
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            GC.KeepAlive(_callback);
            _stopped.TrySetResult();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var dispatcher = Dispatcher;
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        // Never join from the UI: CallNextHookEx may be waiting for another
        // hook owned by that very UI thread. The worker unhooks in finally.
    }
}
