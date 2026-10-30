using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class FileLocksmithModule : ITweakModule, IAppCommandModule
{
    private readonly SettingDescriptor[] _settings;

    private readonly ExplorerFileVerb _explorerVerb = new("WinForge.FileLocksmith", "Dosya kilitlerini bul (WinForge)", "--file-locksmith \"%1\"");
    private readonly ExplorerFileVerb _folderVerb = new("WinForge.FileLocksmith", "Dosya kilitlerini bul (WinForge)", "--file-locksmith \"%1\"", false, @"Software\Classes\Directory\shell");
    private bool _isActive;
    private IModuleContext? _context;

    public FileLocksmithModule()
    {
        _settings =
        [
            new SettingDescriptor
            {
                Key = "open",
                Label = "Dosya kilitlerini bul",
                Kind = SettingKind.Action,
                Description = "Dosyayı kullanan uygulama ve hizmetleri listeleyin.",
                Execute = Open
            }
        ];
    }

    public string Id => "file-locksmith";
    public string Name => "Dosya Kilidi Bulucu";
    public string Category => "Gezgin";
    public string Description => "Seçilen dosyayı kullanan uygulama ve hizmetleri Windows Restart Manager ile bulur.";
    public string? Warning => "Windows'un Restart Manager API'sinin bildirdiği süreçler gösterilir. Sonlandırma kaydedilmemiş işi kaybettirebilir; yalnızca yeniden doğrulanmış kullanıcı uygulamaları için kullanılabilir.";
    public bool IsActive => _isActive;
    public bool IsPersistent => true;
    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate()
    {
        if (!_explorerVerb.Install() || !_folderVerb.Install())
        {
            _explorerVerb.Uninstall();
            _folderVerb.Uninstall();
            _context?.Notify("Gezgin menüsü kaydedilemedi. WinForge.exe üzerinden çalıştırın ve mevcut menü kaydını kontrol edin.");
        }
        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
        _explorerVerb.Uninstall();
        _folderVerb.Uninstall();
    }

    public void Apply() { }

    public bool HandleCommand(string command, IReadOnlyList<string> arguments)
    {
        if (!string.Equals(command, "--file-locksmith", StringComparison.OrdinalIgnoreCase)) return false;
        if (arguments.Count != 1 || !(File.Exists(arguments[0]) || Directory.Exists(arguments[0])))
            throw new FileNotFoundException("Dosya kilitlerini bulmak için geçerli bir dosya veya klasör yolu gerekli.");
        new FileLocksmithWindow(arguments[0], (_context as IFeatureUiContext)?.FeatureUi).ShowDialog();
        return true;
    }

    private void Open() => new FileLocksmithWindow(null, (_context as IFeatureUiContext)?.FeatureUi).ShowDialog();
}

internal sealed class FileLocksmithWindow : Window
{
    private readonly List<LockingProcess> _processes = [];
    private readonly IFeatureUiSurface _uiSurface;
    private string _path = "";
    private string _status = "";
    private LockingProcess? _selectedProcess;
    private bool _confirmTerminate;

    public FileLocksmithWindow() : this(null) { }

