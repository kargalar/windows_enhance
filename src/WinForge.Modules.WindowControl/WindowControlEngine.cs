using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.WindowControl.WindowControlNative;

namespace WinForge.Modules.WindowControl;

/// <summary>Owns input interception and gestures; window updates are coalesced outside the hook callback.</summary>
internal sealed class WindowControlEngine : IDisposable
{
    private const int SmartSnapTolerance = 8;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Action<Exception> _report;
    private readonly Action<IntPtr>? _beginZoneDrag;
    private readonly Func<IntPtr, bool, bool>? _endZoneDrag;
    private readonly HashSet<int> _swallowedButtons = new();
    private readonly Dictionary<IntPtr, WindowAppearance> _appearance = new();
    private readonly HashSet<int> _heldHotclicks = new();
    private readonly HashSet<int> _swallowedHotclicks = new();
    private readonly WindowActions _actions;
    private readonly Func<IntPtr, bool, IReadOnlyList<IntPtr>> _candidates;
    private Native.LowLevelMouseProc? _callback;
    private Native.LowLevelKeyboardProc? _keyboardCallback;
    private IntPtr _hook;
    private KeyboardHookThread? _keyboardHook;
    private bool _nativeSpaceDown;
    private WindowControlOptions _options;
    private volatile Gesture? _gesture;
    private POINT _cursor;
    private int _generation;
    private int _dragButton;
    private long _lastWheel;
    private WindowGestureClick? _lastGestureClick;
    private bool _snapSuppressed;
    private bool _spaceDown;

