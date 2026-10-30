using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinForge.Core;
using static WinForge.Modules.WindowControl.WindowControlNative;

namespace WinForge.Modules.WindowControl;

/// <summary>Native actions and only the state needed to undo temporary window/process changes.</summary>
internal sealed class WindowActions : IDisposable
{
    private const uint ActivationTimeoutMilliseconds = 150;
    private const long OverlappedWindowStyle = 0x00CF0000;
    private const long PopupWindowStyle = unchecked((int)0x80000000);
    private const uint FrameChanged = 0x0020;
    private const uint ShowWindow = 0x0040;
    private readonly Dictionary<IntPtr, SavedBounds> _rolled = new();
    private readonly Dictionary<IntPtr, SavedBounds> _snapped = new();
    private readonly Dictionary<IntPtr, FullscreenState> _fullscreen = new();
    private readonly Dictionary<IntPtr, WindowIdentity> _minimized = new();
    private WindowIdentity? _minimizeOwner;
    private readonly Dictionary<uint, List<IntPtr>> _suspendedThreads = new();
    private readonly Action<Exception> _report;
    private readonly Func<IntPtr, POINT, bool>? _placeInZone;
    private readonly Func<IntPtr, bool, IReadOnlyList<IntPtr>> _candidates;
    private readonly MasterAudioEndpoint _masterAudio;

    internal WindowActions(Action<Exception> report, Func<IntPtr, POINT, bool>? placeInZone = null,
        Func<IntPtr, bool, IReadOnlyList<IntPtr>>? candidates = null, MasterAudioEndpoint? masterAudio = null)
    {
        _report = report;
        _placeInZone = placeInZone;
        _candidates = candidates ?? Candidates;
        _masterAudio = masterAudio ?? MasterAudioEndpoint.Windows;
    }

    internal bool Execute(IntPtr window, WindowAction action, POINT point, WindowControlOptions options, int direction = 0,
        WindowGestureModifiers modifiers = default)
    {
        if (!Native.IsWindow(window)) return false;
        switch (action)
        {
            case WindowAction.Roll: Roll(window, direction); break;
            case WindowAction.ToggleFullscreen: ToggleFullscreen(window, point); break;
            case WindowAction.MaximizeAxis: MaximizeAxis(window, point, modifiers.Shift); break;
            case WindowAction.MinimizeOthers: MinimizeOthers(window, modifiers.Shift); break;
            case WindowAction.Center: Center(window, point, modifiers.Shift); break;
            case WindowAction.MasterMute: _masterAudio.ToggleMute(); break;
            case WindowAction.Volume: _masterAudio.StepVolume(direction, modifiers.Shift); break;
            case WindowAction.ApplicationMute: ToggleMute(window); break;
            case WindowAction.SideSnap: SideSnap(window, point, options, modifiers.Shift); break;
            case WindowAction.PlaceInZone:
                if (_placeInZone?.Invoke(window, point) != true)
                    throw new InvalidOperationException("Bölgeye yerleştirmek için Otomatik Döşeme modülünde geçerli bir bölge düzeni seçin.");
                break;
            case WindowAction.NextStacked: Cycle(window, point, 1, stacked: true); break;
            case WindowAction.PreviousStacked: Cycle(window, point, -1, stacked: true); break;
            case WindowAction.StackList: ShowWindowList(window, point, stacked: true); break;
            case WindowAction.WindowList: ShowWindowList(window, point, stacked: false, modifiers.Shift); break;
            case WindowAction.Kill: Kill(window); break;
            case WindowAction.Pause: Pause(window); break;
            case WindowAction.Resume: Resume(window); break;
            case WindowAction.SwitchWindow: Cycle(window, point, direction, stacked: false); break;
            case WindowAction.CycleStacked: Cycle(window, point, direction, stacked: true); break;
            case WindowAction.RaiseLower:
                if (modifiers.Shift) ShowWindowAsync(window, direction > 0 ? IsZoomed(window) ? Restore : Maximize : Minimize);
                else RaiseLower(window, direction);
                break;
            case WindowAction.MaximizeMinimize:
                if (modifiers.Shift) RaiseLower(window, direction);
                else ShowWindowAsync(window, direction > 0 ? Maximize : IsZoomed(window) ? Restore : Minimize);
                break;
            case WindowAction.HorizontalScroll:
                var target = Native.WindowFromPoint(point);
                if (Native.GetAncestor(target, Native.GA_ROOT) != Native.GetAncestor(window, Native.GA_ROOT)) target = window;
                Native.PostMessage(target, HorizontalWheel, new IntPtr(unchecked((int)((uint)(short)(-direction * 120) << 16))),
                    new IntPtr(unchecked((int)((uint)(ushort)point.Y << 16 | (ushort)point.X))));
                break;
            case WindowAction.Zoom: Zoom(window, point, direction, options, modifiers); break;
            default: return false;
        }
        return true;
    }

