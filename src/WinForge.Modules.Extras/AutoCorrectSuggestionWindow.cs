using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

/// <summary>Small non-activating suggestion strip positioned at the active text caret.</summary>
internal sealed class AutoCorrectSuggestionWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateStyle = 0x08000000;
    private const long ToolWindowStyle = 0x00000080;
    private const double VerticalOffset = 44;
    private readonly StackPanel _suggestions = new() { Orientation = Orientation.Horizontal };
    private readonly Action<string> _acceptSuggestion;

    internal AutoCorrectSuggestionWindow(Action<string> acceptSuggestion)
    {
        _acceptSuggestion = acceptSuggestion;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(35, 38, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(76, 80, 90)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = _suggestions
        };
        SourceInitialized += ApplyNonActivatingStyle;
    }

    internal void ShowSuggestions(IReadOnlyList<string> suggestions, IntPtr targetWindow, POINT caret)
    {
        if (suggestions.Count == 0)
        {
            Hide();
            return;
        }

        _suggestions.Children.Clear();
        foreach (var suggestion in suggestions)
        {
            var button = new Button
            {
                Content = suggestion,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(2, 0, 2, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Focusable = false,
                IsTabStop = false,
                FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => _acceptSuggestion(suggestion);
            _suggestions.Children.Add(button);
        }

        var dpi = GetDpiForWindow(targetWindow);
        var scale = dpi == 0 ? 1.0 : 96.0 / dpi;
        Left = caret.X * scale;
        Top = Math.Max(0, caret.Y * scale - VerticalOffset);
        if (!IsVisible) Show();
    }

    internal bool ContainsScreenPoint(POINT point)
    {
        if (!IsVisible || !GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds)) return false;
        return point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;
    }

    internal static bool TryGetCaret(IntPtr targetWindow, out POINT caret)
    {
        caret = default;
        var threadId = GetWindowThreadProcessId(targetWindow, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (threadId == 0 || !GetGUIThreadInfo(threadId, ref info) || info.CaretWindow == IntPtr.Zero) return false;

        caret = new POINT { X = info.CaretRectangle.Left, Y = info.CaretRectangle.Bottom };
        return ClientToScreen(info.CaretWindow, ref caret);
    }

    private void ApplyNonActivatingStyle(object? sender, EventArgs args)
    {
        var window = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(window, ExtendedStyleIndex, new IntPtr(style | NoActivateStyle | ToolWindowStyle));
    }

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) : new IntPtr(GetWindowLong32(window, index));

    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(window, index, value) : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal IntPtr ActiveWindow;
        internal IntPtr FocusWindow;
        internal IntPtr CaptureWindow;
        internal IntPtr MenuOwnerWindow;
        internal IntPtr MoveSizeWindow;
        internal IntPtr CaretWindow;
        internal NativeRectangle CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr window, ref POINT point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeWindowRectangle rectangle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
}
