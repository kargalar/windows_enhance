using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record WorkspaceWindow(string ExecutablePath, string ProcessName, string Title,
    int Left, int Top, int Width, int Height, bool Maximized, string MonitorName,
    int MonitorLeft, int MonitorTop, int MonitorWidth, int MonitorHeight)
{
    public string[] Arguments { get; init; } = [];
    public string? AppUserModelId { get; init; }
}
internal sealed record WorkspacePreset(string Id, string Name, DateTimeOffset SavedAt, IReadOnlyList<WorkspaceWindow> Windows);
internal sealed record WorkspaceBounds(int Left, int Top, int Right, int Bottom, string DeviceName, bool IsPrimary)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
internal sealed record WorkspaceTarget(IntPtr Handle, Process? Process);

internal static class WindowsCommandLine
{
    internal static string[] ParseArguments(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var arguments = CommandLineToArgvW(commandLine, out var count);
        if (arguments == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(arguments, index * IntPtr.Size);
                result[index] = Marshal.PtrToStringUni(argumentPointer) ?? "";
            }
            return result;
        }
        finally { LocalFree(arguments); }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal static class WindowsAppUserModel
{
    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid AppUserModelIdFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    internal static string? GetWindowId(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        IPropertyStore? store = null;
        PropVariant value = default;
        try
        {
            var interfaceId = PropertyStoreId;
            if (SHGetPropertyStoreForWindow(window, ref interfaceId, out store) < 0 || store is null) return null;
            var key = new PropertyKey(AppUserModelIdFormat, 5);
            if (store.GetValue(ref key, out value) < 0) return null;
            if (PropVariantToStringAlloc(ref value, out var text) < 0 || text == IntPtr.Zero) return null;
            try
            {
                var appId = Marshal.PtrToStringUni(text);
                return IsValid(appId) ? appId : null;
            }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        catch (COMException) { return null; }
        finally
        {
            PropVariantClear(ref value);
            if (store is not null && Marshal.IsComObject(store)) Marshal.ReleaseComObject(store);
        }
    }

    internal static bool IsValid(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId) || appUserModelId.Length > 512) return false;
        var separator = appUserModelId.IndexOf('!');
        if (separator <= 0 || separator == appUserModelId.Length - 1 ||
            appUserModelId.IndexOf('!', separator + 1) >= 0) return false;
        return appUserModelId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '!');
    }

    internal static string BuildActivationArguments(string appUserModelId)
    {
        if (!IsValid(appUserModelId)) throw new ArgumentException("AppUserModelID biçimi geçersiz.", nameof(appUserModelId));
        return appUserModelId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    // PROPVARIANT's union includes a 16-byte counted-array member on x64.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid interfaceId, out IPropertyStore store);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToStringAlloc(ref PropVariant value, out IntPtr text);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}

/// <summary>Saves and restores named groups of desktop applications and window positions.</summary>
public sealed class WorkspacesModule : ITweakModule, IQuickActionProvider
{
    private IModuleContext? _context;
    private WorkspacesWindow? _window;
    private readonly SettingDescriptor[] _settings;

    public WorkspacesModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open-manager",
                Label = "Çalışma alanlarını yönet",
                Kind = SettingKind.Action,
                Description = "Açık uygulamaları ve pencere konumlarını kaydedip daha sonra birlikte açın.",
                Execute = OpenManager
            },
            new SettingDescriptor
            {
                Key = "capture-command-line-arguments",
                Label = "Komut satırı bağımsız değişkenlerini kaydet",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Açık uygulamaların başlatma bağımsız değişkenlerini ve dosya yollarını çalışma alanına ekler. Bazı uygulamalar hassas bilgileri bağımsız değişkenlerde tutabileceğinden bu veri yalnızca isteğe bağlı olarak yerel ayarlara kaydedilir."
            }
        ];
    }

    public string Id => "workspaces";
    public string Name => "Çalışma Alanları";
    public string Category => "Verimlilik";
    public string Description => "Açık masaüstü uygulamalarını ve pencere düzenlerini adlandırılmış çalışma alanları olarak kaydedin ve geri yükleyin.";
    public string? Warning => "Uygulamaya özgü belge sekmeleri henüz desteklenmez. Komut satırı bağımsız değişkenlerini kaydetmek isteğe bağlıdır ve hassas bilgiler içerebilir. Geri yükleme uygulamanın kendi açılış süresine bağlıdır.";
    public IReadOnlyList<SettingDescriptor> Settings => _settings;
    public bool IsActive { get; private set; }

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        IsActive = false;
        _window?.Close();
        _window = null;
    }

    public void Apply() { }

    public IEnumerable<QuickAction> GetQuickActions(string query)
    {
        if (!IsActive || _context is null || string.IsNullOrWhiteSpace(query)) return Array.Empty<QuickAction>();
        return WorkspacesWindow.LoadPresets(_context.Settings.GetString("presets", "[]"))
            .Take(40)
            .Select(preset =>
            {
                var apps = string.Join(' ', preset.Windows.Select(window => window.ProcessName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(10));
                var presetId = preset.Id;
                return new QuickAction("workspace:" + presetId, $"Çalışma alanı · {preset.Name}",
                    $"{preset.Name} {apps} çalışma alanı workspace restore geri yükle",
                    () => RestoreWorkspace(presetId));
            })
            .ToArray();
    }

    private void RestoreWorkspace(string presetId)
    {
        if (!IsActive || _context is null) return;
        var featureUi = (_context as IFeatureUiContext)?.FeatureUi;
        if (featureUi is null) return;
        _window ??= new WorkspacesWindow(_context.Settings, featureUi);
        _window.Show();
        _window.Activate();
        _ = _window.RestorePresetAsync(presetId);
    }

    private void OpenManager()
    {
        if (!IsActive || _context is null) return;
        var featureUi = (_context as IFeatureUiContext)?.FeatureUi;
        if (featureUi is null) return;
        _window ??= new WorkspacesWindow(_context.Settings, featureUi);
        _window.Show();
        _window.Activate();
    }
}

