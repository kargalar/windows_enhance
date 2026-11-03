namespace WinForge.History;

public sealed record NetworkCounters(string Id, long Received, long Sent);

/// <summary>Uses per-adapter deltas so reconnects and counter resets cannot inflate usage.</summary>
public sealed class NetworkUsageTracker(Action<DateTimeOffset, DateTimeOffset, long, long> write)
{
    private Dictionary<string, NetworkCounters> previous = new();
    private DateTimeOffset started;
    private long? previousTick;

    public void Reset()
    {
        previous.Clear(); previousTick = null;
    }

    public void Observe(IEnumerable<NetworkCounters> counters, DateTimeOffset now, long tick)
    {
        var current = counters.ToDictionary(row => row.Id);
        if (previousTick is { } last && tick > last && tick - last <= 30_000 && now > started)
        {
            long received = 0, sent = 0;
            foreach (var row in current.Values)
            {
                if (!previous.TryGetValue(row.Id, out var old)) continue;
                // A reset invalidates both counters for this adapter until the next observation.
                if (row.Received < old.Received || row.Sent < old.Sent) continue;
                received += row.Received - old.Received; sent += row.Sent - old.Sent;
            }
            if (received > 0 || sent > 0) write(started, now, received, sent);
        }
        previous = current; started = now; previousTick = tick;
    }
}
