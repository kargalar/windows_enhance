using System.Text;

namespace WinForge.Core;

public sealed class FileLogger : ILogger, IDisposable
{
    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly Queue<string> _queue = new();
    private readonly bool _asynchronous;
    private readonly int _capacity;
    private readonly long _maximumFileBytes;
    private readonly int _retainedFiles;
    private Task _writer = Task.CompletedTask;
    private bool _writerRunning;
    private bool _disposed;
    private long _dropped;
    private long _failed;
    private string? _lastError;
    public string Path { get; }
    public long DroppedMessages => Interlocked.Read(ref _dropped);
    public long FailedMessages => Interlocked.Read(ref _failed);
    public string? LastError => Volatile.Read(ref _lastError);
    public event Action<string>? Written;

    // Synchronous mode preserves existing small, short-lived fixture callers.
    // The application opts into the bounded, single background writer.
    public FileLogger(string path, bool asynchronous = false, int capacity = 1024,
        long maximumFileBytes = 2 * 1024 * 1024, int retainedFiles = 3)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumFileBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        if (retainedFiles < 1) throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        Path = path;
        _asynchronous = asynchronous;
        _capacity = capacity;
        _maximumFileBytes = maximumFileBytes;
        _retainedFiles = retainedFiles;
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");
        if (exception is not null) Write("TRACE", exception.StackTrace ?? "");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        var maximumCharacters = (int)Math.Min(4096, _maximumFileBytes / 4 - 4);
        if (line.Length > maximumCharacters) line = line[..(maximumCharacters - 1)] + "…";
        lock (_gate)
        {
            if (_disposed) return;
            if (!_asynchronous) { Persist([line]); return; }
            if (_queue.Count >= _capacity) { Interlocked.Increment(ref _dropped); return; }
            _queue.Enqueue(line);
            if (!_writerRunning)
            {
                _writerRunning = true;
                _writer = Task.Run(Drain);
            }
        }
    }

    private void Drain()
    {
        while (true)
        {
            List<string> batch = new(128);
            lock (_gate)
            {
                if (_queue.Count == 0) { _writerRunning = false; return; }
                while (batch.Count < 128 && _queue.TryDequeue(out var line)) batch.Add(line);
            }
            Persist(batch);
        }
    }

    private void Persist(IReadOnlyList<string> lines)
    {
        lock (_fileGate)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var length = File.Exists(Path) ? new FileInfo(Path).Length : 0;
                var pending = new StringBuilder();
                long pendingBytes = 0;
                foreach (var line in lines)
                {
                    var text = line + Environment.NewLine;
                    var bytes = Encoding.UTF8.GetByteCount(text);
                    if (length + pendingBytes + bytes > _maximumFileBytes && length + pendingBytes > 0)
                    {
                        if (pending.Length > 0) File.AppendAllText(Path, pending.ToString());
                        Rotate();
                        pending.Clear(); pendingBytes = 0; length = 0;
                    }
                    pending.Append(text); pendingBytes += bytes;
                }
                if (pending.Length > 0) File.AppendAllText(Path, pending.ToString());
                Volatile.Write(ref _lastError, null);
            }
            catch (Exception error)
            {
                Interlocked.Add(ref _failed, lines.Count);
                Volatile.Write(ref _lastError, error.Message);
            }
        }
        foreach (var line in lines)
        {
            if (Written is not { } observers) continue;
            foreach (Action<string> observer in observers.GetInvocationList())
                try { observer(line); } catch { /* Observers cannot stop the writer. */ }
        }
    }

    private void Rotate()
    {
        for (var index = _retainedFiles - 1; index >= 1; index--)
            if (File.Exists(Path + "." + index)) File.Move(Path + "." + index, Path + "." + (index + 1), true);
        if (File.Exists(Path)) File.Move(Path, Path + ".1", true);
    }

    public Task FlushAsync() { lock (_gate) return _writer; }
    public void Flush() => FlushAsync().GetAwaiter().GetResult();
    public void Dispose()
    {
        Task writer;
        lock (_gate) { if (_disposed) return; _disposed = true; writer = _writer; }
        writer.GetAwaiter().GetResult();
    }
}
