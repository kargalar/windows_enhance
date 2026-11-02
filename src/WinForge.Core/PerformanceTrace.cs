using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace WinForge.Core;

/// <summary>Opt-in, bounded local timing trace. Never records settings or user text.</summary>
public static class PerformanceTrace
{
    private const int Capacity = 4096;
    private static readonly ConcurrentQueue<Entry> Entries = new();
    private static readonly object WriteGate = new();
    private static readonly string? OutputPath = Environment.GetEnvironmentVariable("WINFORGE_PERF_PATH");
    private static readonly Timer? FlushTimer = string.IsNullOrWhiteSpace(OutputPath) ? null :
        new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    private static int pending;
    private static long dropped;
    public static bool Enabled => !string.IsNullOrWhiteSpace(OutputPath);
    public static long DroppedEntries => Interlocked.Read(ref dropped);
    public static IDisposable Measure(string operation, string? component = null) =>
        Enabled ? new Measurement(operation, component) : Empty.Instance;

    public static void Record(string operation, double milliseconds, string? component = null, long? bytes = null)
    {
        if (!Enabled) return;
        if (Interlocked.Increment(ref pending) > Capacity)
        {
            Interlocked.Decrement(ref pending);
            Interlocked.Increment(ref dropped);
            return;
        }
        Entries.Enqueue(new(DateTimeOffset.UtcNow, operation, component, milliseconds, bytes, Environment.ProcessId));
    }

    public static void Flush()
    {
        if (!Enabled) return;
        lock (WriteGate)
        {
            var lines = new List<string>();
            while (Entries.TryDequeue(out var entry))
            {
                Interlocked.Decrement(ref pending);
                lines.Add(JsonSerializer.Serialize(entry));
            }
            if (lines.Count == 0) return;
            try
            {
                var path = Path.GetFullPath(OutputPath!);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllLines(path, lines);
            }
            catch { Interlocked.Add(ref dropped, lines.Count); }
        }
    }

    private sealed record Entry(DateTimeOffset Utc, string Operation, string? Component, double Milliseconds, long? Bytes, int ProcessId);
    private sealed class Empty : IDisposable
    {
        public static readonly Empty Instance = new();
        public void Dispose() { }
    }
    private sealed class Measurement(string operation, string? component) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                Record(operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds, component);
        }
    }
}
