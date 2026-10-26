using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinForge.Core;

/// <summary>Reads Windows shell identities without inferring an application from its display name.</summary>
internal static class ShellApplicationIdentity
{
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey ApplicationIdKey = new(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PropertyKey LinkTargetKey = new(new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);
    private static readonly TimeSpan ShortcutCacheLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ShortcutScanLimit = TimeSpan.FromSeconds(1);
    private const int MaximumShortcuts = 750;
    private const int MaximumDirectoryDepth = 8;
    private const int MaximumApplicationIdLength = 512;
    private static Dictionary<string, HashSet<string>> _shortcutTargets = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastShortcutScan;

    internal static string ReadWindowId(IntPtr window)
    {
        IPropertyStore? store = null;
        try
        {
            var id = PropertyStoreId;
            return SHGetPropertyStoreForWindow(window, ref id, out store) >= 0 ? ReadString(store, ApplicationIdKey) : "";
        }
        catch (COMException) { return ""; }
        finally { Release(store); }
    }

    internal static string ReadProcessId(uint processId)
    {
        using var process = OpenProcess(0x1000, false, processId);
        if (process.IsInvalid) return "";
        uint length = 0;
        if (GetApplicationUserModelId(process, ref length, null) != 122 || length is 0 or > MaximumApplicationIdLength) return "";
        var text = new StringBuilder((int)length);
        return GetApplicationUserModelId(process, ref length, text) == 0 ? text.ToString() : "";
    }

    internal static IReadOnlySet<string> GetExecutablePaths(string applicationId)
    {
        if (TryExpandApplicationPath(applicationId, out var path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        if (_lastShortcutScan == 0 || Environment.TickCount64 - _lastShortcutScan > ShortcutCacheLifetime.TotalMilliseconds)
            RefreshShortcutTargets();
        if (!_shortcutTargets.TryGetValue(applicationId, out var targets)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Browser profiles and other app groups can share one executable with distinct explicit IDs.
        return targets.Where(target => !_shortcutTargets.Any(entry =>
                !entry.Key.Equals(applicationId, StringComparison.OrdinalIgnoreCase) && entry.Value.Contains(target)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool TryExpandApplicationPath(string applicationId, out string path)
    {
        path = "";
        var value = Environment.ExpandEnvironmentVariables(applicationId);
        if (value.StartsWith('{') && value.IndexOf('}') is var end && end > 0 &&
            Guid.TryParse(value[..(end + 1)], out var knownFolder) &&
            end + 1 < value.Length && value[end + 1] is '\\' or '/')
        {
            if (SHGetKnownFolderPath(ref knownFolder, 0, IntPtr.Zero, out var directory) < 0) return false;
            try { value = Path.Combine(Marshal.PtrToStringUni(directory) ?? "", value[(end + 2)..]); }
            finally { Marshal.FreeCoTaskMem(directory); }
        }
        if (!Path.IsPathFullyQualified(value) || !Path.GetExtension(value).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try { path = Path.GetFullPath(value); return true; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static void RefreshShortcutTargets()
    {
        var targets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var clock = Stopwatch.StartNew();
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var pending = new Stack<(string Path, int Depth)>(roots.Where(Directory.Exists).Reverse().Select(root => (root, 0)));
        var visited = 0;
        while (pending.TryPop(out var directory) && visited < MaximumShortcuts && clock.Elapsed < ShortcutScanLimit)
        {
            try
            {
                foreach (var link in Directory.EnumerateFiles(directory.Path, "*.lnk"))
                {
                    if (++visited > MaximumShortcuts || clock.Elapsed >= ShortcutScanLimit) break;
                    IPropertyStore? store = null;
                    try
                    {
                        var id = PropertyStoreId;
                        if (SHGetPropertyStoreFromParsingName(link, IntPtr.Zero, 0, ref id, out store) < 0) continue;
                        var appId = ReadString(store, ApplicationIdKey);
                        if (string.IsNullOrWhiteSpace(appId) || !TryExpandApplicationPath(ReadString(store, LinkTargetKey), out var target)) continue;
                        if (!targets.TryGetValue(appId, out var paths)) targets[appId] = paths = new(StringComparer.OrdinalIgnoreCase);
                        paths.Add(target);
                    }
                    catch (COMException) { }
                    finally { Release(store); }
                }
                if (directory.Depth < MaximumDirectoryDepth)
                    foreach (var child in Directory.EnumerateDirectories(directory.Path))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push((child, directory.Depth + 1));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        _shortcutTargets = targets;
        _lastShortcutScan = Environment.TickCount64;
    }

    private static string ReadString(IPropertyStore store, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            if (store.GetValue(ref key, out value) < 0 || PropVariantToStringAlloc(ref value, out var text) < 0 || text == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(text)?.Trim() ?? ""; }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally { PropVariantClear(ref value); }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId) { public Guid FormatId = formatId; public uint PropertyId = propertyId; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid id, out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr context, int flags, ref Guid id, out IPropertyStore store);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
    [DllImport("propsys.dll")] private static extern int PropVariantToStringAlloc(ref PropVariant value, out IntPtr text);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? applicationId);
}
