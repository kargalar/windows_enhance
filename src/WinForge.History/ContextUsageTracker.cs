namespace WinForge.History;

/// <summary>Tracks time spent in the same page or folder independently from screenshot cadence.</summary>
public sealed class ContextUsageTracker(Action<string, string, string, DateTimeOffset, DateTimeOffset, double> write)
{
    private string? app;
    private string? type;
    private string? context;
    private DateTimeOffset started;
    private long tick;

    public void Observe(string? activeApp, string? activeType, string? activeContext, DateTimeOffset now, long monotonicMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(activeApp) || string.IsNullOrWhiteSpace(activeType) || string.IsNullOrWhiteSpace(activeContext))
            activeApp = activeType = activeContext = null;
        if (app == activeApp && type == activeType && context == activeContext) return;
        Flush(now, monotonicMilliseconds);
        app = activeApp; type = activeType; context = activeContext; started = now; tick = monotonicMilliseconds;
    }

    public void Flush(DateTimeOffset now, long monotonicMilliseconds)
    {
        if (app is not null && type is not null && context is not null)
            write(app, type, context, started, now, Math.Max(0, monotonicMilliseconds - tick) / 1000d);
        started = now; tick = monotonicMilliseconds;
    }
}
