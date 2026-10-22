namespace WinForge.Core;

public sealed class FileLogger : ILogger
{
    private readonly object _gate = new();

    public string Path { get; }

    public event Action<string>? Written;

    public FileLogger(string path)
    {
        Path = path;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text);
        if (exception is not null) Write("TRACE", exception.StackTrace ?? "");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never throw.
            }
        }

        try { Written?.Invoke(line); } catch { }
    }
}
