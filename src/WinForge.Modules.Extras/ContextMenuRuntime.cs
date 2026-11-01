using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed class ContextMenuRuntime(ILogger log)
{
    private readonly Dictionary<uint, IntPtr> _hooks = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _library;
    private InstallHook? _install;
    private bool _active;
    internal void Apply(IReadOnlyList<ContextMenuEntry> entries, IReadOnlyList<ContextMenuRule> rules)
    {
        EnsureLibrary();
        var prepared = rules.Select(rule => (Rule: rule, Entry: entries.FirstOrDefault(e => e.Id == rule.Id),
            Icon: ContextMenuCatalog.ExportIcon(rule.Icon))).Where(row => row.Entry is not null).ToArray();
        using var runtime = Registry.CurrentUser.CreateSubKey(ContextMenuCatalog.RuntimeKey);
        runtime.SetValue("active", 0);
        runtime.DeleteSubKeyTree("Rules", false);
        using var root = runtime.CreateSubKey("Rules");
        for (var i = 0; i < prepared.Length; i++)
        {
            using var key = root.CreateSubKey(i.ToString());
            var row = prepared[i];
            key.SetValue("match", row.Entry!.Match); key.SetValue("name", row.Rule.Name);
            key.SetValue("visible", row.Rule.Visible ? 1 : 0); key.SetValue("group", row.Rule.Group); key.SetValue("icon", row.Icon);
        }
        var icons = Path.Combine(AppData.DirectoryPath, "ContextMenu", "CapturedIcons"); Directory.CreateDirectory(icons);
        runtime.SetValue("icon-directory", icons); runtime.SetValue("heartbeat", Environment.TickCount64, RegistryValueKind.QWord); runtime.SetValue("active", 1);
        if (!_active) { _active = true; _timer.Tick += Tick; _timer.Start(); }
        RefreshHooks();
    }
    internal void Deactivate()
    {
        _active = false; _timer.Stop(); _timer.Tick -= Tick;
        using var runtime = Registry.CurrentUser.CreateSubKey(ContextMenuCatalog.RuntimeKey); runtime.SetValue("active", 0);
        foreach (var hook in _hooks.Values) Native.UnhookWindowsHookEx(hook);
        _hooks.Clear();
        // Explorer can still be returning through a callback; keep the library mapped.
    }
    private void Tick(object? sender, EventArgs args)
    {
        try { using var runtime = Registry.CurrentUser.CreateSubKey(ContextMenuCatalog.RuntimeKey); runtime.SetValue("heartbeat", Environment.TickCount64, RegistryValueKind.QWord); RefreshHooks(); }
        catch (Exception error) { log.Error("Sağ tık menüsü bağlantısı güncellenemedi.", error); }
    }
    private void EnsureLibrary()
    {
        if (_library != IntPtr.Zero) return;
        var source = Path.Combine(AppContext.BaseDirectory, "native", "WinForge.ContextMenuTap.dll");
        var digest = SHA256.HashData(File.ReadAllBytes(source));
        var directory = Path.Combine(AppData.DirectoryPath, "ContextMenu", Convert.ToHexString(digest)[..16]); Directory.CreateDirectory(directory);
        var cached = Path.Combine(directory, Path.GetFileName(source)); if (!File.Exists(cached)) File.Copy(source, cached);
        if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(File.ReadAllBytes(cached)))) throw new InvalidDataException("Menü bileşeni doğrulanamadı.");
        _library = NativeLibrary.Load(cached);
        _install = Marshal.GetDelegateForFunctionPointer<InstallHook>(NativeLibrary.GetExport(_library, "InstallContextMenuHook"));
    }
    private void RefreshHooks()
    {
        var processes = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("explorer")) { using (process) processes.Add((uint)process.Id); }
        var threads = new HashSet<uint>();
        Native.EnumWindows((window, parameter) =>
        {
            var thread = Native.GetWindowThreadProcessId(window, out var pid); if (!processes.Contains(pid)) return true;
            var name = new StringBuilder(128); Native.GetClassName(window, name, name.Capacity);
            if (name.ToString() is not ("CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")) return true;
            threads.Add(thread);
            EnumChildWindows(window, (child, childParameter) => { threads.Add(Native.GetWindowThreadProcessId(child, out _)); return true; }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        foreach (var thread in _hooks.Keys.Where(t => !threads.Contains(t)).ToArray()) { Native.UnhookWindowsHookEx(_hooks[thread]); _hooks.Remove(thread); }
        foreach (var thread in threads.Where(t => !_hooks.ContainsKey(t)))
        {
            var hook = _install!(thread); if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Explorer menü kancası kurulamadı.");
            _hooks[thread] = hook;
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)] private delegate IntPtr InstallHook(uint thread);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, Native.EnumWindowsProc callback, IntPtr parameter);
}
