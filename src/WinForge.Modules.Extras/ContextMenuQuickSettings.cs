using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinForge.Modules.Extras;

internal static class ContextMenuQuickSettings
{
    internal static readonly (string Id, string Label)[] PowerModes =
        [("01-performance", "En iyi performans"), ("02-balanced", "Dengeli"), ("03-efficiency", "Güç tasarrufu")];

    internal static void ToggleHiddenItems()
    {
        var state = new ShellState();
        SHGetSetSettings(ref state, 1, false); // SSF_SHOWALLOBJECTS
        state.Flags ^= 1;
        SHGetSetSettings(ref state, 1, true);
        Reg.Broadcast("ShellState", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        RefreshExplorer();
    }

    private static void RefreshExplorer()
    {
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            var count = (int)((dynamic)windows).Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try { window = ((dynamic)windows).Item(i); ((dynamic)window!).Refresh(); }
                catch (COMException) { } // A window may close while the setting changes.
                finally { if (window is not null && Marshal.IsComObject(window)) Marshal.FinalReleaseComObject(window); }
            }
        }
        catch (COMException) { }
        finally
        {
            if (windows is not null) Marshal.FinalReleaseComObject(windows);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
        SHChangeNotify(0x1000, 0, IntPtr.Zero, IntPtr.Zero); // Refresh the desktop too.
    }

    internal static void SetPowerMode(string mode, Func<Guid, uint>? setOverlay = null, Func<Guid, uint>? setPlan = null)
    {
        var (overlay, plan) = mode switch
        {
            "01-performance" => (new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")),
            "02-balanced" => (Guid.Empty, new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")),
            "03-efficiency" => (new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), new Guid("a1841308-3541-4fab-bc81-f71556f20b4a")),
            _ => throw new ArgumentException("Geçersiz güç modu.")
        };
        uint result;
        try { result = setOverlay is null ? PowerSetActiveOverlayScheme(ref overlay) : setOverlay(overlay); }
        catch (EntryPointNotFoundException) { result = 50; }
        // Older devices may expose only the traditional power plans.
        if (result != 0) result = (setPlan ?? (value => PowerSetActiveScheme(IntPtr.Zero, ref value)))(plan);
        if (result != 0) throw new Win32Exception((int)result, "Güç modu değiştirilemedi. Bu mod cihazınızda kullanılamıyor veya erişim engellendi.");
    }

    // SHELLSTATE is a persisted, 32-byte structure on both x86 and x64.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct ShellState { [FieldOffset(0)] public uint Flags; }
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHGetSetSettings(ref ShellState state, uint mask, [MarshalAs(UnmanagedType.Bool)] bool set);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item, IntPtr other);
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerSetActiveOverlayScheme(ref Guid scheme);
    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
}
