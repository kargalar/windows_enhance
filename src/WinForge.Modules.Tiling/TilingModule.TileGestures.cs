using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public partial class TilingModule
{
    private readonly Dictionary<(IntPtr Monitor, int Layout, int Count), RatioRect[]> _tileRatios = new();
    private readonly List<TileLayout> _tileLayouts = new();
    private readonly List<RECT> _recordedBounds = new();
    private Native.LowLevelMouseProc? _tileMouseCallback;
    private IntPtr _tileMouseHook;
    private DispatcherTimer? _tileGestureTimer;
    private TileGesture? _tileGesture;
    private POINT _tileCursor;
    private TiledResizeGesture? _tiledResizeGesture;
    private sealed record TiledResizeGesture(TileLayout Layout, int Index, POINT Start, int? VerticalEdge, int? HorizontalEdge)
    {
        public RECT[]? CurrentBounds { get; set; }
    }

    private readonly record struct RatioRect(double Left, double Top, double Right, double Bottom);
    private sealed record TileLayout((IntPtr Monitor, int Layout, int Count) Key, RECT Work, IntPtr[] Handles, RECT[] Bounds);
    private sealed record TileGesture(TileLayout Layout, int Index, POINT Start, bool Vertical, int Edge, bool Resize, RECT WindowBounds);

    private static RatioRect ToRatio(RECT rect, RECT work) => new(
        (double)(rect.Left - work.Left) / (work.Right - work.Left),
        (double)(rect.Top - work.Top) / (work.Bottom - work.Top),
        (double)(rect.Right - work.Left) / (work.Right - work.Left),
        (double)(rect.Bottom - work.Top) / (work.Bottom - work.Top));

    private static RECT FromRatio(RatioRect rect, RECT work) => new()
    {
        Left = work.Left + (int)Math.Round(rect.Left * (work.Right - work.Left)),
        Top = work.Top + (int)Math.Round(rect.Top * (work.Bottom - work.Top)),
        Right = work.Left + (int)Math.Round(rect.Right * (work.Right - work.Left)),
        Bottom = work.Top + (int)Math.Round(rect.Bottom * (work.Bottom - work.Top))
    };

    private void StartTileMouseHook()
    {
        _tileGestureTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _tileGestureTimer.Tick += (_, _) => UpdateTileGesture();
        _tileMouseCallback = TileMouseHook;
        _tileMouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _tileMouseCallback, Native.GetModuleHandle(null), 0);
        if (_tileMouseHook == IntPtr.Zero) _context?.Log.Error("Döşeme fare kancası kurulamadı.");
    }

    private void StopTileMouseHook()
    {
        _tileGestureTimer?.Stop();
        _tileGestureTimer = null;
        if (_tileMouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_tileMouseHook);
        _tileMouseHook = IntPtr.Zero;
        _tileGesture = null;
        _tiledResizeGesture = null;
        _tileLayouts.Clear();
        _tileRatios.Clear();
    }

    private IntPtr TileMouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return Native.CallNextHookEx(_tileMouseHook, code, message, data);
        var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
        if ((mouse.flags & 1) != 0) return Native.CallNextHookEx(_tileMouseHook, code, message, data);
        var kind = message.ToInt32();
        _tileCursor = mouse.pt;
        if (kind == 0x0201 && (GetAsyncKeyState(0x12) & 0x8000) != 0 &&
            (GetAsyncKeyState(0x09) & 0x8000) == 0 && (GetAsyncKeyState(0x73) & 0x8000) == 0)
        {
            lock (_autoTileLock)
            {
                foreach (var layout in _tileLayouts)
                {
                    var index = Array.FindIndex(layout.Bounds, r => mouse.pt.X >= r.Left && mouse.pt.X < r.Right && mouse.pt.Y >= r.Top && mouse.pt.Y < r.Bottom);
                    if (index < 0 || !Native.GetWindowRect(layout.Handles[index], out var windowBounds)) continue;
                    _tileGesture = new TileGesture(layout, index, mouse.pt, false, 0, false, windowBounds);
                    _dragging = true;
                    _tileGestureTimer?.Start();
                    return new IntPtr(1);
                }
            }
        }
        if (kind == 0x0202 && _tileGesture != null)
        {
            FinishTileGesture();
            return new IntPtr(1);
        }
        return Native.CallNextHookEx(_tileMouseHook, code, message, data);
    }

    public bool BeginTiledResize(IntPtr window, POINT start, int edges)
    {
        if (!IsActive || _mode != AutoTileMode) return false;
        lock (_autoTileLock)
        {
            if (_dragging) return false;
            foreach (var layout in _tileLayouts)
            {
                var index = Array.IndexOf(layout.Handles, window);
                if (index < 0) continue;
                var rect = layout.Bounds[index];
                int? vertical = (edges & 1) != 0 && (edges & 4) != 0
                    ? (HasSharedEdge(layout.Bounds, index, true, rect.Left - _gap, _gap) &&
                       (!HasSharedEdge(layout.Bounds, index, true, rect.Right, _gap) || start.X - rect.Left <= rect.Right - start.X) ? rect.Left - _gap : rect.Right)
                    : (edges & 1) != 0 ? rect.Left - _gap : (edges & 4) != 0 ? rect.Right : null;
                int? horizontal = (edges & 2) != 0 && (edges & 8) != 0
                    ? (HasSharedEdge(layout.Bounds, index, false, rect.Top - _gap, _gap) &&
                       (!HasSharedEdge(layout.Bounds, index, false, rect.Bottom, _gap) || start.Y - rect.Top <= rect.Bottom - start.Y) ? rect.Top - _gap : rect.Bottom)
                    : (edges & 2) != 0 ? rect.Top - _gap : (edges & 8) != 0 ? rect.Bottom : null;
                if (vertical.HasValue && !HasSharedEdge(layout.Bounds, index, true, vertical.Value, _gap)) vertical = null;
                if (horizontal.HasValue && !HasSharedEdge(layout.Bounds, index, false, horizontal.Value, _gap)) horizontal = null;
                if (!vertical.HasValue && !horizontal.HasValue) return false;
                _tiledResizeGesture = new(layout, index, start, vertical, horizontal);
                _dragging = true;
                return true;
            }
            return false;
        }
    }

    private static bool HasSharedEdge(RECT[] bounds, int index, bool vertical, int edge, int gap)
    {
        var a = bounds[index];
        return bounds.Where((_, i) => i != index).Any(b => vertical
            ? b.Top < a.Bottom && b.Bottom > a.Top && ((a.Right == edge && b.Left == edge + gap) || (a.Left == edge + gap && b.Right == edge))
            : b.Left < a.Right && b.Right > a.Left && ((a.Bottom == edge && b.Top == edge + gap) || (a.Top == edge + gap && b.Bottom == edge)));
    }

    public void UpdateTiledResize(POINT cursor)
    {
        lock (_autoTileLock)
        {
            var gesture = _tiledResizeGesture;
            if (gesture is null) return;
            var bounds = gesture.Layout.Bounds;
            if (gesture.VerticalEdge is int vertical)
                bounds = ResizeSharedEdge(bounds, gesture.Index, true, vertical, cursor.X - gesture.Start.X, _gap);
            if (gesture.HorizontalEdge is int horizontal)
                bounds = ResizeSharedEdge(bounds, gesture.Index, false, horizontal, cursor.Y - gesture.Start.Y, _gap);
            gesture.CurrentBounds = bounds;
            for (var i = 0; i < bounds.Length; i++) Move(gesture.Layout.Handles[i], bounds[i]);
        }
    }

    public void EndTiledResize(bool canceled)
    {
        lock (_autoTileLock)
        {
            var gesture = _tiledResizeGesture;
            if (gesture is null) return;
            if (!canceled && gesture.CurrentBounds is { } bounds)
                _tileRatios[gesture.Layout.Key] = bounds.Select(r => ToRatio(r, gesture.Layout.Work)).ToArray();
            _tiledResizeGesture = null;
            _dragging = false;
        }
        Interlocked.Exchange(ref _pulses, 5);
        Restart(1);
    }

    private void UpdateTileGesture()
    {
        var gesture = _tileGesture;
        if (gesture == null) return;
        lock (_autoTileLock)
        {
            var rect = gesture.WindowBounds;
            Native.SetWindowPos(gesture.Layout.Handles[gesture.Index], IntPtr.Zero,
                rect.Left + _tileCursor.X - gesture.Start.X, rect.Top + _tileCursor.Y - gesture.Start.Y,
                0, 0, SWP_NOZORDER | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    private static RECT[] ResizeSharedEdge(RECT[] original, int index, bool vertical, int edge, int delta, int gap)
    {
        var bounds = (RECT[])original.Clone();
        var before = new List<int>();
        var after = new List<int>();
        var connected = new HashSet<int> { index };
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < original.Length; i++)
            {
                if (connected.Contains(i)) continue;
                var r = original[i];
                var onEdge = (vertical ? r.Right : r.Bottom) == edge || (vertical ? r.Left : r.Top) == edge + gap;
                if (!onEdge) continue;
                if (!connected.Any(j => vertical
                    ? r.Top < original[j].Bottom && r.Bottom > original[j].Top
                    : r.Left < original[j].Right && r.Right > original[j].Left)) continue;
                connected.Add(i);
                changed = true;
            }
        }
        foreach (var i in connected)
        {
            var r = original[i];
            if ((vertical ? r.Right : r.Bottom) == edge) before.Add(i);
            if ((vertical ? r.Left : r.Top) == edge + gap) after.Add(i);
        }
        if (before.Count == 0 || after.Count == 0) return bounds;
        var min = before.Max(i => 80 - (vertical ? original[i].Right - original[i].Left : original[i].Bottom - original[i].Top));
        var max = after.Min(i => (vertical ? original[i].Right - original[i].Left : original[i].Bottom - original[i].Top) - 80);
        if (min > max) return bounds;
        delta = Math.Clamp(delta, min, max);
        foreach (var i in before) { var r = bounds[i]; if (vertical) r.Right += delta; else r.Bottom += delta; bounds[i] = r; }
        foreach (var i in after) { var r = bounds[i]; if (vertical) r.Left += delta; else r.Top += delta; bounds[i] = r; }
        return bounds;
    }

    private void FinishTileGesture()
    {
        var gesture = _tileGesture;
        if (gesture == null) return;
        UpdateTileGesture();
        lock (_autoTileLock)
        {
            if (!gesture.Resize)
            {
                var slots = gesture.Layout.Handles.Zip(gesture.Layout.Bounds, (handle, bounds) => (handle, bounds)).ToArray();
                var target = FindTileDropTarget(slots, _tileCursor);
                if (Native.IsWindow(target)) SwapTileOrder(_order, gesture.Layout.Handles[gesture.Index], target);
            }
            _tileGesture = null;
            _dragging = false;
        }
        _tileGestureTimer?.Stop();
        Interlocked.Exchange(ref _pulses, 5);
        Restart(1);
    }
}
