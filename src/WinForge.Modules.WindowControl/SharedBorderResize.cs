using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;
using static WinForge.Modules.WindowControl.WindowControlNative;

namespace WinForge.Modules.WindowControl;

// Tracks visible shared borders independently of the configured Alt-drag gestures.
internal sealed class SharedBorderResize : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private readonly Func<IntPtr, bool, IReadOnlyList<IntPtr>> candidates;
    private readonly List<(Pair Pair, Window Overlay)> borders = new();
    private Pair? dragging;
    private POINT start;
    private bool swallowUp;
    private int refresh;
    private long? altPressedAt;
    private bool _suppressedUntilAltRelease;

    internal SharedBorderResize(Func<IntPtr, bool, IReadOnlyList<IntPtr>> candidates)
    {
        this.candidates = candidates;
        timer.Tick += (_, _) => Tick();
        timer.Start();
    }

    internal bool Mouse(int message, POINT point)
    {
        if (message == Native.WM_RBUTTONUP && swallowUp)
        {
            swallowUp = false;
            if (dragging is not null) Move(point);
            dragging = null;
            Clear();
            return true;
        }
        if (message != Native.WM_RBUTTONDOWN || !AltDown() || dragging is not null) return false;
        var pair = borders.Select(x => x.Pair).FirstOrDefault(p => p.Hit(point));
        if (pair is null || !pair.Valid()) return false;
        dragging = pair;
        start = point;
        swallowUp = true;
        return true;
    }

    private static bool AltDown() => (GetAsyncKeyState(0x12) & 0x8000) != 0;
    private void Tick()
    {
        if (dragging is not null)
        {
            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0)
            {
                if (dragging.Valid()) { WindowActions.Place(dragging.First, dragging.A); WindowActions.Place(dragging.Second, dragging.B); }
                dragging = null;
                Clear();
            }
            else if (Native.GetCursorPos(out var point)) Move(point);
            return;
        }
        if (!AltDown()) { altPressedAt = null; _suppressedUntilAltRelease = false; refresh = 0; Clear(); return; }
        if (_suppressedUntilAltRelease) { Clear(); return; }
        if ((GetAsyncKeyState(0x09) & 0x8000) != 0 || (GetAsyncKeyState(0x1B) & 0x8000) != 0 ||
            (GetAsyncKeyState(0x20) & 0x8000) != 0 || (GetAsyncKeyState(0x73) & 0x8000) != 0 ||
            (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0 ||
            Native.IsShellSwitcherClass(Native.ClassNameOf(Native.GetForegroundWindow())))
        {
            _suppressedUntilAltRelease = true;
            Clear();
            return;
        }
        altPressedAt ??= Environment.TickCount64;
        if (Environment.TickCount64 - altPressedAt.Value < 500) return;
        if (++refresh % 8 != 1) return;
        Refresh();
    }

    private void Refresh()
    {
        var overlayHandles = borders.Select(x => new WindowInteropHelper(x.Overlay).Handle).ToHashSet();
        var windows = candidates(IntPtr.Zero, false).Where(w => !overlayHandles.Contains(w) && !IsZoomed(w) &&
            ((long)Native.GetWindowLongPtr(w, WindowStyleIndex) & ResizableStyle) != 0 && Native.GetWindowRect(w, out _)).ToArray();
        var pairs = new List<Pair>();
        for (var i = 0; i < windows.Length; i++)
        for (var j = i + 1; j < windows.Length; j++)
        {
            var pair = Pair.Create(windows[i], windows[j]);
            if (pair is not null) pairs.Add(pair);
        }

        // Keep unchanged windows alive so the indicators do not flash on every scan.
        var old = borders.ToArray();
        borders.Clear();
        foreach (var pair in pairs)
        {
            var existing = Array.FindIndex(old, x => x.Overlay is not null &&
                x.Pair.First == pair.First && x.Pair.Second == pair.Second && x.Pair.Vertical == pair.Vertical &&
                x.Pair.PidA == pair.PidA && x.Pair.PidB == pair.PidB);
            Window overlay;
            var needsPosition = true;
            if (existing >= 0)
            {
                overlay = old[existing].Overlay;
                needsPosition = old[existing].Pair.Highlight != pair.Highlight;
                old[existing] = default;
            }
            else
            {
                overlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(180, 0, 150, 255)),
                    ShowInTaskbar = false, ShowActivated = false, Topmost = true, IsHitTestVisible = false, ResizeMode = ResizeMode.NoResize, Focusable = false, Opacity = 0 };
                overlay.Show();
                var handle = new WindowInteropHelper(overlay).Handle;
                var ex = Native.GetWindowLongPtr(handle, Native.GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(handle, Native.GWL_EXSTYLE, new IntPtr(ex | 0x20 | 0x80 | 0x08000000));
            }
            if (needsPosition)
            {
                var b = pair.Highlight;
                Native.SetWindowPos(new WindowInteropHelper(overlay).Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE);
            }
            if (existing < 0) overlay.Opacity = 1;
            borders.Add((pair, overlay));
        }
        foreach (var (_, overlay) in old) overlay?.Close();
    }

    private void Move(POINT point)
    {
        if (dragging is not { } p || !p.Valid()) { dragging = null; Clear(); return; }
        var delta = p.Vertical ? point.X - start.X : point.Y - start.Y;
        var min = Math.Max(p.MinA - p.SizeA, p.SizeB - p.MaxB);
        var max = Math.Min(p.MaxA - p.SizeA, p.SizeB - p.MinB);
        if (min > max) return;
        delta = Math.Clamp(delta, min, max);
        var a = p.Vertical ? p.A with { Width = p.A.Width + delta } : p.A with { Height = p.A.Height + delta };
        var b = p.Vertical ? p.B with { X = p.B.X + delta, Width = p.B.Width - delta } : p.B with { Y = p.B.Y + delta, Height = p.B.Height - delta };
        WindowActions.Place(p.First, a);
        WindowActions.Place(p.Second, b);
        foreach (var (pair, overlay) in borders)
        {
            if (pair != p) { overlay.Hide(); continue; }
            var h = new WindowInteropHelper(overlay).Handle;
            var bounds = p.Highlight;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, bounds.X + (p.Vertical ? delta : 0), bounds.Y + (p.Vertical ? 0 : delta), bounds.Width, bounds.Height, Native.SWP_NOACTIVATE);
        }
    }

    private void Clear() { foreach (var (_, overlay) in borders) overlay.Close(); borders.Clear(); }
    public void Dispose() { timer.Stop(); Clear(); }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    private sealed record Pair(IntPtr First, IntPtr Second, uint PidA, uint PidB, WindowBounds A, WindowBounds B, bool Vertical,
        WindowBounds Highlight, int MinA, int MaxA, int MinB, int MaxB)
    {
        internal int SizeA => Vertical ? A.Width : A.Height;
        internal int SizeB => Vertical ? B.Width : B.Height;
        internal bool Hit(POINT p) => p.X >= Highlight.X - 4 && p.X < Highlight.Right + 4 && p.Y >= Highlight.Y - 4 && p.Y < Highlight.Bottom + 4;
        internal bool Valid() => Native.IsWindow(First) && Native.IsWindow(Second) && Native.GetWindowThreadProcessId(First, out var a) != 0 && a == PidA && Native.GetWindowThreadProcessId(Second, out var b) != 0 && b == PidB;
        internal static Pair? Create(IntPtr first, IntPtr second)
        {
            if (!Native.GetWindowRect(first, out var ar) || !Native.GetWindowRect(second, out var br)) return null;
            var a = WindowBounds.From(ar); var b = WindowBounds.From(br);
            var av = Visible(first, a); var bv = Visible(second, b);
            bool vertical;
            if (Math.Abs(av.Right - bv.X) <= 16 && Math.Min(av.Bottom, bv.Bottom) - Math.Max(av.Y, bv.Y) > 24) vertical = true;
            else if (Math.Abs(bv.Right - av.X) <= 16 && Math.Min(av.Bottom, bv.Bottom) - Math.Max(av.Y, bv.Y) > 24) { (first, second) = (second, first); (a, b) = (b, a); (av, bv) = (bv, av); vertical = true; }
            else if (Math.Abs(av.Bottom - bv.Y) <= 16 && Math.Min(av.Right, bv.Right) - Math.Max(av.X, bv.X) > 24) vertical = false;
            else if (Math.Abs(bv.Bottom - av.Y) <= 16 && Math.Min(av.Right, bv.Right) - Math.Max(av.X, bv.X) > 24) { (first, second) = (second, first); (a, b) = (b, a); (av, bv) = (bv, av); vertical = false; }
            else return null;
            var highlight = vertical ? new WindowBounds((av.Right + bv.X) / 2 - 2, Math.Max(av.Y, bv.Y), 4, Math.Min(av.Bottom, bv.Bottom) - Math.Max(av.Y, bv.Y)) :
                new WindowBounds(Math.Max(av.X, bv.X), (av.Bottom + bv.Y) / 2 - 2, Math.Min(av.Right, bv.Right) - Math.Max(av.X, bv.X), 4);
            var la = Limits(first, vertical); var lb = Limits(second, vertical);
            Native.GetWindowThreadProcessId(first, out var pa); Native.GetWindowThreadProcessId(second, out var pb);
            return new(first, second, pa, pb, a, b, vertical, highlight, la.min, la.max, lb.min, lb.max);
        }
        private static WindowBounds Visible(IntPtr w, WindowBounds outer) => Native.DwmGetWindowAttribute(w, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) == 0 ? WindowBounds.From(rect) : outer;
        private static (int min, int max) Limits(IntPtr w, bool vertical)
        {
            var info = new MinMaxInfo();
            SendMessageTimeout(w, GetMinMaxInfo, IntPtr.Zero, ref info, Native.SMTO_ABORTIFHUNG, 20, out _);
            var min = Math.Max(vertical ? 64 : 48, vertical ? info.MinTrackSize.X : info.MinTrackSize.Y);
            var max = vertical ? info.MaxTrackSize.X : info.MaxTrackSize.Y;
            return (min, max > 0 ? Math.Max(min, max) : int.MaxValue);
        }
    }
}