internal sealed class WorkspacesWindow
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IModuleSettings _settings;
    private readonly IFeatureUiWindow _window;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _restoreCancellation;
    private List<WorkspacePreset> _presets;
    private string _selectedId = "";
    private string _name = "Yeni çalışma alanı";
    private string _status = "Hazır";

    public WorkspacesWindow(IModuleSettings settings, IFeatureUiService featureUi)
    {
        _settings = settings;
        _presets = LoadPresets(settings.GetString("presets", "[]"));
        _window = featureUi.Open("workspaces", "workspaces", "Çalışma Alanları",
            new FeatureUiWindowOptions(760, 680, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        PushState();
    }

    public void Show() => _window.Show();
    public void Close() { _restoreCancellation?.Cancel(); _lifetime.Cancel(); _window.Close(); }
    internal Task RestorePresetAsync(string presetId) => RestorePresetCoreAsync(presetId);

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "workspaces.name": _name = payload.TryGetProperty("name", out var name) ? name.GetString() ?? "" : _name; PushState(); break;
            case "workspaces.select": _selectedId = payload.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""; PushState(); break;
            case "workspaces.capture": CaptureCurrentWorkspace(); break;
            case "workspaces.restore": if (_selectedId.Length > 0) _ = RestorePresetCoreAsync(_selectedId); break;
            case "workspaces.delete": DeleteSelected(); break;
            case "workspaces.cancel": _restoreCancellation?.Cancel(); break;
            case "workspaces.close": _window.Hide(); break;
        }
    }

    private void CaptureCurrentWorkspace()
    {
        var name = _name.Trim();
        if (name.Length is < 1 or > 64) { _status = "Adı 1–64 karakter arasında olmalıdır."; PushState(); return; }
        try
        {
            var windows = WorkspaceNative.CaptureWindows(_settings.GetBool("capture-command-line-arguments", false));
            if (windows.Count == 0) { _status = "Kaydedilecek uyumlu, görünür uygulama penceresi bulunamadı."; PushState(); return; }
            var existing = _presets.FindIndex(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var preset = new WorkspacePreset(existing >= 0 ? _presets[existing].Id : Guid.NewGuid().ToString("N"), name, DateTimeOffset.Now, windows);
            if (existing >= 0) _presets[existing] = preset; else _presets.Add(preset);
            SavePresets();
            _selectedId = preset.Id;
            _status = $"‘{name}’ alanına {windows.Count} pencere kaydedildi.";
        }
        catch (Exception exception) { _status = "Pencereler kaydedilemedi: " + exception.Message; }
        PushState();
    }

    private async Task RestorePresetCoreAsync(string presetId)
    {
        if (_restoreCancellation is not null) return;
        var preset = _presets.FirstOrDefault(candidate => candidate.Id == presetId);
        if (preset is null) return;
        _selectedId = presetId;
        _restoreCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var restored = 0;
        var skipped = 0;
        var claimedWindows = new HashSet<IntPtr>();
        PushState();
        try
        {
            for (var index = 0; index < preset.Windows.Count; index++)
            {
                _restoreCancellation.Token.ThrowIfCancellationRequested();
                var saved = preset.Windows[index];
                var appLabel = string.IsNullOrWhiteSpace(saved.ExecutablePath) ? saved.ProcessName : Path.GetFileName(saved.ExecutablePath);
                _status = $"{index + 1}/{preset.Windows.Count}: {appLabel} açılıyor…";
                PushState();
                var target = await WorkspaceNative.LaunchAndWaitAsync(saved, TimeSpan.FromSeconds(12), claimedWindows, _restoreCancellation.Token);
                if (target is null) { skipped++; continue; }
                try
                {
                    WorkspaceNative.RestoreWindow(target.Handle, WorkspaceNative.MapToCurrentMonitor(saved), saved.Maximized);
                    claimedWindows.Add(target.Handle);
                    restored++;
                }
                finally { target.Process?.Dispose(); }
            }
            _status = $"Tamamlandı: {restored} pencere yerleştirildi; {skipped} pencere açılamadı.";
        }
        catch (OperationCanceledException) { _status = $"Geri yükleme durduruldu: {restored} pencere yerleştirildi."; }
        catch (Exception exception) { _status = $"Geri yükleme hatası: {exception.Message}"; }
        finally
        {
            _restoreCancellation.Dispose();
            _restoreCancellation = null;
            PushState();
        }
    }

    private void DeleteSelected()
    {
        if (string.IsNullOrWhiteSpace(_selectedId)) return;
        _presets.RemoveAll(preset => preset.Id == _selectedId);
        _selectedId = "";
        SavePresets();
        _status = "Çalışma alanı silindi.";
        PushState();
    }

    private void SavePresets() => _settings.Set("presets", JsonSerializer.Serialize(_presets, JsonOptions));

    private void PushState() => _window.SetState(new
    {
        name = _name,
        selectedId = _selectedId,
        status = _status,
        busy = _restoreCancellation is not null,
        presets = _presets.Select(preset => new
        {
            id = preset.Id,
            name = preset.Name,
            savedAt = preset.SavedAt.ToLocalTime().ToString("g"),
            windowCount = preset.Windows.Count,
            applications = string.Join(", ", preset.Windows.Select(window => window.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(3)) +
                (preset.Windows.Select(window => window.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Skip(3).Any() ? "…" : "")
        }).ToArray()
    });

    internal static List<WorkspacePreset> LoadPresets(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<WorkspacePreset>>(json, JsonOptions)?
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Id) && !string.IsNullOrWhiteSpace(preset.Name) && preset.Windows is not null)
                .ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }
}
internal static class WorkspaceNative
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;
    private const int SwShowMaximized = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;
    private static readonly Guid ApplicationActivationManagerClassId = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow"
    };

    public static List<WorkspaceWindow> CaptureWindows() => CaptureWindows(captureArguments: false);

    public static List<WorkspaceWindow> CaptureWindows(bool captureArguments)
    {
        var argumentsByProcessId = captureArguments ? ReadProcessArguments() : new Dictionary<uint, string[]>();
        var windows = new List<WorkspaceWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsUsableAppWindow(handle)) return true;
            var title = ReadTitle(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var appUserModelId = ReadWindowAppUserModelId(handle);
                var processPath = TryGetProcessPath(process);
                var isPackaged = WindowsAppUserModel.IsValid(appUserModelId) &&
                    (IsPackagedProcess(process) || string.Equals(Path.GetFileName(processPath),
                        "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase));
                string path;
                string processName;
                string[] arguments;
                if (isPackaged)
                {
                    if (!IsValidAppUserModelId(appUserModelId)) return true;
                    path = "";
                    processName = GetPackagedAppName(appUserModelId!);
                    arguments = [];
                }
                else
                {
                    path = processPath;
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return true;
                    if (string.Equals(Path.GetFileName(path), "WinForge.exe", StringComparison.OrdinalIgnoreCase)) return true;
                    processName = Path.GetFileNameWithoutExtension(path);
                    arguments = argumentsByProcessId.GetValueOrDefault(processId, []);
                }
                if (!GetWindowRect(handle, out var bounds) || bounds.Width < 80 || bounds.Height < 60) return true;

                var monitor = GetMonitorBounds(handle);
                var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
                var maximized = GetWindowPlacement(handle, ref placement) && placement.ShowCommand == SwShowMaximized;
                windows.Add(new WorkspaceWindow(path, processName, title,
                    bounds.Left, bounds.Top, bounds.Width, bounds.Height, maximized,
                    monitor.DeviceName, monitor.Left, monitor.Top, monitor.Width, monitor.Height)
                { Arguments = arguments, AppUserModelId = isPackaged ? appUserModelId : null });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
            return true;
        }, IntPtr.Zero);
        return windows.OrderBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static Dictionary<uint, string[]> ReadProcessArguments()
    {
        var result = new Dictionary<uint, string[]>();
        try
        {
            using var management = new WindowsManagement(@"root\cimv2");
            foreach (var process in management.Query("SELECT ProcessId, CommandLine FROM Win32_Process", "ProcessId", "CommandLine"))
            {
                if (process["ProcessId"] is null || process["CommandLine"] is not string commandLine) continue;
                var processId = Convert.ToUInt32(process["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                result[processId] = WindowsCommandLine.ParseArguments(commandLine).Skip(1).ToArray();
            }
        }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    internal static bool IsValidAppUserModelId(string? appUserModelId) => WindowsAppUserModel.IsValid(appUserModelId);
    internal static string BuildAppUserModelActivationArguments(string appUserModelId) =>
        WindowsAppUserModel.BuildActivationArguments(appUserModelId);
    internal static string GetPackagedAppName(string appUserModelId)
    {
        if (!WindowsAppUserModel.IsValid(appUserModelId)) return "Paketlenmiş uygulama";
        var packageFamilyName = appUserModelId[..appUserModelId.IndexOf('!')];
        var publisherSeparator = packageFamilyName.LastIndexOf('_');
        var packageName = publisherSeparator > 0 ? packageFamilyName[..publisherSeparator] : packageFamilyName;
        return packageName.Replace('.', ' ');
    }

    private static string ReadWindowAppUserModelId(IntPtr handle) => WindowsAppUserModel.GetWindowId(handle) ?? "";

    private static string TryGetProcessPath(Process process)
    {
        try { return process.MainModule?.FileName ?? ""; }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static bool IsPackagedProcess(Process process)
    {
        try
        {
            uint length = 0;
            var result = GetPackageFullName(process.Handle, ref length, null);
            if (result == AppModelErrorNoPackage || length == 0) return false;
            if (result != ErrorInsufficientBuffer) return false;
            var packageName = new System.Text.StringBuilder((int)length);
            return GetPackageFullName(process.Handle, ref length, packageName) == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task<WorkspaceTarget?> LaunchAndWaitAsync(WorkspaceWindow saved, TimeSpan timeout,
        ISet<IntPtr> claimedWindows, CancellationToken cancellationToken)
    {
        var hasPackagedId = !string.IsNullOrWhiteSpace(saved.AppUserModelId);
        if (hasPackagedId && !WindowsAppUserModel.IsValid(saved.AppUserModelId)) return null;
        if (!hasPackagedId && !File.Exists(saved.ExecutablePath)) return null;
        var requiresTitleMatch = hasPackagedId || saved.Arguments is { Length: > 0 };
        var existing = FindExistingWindow(saved, claimedWindows, requiresTitleMatch);
        if (existing is not null) return existing;
        Process? process;
        if (hasPackagedId)
        {
            ActivatePackagedApplication(saved.AppUserModelId!);
            process = null;
        }
        else process = Process.Start(CreateStartInfo(saved.ExecutablePath, saved.Arguments ?? []));
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process is not null)
                {
                    process.Refresh();
                    if (process.HasExited) { process.Dispose(); process = null; }
                    else if (process.MainWindowHandle != IntPtr.Zero && !claimedWindows.Contains(process.MainWindowHandle))
                        return new WorkspaceTarget(process.MainWindowHandle, process);
                }

                existing = FindExistingWindow(saved, claimedWindows, requiresTitleMatch);
                if (existing is not null)
                {
                    process?.Dispose();
                    return existing;
                }
                await Task.Delay(200, cancellationToken).ConfigureAwait(true);
            }
            process?.Dispose();
            return null;
        }
        catch { process?.Dispose(); throw; }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static WorkspaceTarget? FindExistingWindow(WorkspaceWindow saved, ISet<IntPtr> claimedWindows,
        bool requireExactTitle = false)
    {
        var candidates = new List<(IntPtr Handle, string Title, uint ProcessId)>();
        EnumWindows((handle, _) =>
        {
            if (!IsUsableAppWindow(handle) || claimedWindows.Contains(handle)) return true;
            GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (WindowsAppUserModel.IsValid(saved.AppUserModelId))
                {
                    if (!string.Equals(ReadWindowAppUserModelId(handle), saved.AppUserModelId, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (!string.Equals(TryGetProcessPath(process), saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return true;
                var title = ReadTitle(handle);
                if (string.IsNullOrWhiteSpace(title)) return true;
                candidates.Add((handle, title, processId));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
            return true;
        }, IntPtr.Zero);

        var exactCandidate = candidates.FirstOrDefault(item =>
            item.Title.Equals(saved.Title, StringComparison.OrdinalIgnoreCase));
        var candidate = exactCandidate.Handle != IntPtr.Zero
            ? exactCandidate
            : requireExactTitle ? default : candidates.FirstOrDefault();
        if (candidate.Handle == IntPtr.Zero) return null;
        try
        {
            return new WorkspaceTarget(candidate.Handle, Process.GetProcessById((int)candidate.ProcessId));
        }
        catch (ArgumentException) { return null; }
    }

    public static RECT MapToCurrentMonitor(WorkspaceWindow saved)
    {
        var monitors = GetMonitorList();
        var monitor = monitors.FirstOrDefault(item => item.DeviceName.Equals(saved.MonitorName, StringComparison.OrdinalIgnoreCase))
                      ?? monitors.FirstOrDefault(item => item.IsPrimary)
                      ?? new WorkspaceBounds(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, "", true);
        return MapToMonitor(saved, monitor);
    }

    internal static RECT MapToMonitor(WorkspaceWindow saved, WorkspaceBounds monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
            throw new ArgumentException("Monitör çalışma alanı geçersiz.", nameof(monitor));
        var sourceWidth = Math.Max(1, saved.MonitorWidth);
        var sourceHeight = Math.Max(1, saved.MonitorHeight);
        var relativeX = (double)(saved.Left - saved.MonitorLeft) / sourceWidth;
        var relativeY = (double)(saved.Top - saved.MonitorTop) / sourceHeight;
        var width = Math.Clamp(saved.Width, Math.Min(240, monitor.Width), monitor.Width);
        var height = Math.Clamp(saved.Height, Math.Min(160, monitor.Height), monitor.Height);
        var left = monitor.Left + (int)Math.Round(relativeX * monitor.Width);
        var top = monitor.Top + (int)Math.Round(relativeY * monitor.Height);
        left = Math.Clamp(left, monitor.Left, monitor.Right - width);
        top = Math.Clamp(top, monitor.Top, monitor.Bottom - height);
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public static void RestoreWindow(IntPtr handle, RECT bounds, bool maximized)
    {
        if (handle == IntPtr.Zero || !Native.IsWindow(handle))
            throw new InvalidOperationException("Geri yüklenecek pencere artık mevcut değil.");
        ShowWindow(handle, 9);
        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top, SwpNoZOrder | SwpNoActivate | SwpShowWindow))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Çalışma alanı penceresi yerleştirilemedi.");
        if (maximized) ShowWindow(handle, SwShowMaximized);
    }

    private static string ReadTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length is <= 0 or > 2048) return "";
        var buffer = new System.Text.StringBuilder(length + 1);
        return GetWindowText(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString().Trim() : "";
    }

    private static WorkspaceBounds GetMonitorBounds(IntPtr window) => TryGetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), out var info)
        ? new WorkspaceBounds(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom,
            info.DeviceName.TrimEnd('\0'), (info.Flags & 1) != 0)
        : new WorkspaceBounds(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, "", true);

    private static bool IsUsableAppWindow(IntPtr handle)
    {
        if (!IsWindowVisible(handle) || IsIconic(handle) || GetWindow(handle, 4) != IntPtr.Zero) return false;
        if ((Native.GetWindowLongPtr(handle, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return false;
        if (DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var className = new System.Text.StringBuilder(256);
        GetClassName(handle, className, className.Capacity);
        return !IgnoredClasses.Contains(className.ToString());
    }

    private static List<WorkspaceBounds> GetMonitorList()
    {
        var monitors = new List<WorkspaceBounds>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (TryGetMonitorInfo(handle, out var info))
                monitors.Add(new WorkspaceBounds(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom,
                    info.DeviceName.TrimEnd('\0'), (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfoEx info)
    {
        info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(monitor, ref info);
    }

    private static void ActivatePackagedApplication(string appUserModelId)
    {
        var activationManagerType = Type.GetTypeFromCLSID(ApplicationActivationManagerClassId, throwOnError: true)!;
        var activationManager = (IApplicationActivationManager)Activator.CreateInstance(activationManagerType)!;
        try
        {
            var result = activationManager.ActivateApplication(
                WindowsAppUserModel.BuildActivationArguments(appUserModelId), null, 0, out _);
            Marshal.ThrowExceptionForHR(result);
        }
        finally
        {
            if (Marshal.IsComObject(activationManager)) Marshal.ReleaseComObject(activationManager);
        }
    }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string? verb, out uint processId);
        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            out uint processId);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement { public int Length; public int Flags; public int ShowCommand; public POINT MinPosition; public POINT MaxPosition; public NativeRect NormalPosition; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder className, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr handle, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int valueSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr process, ref uint packageFullNameLength, System.Text.StringBuilder? packageFullName);
}

