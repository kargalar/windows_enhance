using System.Reflection;
using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.RegressionTests;

internal static class TilingInteractionChecks
{
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int WindowStyleIndex = -16;
    private const long TopmostStyle = 0x00000008;

    internal static void Run(Type tilingType, Action<bool, string> check)
    {
        var left = new IntPtr(11);
        var right = new IntPtr(12);
        var third = new IntPtr(13);
        var slots = new List<(IntPtr Handle, RECT Bounds)>
        {
            (left, new RECT { Left = -1000, Top = 0, Right = -510, Bottom = 800 }),
            (right, new RECT { Left = -500, Top = 0, Right = 0, Bottom = 800 })
        };
        var findTarget = tilingType.GetMethod("FindTileDropTarget", StaticMethods)!;
        var target = (IntPtr)findTarget.Invoke(null, [slots, new POINT { X = -800, Y = 100 }])!;
        var order = new List<IntPtr> { left, right, third };
        var swap = tilingType.GetMethod("SwapTileOrder", StaticMethods)!;
        check(target == left && (bool)swap.Invoke(null, [order, right, target])! &&
              order.SequenceEqual([right, left, third]),
            "Dragging the right tile onto the left swaps both windows and preserves other tiles on negative-coordinate monitors");
        check(!(bool)swap.Invoke(null, [order, right, right])! &&
              !(bool)swap.Invoke(null, [order, right, new IntPtr(99)])! &&
              (IntPtr)findTarget.Invoke(null, [slots, new POINT { X = -505, Y = 100 }])! == IntPtr.Zero &&
              (IntPtr)findTarget.Invoke(null, [slots, new POINT { X = 100, Y = 100 }])! == IntPtr.Zero &&
              order.SequenceEqual([right, left, third]),
            "Dropping on the same tile, a gutter, outside the layout, or a stale handle leaves tile order unchanged");
        check((bool)swap.Invoke(null, [order, right, left])! && order.SequenceEqual([left, right, third]),
            "Tile swaps can be repeated in either direction");

        VerifyDragLifecycle(tilingType, check);
        VerifyFullscreenStacking(tilingType, check, originallyTopmost: false);
        VerifyFullscreenStacking(tilingType, check, originallyTopmost: true);
    }