    internal FileLocksmithWindow(string? initialPath, IFeatureUiService? featureUi = null)
    {
        _uiSurface = featureUi?.CreateSurface("file-locksmith")
            ?? throw new InvalidOperationException("The React file locksmith view is required.");
        _uiSurface.MessageReceived += OnUiMessage;
        Title = "Dosya Kilidi Bulucu";
        Width = 760;
        Height = 460;
        MinWidth = 600;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        Content = _uiSurface.Element;
        Closed += (_, _) => _uiSurface.Dispose();

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            _path = initialPath;
            Scan();
        }
        else PushUiState();
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "file-locksmith.choose": ChooseFile(); break;
            case "file-locksmith.scan": Scan(); break;
            case "file-locksmith.select":
                if (payload.TryGetProperty("processId", out var pid) && pid.TryGetUInt32(out var processId))
                    _selectedProcess = _processes.FirstOrDefault(process => process.ProcessId == processId);
                UpdateTerminateState();
                break;
            case "file-locksmith.terminate":
                _confirmTerminate = false;
                if (payload.TryGetProperty("confirm", out var confirmed) && confirmed.ValueKind == JsonValueKind.True) TerminateSelected(true);
                else PushUiState();
                break;
            case "file-locksmith.close": Close(); break;
        }
    }

    private void PushUiState() => _uiSurface.SetState(new
    {
        path = _path,
        status = _status,
        selectedProcessId = _selectedProcess?.ProcessId,
        canScan = File.Exists(_path) || Directory.Exists(_path),
        canTerminate = _selectedProcess?.CanTerminate == true,
        confirmTerminate = _confirmTerminate,
        processes = _processes.Select(process => new
        {
            process.ProcessId, process.ProcessName, process.ApplicationName, process.Status, process.CanTerminate
        }).ToArray()
    });

    private void ChooseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Kilit kullanan uygulamaları bul",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        _path = dialog.FileName;
        Scan();
    }

    private void Scan()
    {
        if (!File.Exists(_path) && !Directory.Exists(_path))
        {
            _status = "Dosya/klasör bulunamadı veya artık erişilemiyor.";
            PushUiState();
            return;
        }

        _processes.Clear();
        _selectedProcess = null;
        try
        {
            var processes = RestartManagerQuery.FindProcesses(_path);
            foreach (var process in processes.OrderBy(item => item.ProcessName, StringComparer.CurrentCultureIgnoreCase))
                _processes.Add(process);

            _status = processes.Count == 0
                ? "Restart Manager bu dosyayı kullanan bir uygulama veya hizmet bildirmedi."
                : $"{processes.Count} uygulama veya hizmet bulundu. Tarama salt okunurdur; sonlandırma ayrıca onay ister.";
        }
        catch (Exception exception)
        {
            _status = "Sorgu başarısız: " + exception.Message;
        }
        finally
        {
            UpdateTerminateState();
            PushUiState();
        }
    }

    private void UpdateTerminateState()
    {
        PushUiState();
    }

    private void TerminateSelected(bool confirmed = false)
    {
        var selected = _selectedProcess;
        if (selected is null || !selected.CanTerminate) return;

        if (!confirmed)
        {
            _confirmTerminate = true;
            PushUiState();
            return;
        }

        try
        {
            RestartManagerQuery.Terminate(selected);
            _status = $"{selected.ProcessName} sonlandırıldı. Kilit durumunu yeniden tarıyorum…";
            Scan();
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            Scan();
        }
    }
}

internal sealed class LockingProcess(uint processId, string applicationName, string serviceName, string status,
    uint applicationType, long processStartTime)
{
    public uint ProcessId { get; } = processId;
    public string ApplicationName { get; } = string.IsNullOrWhiteSpace(serviceName) ? applicationName : $"{applicationName} · {serviceName}";
    public string ProcessName { get; } = GetProcessName(processId);
    public string Status { get; } = status;
    public uint ApplicationType { get; } = applicationType;
    public long ProcessStartTime { get; } = processStartTime;
    public bool CanTerminate => (ApplicationType == RestartManagerQuery.MainWindowType ||
        ApplicationType == RestartManagerQuery.ConsoleType) && ProcessId != (uint)Environment.ProcessId;
    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return "Erişilemiyor";
        }
    }
}

internal static class RestartManagerQuery
{
    internal const uint MainWindowType = 1;
    internal const uint ConsoleType = 5;
    private const int ErrorMoreData = 234;
    private const int MaximumResults = 1024;
    private const int MaximumFolderFiles = 5000;
    private const uint StatusRunning = 0x01;
    private const uint StatusStopped = 0x02;
    private const uint StatusStoppedOther = 0x04;
    private const uint StatusRestarted = 0x08;
    private const uint StatusErrorOnStop = 0x10;
    private const uint StatusErrorOnRestart = 0x20;
    private const uint StatusShutdownMasked = 0x40;
    private const uint StatusRestartMasked = 0x80;

