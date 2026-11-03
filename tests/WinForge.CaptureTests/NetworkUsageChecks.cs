using WinForge.History;

internal static class NetworkUsageChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var rows = new List<(long Received, long Sent)>();
        var tracker = new NetworkUsageTracker((_, _, received, sent) => rows.Add((received, sent)));
        var now = DateTimeOffset.UtcNow;
        void Observe(int seconds, params NetworkCounters[] counters) => tracker.Observe(counters, now.AddSeconds(seconds), seconds * 1000L);
        Observe(0, new NetworkCounters("wifi", 1000, 500));
        check(rows.Count == 0, "Network tracker ignores traffic before activation");
        Observe(5, new NetworkCounters("wifi", 1300, 600), new NetworkCounters("ethernet", 50000, 8000));
        check(rows.SequenceEqual(new[] { (300L, 100L) }), "New adapters establish a baseline without inflating traffic");
        Observe(10, new NetworkCounters("wifi", 20, 10), new NetworkCounters("ethernet", 50100, 8050));
        check(rows.Last() == (100L, 50L), "Counter resets are excluded independently for each adapter");
        Observe(15, new NetworkCounters("wifi", 30, 20));
        check(rows.Last() == (10L, 10L), "Disconnected adapters do not subtract from traffic");
        tracker.Reset(); Observe(20, new NetworkCounters("wifi", 9000, 9000));
        check(rows.Count == 3, "Pause and resume do not include unobserved traffic");
        Observe(60, new NetworkCounters("wifi", 10000, 10000));
        check(rows.Count == 3, "Sleep and long sampling gaps establish a fresh baseline");
        Observe(65, new NetworkCounters("wifi", 10100, 10200));
        check(rows.Last() == (100L, 200L), "Traffic measurement resumes after sleep");

        var directory = Path.Combine(root, "network");
        var store = new HistoryStore(directory);
        var date = new DateTime(2026, 1, 2);
        var midnight = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        store.AddNetworkUsage(midnight.AddSeconds(-5), midnight.AddSeconds(5), 1000, 400);
        var days = store.NetworkUsageByDay();
        check(days.Count == 2 && days.All(row => row.Received == 500 && row.Sent == 200), "Network usage splits at local midnight");
        var clipped = store.NetworkUsageByDay(midnight, midnight.AddSeconds(2)).Single();
        check(clipped.Received == 200 && clipped.Sent == 80, "Date filters proportionally clip network samples");
        store.AddNetworkUsage(midnight.AddSeconds(5), midnight.AddSeconds(10), 600, 300);
        store.AddNetworkUsage(midnight.AddSeconds(10), midnight.AddSeconds(15), 400, 200);
        var reopened = new HistoryStore(directory);
        check(reopened.NetworkUsageByDay(midnight).Single().Received == 1500 && reopened.NetworkUsageByDay(midnight).Single().Sent == 700,
            "Compacted traffic samples survive reopening the history store");
        check(reopened.NetworkUsageByDay(midnight.AddDays(1)).Count == 0, "Empty network date ranges return no rows");
        var module = new WinForge.Modules.Capture.RecallModule();
        check(module.Id == "screen-history" && module.Name == "PC Geçmişi", "PC History preserves the existing module identity");
    }
}

