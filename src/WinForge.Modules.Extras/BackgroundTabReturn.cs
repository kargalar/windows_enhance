using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal static class BackgroundTabReturn
{
    private const int MaxWalkNodes = 300;
    private const int PollDelayMs = 25;
    private const int MaxWaitPolls = 80;

    internal static Func<CancellationToken, Task> Prepare(IntPtr target)
    {
        var selectedTab = IsChromeWindow(target) ? FindSelectedTab(target) : null;
        return token => RestoreAsync(target, selectedTab, token);
    }

    internal static bool IsChromeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return ForegroundApplication.Matches("chrome.exe", process.ProcessName);
        }
        catch { return false; }
    }

    private static AutomationElement? FindSelectedTab(IntPtr window)
    {
        try
        {
            var root = AutomationElement.FromHandle(window);
            var walker = TreeWalker.ControlViewWalker;
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            var visited = 0;

            while (queue.Count > 0 && visited < MaxWalkNodes)
            {
                var current = queue.Dequeue();
                visited++;

                ControlType type;
                try { type = current.Current.ControlType; }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { continue; }

                // Skip web page DOMs so we only inspect Chrome's own browser strip.
                if (type == ControlType.Document) continue;

                if (type == ControlType.TabItem && IsTabSelected(current))
                    return current;

                AutomationElement? child;
                try { child = walker.GetFirstChild(current); }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { continue; }

                while (child is not null && visited + queue.Count < MaxWalkNodes)
                {
                    queue.Enqueue(child);
                    try { child = walker.GetNextSibling(child); }
                    catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { break; }
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException) { }
        return null;
    }

    private static async Task RestoreAsync(IntPtr target, AutomationElement? selectedTab, CancellationToken cancellationToken)
    {
        if (selectedTab is not null)
        {
            for (var attempt = 0; attempt < MaxWaitPolls; attempt++)
            {
                await Task.Delay(PollDelayMs, cancellationToken);
                var foregroundChanged = Native.GetForegroundWindow() != target;
                var tabChanged = !IsTabSelected(selectedTab);
                if (!foregroundChanged && !tabChanged) continue;

                if (tabChanged)
                {
                    SelectTab(selectedTab);
                    await Task.Delay(PollDelayMs, cancellationToken);
                    if (!IsTabSelected(selectedTab)) SelectTab(selectedTab);
                }
                if (Native.GetForegroundWindow() != target) RestoreForegroundWindow(target);
                return;
            }
            return;
        }

        for (var attempt = 0; attempt < MaxWaitPolls; attempt++)
        {
            await Task.Delay(PollDelayMs, cancellationToken);
            if (Native.GetForegroundWindow() == target) continue;

            RestoreForegroundWindow(target);
            for (var settle = 0; settle < 6; settle++)
            {
                await Task.Delay(PollDelayMs, cancellationToken);
                if (Native.GetForegroundWindow() != target) RestoreForegroundWindow(target);
            }
            return;
        }
    }

    private static bool IsTabSelected(AutomationElement tab)
    {
        try
        {
            return tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) &&
                   ((SelectionItemPattern)pattern).Current.IsSelected;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static void SelectTab(AutomationElement tab)
    {
        try
        {
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
                ((SelectionItemPattern)pattern).Select();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
    }

    private static void RestoreForegroundWindow(IntPtr target)
    {
        if (target == IntPtr.Zero || !Native.IsWindow(target)) return;
        var foreground = Native.GetForegroundWindow();
        if (foreground == target) return;
        var foregroundThread = foreground != IntPtr.Zero ? Native.GetWindowThreadProcessId(foreground, out _) : 0;
        var targetThread = Native.GetWindowThreadProcessId(target, out _);
        var currentThread = GetCurrentThreadId();
        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread && AttachThreadInput(currentThread, targetThread, true);
        try
        {
            Native.SetForegroundWindow(target);
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
