using System.Runtime.InteropServices;
using System.Text;

namespace WinForge.Core;

public static class ShellTaskbar
{
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
        "Windows.UI.Core.CoreWindow"
    };

    public static bool IsTaskbarPoint(POINT point)
    {
        var window = Native.WindowFromPoint(point);
        if (window == IntPtr.Zero) return false;
        return IsTaskbarWindow(window);
    }

    public static bool IsTaskbarWindow(IntPtr window)
    {
        var root = Native.GetAncestor(window, Native.GA_ROOT);
        if (root == IntPtr.Zero) return false;
        return TaskbarClasses.Contains(Native.ClassNameOf(root));
    }

    /// <summary>Finds the application window whose taskbar button sits under the point.</summary>
    public static IntPtr FindWindowUnderTaskbar(POINT point)
    {
        string? name = AccessibleNameAt(point);

        if (string.IsNullOrWhiteSpace(name))
        {
            // Some builds expose the button's owning HWND directly.
            return IntPtr.Zero;
        }

        return FindWindowByTitle(name);
    }

    private static string? AccessibleNameAt(POINT point)
    {
        try
        {
            var hr = Native.AccessibleObjectFromPoint(point, out var accessible, out var child);
            if (hr != 0 || accessible is not IAccessible acc) return null;

            var name = acc.get_accName(child);
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch
        {
            // Accessibility is optional; never break the click path.
        }

        return null;
    }

    private static IntPtr FindWindowByTitle(string title)
    {
        var ownProcess = (uint)Environment.ProcessId;
        var found = IntPtr.Zero;

        Native.EnumWindows((window, _) =>
        {
            if (!Native.IsWindowVisible(window)) return true;

            var className = Native.ClassNameOf(window);
            if (IgnoredClasses.Contains(className)) return true;

            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId == ownProcess) return true;

            var text = Native.TitleOf(window);
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!string.Equals(text.Trim(), title, StringComparison.OrdinalIgnoreCase)) return true;

            found = window;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    [ComImport]
    [Guid("618736E0-3C3D-11CF-810C-00AA00389B71")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IAccessible
    {
        [DispId(-5003)]
        string? get_accName([MarshalAs(UnmanagedType.Struct)] object child);
    }
}
