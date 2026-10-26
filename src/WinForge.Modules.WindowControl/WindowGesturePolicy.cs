using WinForge.Core;

namespace WinForge.Modules.WindowControl;

internal readonly record struct WindowGestureModifiers(bool Shift = false, bool Control = false, bool SuppressSnap = false);

internal readonly record struct WindowGestureClick(IntPtr Window, WindowAction Action, int Button, POINT Point, long Timestamp)
{
    internal bool Matches(WindowGestureClick next, uint interval, int toleranceX, int toleranceY) =>
        Window == next.Window && Action == next.Action && Button == next.Button && next.Timestamp >= Timestamp &&
        next.Timestamp - Timestamp <= interval && Math.Abs(next.Point.X - Point.X) <= toleranceX && Math.Abs(next.Point.Y - Point.Y) <= toleranceY;
}
