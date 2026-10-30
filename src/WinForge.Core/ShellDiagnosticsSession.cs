using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace WinForge.Core;

public sealed record ShellStyleRule(string Selector, IReadOnlyDictionary<string, string> Properties);
public sealed record ShellVisualNode(ulong Handle, ulong Parent, string Type, string Name,
    string AutomationId, string AccessibleName, double Width, double Height, double Opacity, string Visibility, string Text, string Fill,
    double ScreenLeft, double ScreenTop, double ScreenWidth, double ScreenHeight, double HostHeight = 0);
public sealed record ShellDiagnosticsStatus(int Attached, int Elements, int Changes, int Errors, int Restored, uint ProcessId, int AppliedRevision, int LastError, string LastErrorMessage);

/// <summary>Owns the heartbeat and bounded protocol of a WinForge native XAML diagnostics session.</summary>
public sealed class ShellDiagnosticsSession : IDisposable
{
    private const int MappingBytes = 1048576;
    private const int PayloadOffset = 1024;
    private const int SnapshotOffset = 65536;
    private const uint Magic = 0x57465348;
    private const uint Version = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly object _gate = new();
    private readonly MemoryMappedFile _mapping;
    private readonly FileStream _mappingFile;
    private readonly string _mappingPath;
    private readonly MemoryMappedViewAccessor _view;
    private readonly Timer _heartbeat;
    private int _revision;
    private bool _disposed;

    private ShellDiagnosticsSession()
    {
        var directory = Path.Combine(WinForge.Core.AppData.DirectoryPath, "ShellEngine", "Sessions");
        Directory.CreateDirectory(directory);
        _mappingPath = Path.Combine(directory, $"{Environment.ProcessId}-{Guid.NewGuid():N}.bin");
        _mappingFile = new FileStream(_mappingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        _mappingFile.SetLength(MappingBytes);
        MappingName = PhysicalPath(_mappingFile.SafeFileHandle);
        _mapping = MemoryMappedFile.CreateFromFile(_mappingFile, null, MappingBytes, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
        _view = _mapping.CreateViewAccessor(0, MappingBytes);
        _view.Write(0, Magic);
        _view.Write(4, Version);
        Apply(false, []);
        Beat();
        _heartbeat = new Timer(_ => Beat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public string MappingName { get; }

    public bool IsConnected
    {
        get { lock (_gate) return !_disposed && _view.ReadInt32(24) > 0 && _view.ReadInt32(60) == 0; }
    }

    public static async Task<ShellDiagnosticsSession> ConnectAsync(uint processId, string nativeDirectory, int endpoint = 0,
        bool winUi = false, CancellationToken cancellation = default)
    {
        var host = Path.GetFullPath(Path.Combine(nativeDirectory, "WinForge.ShellHost.exe"));
        var library = Path.GetFullPath(Path.Combine(nativeDirectory, "WinForge.ShellTap.dll"));
        if (!File.Exists(host) || !File.Exists(library)) throw new FileNotFoundException("WinForge yerel kabuk bileşenleri bulunamadı.");
        var cachedLibrary = CacheLibrary(library);
        var session = new ShellDiagnosticsSession();
        try
        {
            var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "attach", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), cachedLibrary, session.MappingName })
                info.ArgumentList.Add(argument);
            if (endpoint > 0 || winUi) info.ArgumentList.Add(endpoint.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (winUi) info.ArgumentList.Add("winui");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("WinForge kabuk bağlantısı başlatılamadı.");
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            try { await process.WaitForExitAsync(cancellation).WaitAsync(TimeSpan.FromSeconds(15), cancellation).ConfigureAwait(false); }
            catch { if (!process.HasExited) process.Kill(); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException($"Windows XAML bağlantısı kurulamadı: {(await output.ConfigureAwait(false)).Trim()} {(await error.ConfigureAwait(false)).Trim()}".Trim());
            var clock = Stopwatch.StartNew();
            while (session.Status.Attached == 0 && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(50, cancellation).ConfigureAwait(false);
            if (session.Status.Attached == 0) throw new TimeoutException("Windows, WinForge kabuk bağlantısını doğrulamadı.");
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static string CacheLibrary(string source)
    {
        using var stream = File.OpenRead(source);
        var digest = Convert.ToHexString(SHA256.HashData(stream));
        var directory = Path.Combine(WinForge.Core.AppData.DirectoryPath, "ShellEngine", digest);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "WinForge.ShellTap.dll");
        if (!File.Exists(target))
        {
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary);
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        using var cached = File.OpenRead(target);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(cached), Convert.FromHexString(digest)))
            throw new InvalidDataException("Önbellekteki WinForge kabuk bileşeni doğrulanamadı.");
        return PhysicalPath(cached.SafeFileHandle);
    }

    private static string PhysicalPath(SafeFileHandle handle)
    {
        var path = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint characters, uint flags);

    public int Apply(bool active, IReadOnlyList<ShellStyleRule> rules)
    {
        var payload = Encoding.Unicode.GetBytes(JsonSerializer.Serialize(new { active, rules }, JsonOptions));
        if (payload.Length > SnapshotOffset - PayloadOffset) throw new ArgumentOutOfRangeException(nameof(rules), "Kabuk stili protokol sınırını aşıyor.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _view.Write(8, ++_revision);
            _view.WriteArray(PayloadOffset, payload, 0, payload.Length);
            _view.Write(12, payload.Length / sizeof(char));
            Thread.MemoryBarrier();
            _view.Write(8, ++_revision);
            return _revision;
        }
    }

    private void Beat()
    {
        lock (_gate) { if (!_disposed) _view.Write(16, unchecked((ulong)Environment.TickCount64)); }
    }

    public ShellDiagnosticsStatus Status
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var error = new byte[512];
                _view.ReadArray(68, error, 0, error.Length);
                var message = Encoding.Unicode.GetString(error);
                var end = message.IndexOf('\0');
                return new(_view.ReadInt32(24), _view.ReadInt32(28), _view.ReadInt32(32), _view.ReadInt32(36), _view.ReadInt32(40), _view.ReadUInt32(44), _view.ReadInt32(56), _view.ReadInt32(64), end >= 0 ? message[..end] : message);
            }
        }
    }

    public IReadOnlyList<ShellVisualNode> ReadVisualTree()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var first = _view.ReadInt32(52);
            if ((first & 1) != 0) return [];
            var characters = _view.ReadInt32(48);
            if (characters <= 0 || characters > (MappingBytes - SnapshotOffset) / sizeof(char)) return [];
            var bytes = new byte[characters * sizeof(char)];
            _view.ReadArray(SnapshotOffset, bytes, 0, bytes.Length);
            Thread.MemoryBarrier();
            if (first != _view.ReadInt32(52)) return [];
            try { return JsonSerializer.Deserialize<ShellVisualNode[]>(Encoding.Unicode.GetString(bytes), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? []; }
            catch (JsonException) { return []; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Apply(false, []);
            _view.Write(16, 0UL);
            if (_view.ReadInt32(24) > 0)
            {
                var clock = Stopwatch.StartNew();
                while (_view.ReadInt32(60) == 0 && clock.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(10);
            }
            _disposed = true;
            _heartbeat.Dispose();
            _view.Dispose();
            _mapping.Dispose();
            _mappingFile.Dispose();
            File.Delete(_mappingPath);
        }
    }
}
