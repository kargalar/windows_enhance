using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class FolderDialogModule : ITweakModule
{
    internal const string RuntimeKey = @"Software\WinForge\FolderDialog\Runtime";
    private const string LibraryName = "WinForge.FolderDialogTap.dll";
    private const int LegacyTreeControl = 0x3741;
    private readonly Dictionary<uint, nint> _hooks = [];
    private IModuleContext? _context;
    private DispatcherTimer? _timer;
    private nint _library;
    private InstallHook? _install;
    private string? _error;

    public string Id => "modern-folder-dialog";
    public string Name => "Modern Klasör Seçici";
    public string Category => "Gezgin";
    public string Description => "Klasik Browse For Folder penceresini adres çubuğu ve arama destekleyen modern Windows klasör seçicisiyle değiştirir.";
    public string? Warning => _error ?? "64 bit uygulamaların standart klasör pencerelerinde çalışır. Yönetici uygulamaları için WinForge'u yönetici olarak çalıştırın. Desteklenmeyen veya kısıtlı konumlarda klasik pencere korunur.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; } = [];

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        EnsureLibrary();
        IsActive = true;
        try
        {
            Apply();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        catch { Deactivate(); throw; }
    }

    public void Apply()
    {
        if (!IsActive) return;
        using var runtime = Registry.CurrentUser.CreateSubKey(RuntimeKey);
        runtime.SetValue("heartbeat", Environment.TickCount64, RegistryValueKind.QWord);
        runtime.SetValue("active", 1, RegistryValueKind.DWord);
        RefreshHooks();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        try { Apply(); }
        catch (Exception error)
        {
            _error = "Klasör seçici bağlantısı: " + error.Message;
            _context?.Log.Error(_error, error);
        }
    }

    private void EnsureLibrary()
    {
        if (_library != 0) return;
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "native", LibraryName);
        using var source = File.OpenRead(sourcePath);
        var digest = SHA256.HashData(source);
        var directory = Path.Combine(AppData.DirectoryPath, "FolderDialog", Convert.ToHexString(digest)[..16]);
        Directory.CreateDirectory(directory);
        var cachedPath = Path.Combine(directory, LibraryName);
        if (!File.Exists(cachedPath)) File.Copy(sourcePath, cachedPath);
        using var cached = File.OpenRead(cachedPath);
        if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(cached)))
            throw new InvalidDataException("Klasör seçici bileşeninin önbellek doğrulaması başarısız.");
        _library = NativeLibrary.Load(cachedPath);
        _install = Marshal.GetDelegateForFunctionPointer<InstallHook>(NativeLibrary.GetExport(_library, "InstallFolderDialogHook"));
    }

    private void RefreshHooks()
    {
        var windows = new Dictionary<uint, nint>();
        Native.EnumWindows((window, parameter) =>
        {
            if (!Native.IsWindowVisible(window) || Native.ClassNameOf(window) != "#32770" ||
                (FindWindowEx(window, 0, "SHBrowseForFolder ShellNameSpace Control", null) == 0 &&
                 Native.ClassNameOf(GetDlgItem(window, LegacyTreeControl)) != "SysTreeView32")) return true;
            var thread = Native.GetWindowThreadProcessId(window, out _);
            windows.TryAdd(thread, window);
            return true;
        }, 0);
        foreach (var thread in _hooks.Keys.Where(thread => !windows.ContainsKey(thread)).ToArray())
        {
            UnhookWindowsHookEx(_hooks[thread]);
            _hooks.Remove(thread);
        }
        foreach (var (thread, window) in windows)
        {
            if (!_hooks.ContainsKey(thread))
            {
                var hook = _install!(thread);
                if (hook == 0)
                {
                    _error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    continue;
                }
                _hooks.Add(thread, hook);
            }
            PostMessage(window, 0, 0, 0);
        }
    }

    public void Deactivate()
    {
        IsActive = false;
        _timer?.Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
        _timer = null;
        using var runtime = Registry.CurrentUser.CreateSubKey(RuntimeKey);
        runtime.SetValue("active", 0, RegistryValueKind.DWord);
        foreach (var hook in _hooks.Values) UnhookWindowsHookEx(hook);
        _hooks.Clear();
        _error = null;
        // Subclass callbacks may still be returning in another process; keep the library mapped.
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    private delegate nint InstallHook(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint dialog, int control);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
}