    internal WindowControlEngine(WindowControlOptions options, Action<Exception> report, Func<IntPtr, POINT, bool>? placeInZone = null,
        Func<IntPtr, bool, IReadOnlyList<IntPtr>>? candidates = null, Action<IntPtr>? beginZoneDrag = null,
        Func<IntPtr, bool, bool>? endZoneDrag = null)
    {
        _options = options;
        _report = report;
        _beginZoneDrag = beginZoneDrag;
        _endZoneDrag = endZoneDrag;
        _candidates = candidates ?? WindowActions.Candidates;
        _actions = new WindowActions(report, placeInZone, _candidates);
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(8) };
        _timer.Tick += (_, _) => Guard(UpdateGesture);
    }

    internal bool IsRunning => _hook != IntPtr.Zero;

    internal void Start()
    {
        _dispatcher.VerifyAccess();
        if (IsRunning) return;
        _callback = Hook;
        _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Pencere kontrolü fare kancası kurulamadı.");
        _keyboardCallback = KeyboardHook;
        try { _keyboardHook = new KeyboardHookThread(_keyboardCallback); }
        catch
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            throw;
        }
    }

    internal void Update(WindowControlOptions options)
    {
        _dispatcher.VerifyAccess();
        CancelGesture();
        _heldHotclicks.Clear();
        _lastGestureClick = null;
        _options = options;
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return Native.CallNextHookEx(_hook, code, wParam, lParam);
        try
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & 1) != 0) return Native.CallNextHookEx(_hook, code, wParam, lParam);
            var message = (int)wParam;
            _cursor = info.pt;
            var (button, isDown) = Button(message, info.mouseData);
            if (button != 0 && !isDown && _swallowedHotclicks.Remove(button))
            {
                _heldHotclicks.Remove(button);
                if (_heldHotclicks.Count == 0 && !ActivationPressed(_options.ActivationKey)) Queue(FinishGesture);
                return new IntPtr(1);
            }
            if (TryTitlebarInput(info, message, button, isDown)) return new IntPtr(1);
            if (button != 0 && isDown && IsHotclick(_options.Hotclicks, button))
            {
                _heldHotclicks.Add(button);
                _swallowedHotclicks.Add(button);
                return new IntPtr(1);
            }
            if (button != 0 && !isDown && _swallowedButtons.Remove(button))
            {
                if (_dragButton == button)
                {
                    _dragButton = 0;
                    Queue(FinishGesture);
                }
                return new IntPtr(1);
            }
            if (!ActivationPressed(_options.ActivationKey) && _heldHotclicks.Count == 0) return Native.CallNextHookEx(_hook, code, wParam, lParam);
            var wheel = message is Native.WM_MOUSEWHEEL or HorizontalWheel;
            if ((!isDown || button == 0) && !wheel) return Native.CallNextHookEx(_hook, code, wParam, lParam);
            if (_dragButton != 0)
            {
                if (isDown && button == 2 && _gesture is { Edges: ResizeEdges.None })
                {
                    _swallowedButtons.Add(button);
                    Queue(() => MaximizeWhileMoving());
                    return new IntPtr(1);
                }
                return Native.CallNextHookEx(_hook, code, wParam, lParam);
            }
            var key = InputKey(message, button, wheel);
            if (AlternativePressed(_options.AlternativeKey)) key += "B";
            var action = _options.Actions.GetValueOrDefault(key, WindowAction.None);
            if (action == WindowAction.None) return Native.CallNextHookEx(_hook, code, wParam, lParam);
            var window = InputWindow(info.pt);
            if (!Eligible(window, info.pt)) return Native.CallNextHookEx(_hook, code, wParam, lParam);
            if (action == WindowAction.Resize && !_options.AllowFixedResize && ((long)Native.GetWindowLongPtr(window, WindowStyleIndex) & ResizableStyle) == 0)
                return Native.CallNextHookEx(_hook, code, wParam, lParam);
            QueueInput(window, action, info, button, wheel, ReadModifiers());
            return new IntPtr(1);
        }
        catch (Exception exception) { Queue(() => _report(exception)); }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static string InputKey(int message, int button, bool wheel) => wheel ? message == HorizontalWheel ? "HScroll" : "Scroll" : button switch
    { 1 => "LMB", 2 => "RMB", 3 => "MMB", 4 => "MB4", _ => "MB5" };

    private IntPtr InputWindow(POINT point)
    {
        var hit = Native.WindowFromPoint(point);
        var window = Native.GetAncestor(hit, Native.GA_ROOT);
        if (_options.SupportMdi)
            for (var child = hit; child != IntPtr.Zero && child != window; child = GetParent(child))
                if (((long)Native.GetWindowLongPtr(child, Native.GWL_EXSTYLE) & MdiChildStyle) != 0) { window = child; break; }
        return window;
    }

    internal void QueueInput(IntPtr window, WindowAction action, MSLLHOOKSTRUCT info, int button, bool wheel, WindowGestureModifiers modifiers)
    {
        if (wheel)
        {
            var delta = (short)(info.mouseData >> 16);
            if (delta == 0 || Environment.TickCount64 - _lastWheel < 30) return;
            _lastWheel = Environment.TickCount64;
            Queue(() => Wheel(window, action, Math.Sign(delta), info.pt, modifiers));
            return;
        }
        _swallowedButtons.Add(button);
        if (action is WindowAction.Move or WindowAction.Resize) _dragButton = button;
        var timestamp = Environment.TickCount64;
        Queue(() => Press(window, action, info.pt, button, modifiers, timestamp));
    }

    private bool TryTitlebarInput(MSLLHOOKSTRUCT info, int message, int button, bool isDown)
    {
        var wheel = message is Native.WM_MOUSEWHEEL or HorizontalWheel;
        if (_dragButton != 0 || (!isDown && !wheel) || _options.TitlebarMode == "Kapalı") return false;
        var window = InputWindow(info.pt);
        var action = TitlebarAction(window, info.pt, InputKey(message, button, wheel),
            ActivationPressed(_options.ActivationKey) || _heldHotclicks.Count != 0, AlternativePressed(_options.AlternativeKey));
        if (action == WindowAction.None || !Eligible(window, info.pt)) return false;
        if (action == WindowAction.Resize && !_options.AllowFixedResize && ((long)Native.GetWindowLongPtr(window, WindowStyleIndex) & ResizableStyle) == 0) return false;
        QueueInput(window, action, info, button, wheel, ReadModifiers());
        return true;
    }

    internal WindowAction TitlebarAction(IntPtr window, POINT point, string input, bool activated, bool alternate)
    {
        var enabled = _options.TitlebarMode switch
        {
            "Her zaman" => true,
            "Etkinleştirme tuşuyla" => activated,
            "Etkinleştirme tuşu olmadan" => !activated,
            _ => false
        };
        if (!enabled) return WindowAction.None;
        var action = _options.Actions.GetValueOrDefault(input + "T" + (alternate ? "B" : ""), WindowAction.None);
        if (action == WindowAction.None) return action;
        var coordinate = new IntPtr(unchecked((int)((uint)(ushort)point.Y << 16 | (ushort)point.X)));
        return SendWindowMessageTimeout(window, 0x0084, IntPtr.Zero, coordinate, Native.SMTO_ABORTIFHUNG, 20, out var hit) != IntPtr.Zero && hit == new IntPtr(2)
            ? action : WindowAction.None;
    }

    internal bool MaximizeWhileMoving()
    {
        if (_gesture is not { Edges: ResizeEdges.None } gesture || !SameWindow(gesture.Window, gesture.ProcessId)) return false;
        _dragButton = 0;
        FinishGesture(ReadModifiers() with { SuppressSnap = true });
        Execute(gesture.Window, WindowAction.ToggleMaximize, _cursor);
        return true;
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (info.vkCode == 0x20)
                {
                    var down = (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                    if ((info.flags & 0x10) == 0 && (down ? _gesture is not null || _nativeSpaceDown : _nativeSpaceDown))
                    {
                        _nativeSpaceDown = down;
                        Queue(() => HandleSnapKey(down, false));
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception exception) { Queue(() => _report(exception)); }
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    internal bool HandleSnapKey(bool down, bool injected)
    {
        if (injected) return false;
        if (_gesture is not null && down)
        {
            if (!_spaceDown) _snapSuppressed = !_snapSuppressed;
            _spaceDown = true;
            Queue(UpdateGesture);
            return true;
        }
        if (!down && _spaceDown)
        {
            _spaceDown = false;
            return true;
        }
        return false;
    }

    private WindowGestureModifiers ReadModifiers() => new(KeyDown(0x10), KeyDown(0x11) && !KeyDown(0xA5), _snapSuppressed);

    internal void Press(IntPtr window, WindowAction action, POINT point, int button, WindowGestureModifiers modifiers, long timestamp)
    {
        if (action is not (WindowAction.Move or WindowAction.Resize))
        {
            _lastGestureClick = null;
            Execute(window, action, point, modifiers);
            return;
        }
        var click = new WindowGestureClick(window, action, button, point, timestamp);
        var previous = _lastGestureClick;
        _lastGestureClick = click;
        if (previous is { } first && first.Matches(click, GetDoubleClickTime(),
            Native.GetSystemMetrics(DoubleClickWidthMetric), Native.GetSystemMetrics(DoubleClickHeightMetric)))
        {
            _lastGestureClick = null;
            _dragButton = 0;
            FinishGesture(modifiers);
            if (action == WindowAction.Move)
                Execute(window, modifiers.Shift ? WindowAction.Roll : modifiers.Control ? WindowAction.Minimize : WindowAction.ToggleMaximize, point, modifiers);
            else DoubleResize(window, point, modifiers);
            return;
        }
        Execute(window, action, point);
        if (modifiers.Control) WindowActions.Activate(window);
    }

    private void DoubleResize(IntPtr window, POINT point, WindowGestureModifiers modifiers)
    {
        if (!Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = WindowBounds.From(rectangle);
        var edges = WindowGeometry.ResizeRegion(bounds, point, _options.CenterFraction, "Taşı");
        if (edges == ResizeEdges.None && modifiers.Control)
        {
            Execute(window, WindowAction.ToggleMaximize, point);
            return;
        }
        if (IsZoomed(window)) ShowWindowAsync(window, Restore);
        var work = WindowActions.WorkArea(window, point);
        var target = WindowGeometry.DoubleResizeTarget(bounds, work, edges, _options.HorizontalSplit, _options.VerticalSplit, _options.Gap, modifiers.Shift);
        _actions.RememberSnap(window, bounds);
        Place(window, target);
    }

    private void Queue(Action action)
    {
        var generation = _generation;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsRunning && generation == _generation) Guard(action);
        });
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) { CancelGesture(); _report(exception); }
    }

    private bool Eligible(IntPtr window, POINT point)
    {
        if (window == IntPtr.Zero || !Native.IsWindowVisible(window) || Native.IsIconic(window)) return false;
        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId || processId == 0) return false;
        var className = Text(window, true);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return false;
        if (!Native.GetWindowRect(window, out var rectangle)) return false;
        var bounds = WindowBounds.From(rectangle);
        var monitor = Monitor(point);
        if (!_options.AllowFullscreen && !IsZoomed(window) && bounds.X <= monitor.Monitor.Left && bounds.Y <= monitor.Monitor.Top &&
            bounds.Right >= monitor.Monitor.Right && bounds.Bottom >= monitor.Monitor.Bottom) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return !WindowGeometry.IsExcluded(_options.ExcludedProcesses, _options.ExcludedWindows,
                process.ProcessName + ".exe", Text(window, false), className);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }

    internal void Execute(IntPtr window, WindowAction action, POINT point, WindowGestureModifiers? modifiers = null)
    {
        if (!Native.IsWindow(window)) return;
        var effectiveModifiers = modifiers ?? ReadModifiers();
        if (_actions.Execute(window, action, point, _options, modifiers: effectiveModifiers)) return;
        switch (action)
        {
            case WindowAction.Move: BeginGesture(window, point, resize: false); break;
            case WindowAction.Resize: BeginGesture(window, point, resize: true); break;
            case WindowAction.Minimize: ShowWindowAsync(window, Minimize); break;
            case WindowAction.ToggleMaximize: ShowWindowAsync(window, effectiveModifiers.Shift ? Minimize : IsZoomed(window) ? Restore : Maximize); break;
            case WindowAction.Close: Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
            case WindowAction.ToggleTopmost:
                var topmost = ((long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
                Native.SetWindowPos(window, topmost ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | AsyncWindowPosition);
                break;
            case WindowAction.Lower:
                if (effectiveModifiers.Control) { Execute(window, WindowAction.ToggleTopmost, point, effectiveModifiers); break; }
                if (effectiveModifiers.Shift) { ShowWindowAsync(window, Minimize); break; }
                Native.SetWindowPos(window, new IntPtr(1), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | AsyncWindowPosition);
                break;
            case WindowAction.SystemMenu:
                var command = WindowActions.ShowPopup(GetSystemMenu(window, false), point, window);
                if (command != 0) Native.PostMessage(window, SystemCommand, new IntPtr(command), IntPtr.Zero);
                break;
            case WindowAction.ActionMenu: ShowActionMenu(window, point, effectiveModifiers); break;
            case WindowAction.ToggleBorder: ToggleBorder(window, effectiveModifiers.Shift); break;
        }
    }

    internal WindowAction ShowActionMenu(IntPtr window, POINT point, WindowGestureModifiers modifiers)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var entries = WindowControlModule.ActionMenuEntries;
            for (var index = 0; index < entries.Count; index++)
                if (!AppendMenu(menu, MenuString, (nuint)(index + 1), entries[index].Key))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var selected = WindowAction.None;
            WindowActions.ShowPopup(menu, point, window, command =>
            {
                if (command > entries.Count) return;
                selected = entries[(int)command - 1].Value;
                Execute(window, selected, point, modifiers);
            });
            return selected;
        }
        finally { DestroyMenu(menu); }
    }

    private void BeginGesture(IntPtr window, POINT point, bool resize)
    {
        if (!Native.GetWindowRect(window, out var rect) || (resize && IsZoomed(window))) return;
        var bounds = WindowBounds.From(rect);
        var initialBounds = bounds;
        var wasMaximized = IsZoomed(window);
        var snapNormal = !resize ? _actions.SnapNormalBounds(window) : null;
        if (wasMaximized)
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(window, ref placement)) return;
            var normal = snapNormal ?? WindowBounds.From(placement.NormalPosition);
            if (normal.Width < 1 || normal.Height < 1) return;
            bounds = WindowGeometry.RestoreUnderPointer(bounds, normal, point);
        }
        else if (snapNormal is { } snappedNormal) bounds = WindowGeometry.RestoreUnderPointer(bounds, snappedNormal, point);
        var minMax = new MinMaxInfo();
        SendMessageTimeout(window, GetMinMaxInfo, IntPtr.Zero, ref minMax, Native.SMTO_ABORTIFHUNG, 20, out _);
        var minWidth = Math.Max(64, minMax.MinTrackSize.X);
        var minHeight = Math.Max(48, minMax.MinTrackSize.Y);
        var maxWidth = minMax.MaxTrackSize.X > 0 ? Math.Max(minWidth, minMax.MaxTrackSize.X) : int.MaxValue;
        var maxHeight = minMax.MaxTrackSize.Y > 0 ? Math.Max(minHeight, minMax.MaxTrackSize.Y) : int.MaxValue;
        var targets = new List<WindowBounds>();
        var neighbors = new List<Neighbor>();
        foreach (var handle in _candidates(window, false))
        {
            if (handle != window && Native.IsWindowVisible(handle) && !Native.IsIconic(handle) &&
                Native.GetWindowRect(handle, out var other))
            {
                var target = WindowBounds.From(other);
                if (target.Width > 0 && target.Height > 0)
                {
                    targets.Add(target);
                    if (_options.StickyResize != "Kapalı" && !IsZoomed(handle) && ((long)Native.GetWindowLongPtr(handle, WindowStyleIndex) & ResizableStyle) != 0)
                    {
                        Native.GetWindowThreadProcessId(handle, out var neighborProcess);
                        var limits = new MinMaxInfo();
                        SendMessageTimeout(handle, GetMinMaxInfo, IntPtr.Zero, ref limits, Native.SMTO_ABORTIFHUNG, 20, out _);
                        neighbors.Add(new(handle, neighborProcess, target, Math.Max(64, limits.MinTrackSize.X), Math.Max(48, limits.MinTrackSize.Y),
                            limits.MaxTrackSize.X > 0 ? limits.MaxTrackSize.X : int.MaxValue,
                            limits.MaxTrackSize.Y > 0 ? limits.MaxTrackSize.Y : int.MaxValue));
                    }
                }
            }
        }
        Native.GetWindowThreadProcessId(window, out var processId);
        var edges = resize ? WindowGeometry.ResizeRegion(bounds, point, _options.CenterFraction, _options.ResizeCenter) : ResizeEdges.None;
        var opacity = edges == ResizeEdges.None && _options.MoveOpacity < 255 ? TemporaryOpacity.Create(window, (byte)_options.MoveOpacity) : null;
        _snapSuppressed = false;
        _gesture = new Gesture(window, processId, bounds, point, edges,
            minWidth, minHeight, maxWidth, maxHeight, wasMaximized, targets, neighbors, opacity, WindowActions.WorkArea(window, point), initialBounds, snapNormal is not null);
        if (_options.AutoFocus || ReadModifiers().Control) WindowActions.Activate(window);
        _timer.Start();
    }

    private void UpdateGesture() => UpdateGesture(ReadModifiers());

    private void UpdateGesture(WindowGestureModifiers modifiers)
    {
        if (_gesture is not { } gesture) return;
        if (!SameWindow(gesture.Window, gesture.ProcessId) || KeyDown(0x1B)) { CancelGesture(); return; }
        var moved = Math.Abs(_cursor.X - gesture.Start.X) > Native.GetSystemMetrics(DoubleClickWidthMetric) ||
            Math.Abs(_cursor.Y - gesture.Start.Y) > Native.GetSystemMetrics(DoubleClickHeightMetric);
        if (moved) _lastGestureClick = null;
        if ((gesture.WasMaximized || gesture.HadSnapRestore) && !gesture.DragStarted)
        {
            if (!moved) return;
            if (gesture.WasMaximized) ShowWindowAsync(gesture.Window, Restore);
            Place(gesture.Window, gesture.Original);
        }
        if (_cursor.X != gesture.Start.X || _cursor.Y != gesture.Start.Y) gesture.DragStarted = true;
        if (gesture.DragStarted && !gesture.ZoneDragStarted && _beginZoneDrag is not null)
        {
            gesture.ZoneDragStarted = true;
            _beginZoneDrag(gesture.Window);
        }
        var bounds = WindowGeometry.Drag(gesture.Original, gesture.Start, _cursor, gesture.Edges,
            gesture.MinWidth, gesture.MinHeight, gesture.MaxWidth, gesture.MaxHeight);
        if (gesture.Edges == ResizeEdges.None && !modifiers.SuppressSnap && (_options.MagnetMode != "Kapalı" || modifiers.Shift))
        {
            var work = modifiers.Control || modifiers.Shift ? gesture.OriginWork : WindowActions.WorkArea(gesture.Window, _cursor);
            if (_options.MagnetMode is "Pencerelerin dış kenarları" or "İç ve dış kenarlar" || modifiers.Shift)
                bounds = WindowGeometry.Magnet(bounds, gesture.Targets, _options.MagnetDistance, _options.Gap,
                    includeInner: _options.MagnetMode == "İç ve dış kenarlar");
            bounds = WindowGeometry.Magnet(bounds, [work], _options.MagnetDistance, 0, includeInner: true, includeOuter: false);
        }
        if (gesture.Edges == ResizeEdges.None && (modifiers.Control || modifiers.Shift)) bounds = WindowGeometry.Constrain(bounds, gesture.OriginWork);
        var shift = modifiers.Shift;
        var sticky = _options.StickyResize switch { "Her zaman" => true, "Shift ile" => shift, "Shift ile alternatif" => !shift, _ => false };
        if (gesture.Edges != ResizeEdges.None && sticky && !UpdateNeighbors(gesture, bounds)) return;
        if (bounds != gesture.Last)
        {
            Place(gesture.Window, bounds);
            gesture.Last = bounds;
        }
    }

    internal void UpdateGesture(POINT point)
    {
        _cursor = point;
        UpdateGesture();
    }

    internal void UpdateGesture(POINT point, WindowGestureModifiers modifiers)
    {
        _cursor = point;
        UpdateGesture(modifiers);
    }

    internal void FinishGesture() => FinishGesture(ReadModifiers());

    internal void FinishGesture(WindowGestureModifiers modifiers)
    {
        if (_gesture is not { } gesture) return;
        UpdateGesture(modifiers);
        if (_gesture is null) return;
        var zoneHandled = gesture.ZoneDragStarted && _endZoneDrag?.Invoke(gesture.Window, false) == true;
        var snapped = false;
        if (!zoneHandled && gesture.DragStarted && gesture.Edges == ResizeEdges.None && _options.EdgeSnap && !modifiers.SuppressSnap)
        {
            var confined = modifiers.Control || modifiers.Shift;
            var work = confined ? gesture.OriginWork : WindowActions.WorkArea(gesture.Window, _cursor);
            var cursor = confined ? new POINT(Math.Clamp(_cursor.X, work.X, work.Right - 1), Math.Clamp(_cursor.Y, work.Y, work.Bottom - 1)) : _cursor;
            var target = WindowGeometry.EdgeTarget(work, cursor, _options.EdgeDistance, _options.HorizontalSplit, _options.VerticalSplit, _options.Gap);
            if (_options.SmartSnap && target is { } candidate && candidate != work)
                target = SmartTarget(work, candidate, gesture.Targets, cursor, _options.Gap);
            if (target is { } bounds && bounds.Width >= gesture.MinWidth && bounds.Height >= gesture.MinHeight &&
                bounds.Width <= gesture.MaxWidth && bounds.Height <= gesture.MaxHeight)
            {
                _actions.RememberSnap(gesture.Window, gesture.Original);
                if (bounds == work) ShowWindowAsync(gesture.Window, Maximize);
                else Place(gesture.Window, bounds);
                snapped = true;
            }
        }
        if (gesture.HadSnapRestore && gesture.DragStarted && !snapped) _actions.ForgetSnap(gesture.Window);
        gesture.Opacity?.Dispose();
        _snapSuppressed = false;
        _gesture = null;
        _timer.Stop();
    }

    private void CancelGesture()
    {
        if (_gesture is { } gesture && SameWindow(gesture.Window, gesture.ProcessId))
        {
            if (!(gesture.WasMaximized || gesture.HadSnapRestore) || gesture.DragStarted)
            {
                Place(gesture.Window, gesture.HadSnapRestore && !gesture.WasMaximized ? gesture.InitialBounds : gesture.Original);
                if (gesture.WasMaximized) ShowWindowAsync(gesture.Window, Maximize);
            }
            foreach (var neighbor in gesture.Neighbors)
                if (gesture.ChangedNeighbors.Contains(neighbor.Window) && SameWindow(neighbor.Window, neighbor.ProcessId)) Place(neighbor.Window, neighbor.Original);
        }
        if (_gesture is { ZoneDragStarted: true } canceledGesture)
            _ = _endZoneDrag?.Invoke(canceledGesture.Window, true);
        _gesture?.Opacity?.Dispose();
        _snapSuppressed = false;
        _gesture = null;
        _timer.Stop();
    }

    internal void Wheel(IntPtr window, WindowAction action, int direction, POINT point, WindowGestureModifiers? modifiers = null)
    {
        if (!Native.IsWindow(window)) return;
        var effectiveModifiers = modifiers ?? ReadModifiers();
        if (_actions.Execute(window, action, point, _options, direction, effectiveModifiers)) return;
        if (action != WindowAction.Opacity) return;
        var extendedStyle = (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
        var hasAlpha = GetLayeredWindowAttributes(window, out _, out var existing, out _);
        // Per-pixel layered windows own their rendering; changing their alpha API can break that surface.
        if ((extendedStyle & LayeredStyle) != 0 && !hasAlpha) return;
        var original = RememberAppearance(window);
        original.OpacityChanged = true;
        var alpha = hasAlpha ? existing : (byte)255;
        alpha = (byte)Math.Clamp(alpha + direction * (effectiveModifiers.Shift ? _options.AlphaDeltaShift : _options.AlphaDelta), _options.MinimumAlpha, 255);
        Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr(extendedStyle | LayeredStyle));
        SetLayeredWindowAttributes(window, 0, alpha, 2);
        if (alpha == 255 && (original.ExtendedStyle & LayeredStyle) == 0)
            Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr(extendedStyle & ~LayeredStyle));
    }

    private void ToggleBorder(IntPtr window, bool captionOnly)
    {
        var original = RememberAppearance(window);
        original.BorderChanged = true;
        var current = (long)Native.GetWindowLongPtr(window, WindowStyleIndex);
        var mask = captionOnly ? CaptionStyle : BorderStyles;
        Native.SetWindowLongPtr(window, WindowStyleIndex, new IntPtr((current & mask) != 0
            ? current & ~mask : current | (original.Style & mask)));
        Native.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOMOVE | NoZOrder | FrameChanged | Native.SWP_NOACTIVATE);
    }

    private WindowAppearance RememberAppearance(IntPtr window)
    {
        Native.GetWindowThreadProcessId(window, out var processId);
        if (_appearance.TryGetValue(window, out var saved) && saved.ProcessId == processId) return saved;
        GetLayeredWindowAttributes(window, out var color, out var alpha, out var flags);
        saved = new WindowAppearance(processId, (long)Native.GetWindowLongPtr(window, WindowStyleIndex),
            (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE), color, alpha, flags);
        _appearance[window] = saved;
        return saved;
    }

    private static bool SameWindow(IntPtr window, uint processId) => Native.IsWindow(window) &&
        Native.GetWindowThreadProcessId(window, out var current) != 0 && current == processId;
    private static bool KeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool ActivationPressed(string key) => key switch
    {
        "Sol Alt" => KeyDown(0xA4) && !KeyDown(0xA5),
        "Sağ Alt" => KeyDown(0xA5), "Her iki Alt" => KeyDown(0xA4) || KeyDown(0xA5),
        "Sol Windows" => KeyDown(0x5B), "Her iki Windows" => KeyDown(0x5B) || KeyDown(0x5C),
        "Sol Ctrl" => KeyDown(0xA2) && !KeyDown(0xA5), _ => false
    };
    private static bool AlternativePressed(string key) => key switch
    { "Sol Windows" => KeyDown(0x5B), "Sol Ctrl" => KeyDown(0xA2), "Sağ Ctrl" => KeyDown(0xA3), _ => false };
    private static (int Button, bool Down) Button(int message, uint data) => message switch
    {
        Native.WM_LBUTTONDOWN => (1, true), Native.WM_LBUTTONUP => (1, false),
        Native.WM_RBUTTONDOWN => (2, true), Native.WM_RBUTTONUP => (2, false),
        Native.WM_MBUTTONDOWN => (3, true), Native.WM_MBUTTONUP => (3, false),
        Native.WM_XBUTTONDOWN => ((data >> 16) == 1 ? 4 : 5, true), XButtonUp => ((data >> 16) == 1 ? 4 : 5, false), _ => (0, false)
    };
    private static string Text(IntPtr window, bool className)
    {
        var text = new StringBuilder(512);
        if (className) Native.GetClassName(window, text, text.Capacity);
        else Native.GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
    private static MonitorInfo Monitor(POINT point)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(point, 2), ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }
    private static void Place(IntPtr window, WindowBounds bounds) => WindowActions.Place(window, bounds);

    internal static bool IsHotclick(string hotclicks, int button)
    {
        var token = button switch { 3 => "04", 4 => "05", 5 => "06", _ => "" };
        return token.Length != 0 && hotclicks.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private bool UpdateNeighbors(Gesture gesture, WindowBounds bounds)
    {
        var changes = new List<(Neighbor Neighbor, WindowBounds Bounds)>();
        foreach (var neighbor in gesture.Neighbors)
        {
            if (!SameWindow(neighbor.Window, neighbor.ProcessId)) continue;
            var resized = WindowGeometry.StickyNeighbor(gesture.Original, bounds, neighbor.Original, _options.Gap, _options.MagnetDistance);
            if (resized is not { } result) continue;
            if (result.Width < neighbor.MinWidth || result.Height < neighbor.MinHeight || result.Width > neighbor.MaxWidth || result.Height > neighbor.MaxHeight)
                return false;
            changes.Add((neighbor, result));
        }
        foreach (var (neighbor, result) in changes)
        {
            gesture.ChangedNeighbors.Add(neighbor.Window);
            Place(neighbor.Window, result);
        }
        return true;
    }

    internal static WindowBounds SmartTarget(WindowBounds work, WindowBounds target, IEnumerable<WindowBounds> neighbors, POINT cursor, int gap = 0)
    {
        // A full-height window at the opposite edge defines the actual free split boundary.
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Y > work.Y + SmartSnapTolerance || neighbor.Bottom < work.Bottom - SmartSnapTolerance) continue;
            if (cursor.X < work.X + work.Width / 2 && Math.Abs(neighbor.Right - work.Right) <= SmartSnapTolerance && neighbor.X > work.X)
                return target with { Width = Math.Max(1, neighbor.X - gap - target.X) };
            if (cursor.X >= work.X + work.Width / 2 && Math.Abs(neighbor.X - work.X) <= SmartSnapTolerance && neighbor.Right < work.Right)
                return target with { X = neighbor.Right + gap, Width = Math.Max(1, target.Right - neighbor.Right - gap) };
        }
        return target;
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        _generation++;
        CancelGesture();
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _callback = null;
        _keyboardCallback = null;
        _swallowedButtons.Clear();
        _heldHotclicks.Clear();
        _swallowedHotclicks.Clear();
        _dragButton = 0;
        _lastGestureClick = null;
        _spaceDown = false;
        _actions.Dispose();
        foreach (var (window, original) in _appearance)
        {
            if (!SameWindow(window, original.ProcessId)) continue;
            if (original.BorderChanged)
            {
                var currentStyle = (long)Native.GetWindowLongPtr(window, WindowStyleIndex);
                Native.SetWindowLongPtr(window, WindowStyleIndex, new IntPtr((currentStyle & ~BorderStyles) | (original.Style & BorderStyles)));
            }
            if (original.OpacityChanged)
            {
                var currentStyle = (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
                Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr((currentStyle & ~LayeredStyle) | (original.ExtendedStyle & LayeredStyle)));
                if (original.Flags != 0) SetLayeredWindowAttributes(window, original.Color, original.Alpha, original.Flags);
            }
            Native.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | NoZOrder | FrameChanged);
        }
        _appearance.Clear();
    }

    private sealed class Gesture(IntPtr window, uint processId, WindowBounds original, POINT start, ResizeEdges edges,
        int minWidth, int minHeight, int maxWidth, int maxHeight, bool wasMaximized, IReadOnlyList<WindowBounds> targets,
        IReadOnlyList<Neighbor> neighbors, TemporaryOpacity? opacity, WindowBounds originWork, WindowBounds initialBounds, bool hadSnapRestore)
    {
        internal IntPtr Window { get; } = window;
        internal uint ProcessId { get; } = processId;
        internal WindowBounds Original { get; } = original;
        internal POINT Start { get; } = start;
        internal ResizeEdges Edges { get; } = edges;
        internal int MinWidth { get; } = minWidth;
        internal int MinHeight { get; } = minHeight;
        internal int MaxWidth { get; } = maxWidth;
        internal int MaxHeight { get; } = maxHeight;
        internal bool WasMaximized { get; } = wasMaximized;
        internal IReadOnlyList<WindowBounds> Targets { get; } = targets;
        internal IReadOnlyList<Neighbor> Neighbors { get; } = neighbors;
        internal HashSet<IntPtr> ChangedNeighbors { get; } = new();
        internal TemporaryOpacity? Opacity { get; } = opacity;
        internal WindowBounds OriginWork { get; } = originWork;
        internal bool DragStarted { get; set; }
        internal bool ZoneDragStarted { get; set; }
        internal WindowBounds InitialBounds { get; } = initialBounds;
        internal bool HadSnapRestore { get; } = hadSnapRestore;
        internal WindowBounds Last { get; set; } = original;
    }
    private sealed record Neighbor(IntPtr Window, uint ProcessId, WindowBounds Original, int MinWidth, int MinHeight, int MaxWidth, int MaxHeight);

    private sealed class TemporaryOpacity(IntPtr window, uint processId, long style, uint color, byte alpha, uint flags) : IDisposable
    {
        internal static TemporaryOpacity? Create(IntPtr window, byte opacity)
        {
            var style = (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
            var hasAlpha = GetLayeredWindowAttributes(window, out var color, out var alpha, out var flags);
            if ((style & LayeredStyle) != 0 && !hasAlpha) return null;
            Native.GetWindowThreadProcessId(window, out var processId);
            Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr(style | LayeredStyle));
            if (!SetLayeredWindowAttributes(window, 0, opacity, 2))
            {
                Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr(style));
                return null;
            }
            return new(window, processId, style, color, hasAlpha ? alpha : (byte)255, hasAlpha ? flags : 0);
        }
        public void Dispose()
        {
            if (!SameWindow(window, processId)) return;
            var current = (long)Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(window, Native.GWL_EXSTYLE, new IntPtr((current & ~LayeredStyle) | (style & LayeredStyle)));
            if (flags != 0) SetLayeredWindowAttributes(window, color, alpha, flags);
        }
    }
    private sealed record WindowAppearance(uint ProcessId, long Style, long ExtendedStyle, uint Color, byte Alpha, uint Flags)
    {
        internal bool BorderChanged { get; set; }
        internal bool OpacityChanged { get; set; }
    }
}
