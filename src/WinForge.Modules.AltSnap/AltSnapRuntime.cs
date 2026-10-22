using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace WinForge.Modules.AltSnap;

internal sealed class AltSnapRuntime : IDisposable
{
    internal const string Version = "1.68";
    private const string BinaryArchive = "AltSnap1.68bin_x64.zip";
    private const string SourceArchive = "AltSnap1.68src.zip";
    private const string BinaryHash = "7DB3DAD3746E7B23857DB92FC05EE2D3E17EEE1B28E237709A612366BA909C79";
    private const string SourceHash = "0881CCC46BFDFC244BF82E5C0DE5B22BDC631A00B98674CA784794728314AB8F";
    private const int ShutdownTimeoutMilliseconds = 2000;
    private const int StartupTimeoutMilliseconds = 3000;
    private Process? _process;
    private SafeFileHandle? _job;

    internal AltSnapRuntime(string? directory = null) => DirectoryPath = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "AltSnap", Version);

    internal string DirectoryPath { get; }
    internal string ConfigurationPath => Path.Combine(DirectoryPath, "AltSnap.ini");
    internal bool IsRunning => _process is { HasExited: false };

    internal AltSnapConfiguration ReadConfiguration()
    {
        if (File.Exists(ConfigurationPath)) return new AltSnapConfiguration(File.ReadAllText(ConfigurationPath));
        using var archive = new ZipArchive(ReadVerifiedResource(BinaryArchive, BinaryHash));
        using var reader = new StreamReader(archive.GetEntry("AltSnap.ini")!.Open());
        var configuration = new AltSnapConfiguration(reader.ReadToEnd());
        configuration.Set("Input", "Hotkeys", "A4");
        configuration.Set("Input", "ModKey", "5B");
        configuration.Set("Advanced", "ResizeAll", "0");
        return configuration;
    }

    internal void Prepare()
    {
        var initialConfiguration = File.Exists(ConfigurationPath) ? null : ReadConfiguration();
        Directory.CreateDirectory(DirectoryPath);
        using var archive = new ZipArchive(ReadVerifiedResource(BinaryArchive, BinaryHash));
        foreach (var entry in archive.Entries)
        {
            // Preserve the user's portable configuration across restarts and upgrades.
            if (entry.FullName == "AltSnap.ini" && File.Exists(ConfigurationPath)) continue;
            var path = Path.GetFullPath(Path.Combine(DirectoryPath, entry.FullName));
            var root = Path.GetFullPath(DirectoryPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Geçersiz AltSnap arşiv yolu.");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
        initialConfiguration?.Save(ConfigurationPath);
        using var source = ReadVerifiedResource(SourceArchive, SourceHash);
        using var output = File.Create(Path.Combine(DirectoryPath, SourceArchive));
        source.CopyTo(output);
    }

    internal void Start()
    {
        if (IsRunning) return;
        EnsureNoOtherInstance();
        _job = AltSnapNative.CreateLifetimeJob();
        try
        {
            var info = new ProcessStartInfo(Path.Combine(DirectoryPath, "AltSnap.exe"))
            {
                UseShellExecute = false,
                WorkingDirectory = DirectoryPath
            };
            // Never route commands to a standalone AltSnap instance; WinForge owns this PID.
            info.ArgumentList.Add("-m");
            info.ArgumentList.Add("-h");
            _process = Process.Start(info) ?? throw new InvalidOperationException("AltSnap başlatılamadı.");
            AltSnapNative.Assign(_job, _process);
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < StartupTimeoutMilliseconds)
            {
                if (_process.HasExited) throw new InvalidOperationException("AltSnap başlangıçta kapandı.");
                if (AltSnapNative.FindWindow(_process.Id) != IntPtr.Zero) return;
                Thread.Sleep(20);
            }
            throw new TimeoutException("AltSnap denetim penceresi açılamadı.");
        }
        catch { Dispose(); throw; }
    }

    internal void Send(uint message, int parameter = 0)
    {
        if (!IsRunning) throw new InvalidOperationException("Önce AltSnap modülünü etkinleştirin.");
        AltSnapNative.Send(AltSnapNative.FindWindow(_process!.Id), message, parameter);
    }

    internal static void EnsureNoOtherInstance()
    {
        var processes = Process.GetProcessesByName("AltSnap");
        try
        {
            if (processes.Any(process => !process.HasExited))
                throw new InvalidOperationException("AltSnap zaten çalışıyor. Fare kancalarının çakışmaması için mevcut AltSnap'i kapatıp modülü yeniden açın.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                // WM_CLOSE lets AltSnap unhook and remove its tray icon cleanly.
                var window = AltSnapNative.FindWindow(_process.Id);
                if (window != IntPtr.Zero) AltSnapNative.Send(window, 0x0010);
                if (window == IntPtr.Zero || !_process.WaitForExit(ShutdownTimeoutMilliseconds))
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(ShutdownTimeoutMilliseconds);
                }
            }
        }
        finally
        {
            // This job also removes the engine if WinForge terminates unexpectedly.
            _job?.Dispose();
            _job = null;
            _process?.Dispose();
            _process = null;
        }
    }

    private static Stream ReadVerifiedResource(string name, string expectedHash)
    {
        var assembly = typeof(AltSnapRuntime).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith(name, StringComparison.Ordinal));
        var stream = assembly.GetManifestResourceStream(resource)!;
        if (Convert.ToHexString(SHA256.HashData(stream)) != expectedHash)
        {
            stream.Dispose();
            throw new InvalidDataException("AltSnap paketinin SHA-256 doğrulaması başarısız.");
        }
        stream.Position = 0;
        return stream;
    }
}
