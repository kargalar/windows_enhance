using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Automation;

namespace WinForge.Core;

public static class ShellTaskbar
{
    private static readonly SemaphoreSlim HitTestGate = new(1, 1);
    private static readonly TimeSpan HitTestTimeout = TimeSpan.FromSeconds(2);
    private const int MaximumAncestors = 10;
    private const long CandidateCacheMilliseconds = 250;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private static IReadOnlyList<TaskbarWindowTarget> _candidates = [];
    private static long _candidateSnapshotAt;

    public enum HitKind { Unavailable, ApplicationButton, EmptyArea, OtherControl }
    public sealed record ButtonIdentity(string ApplicationId, string Name, IntPtr WindowHandle);
    public sealed record TaskbarWindowTarget(IntPtr Handle, uint ProcessId, long ProcessStartTicks,
        string Title, string ExecutablePath, string ApplicationId);
    public sealed record HitTestResult(HitKind Kind, ButtonIdentity? Button, TaskbarWindowTarget? Target);
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    };

    public static bool IsTaskbarPoint(POINT point)
    {
        var window = Native.WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        if (!IsTaskbarWindow(window)) return false;
        var root = Native.GetAncestor(window, Native.GA_ROOT);
        return Native.GetWindowRect(root, out var bounds) && IsTaskbarRevealed(bounds, DesktopDisplays.ForWindow(root).Bounds);
    }

    internal static bool IsTaskbarRevealed(RECT bounds, RECT screen)
    {
        var horizontal = bounds.Right - bounds.Left >= bounds.Bottom - bounds.Top;
        var thickness = horizontal ? bounds.Bottom - bounds.Top : bounds.Right - bounds.Left;
        var visible = horizontal ? Math.Min(bounds.Bottom, screen.Bottom) - Math.Max(bounds.Top, screen.Top)
            : Math.Min(bounds.Right, screen.Right) - Math.Max(bounds.Left, screen.Left);
        return thickness > 0 && visible >= thickness / 2.0;
    }

    public static bool IsTaskbarWindow(IntPtr window)
    {
        var root = Native.GetAncestor(window, Native.GA_ROOT);
        if (root == IntPtr.Zero) return false;
        return TaskbarClasses.Contains(Native.ClassNameOf(root));
    }

    /// <summary>Resolves application identity on a worker, without blocking a low-level mouse hook.</summary>
    public static async Task<HitTestResult> HitTestAsync(POINT point,
        Func<IReadOnlyList<ShellVisualNode>>? readNativeTree = null, CancellationToken cancellationToken = default)
    {
        if (!IsTaskbarPoint(point) || !await HitTestGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new(HitKind.Unavailable, null, null);
        var operation = Task.Run(() =>
        {
            try
            {
                HitTestResult hit;
                try { hit = ReadHit(point); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { hit = new(HitKind.Unavailable, null, null); }
                var needsNativeIdentity = hit.Kind == HitKind.Unavailable ||
                    (hit.Kind == HitKind.ApplicationButton && string.IsNullOrEmpty(hit.Button?.ApplicationId));
                if (!needsNativeIdentity || readNativeTree is null) return hit;
                var nativeHit = ResolveVisualHit(point, readNativeTree());
                if (nativeHit.Kind == HitKind.Unavailable) return hit;
                if (hit.Kind == HitKind.ApplicationButton && nativeHit.Kind != HitKind.ApplicationButton)
                    return new HitTestResult(HitKind.Unavailable, null, null);
                return nativeHit.Button is { } button ? ResolveButton(button) : nativeHit;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { return new HitTestResult(HitKind.Unavailable, null, null); }
            finally { HitTestGate.Release(); }
        });
        try { return await operation.WaitAsync(HitTestTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return new(HitKind.Unavailable, null, null); }
    }

    /// <summary>Compatibility entry point. Hook handlers should await HitTestAsync instead.</summary>
    public static IntPtr FindWindowUnderTaskbar(POINT point) =>
        HitTestAsync(point).GetAwaiter().GetResult().Target?.Handle ?? IntPtr.Zero;

    private static HitTestResult ReadHit(POINT point)
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
        ButtonIdentity? button = null;
        var sawInteractiveControl = false;
        for (var depth = 0; element is not null && depth < MaximumAncestors; depth++)
        {
            var current = element.Current;
            if (TaskbarClasses.Contains(current.ClassName)) break;
            var appId = ParseApplicationId(current.AutomationId);
            if (appId.Length > 0)
            {
                button = new(appId, current.Name?.Trim() ?? "", new IntPtr(current.NativeWindowHandle));
                break;
            }
            if (current.ControlType == ControlType.Button || current.ControlType == ControlType.TabItem)
            {
                sawInteractiveControl = true;
                if (current.ClassName.Equals("Taskbar.TaskListButton", StringComparison.Ordinal) ||
                    current.ClassName.Equals("TaskListButton", StringComparison.Ordinal))
                    button ??= new("", current.Name?.Trim() ?? "", new IntPtr(current.NativeWindowHandle));
            }
            element = TreeWalker.RawViewWalker.GetParent(element);
        }

        if (button is null)
        {
            if (sawInteractiveControl) return new(HitKind.OtherControl, null, null);
            // A root-only provider may hide real buttons. It is never evidence of empty space.
            return new(HitKind.Unavailable, null, null);
        }
        return ResolveButton(button);
    }

    private static HitTestResult ResolveButton(ButtonIdentity button)
    {
        var candidates = GetCandidateWindows();
        var paths = button.ApplicationId.Length > 0 && !candidates.Any(candidate =>
                candidate.ApplicationId.Equals(button.ApplicationId, StringComparison.OrdinalIgnoreCase))
            ? ShellApplicationIdentity.GetExecutablePaths(button.ApplicationId)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new(HitKind.ApplicationButton, button, ResolveWindow(button, candidates, paths, Native.GetForegroundWindow()));
    }

    public static HitTestResult ResolveVisualHit(POINT point, IReadOnlyList<ShellVisualNode> nodes)
    {
        var buttons = nodes.Where(node => node.Type.EndsWith("TaskListButton", StringComparison.Ordinal) && ValidBounds(node)).ToArray();
        if (buttons.Length == 0) return new(HitKind.Unavailable, null, null);
        var hit = buttons.Where(node => Contains(node, point)).ToArray();
        if (hit.Length > 1) return new(HitKind.Unavailable, null, null);
        if (hit.Length == 1)
        {
            var node = hit[0];
            return new(HitKind.ApplicationButton,
                new(ParseApplicationId(node.AutomationId), node.AccessibleName.Trim(), IntPtr.Zero), null);
        }
        if (nodes.Any(node => ValidBounds(node) && Contains(node, point) &&
            (node.Type.EndsWith("Button", StringComparison.Ordinal) || node.Type.Contains("SystemTray", StringComparison.Ordinal))))
            return new(HitKind.OtherControl, null, null);
        var taskbarFrame = nodes.Any(node => node.Type == "Taskbar.TaskbarFrame" && ValidBounds(node) && Contains(node, point));
        return new(taskbarFrame ? HitKind.EmptyArea : HitKind.Unavailable, null, null);
    }

    private static bool ValidBounds(ShellVisualNode node) =>
        node.Visibility.Equals("Visible", StringComparison.OrdinalIgnoreCase) &&
        double.IsFinite(node.Opacity) && node.Opacity > 0 &&
        double.IsFinite(node.ScreenLeft) && double.IsFinite(node.ScreenTop) &&
        double.IsFinite(node.ScreenWidth) && double.IsFinite(node.ScreenHeight) && node.ScreenWidth > 0 && node.ScreenHeight > 0;

    private static bool Contains(ShellVisualNode node, POINT point) => point.X >= node.ScreenLeft && point.X < node.ScreenLeft + node.ScreenWidth &&
        point.Y >= node.ScreenTop && point.Y < node.ScreenTop + node.ScreenHeight;

    internal static string ParseApplicationId(string automationId)
    {
        const string prefix = "Appid:";
        if (!automationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        var id = automationId[prefix.Length..].Trim();
        return id.Length is > 0 and <= 512 && !id.Any(char.IsControl) ? id : "";
    }

    internal static TaskbarWindowTarget? ResolveWindow(ButtonIdentity button, IReadOnlyList<TaskbarWindowTarget> candidates,
        IReadOnlySet<string> executablePaths, IntPtr foreground)
    {
        IEnumerable<TaskbarWindowTarget> matches;
        if (button.ApplicationId.Length > 0)
            matches = candidates.Where(candidate => candidate.ApplicationId.Equals(button.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
                (candidate.ApplicationId.Length == 0 && candidate.ExecutablePath.Length > 0 && executablePaths.Contains(candidate.ExecutablePath)));
        else
        {
            var direct = candidates.SingleOrDefault(candidate => candidate.Handle == button.WindowHandle);
            if (direct is not null) return direct;
            matches = candidates.Where(candidate => button.Name.Length > 0 && candidate.Title.Equals(button.Name, StringComparison.OrdinalIgnoreCase));
        }
        var group = matches.ToArray();
        if (group.Length == 1) return group[0];
        if (group.Length == 0) return null;
        if (button.ApplicationId.Length == 0) return null;
        var exactTitle = group.Where(candidate => button.Name.Length > 0 && candidate.Title.Equals(button.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exactTitle.Length == 1) return exactTitle[0];
        // A group may contain several documents. Only a known active member is unambiguous.
        return group.SingleOrDefault(candidate => candidate.Handle == foreground);
    }

    public static bool IsCurrentTarget(TaskbarWindowTarget target)
    {
        if (!Native.IsWindow(target.Handle)) return false;
        Native.GetWindowThreadProcessId(target.Handle, out var processId);
        if (processId != target.ProcessId || !Native.TitleOf(target.Handle).Trim().Equals(target.Title, StringComparison.Ordinal)) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return target.ProcessStartTicks == 0 || process.StartTime.ToUniversalTime().Ticks == target.ProcessStartTicks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static IReadOnlyList<TaskbarWindowTarget> GetCandidateWindows()
    {
        if (_candidateSnapshotAt != 0 && Environment.TickCount64 - _candidateSnapshotAt < CandidateCacheMilliseconds) return _candidates;
        var result = new List<TaskbarWindowTarget>();
        using var applicationResolver = WindowsApplicationResolver.TryCreate();
        var processIdentities = new Dictionary<uint, (string Path, long StartTicks, string AppId)>();
        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window)) return true;
            var className = Native.ClassNameOf(window);
            if (IgnoredClasses.Contains(className)) return true;
            var style = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64();
            if ((style & WsExAppWindow) == 0 && ((style & WsExToolWindow) != 0 || GetWindow(window, 4) != IntPtr.Zero)) return true;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) >= 0 && cloaked != 0) return true;
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId == (uint)Environment.ProcessId) return true;
            var text = Native.TitleOf(window).Trim();
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!processIdentities.TryGetValue(processId, out var identity))
            {
                identity = ("", 0, ShellApplicationIdentity.ReadProcessId(processId));
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    identity = (process.MainModule?.FileName ?? "", process.StartTime.ToUniversalTime().Ticks, identity.AppId);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                processIdentities[processId] = identity;
            }
            var windowId = applicationResolver?.ReadWindowId(window) ?? "";
            if (windowId.Length == 0) windowId = ShellApplicationIdentity.ReadWindowId(window);
            result.Add(new(window, processId, identity.StartTicks, text, identity.Path, windowId.Length > 0 ? windowId : identity.AppId));
            return true;
        }, IntPtr.Zero);
        _candidates = result;
        _candidateSnapshotAt = Environment.TickCount64;
        return result;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
