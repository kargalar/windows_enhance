using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WinForge.Core;

public sealed record ExplorerFolderSizeStatus(bool Attached, bool Closed, int Error, string Message,
    int Requests, int CacheHits, int Completed, int Incomplete, int Pending, int Callbacks,
    int PriorityScans, int SubtreeCacheHits, int IncrementalUpdates, int FullScans,
    int WatchEvents, int WatcherOverflows, int ActiveWatchers, long ScanMicroseconds);

/// <summary>Owns an independent, heartbeat-protected Explorer property callback and native scan worker.</summary>
public sealed class ExplorerFolderSizeSession : IDisposable
{
    private const int SessionBytes = 4096;
    private const int HeartbeatOffset = 8;
    private const int ActiveOffset = 16;
    private const int AttachedOffset = 20;
    private const int ClosedOffset = 24;
    private const int ErrorOffset = 28;
    private const int ScanSecondsOffset = 100;
    private const int CacheSecondsOffset = 104;
    private const int StatisticsOffset = 108;
    private const int MessageOffset = 168;
    private const int MessageBytes = 512;
    private const uint Magic = 0x57464653;
    private const uint ProtocolVersion = 3;
    private static readonly SemaphoreSlim PreparationGate = new(1, 1);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileStream _file;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Timer _heartbeat;
    private bool _disposed;

    private ExplorerFolderSizeSession(int scanSeconds, int cacheSeconds)
    {
        var directory = Path.Combine(AppData.DirectoryPath, "FolderSizes", "Sessions");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"{Environment.ProcessId}-{Guid.NewGuid():N}.bin");
        _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        _file.SetLength(SessionBytes);
        _mapping = MemoryMappedFile.CreateFromFile(_file, null, SessionBytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, true);
        _view = _mapping.CreateViewAccessor();
        _view.Write(0, Magic);
        _view.Write(4, ProtocolVersion);
        _view.Write(ActiveOffset, 1);
        _view.Write(ScanSecondsOffset, Math.Clamp(scanSeconds, 5, 120));
        _view.Write(CacheSecondsOffset, Math.Clamp(cacheSeconds, 5, 300));
        Beat();
        _heartbeat = new Timer(_ => Beat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public static async Task<ExplorerFolderSizeSession> ConnectAsync(uint processId, string nativeDirectory,
        int scanSeconds = 30, int cacheSeconds = 60, CancellationToken cancellation = default)
    {
        var host = Path.GetFullPath(Path.Combine(nativeDirectory, "WinForge.FolderSizeHost.exe"));
        var source = Path.GetFullPath(Path.Combine(nativeDirectory, "WinForge.FolderSizeTap.dll"));
        if (!File.Exists(host) || !File.Exists(source)) throw new FileNotFoundException("WinForge klasör boyutu bileşenleri bulunamadı.");
        var library = CacheLibrary(source);
        var session = new ExplorerFolderSizeSession(scanSeconds, cacheSeconds);
        try
        {
            var symbols = Path.Combine(AppData.DirectoryPath, "FolderSizes", "Symbols");
            await PreparationGate.WaitAsync(cancellation).ConfigureAwait(false);
            try { await RunHostAsync(host, ["prepare", symbols, session.PhysicalPath()], TimeSpan.FromMinutes(3), cancellation).ConfigureAwait(false); }
            finally { PreparationGate.Release(); }
            await RunHostAsync(host, ["attach", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), library, session.PhysicalPath()],
                TimeSpan.FromSeconds(20), cancellation).ConfigureAwait(false);
            var clock = Stopwatch.StartNew();
            while (!session.Status.Attached && session.Status.Error == 0 && clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50, cancellation).ConfigureAwait(false);
            if (!session.Status.Attached || session.Status.Error != 0)
                throw new InvalidOperationException($"Explorer klasör boyutu bağlantısı kurulamadı: {session.Status.Message}");
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    public ExplorerFolderSizeStatus Status
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var message = new byte[MessageBytes];
                _view.ReadArray(MessageOffset, message, 0, message.Length);
                var text = Encoding.Unicode.GetString(message).TrimEnd('\0');
                return new(_view.ReadInt32(AttachedOffset) != 0, _view.ReadInt32(ClosedOffset) != 0,
                    _view.ReadInt32(ErrorOffset), text, _view.ReadInt32(StatisticsOffset), _view.ReadInt32(StatisticsOffset + 4),
                    _view.ReadInt32(StatisticsOffset + 8), _view.ReadInt32(StatisticsOffset + 12),
                    _view.ReadInt32(StatisticsOffset + 16), _view.ReadInt32(StatisticsOffset + 20),
                    _view.ReadInt32(StatisticsOffset + 24), _view.ReadInt32(StatisticsOffset + 28),
                    _view.ReadInt32(StatisticsOffset + 32), _view.ReadInt32(StatisticsOffset + 36),
                    _view.ReadInt32(StatisticsOffset + 40), _view.ReadInt32(StatisticsOffset + 44),
                    _view.ReadInt32(StatisticsOffset + 48), _view.ReadInt64(StatisticsOffset + 52));
            }
        }
    }

    public bool IsConnected { get { var status = Status; return status.Attached && !status.Closed && status.Error == 0; } }

    private static async Task RunHostAsync(string host, string[] arguments, TimeSpan timeout, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Klasör boyutu bağlantı işlemi başlatılamadı.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var error = process.StandardError.ReadToEndAsync(cancellation);
        try { await process.WaitForExitAsync(cancellation).WaitAsync(timeout, cancellation).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        var standardOutput = await output.ConfigureAwait(false);
        var standardError = await error.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException($"WinForge klasör boyutu bağlantısı: {standardError.Trim()} {standardOutput.Trim()}".Trim());
    }

    private static string CacheLibrary(string source)
    {
        using var input = File.OpenRead(source);
        var digest = SHA256.HashData(input);
        var directory = Path.Combine(AppData.DirectoryPath, "FolderSizes", "Engine", Convert.ToHexString(digest));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "WinForge.FolderSizeTap.dll");
        if (!File.Exists(destination))
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary);
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        using var cached = File.OpenRead(destination);
        if (!CryptographicOperations.FixedTimeEquals(digest, SHA256.HashData(cached)))
            throw new InvalidDataException("WinForge klasör boyutu bileşeninin önbellek doğrulaması başarısız.");
        return Path.GetFullPath(destination);
    }

    private string PhysicalPath()
    {
        var path = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(_file.SafeFileHandle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }

    private void Beat()
    {
        lock (_gate) { if (!_disposed) _view.Write(HeartbeatOffset, unchecked((ulong)Environment.TickCount64)); }
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _view.Write(ActiveOffset, 0);
            _view.Write(HeartbeatOffset, 0UL);
        }
        try
        {
            var clock = Stopwatch.StartNew();
            while (Status.Attached && !Status.Closed && clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50).ConfigureAwait(false);
            var status = Status;
            if (status.Attached && !status.Closed) throw new TimeoutException("Explorer klasör boyutu bağlantısı zamanında kapatılamadı.");
            if (status.Error != 0) throw new InvalidOperationException($"Explorer klasör boyutu bağlantısı geri alınamadı: {status.Message}");
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _view.Write(ActiveOffset, 0);
            _view.Write(HeartbeatOffset, 0UL);
            _disposed = true;
            _heartbeat.Dispose();
            _view.Dispose();
            _mapping.Dispose();
            _file.Dispose();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint characters, uint flags);
}
