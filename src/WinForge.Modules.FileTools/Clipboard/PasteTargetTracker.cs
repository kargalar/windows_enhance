using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace WinForge.ClipboardLine;

/// <summary>Tracks the identity, never the contents, of the last focused external editable field.</summary>
internal sealed class PasteTargetTracker : IDisposable
{
    private readonly int _ownProcessId;
    private readonly object _gate = new();
    private PasteTarget? _target;
    private bool _disposed;

    internal PasteTargetTracker(int? ownProcessId = null)
    {
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
        Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
        _ = Task.Run(() =>
        {
            try { Observe(AutomationElement.FocusedElement); }
            catch (Exception error) when (IsAutomationFailure(error)) { Clear(); }
        });
    }

    internal PasteTarget? CaptureCurrent()
    {
        lock (_gate) return _target;
    }

    internal async Task<PasteTarget?> CaptureFocusedAsync()
    {
        try
        {
            var target = await Task.Run(() => Snapshot(AutomationElement.FocusedElement)).WaitAsync(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            lock (_gate) if (!_disposed && target is not null && GetForegroundWindow() == target.Window) _target = target;
            return target;
        }
        catch (Exception error) when (IsAutomationFailure(error)) { return null; }
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs args)
    {
        lock (_gate) if (_disposed) return;
        try
        {
            var element = sender as AutomationElement;
            if (element is null)
            {
                if (!OwnProcessHasForeground()) Clear();
                return;
            }
            Observe(element);
        }
        catch (Exception error) when (IsAutomationFailure(error)) { Clear(); }
    }

    private bool OwnProcessHasForeground()
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        return foreground != IntPtr.Zero && foregroundProcess == _ownProcessId;
    }

    private void Observe(AutomationElement? element)
    {
        if (element is null) { Clear(); return; }
        var info = element.Current;
        if (info.ProcessId == _ownProcessId) return;
        var target = Snapshot(element);
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (foregroundProcess == _ownProcessId) return;
        lock (_gate) if (!_disposed) _target = target;
    }

    private PasteTarget? Snapshot(AutomationElement? element)
    {
        if (element is null) return null;
        var info = element.Current;
        if (info.ProcessId == _ownProcessId) return null;
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (foreground == IntPtr.Zero || foregroundProcess != info.ProcessId || !info.IsEnabled || info.IsPassword ||
            !element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) || ((ValuePattern)raw).Current.IsReadOnly)
            return null;
        var runtimeId = element.GetRuntimeId();
        return runtimeId is { Length: > 0 and <= 128 } ? new PasteTarget(foreground, info.ProcessId, runtimeId) : null;
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
            var element = AutomationElement.FocusedElement;
            if (element is null || element.Current.ProcessId != expected.ProcessId || !element.Current.IsEnabled || element.Current.IsPassword ||
                !element.TryGetCurrentPattern(ValuePattern.Pattern, out var raw) || ((ValuePattern)raw).Current.IsReadOnly) return false;
            return element.GetRuntimeId().SequenceEqual(expected.RuntimeId);
        }
        catch (Exception error) when (IsAutomationFailure(error)) { return false; }
    }

    private void Clear() { lock (_gate) _target = null; }
    private static bool IsAutomationFailure(Exception error) => error is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or TimeoutException;
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _target = null; }
        _ = Task.Run(() =>
        {
            try { Automation.RemoveAutomationFocusChangedEventHandler(OnFocusChanged); }
            catch (Exception error) when (IsAutomationFailure(error)) { }
        });
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
}

internal sealed record PasteTarget(IntPtr Window, int ProcessId, int[] RuntimeId);