    internal static WindowBounds WorkArea(IntPtr window, POINT point)
    {
        if (((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & MdiChildStyle) != 0)
        {
            var parent = GetParent(window);
            if (GetClientRect(parent, out var client))
            {
                var origin = new POINT(client.Left, client.Top);
                MapWindowPoints(parent, IntPtr.Zero, ref origin, 1);
                return new(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
            }
        }
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(point, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return WindowBounds.From(info.Work);
    }

    internal static bool Activate(IntPtr window)
    {
        if (Native.IsIconic(window)) ShowWindowAsync(window, Restore);
        var root = Native.GetAncestor(window, Native.GA_ROOT);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!Native.SetForegroundWindow(root)) return false;
            // Cross-thread activation is asynchronous. A bounded WM_NULL waits for that
            // exact input queue without sharing a potentially unresponsive app's queue.
            if (SendWindowMessageTimeout(root, 0, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, ActivationTimeoutMilliseconds, out _) == IntPtr.Zero)
                return false;
            if (Native.GetForegroundWindow() != root) continue;
            if (root != window && ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & MdiChildStyle) != 0)
                Native.PostMessage(GetParent(window), 0x0222, window, IntPtr.Zero); // WM_MDIACTIVATE.
            return true;
        }
        return false;
    }

    internal static uint ShowPopup(IntPtr menu, POINT point, IntPtr selected, Action<uint>? dispatch = null)
    {
        // TrackPopupMenu requires a window owned by the calling UI thread. Foreign target
        // windows own the system command, but must never own our popup's message loop.
        var owner = WindowMenuOwner.Create(point);
        try
        {
            Activate(selected);
            if (!Activate(owner))
                throw new InvalidOperationException("Pencere eylem menüsü odaklanamadı; etkin pencereyi seçerek yeniden deneyin.");
            var result = TrackPopupMenu(menu, MenuReturnCommand | MenuRightButton, point.X, point.Y, 0, owner, IntPtr.Zero);
            // Dispatch before destroying the foreground owner. Destroying it first may
            // restore an unrelated foreground process and revoke our activation rights.
            if (result != 0) dispatch?.Invoke(result);
            return result;
        }
        finally { DestroyWindow(owner); }
    }

