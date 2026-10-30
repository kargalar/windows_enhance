namespace WinForge.History;

/// <summary>Elapsed monotonic time is independent from capture count and wall-clock adjustments.</summary>
public sealed class UsageTracker(Action<string, DateTimeOffset, DateTimeOffset, double> write)
{
    private string? app;
    private DateTimeOffset started;
    private long tick;
    public void Observe(string? activeApp, DateTimeOffset now, long monotonicMilliseconds)
    {
        if (app == activeApp) return;
        Flush(now, monotonicMilliseconds);
        app = activeApp; started = now; tick = monotonicMilliseconds;
    }
    public void Flush(DateTimeOffset now, long monotonicMilliseconds)
    {
        if (app is not null) write(app, started, now, Math.Max(0, monotonicMilliseconds - tick) / 1000d);
        started = now; tick = monotonicMilliseconds;
    }
}
