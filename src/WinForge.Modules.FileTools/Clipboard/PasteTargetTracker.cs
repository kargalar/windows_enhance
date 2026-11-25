using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.ClipboardLine;

/// <summary>Tracks the identity, never the contents, of the last focused external editable field.</summary>
internal sealed class PasteTargetTracker : IDisposable
{
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly object _gate = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Action<Exception>? _reportError;
    private bool _refreshing;
    private PasteTarget? _target;
    private bool _disposed;

    internal PasteTargetTracker(Action<Exception>? reportError = null)
    {
        _reportError = reportError;
        // The managed focus subscription runs a provider callback outside our code.
        // Query on demand, and refresh while an open shelf can regain focus.
        _timer.Tick += (_, _) => QueueRefresh();
    }

    internal void SetTracking(bool enabled)
    {
        if (!enabled)
        {
            _timer.Stop();
            return;
        }
        lock (_gate) if (_disposed) return;
        _timer.Start();
        QueueRefresh();
    }

    internal PasteTarget? CaptureCurrent()
    {
        lock (_gate) return _target;
    }

    internal async Task<PasteTarget?> CaptureFocusedAsync()
    {
        var foreground = GetExternalForeground();
        if (foreground == IntPtr.Zero) return null;
        try
        {
            var target = await Task.Run(() => ReadTarget(foreground)).WaitAsync(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || GetForegroundWindow() != foreground) return null;
                _target = target;
                return target;
            }
        }
        catch (TimeoutException) { return null; }
    }

    private void QueueRefresh()
    {
        var foreground = GetExternalForeground();
        if (foreground == IntPtr.Zero) return;
        lock (_gate)
        {
            if (_disposed || _refreshing) return;
            _refreshing = true;
        }
        _ = Task.Run(() => Refresh(foreground));
    }

    private void Refresh(IntPtr foreground)
    {
        try
        {
            var target = ReadTarget(foreground);
            lock (_gate)
                if (!_disposed && GetForegroundWindow() == foreground)
                    _target = target;
        }
        catch (Exception error) { _reportError?.Invoke(error); }
        finally { lock (_gate) _refreshing = false; }
    }

    private IntPtr GetExternalForeground()
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        return foregroundProcess == 0 || foregroundProcess == _ownProcessId ? IntPtr.Zero : foreground;
    }

    private PasteTarget? ReadTarget(IntPtr foreground)
    {
        try
        {
            if (GetForegroundWindow() != foreground) return null;
            var element = AutomationElement.FocusedElement;
            if (element is null) return null;
            var info = element.Current;
            GetWindowThreadProcessId(foreground, out var foregroundProcess);
            if (foregroundProcess != info.ProcessId || !info.IsEnabled || info.IsPassword || !FocusedTextOutput.IsEditable(element))
                return null;
            var runtimeId = element.GetRuntimeId();
            return runtimeId is { Length: > 0 and <= 128 } ? new PasteTarget(foreground, info.ProcessId, runtimeId) : null;
        }
        catch (Exception error) when (IsAutomationFailure(error)) { return null; }
    }

    internal async Task<bool> IsCurrentAsync(PasteTarget expected)
    {
        try { return await Task.Run(() => IsCurrent(expected)).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }

    private bool IsCurrent(PasteTarget expected)
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground != expected.Window || !IsWindow(foreground)) return false;
            GetWindowThreadProcessId(foreground, out var process);
            if (process != expected.ProcessId) return false;
            var current = ReadTarget(foreground);
            return current is not null && current.RuntimeId.SequenceEqual(expected.RuntimeId) &&
                GetForegroundWindow() == foreground;
        }
        catch (Exception error) when (IsAutomationFailure(error)) { return false; }
    }

    private static bool IsAutomationFailure(Exception error) =>
        error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or TimeoutException or NullReferenceException;
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _target = null;
        }
        _timer.Stop();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
}

internal sealed record PasteTarget(IntPtr Window, int ProcessId, int[] RuntimeId);