    internal static void Place(IntPtr window, WindowBounds bounds)
    {
        if (((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & MdiChildStyle) != 0)
        {
            var origin = new POINT(bounds.X, bounds.Y);
            MapWindowPoints(IntPtr.Zero, GetParent(window), ref origin, 1);
            bounds = bounds with { X = origin.X, Y = origin.Y };
        }
        Native.SetWindowPos(window, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NoZOrder | Native.SWP_NOACTIVATE | AsyncWindowPosition);
    }

    private void Roll(IntPtr window, int direction)
    {
        if (TryRestore(_rolled, window, direction <= 0)) return;
        if (direction < 0 || IsZoomed(window) || !Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var dpi = Native.GetDpiForWindow(window);
        var caption = Math.Max(1, GetSystemMetricsForDpi(CaptionHeightMetric, dpi));
        var frame = GetSystemMetricsForDpi(SizeFrameHeightMetric, dpi) + GetSystemMetricsForDpi(PaddedBorderMetric, dpi);
        _rolled[window] = Save(window, bounds);
        Place(window, bounds with { Height = Math.Min(bounds.Height, caption + 2 * frame) });
    }

    private static bool TryRestore(Dictionary<IntPtr, SavedBounds> saved, IntPtr window, bool restore)
    {
        if (!saved.TryGetValue(window, out var original)) return false;
        if (!original.Identity.IsCurrent) { saved.Remove(window); return false; }
        if (restore)
        {
            Place(window, original.Bounds);
            saved.Remove(window);
        }
        return true;
    }

    internal void RememberSnap(IntPtr window, WindowBounds normal)
    {
        if (!_snapped.TryGetValue(window, out var saved) || !saved.Identity.IsCurrent) _snapped[window] = Save(window, normal);
    }

    internal WindowBounds? SnapNormalBounds(IntPtr window) => _snapped.TryGetValue(window, out var saved) && saved.Identity.IsCurrent ? saved.Bounds : null;
    internal void ForgetSnap(IntPtr window) => _snapped.Remove(window);

    private void MaximizeAxis(IntPtr window, POINT point, bool horizontal)
    {
        if (IsZoomed(window) || !Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var work = WorkArea(window, point);
        RememberSnap(window, bounds);
        Place(window, horizontal ? bounds with { X = work.X, Width = work.Width } : bounds with { Y = work.Y, Height = work.Height });
    }

    private void SideSnap(IntPtr window, POINT point, WindowControlOptions options, bool extend)
    {
        if (!Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var edges = WindowGeometry.ResizeRegion(bounds, point, options.CenterFraction, "Taşı");
        RememberSnap(window, bounds);
        Place(window, WindowGeometry.DoubleResizeTarget(bounds, WorkArea(window, point), edges,
            options.HorizontalSplit, options.VerticalSplit, options.Gap, extend));
    }

    private static void RaiseLower(IntPtr window, int direction)
    {
        if (direction > 0) Activate(window);
        else Native.SetWindowPos(window, new IntPtr(1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    private void MinimizeOthers(IntPtr selected, bool currentMonitorOnly)
    {
        var monitor = currentMonitorOnly ? MonitorFromWindow(selected, 2) : IntPtr.Zero;
        if (_minimizeOwner is { } owner && owner.Window == selected && owner.IsCurrent)
        { RestoreMinimized(monitor); return; }
        _minimized.Clear();
        _minimizeOwner = WindowIdentity.Capture(selected);
        foreach (var window in _candidates(selected, false))
        {
            if (window == selected) continue;
            if (monitor != IntPtr.Zero && MonitorFromWindow(window, 2) != monitor) continue;
            var identity = WindowIdentity.Capture(window);
            if (identity is null) continue;
            _minimized[window] = identity;
            ShowWindowAsync(window, Minimize);
        }
    }

    private void RestoreMinimized(IntPtr monitor = default)
    {
        foreach (var (window, identity) in _minimized)
            if (identity.IsCurrent && Native.IsIconic(window) && (monitor == IntPtr.Zero || MonitorFromWindow(window, 2) == monitor))
                ShowWindowAsync(window, Restore);
        _minimized.Clear();
        _minimizeOwner = null;
    }

    private void Center(IntPtr window, POINT point, bool preserveSize)
    {
        if (!Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var maximized = IsZoomed(window);
        if (!preserveSize)
        {
            if (SnapNormalBounds(window) is { } normal) bounds = normal;
            else if (maximized)
            {
                var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
                if (!GetWindowPlacement(window, ref placement)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var placementBounds = WindowBounds.From(placement.NormalPosition);
                if (placementBounds.Width > 0 && placementBounds.Height > 0) bounds = placementBounds;
            }
        }
        if (maximized) RestoreBeforeResize(window);
        if (!preserveSize) ForgetSnap(window);
        var work = WorkArea(window, point);
        Place(window, bounds with { X = work.X + (work.Width - bounds.Width) / 2, Y = work.Y + (work.Height - bounds.Height) / 2 });
    }

    private static void ToggleMute(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out var processId);
        var sessions = AudioSessionService.GetActiveSessions().Where(session => session.ProcessId == processId).ToArray();
        if (sessions.Length == 0) throw new InvalidOperationException("Bu pencerenin uygulamasında etkin bir ses oturumu yok.");
        var mute = sessions.Any(session => !session.IsMuted);
        foreach (var session in sessions)
            if (!AudioSessionService.SetMuted(session.InstanceId, mute))
                throw new InvalidOperationException("Uygulamanın ses oturumu değişti; işlemi yeniden deneyin.");
    }

    internal static IReadOnlyList<IntPtr> Candidates(IntPtr selected, bool includeMinimized)
    {
        var windows = new List<IntPtr>();
        var mdi = ((long)Native.GetWindowLongPtr(selected, Native.GWL_EXSTYLE) & MdiChildStyle) != 0;
        bool Visit(IntPtr window, IntPtr _)
        {
            if (!Native.IsWindowVisible(window) || (!includeMinimized && Native.IsIconic(window))) return true;
            var style = (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
            if ((style & Native.WS_EX_TOOLWINDOW) != 0 || GetWindow(window, 4) != IntPtr.Zero) return true;
            if (mdi && (style & MdiChildStyle) == 0) return true;
            var name = Text(window, true);
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return true;
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId && window != selected) return true;
            if (DwmGetWindowAttributeInt(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            windows.Add(window);
            return true;
        }
        if (mdi) EnumChildWindows(GetParent(selected), Visit, IntPtr.Zero);
        else Native.EnumWindows(Visit, IntPtr.Zero);
        return windows;
    }

    private List<IntPtr> Stacked(IntPtr selected, POINT point) => _candidates(selected, false)
        .Where(window => Native.GetWindowRect(window, out var bounds) && point.X >= bounds.Left && point.X < bounds.Right &&
            point.Y >= bounds.Top && point.Y < bounds.Bottom).ToList();

    private void Cycle(IntPtr selected, POINT point, int direction, bool stacked)
    {
        var windows = stacked ? Stacked(selected, point) : _candidates(selected, true).ToList();
        if (windows.Count < 2) return;
        var index = windows.IndexOf(selected);
        var next = windows[(Math.Max(0, index) + (direction >= 0 ? 1 : windows.Count - 1)) % windows.Count];
        if (Native.IsIconic(next)) ShowWindowAsync(next, Restore);
        Activate(next);
    }

    internal (uint Command, IntPtr Window, bool Activated) ShowWindowList(IntPtr selected, POINT point, bool stacked, bool allMonitors = false)
    {
        var windows = stacked ? Stacked(selected, point) : _candidates(selected, true).ToList();
        if (!stacked && !allMonitors) windows.RemoveAll(window => MonitorFromWindow(window, 2) != MonitorFromPoint(point, 2));
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (var index = 0; index < windows.Count; index++)
            {
                var title = Text(windows[index], false);
                if (string.IsNullOrWhiteSpace(title)) title = Text(windows[index], true);
                AppendMenu(menu, MenuString | (windows[index] == selected ? MenuChecked : 0), (nuint)(index + 1), title.Replace("&", "&&", StringComparison.Ordinal));
            }
            var target = IntPtr.Zero;
            var activated = false;
            var command = ShowPopup(menu, point, selected, choice =>
            {
                if (choice > windows.Count || !Native.IsWindow(windows[(int)choice - 1])) return;
                target = windows[(int)choice - 1];
                activated = Activate(target);
            });
            return (command, target, activated);
        }
        finally { DestroyMenu(menu); }
    }

    private void Zoom(IntPtr window, POINT point, int direction, WindowControlOptions options, WindowGestureModifiers modifiers)
    {
        if (!options.AllowFixedResize && ((long)Native.GetWindowLongPtr(window, WindowStyleIndex) & ResizableStyle) == 0)
            throw new InvalidOperationException("Bu pencere sabit boyutlu; Pencere Kontrolü ayarlarında sabit pencereleri boyutlandırmayı etkinleştirin.");
        if (IsZoomed(window))
            RestoreBeforeResize(window);
        if (!Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var limits = new MinMaxInfo();
        SendMessageTimeout(window, GetMinMaxInfo, IntPtr.Zero, ref limits, Native.SMTO_ABORTIFHUNG, 20, out _);
        var step = modifiers.Shift || options.MagnetMode == "Kapalı" ? 1 : options.MagnetDistance / 2 + 1;
        var resized = WindowGeometry.Zoom(bounds, point, direction, modifiers.Shift ? options.ZoomFractionShift : options.ZoomFraction,
            step, options.CenterFraction, modifiers.Control,
            new(Math.Max(64, limits.MinTrackSize.X), Math.Max(48, limits.MinTrackSize.Y),
                limits.MaxTrackSize.X > 0 ? limits.MaxTrackSize.X : int.MaxValue,
                limits.MaxTrackSize.Y > 0 ? limits.MaxTrackSize.Y : int.MaxValue));
        ForgetSnap(window);
        Place(window, resized);
    }

    private static void RestoreBeforeResize(IntPtr window)
    {
        if (Native.GetWindowThreadProcessId(window, out _) == GetCurrentThreadId())
        {
            ShowWindowSynchronously(window, Restore);
            if (IsZoomed(window))
                throw new InvalidOperationException("Büyütülmüş pencere yerleştirmeden önce normal boyutuna döndürülemedi.");
            return;
        }
        ShowWindowAsync(window, Restore);
        var deadline = Environment.TickCount64 + ActivationTimeoutMilliseconds;
        while (IsZoomed(window) && Environment.TickCount64 < deadline) Thread.Sleep(5);
        if (IsZoomed(window))
            throw new InvalidOperationException("Büyütülmüş pencere yerleştirmeden önce normal boyutuna döndürülemedi.");
    }

    private static WindowIdentity TargetProcess(IntPtr window)
    {
        var identity = WindowIdentity.Capture(window) ?? throw new InvalidOperationException("Seçilen pencere artık mevcut değil.");
        if (identity.ProcessId == Environment.ProcessId || identity.ProcessId <= 4)
            throw new InvalidOperationException("WinForge ve çekirdek sistem işlemlerine bu eylem uygulanamaz.");
        return identity;
    }

    private static void Kill(IntPtr window)
    {
        var identity = TargetProcess(window);
        using var process = Process.GetProcessById((int)identity.ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != identity.StartTicks || !identity.IsCurrent) return;
        process.Kill(entireProcessTree: false);
    }

    private void Pause(IntPtr window)
    {
        var identity = TargetProcess(window);
        if (_suspendedThreads.ContainsKey(identity.ProcessId)) return;
        using var process = Process.GetProcessById((int)identity.ProcessId);
        if (process.StartTime.ToUniversalTime().Ticks != identity.StartTicks || !identity.IsCurrent) return;
        var suspended = new List<IntPtr>();
        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                using (thread)
                {
                    var handle = OpenThread(ThreadSuspendResume | ThreadQueryLimitedInformation, false, (uint)thread.Id);
                    if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (GetProcessIdOfThread(handle) != identity.ProcessId) { CloseHandle(handle); continue; }
                    if (SuspendThread(handle) == FailedSuspendCount)
                    {
                        var error = Marshal.GetLastWin32Error();
                        CloseHandle(handle);
                        throw new Win32Exception(error);
                    }
                    suspended.Add(handle);
                }
            }
            _suspendedThreads[identity.ProcessId] = suspended;
        }
        catch { ResumeThreads(suspended); throw; }
    }

    private void Resume(IntPtr window)
    {
        var identity = TargetProcess(window);
        if (_suspendedThreads.Remove(identity.ProcessId, out var handles)) ResumeThreads(handles);
    }

    private void ResumeThreads(IEnumerable<IntPtr> threads)
    {
        foreach (var thread in threads)
        {
            if (ResumeThread(thread) == FailedSuspendCount)
                _report(new Win32Exception(Marshal.GetLastWin32Error(), "WinForge tarafından duraklatılan iş parçacığına devam edilemedi."));
            CloseHandle(thread);
        }
    }

    private void ToggleFullscreen(IntPtr window, POINT point)
    {
        if (_fullscreen.ContainsKey(window))
        {
            RestoreFullscreen(window);
            return;
        }

        var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(window, ref placement)) throw new Win32Exception(Marshal.GetLastWin32Error());

        var monitor = MonitorFromPoint(point, 2);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) throw new Win32Exception(Marshal.GetLastWin32Error());

        var style = Native.GetWindowLongPtr(window, WindowStyleIndex).ToInt64();
        var extendedStyle = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64();
        _fullscreen[window] = new FullscreenState(style, extendedStyle, placement);

        ShowWindowSynchronously(window, Restore);
        Native.SetWindowLongPtr(window, WindowStyleIndex, new IntPtr((style & ~OverlappedWindowStyle) | PopupWindowStyle));
        if (!Native.SetWindowPos(window, IntPtr.Zero, monitorInfo.Monitor.Left, monitorInfo.Monitor.Top,
                monitorInfo.Monitor.Right - monitorInfo.Monitor.Left, monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top,
                NoZOrder | FrameChanged | ShowWindow))
        {
            var error = Marshal.GetLastWin32Error();
            RestoreFullscreen(window);
            throw new Win32Exception(error);
        }

        Native.SetForegroundWindow(window);
    }

    private void RestoreFullscreen(IntPtr window)
    {
        if (!_fullscreen.Remove(window, out var state) || !Native.IsWindow(window)) return;

        Native.SetWindowLongPtr(window, WindowStyleIndex, new IntPtr(state.Style));
        var currentTopmost = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST;
        Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr((state.ExtendedStyle & ~Native.WS_EX_TOPMOST) | currentTopmost));
        Native.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | NoZOrder | FrameChanged);
        var placement = state.Placement;
        placement.Length = Marshal.SizeOf<WindowPlacement>();
        SetWindowPlacement(window, ref placement);
    }

    private static SavedBounds Save(IntPtr window, WindowBounds bounds) =>
        new(WindowIdentity.Capture(window) ?? throw new InvalidOperationException("Pencere kapandı."), bounds);

    private static string Text(IntPtr window, bool className)
    {
        var result = new StringBuilder(512);
        if (className) Native.GetClassName(window, result, result.Capacity);
        else Native.GetWindowText(window, result, result.Capacity);
        return result.ToString();
    }

    public void Dispose()
    {
        foreach (var window in _fullscreen.Keys.ToArray()) RestoreFullscreen(window);
        foreach (var threads in _suspendedThreads.Values) ResumeThreads(threads);
        _suspendedThreads.Clear();
        RestoreMinimized();
        foreach (var saved in _rolled.Values.Concat(_snapped.Values))
            if (saved.Identity.IsCurrent) Place(saved.Identity.Window, saved.Bounds);
        _rolled.Clear();
        _snapped.Clear();
        GC.SuppressFinalize(this);
    }

    private sealed record SavedBounds(WindowIdentity Identity, WindowBounds Bounds);
    private readonly record struct FullscreenState(long Style, long ExtendedStyle, WindowPlacement Placement);
    private sealed record WindowIdentity(IntPtr Window, uint ProcessId, long StartTicks)
    {
        internal bool IsCurrent
        {
            get
            {
                if (!Native.IsWindow(Window)) return false;
                Native.GetWindowThreadProcessId(Window, out var current);
                if (current != ProcessId) return false;
                try { using var process = Process.GetProcessById((int)ProcessId); return process.StartTime.ToUniversalTime().Ticks == StartTicks; }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
            }
        }
        internal static WindowIdentity? Capture(IntPtr window)
        {
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0) return null;
            try { using var process = Process.GetProcessById((int)processId); return new(window, processId, process.StartTime.ToUniversalTime().Ticks); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return null; }
        }
    }
}
