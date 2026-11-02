using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public partial class TilingModule
{
    private void TileMaster(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var area = work;
        if (windows.Count == 1)
        {
            Move(windows[0], area);
            return;
        }

        var width = area.Right - area.Left;
        var height = area.Bottom - area.Top;
        var masterWidth = (int)((long)(width - gap) * _masterPercent / 100);
        if (masterWidth < 160) masterWidth = (width - gap) / 2;
        if (masterWidth < 80) return;

        Move(windows[0], new RECT
        {
            Left = area.Left,
            Top = area.Top,
            Right = area.Left + masterWidth,
            Bottom = area.Bottom
        });

        var stackLeft = area.Left + masterWidth + gap;
        var stackWidth = area.Right - stackLeft;
        if (stackWidth < 80) return;

        var heights = SplitSizes(height, windows.Count - 1, gap);
        var top = area.Top;
        for (var i = 1; i < windows.Count; i++)
        {
            var bottom = top + heights[i - 1];
            Move(windows[i], new RECT
            {
                Left = stackLeft,
                Top = top,
                Right = stackLeft + stackWidth,
                Bottom = bottom
            });
            top = bottom + gap;
        }
    }

    private void TileColumns(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var widths = SplitSizes(work.Right - work.Left, windows.Count, gap);
        var left = work.Left;

        for (var i = 0; i < windows.Count; i++)
        {
            var right = left + widths[i];
            Move(windows[i], new RECT
            {
                Left = left,
                Top = work.Top,
                Right = right,
                Bottom = work.Bottom
            });
            left = right + gap;
        }
    }

    private void TileGrid(IReadOnlyList<IntPtr> windows, RECT work, int gap)
    {
        var area = work;
        var count = windows.Count;
        var width = area.Right - area.Left;
        var height = area.Bottom - area.Top;
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        if (columns < 1 || rows < 1) return;

        var heights = SplitSizes(height, rows, gap);
        var top = area.Top;
        var index = 0;

        for (var row = 0; row < rows; row++)
        {
            var inRow = Math.Min(columns, count - index);
            var widths = SplitSizes(width, inRow, gap);
            var left = area.Left;

            for (var column = 0; column < inRow; column++)
            {
                var right = left + widths[column];
                Move(windows[index++], new RECT
                {
                    Left = left,
                    Top = top,
                    Right = right,
                    Bottom = top + heights[row]
                });
                left = right + gap;
            }

            top += heights[row] + gap;
        }
    }

    private static int[] SplitSizes(int total, int count, int gap)
    {
        var sizes = new int[count];
        if (count <= 0) return sizes;

        var available = total - gap * (count - 1);
        if (available < count) available = count;

        var previous = 0;
        for (var i = 0; i < count; i++)
        {
            var edge = (int)((long)available * (i + 1) / count);
            sizes[i] = edge - previous;
            previous = edge;
        }

        return sizes;
    }

    private void Move(IntPtr hwnd, RECT target)
    {
        RestoreFullscreenWindow(hwnd, restorePlacement: true);
        if (!Native.GetWindowRect(hwnd, out var current)) return;

        var windowTarget = target;
        if (DwmGetWindowAttributeRect(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame,
                Marshal.SizeOf<RECT>()) == 0 && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            windowTarget.Left -= frame.Left - current.Left;
            windowTarget.Top -= frame.Top - current.Top;
            windowTarget.Right += current.Right - frame.Right;
            windowTarget.Bottom += current.Bottom - frame.Bottom;
        }

        if (_placed.Count > 64)
        {
            foreach (var stale in _placed.Keys.Where(h => !Native.IsWindow(h)).ToList())
                _placed.Remove(stale);
        }

        if (current.Left == windowTarget.Left && current.Top == windowTarget.Top &&
            current.Right == windowTarget.Right && current.Bottom == windowTarget.Bottom)
        {
            _placed[hwnd] = current;
            return;
        }

        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, windowTarget.Left, windowTarget.Top,
            windowTarget.Right - windowTarget.Left, windowTarget.Bottom - windowTarget.Top,
            SWP_NOZORDER | Native.SWP_NOACTIVATE)) return;

        _placed[hwnd] = windowTarget;
    }

    private void EnterTrueFullscreen(IntPtr hwnd, IntPtr monitor)
    {
        RestoreFullscreenWindow(hwnd, restorePlacement: true);
        if (!TryGetMonitorBounds(monitor, out var bounds)) return;

        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref placement)) return;

        var style = Native.GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        var extendedStyle = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        _fullscreenWindows[hwnd] = new FullscreenWindowState(style, extendedStyle, placement);

        ShowWindow(hwnd, SW_RESTORE);
        var fullscreenStyle = (style & ~WS_OVERLAPPEDWINDOW) | WS_POPUP;
        Native.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(fullscreenStyle));
        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW))
        {
            RestoreFullscreenWindow(hwnd, restorePlacement: true);
            return;
        }

        Native.SetForegroundWindow(hwnd);
        _placed.Remove(hwnd);
    }

    private void RestoreFullscreenWindow(IntPtr hwnd, bool restorePlacement)
    {
        if (!_fullscreenWindows.Remove(hwnd, out var state) || !Native.IsWindow(hwnd)) return;

        Native.SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(state.Style));
        var currentTopmost = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST;
        var restoredExtendedStyle = (state.ExtendedStyle & ~WS_EX_TOPMOST) | currentTopmost;
        Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(restoredExtendedStyle));
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED);
        if (restorePlacement)
        {
            var placement = state.Placement;
            placement.Length = Marshal.SizeOf<WINDOWPLACEMENT>();
            SetWindowPlacement(hwnd, ref placement);
        }
    }

    private static bool TryGetMonitorBounds(IntPtr monitor, out RECT bounds)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            bounds = info.rcMonitor;
            return true;
        }

        bounds = default;
        return false;
    }

    private readonly record struct FullscreenWindowState(long Style, long ExtendedStyle, WINDOWPLACEMENT Placement);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }
}