    public static IReadOnlyList<LockingProcess> FindProcesses(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        var isDirectory = Directory.Exists(fullPath);
        if (!isDirectory && !File.Exists(fullPath)) throw new FileNotFoundException("Dosya bulunamadı.", fullPath);
        var paths = isDirectory ? EnumerateFolderFiles(fullPath) : new[] { fullPath };
        if (paths.Length == 0) return Array.Empty<LockingProcess>();

        var sessionKey = new StringBuilder(33);
        var result = RmStartSession(out var sessionHandle, 0, sessionKey);
        ThrowIfFailed(result, "Restart Manager oturumu başlatılamadı");

        try
        {
            result = RmRegisterResources(sessionHandle, (uint)paths.Length, paths, 0, IntPtr.Zero, 0, IntPtr.Zero);
            ThrowIfFailed(result, "Dosya Restart Manager oturumuna kaydedilemedi");

            uint needed = 0;
            uint count = 0;
            result = RmGetList(sessionHandle, out needed, ref count, null, out _);
            if (result == 0) return Array.Empty<LockingProcess>();
            if (result != ErrorMoreData) ThrowIfFailed(result, "Dosyayı kullanan süreçler sorgulanamadı");

            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (needed == 0 || needed > MaximumResults)
                    throw new InvalidOperationException("Restart Manager beklenmeyen sayıda süreç döndürdü.");

                var entries = new RestartManagerProcessInfo[needed];
                count = needed;
                result = RmGetList(sessionHandle, out needed, ref count, entries, out _);
                if (result == ErrorMoreData) continue;
                ThrowIfFailed(result, "Dosyayı kullanan süreçler listelenemedi");

                return entries.Take((int)count)
                    .Select(entry => new LockingProcess(
                        entry.Process.ProcessId,
                        entry.ApplicationName ?? "Bilinmeyen uygulama",
                        entry.ServiceName ?? "",
                        DescribeStatus(entry.ApplicationStatus),
                        entry.ApplicationType,
                        ToFileTime(entry.Process.ProcessStartTime)))
                    .ToArray();
            }

            throw new IOException("Dosya kullanımı sorgu sırasında sürekli değişti. Yeniden deneyin.");
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    private static string[] EnumerateFolderFiles(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var paths = Directory.EnumerateFiles(folder, "*", options).Take(MaximumFolderFiles + 1).ToArray();
        if (paths.Length > MaximumFolderFiles)
            throw new InvalidOperationException($"Klasör {MaximumFolderFiles} dosya sınırını aşıyor; daha küçük bir klasör seçin.");
        return paths;
    }

    public static void Terminate(LockingProcess target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.CanTerminate) throw new InvalidOperationException("Bu süreç türü güvenlik nedeniyle sonlandırılamaz.");

        using var process = Process.GetProcessById(checked((int)target.ProcessId));
        if (process.StartTime.ToUniversalTime().ToFileTimeUtc() != target.ProcessStartTime)
            throw new InvalidOperationException("Süreç kimliği değişmiş. Yeniden tarayıp tekrar deneyin.");
        using var currentProcess = Process.GetCurrentProcess();
        if (process.SessionId != currentProcess.SessionId)
            throw new InvalidOperationException("Başka bir kullanıcı oturumundaki süreç sonlandırılamaz.");

        process.Kill();
        if (!process.WaitForExit(3000)) throw new IOException("Süreç sonlandırma isteğine zamanında yanıt vermedi.");
    }

    private static long ToFileTime(FILETIME value) => ((long)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    private static string DescribeStatus(uint status)
    {
        var labels = new List<string>();
        if ((status & StatusRunning) != 0) labels.Add("çalışıyor");
        if ((status & StatusStopped) != 0) labels.Add("Restart Manager tarafından durduruldu");
        if ((status & StatusStoppedOther) != 0) labels.Add("başka bir işlem tarafından durduruldu");
        if ((status & StatusRestarted) != 0) labels.Add("yeniden başlatıldı");
        if ((status & StatusErrorOnStop) != 0) labels.Add("durdurma hatası");
        if ((status & StatusErrorOnRestart) != 0) labels.Add("yeniden başlatma hatası");
        if ((status & StatusShutdownMasked) != 0) labels.Add("durdurma filtreyle engellendi");
        if ((status & StatusRestartMasked) != 0) labels.Add("yeniden başlatma filtreyle engellendi");
        return labels.Count == 0 ? "bilinmiyor" : string.Join(", ", labels);
    }

    private static void ThrowIfFailed(int error, string message)
    {
        if (error != 0) throw new System.ComponentModel.Win32Exception(error, message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RestartManagerUniqueProcess
    {
        public uint ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestartManagerProcessInfo
    {
        public RestartManagerUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? ServiceName;
        public uint ApplicationType;
        public uint ApplicationStatus;
        public uint TerminalSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint fileCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[]? fileNames,
        uint applicationCount, IntPtr applications, uint serviceCount, IntPtr services);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint sessionHandle, out uint processInfoNeeded, ref uint processInfoCount,
        [In, Out] RestartManagerProcessInfo[]? affectedApplications, out uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);
}