    private static void VerifyDragLifecycle(Type tilingType, Action<bool, string> check)
    {
        using var left = new NativeWindowFixture($"WinForge.Tile.Left.{Guid.NewGuid():N}", "Left tile",
            new RECT { Left = 100, Top = 100, Right = 500, Bottom = 500 }, 0x00CF0000, 0);
        using var right = new NativeWindowFixture($"WinForge.Tile.Right.{Guid.NewGuid():N}", "Right tile",
            new RECT { Left = 520, Top = 100, Right = 920, Bottom = 500 }, 0x00CF0000, 0);
        left.Show();
        right.Show();
        var module = (IWindowZoneDragProvider)Activator.CreateInstance(tilingType)!;
        tilingType.GetProperty("IsActive")!.SetValue(module, true);
        var order = (List<IntPtr>)tilingType.GetField("_order", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        order.AddRange([left.Handle, right.Handle]);
        var move = tilingType.GetMethod("Move", InstanceMethods)!;
        var leftSlot = new RECT { Left = 100, Top = 100, Right = 500, Bottom = 500 };
        var rightSlot = new RECT { Left = 520, Top = 100, Right = 920, Bottom = 500 };
        Native.GetCursorPos(out var originalCursor);
        try
        {
            move.Invoke(module, [left.Handle, leftSlot]);
            move.Invoke(module, [right.Handle, rightSlot]);
            module.BeginWindowZoneDrag(right.Handle);
            module.BeginWindowZoneDrag(right.Handle);
            Native.SetWindowPos(right.Handle, IntPtr.Zero, 150, 150, 0, 0, Native.SWP_NOSIZE | 0x0004 | Native.SWP_NOACTIVATE);
            if (!SetCursorPos(200, 200)) throw new InvalidOperationException("Could not position the drag fixture cursor.");
            check(module.EndWindowZoneDrag(right.Handle, canceled: false) && order.SequenceEqual([right.Handle, left.Handle]),
                "A real window drag swaps the captured tile order, including duplicate native/control drag-start notifications");
            check(!module.EndWindowZoneDrag(right.Handle, canceled: false) && order.SequenceEqual([right.Handle, left.Handle]),
                "Duplicate drag-end notifications cannot undo a tile swap");

            move.Invoke(module, [right.Handle, leftSlot]);
            move.Invoke(module, [left.Handle, rightSlot]);
            module.BeginWindowZoneDrag(left.Handle);
            check(!module.EndWindowZoneDrag(left.Handle, canceled: true) && order.SequenceEqual([right.Handle, left.Handle]),
                "Cancelling a tile drag preserves the previous order");
            module.BeginWindowZoneDrag(left.Handle);
            check(!module.EndWindowZoneDrag(left.Handle, canceled: false) && order.SequenceEqual([right.Handle, left.Handle]),
                "Native drag cancellation or no movement cannot swap a tile merely because the cursor is over its neighbor");
            module.BeginWindowZoneDrag(left.Handle);
            Native.GetWindowRect(left.Handle, out var bounds);
            Native.SetWindowPos(left.Handle, IntPtr.Zero, 150, 150,
                bounds.Right - bounds.Left + 50, bounds.Bottom - bounds.Top, 0x0004 | Native.SWP_NOACTIVATE);
            check(!module.EndWindowZoneDrag(left.Handle, canceled: false) && order.SequenceEqual([right.Handle, left.Handle]),
                "Resizing a real window over another tile cannot change tile order");
        }
        finally
        {
            SetCursorPos(originalCursor.X, originalCursor.Y);
            ((ITweakModule)module).Deactivate();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    private static void VerifyFullscreenStacking(Type tilingType, Action<bool, string> check, bool originallyTopmost)
    {
        using var window = new NativeWindowFixture($"WinForge.Tiling.{Guid.NewGuid():N}", "Tiling fullscreen fixture",
            new RECT { Left = 100, Top = 100, Right = 500, Bottom = 400 }, 0x00CF0000,
            originallyTopmost ? (uint)TopmostStyle : 0);
        window.Show();
        var module = Activator.CreateInstance(tilingType)!;
        var enter = tilingType.GetMethod("EnterTrueFullscreen", InstanceMethods)!;
        var restore = tilingType.GetMethod("RestoreFullscreenWindow", InstanceMethods)!;
        var monitor = tilingType.GetMethod("MonitorFromWindow", StaticMethods)!.Invoke(null, [window.Handle, 2u])!;
        var originalStyle = Native.GetWindowLongPtr(window.Handle, WindowStyleIndex);
        Native.GetWindowRect(window.Handle, out var originalBounds);
        try
        {
            enter.Invoke(module, [window.Handle, monitor]);
            var isTopmost = (Native.GetWindowLongPtr(window.Handle, Native.GWL_EXSTYLE).ToInt64() & TopmostStyle) != 0;
            check(isTopmost == originallyTopmost && Native.GetWindowLongPtr(window.Handle, WindowStyleIndex) != originalStyle,
                originallyTopmost
                    ? "FancyZones fullscreen preserves an explicitly pinned window"
                    : "FancyZones fullscreen keeps a normal window in the normal Alt+Tab stacking group");
            if (!originallyTopmost)
            {
                using var other = new NativeWindowFixture($"WinForge.Tiling.Other.{Guid.NewGuid():N}", "Other normal window",
                    new RECT { Left = 200, Top = 200, Right = 600, Bottom = 500 }, 0x00CF0000, 0);
                other.Show();
                Native.SetWindowPos(other.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                var firstWindow = IntPtr.Zero;
                Native.EnumWindows((candidate, _) =>
                {
                    if (candidate != window.Handle && candidate != other.Handle) return true;
                    firstWindow = candidate;
                    return false;
                }, IntPtr.Zero);
                check(firstWindow == other.Handle,
                    "Another normal window can be raised above a FancyZones fullscreen window");
            }
        }
        finally
        {
            restore.Invoke(module, [window.Handle, true]);
        }
        Native.GetWindowRect(window.Handle, out var restoredBounds);
        check(Native.GetWindowLongPtr(window.Handle, WindowStyleIndex) == originalStyle &&
              restoredBounds.Left == originalBounds.Left && restoredBounds.Top == originalBounds.Top &&
              restoredBounds.Right == originalBounds.Right && restoredBounds.Bottom == originalBounds.Bottom &&
              ((Native.GetWindowLongPtr(window.Handle, Native.GWL_EXSTYLE).ToInt64() & TopmostStyle) != 0) == originallyTopmost,
            "Leaving FancyZones fullscreen restores the original frame, placement, and pin state");
    }
}
