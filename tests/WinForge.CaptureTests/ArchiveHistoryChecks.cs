using WinForge.History;

internal static class ArchiveHistoryChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var store = new HistoryStore(Path.Combine(root, "archive-statistics"));
        var start = new DateTimeOffset(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
        store.Add("recall", start, "editor.exe", media: "image.webp", seconds: 60, estimated: true, metadata: "{\"is_idle\":\"0\"}");
        store.Add("recall", start.AddMinutes(1), "editor.exe", seconds: 60, estimated: true, metadata: "{\"is_idle\":\"1\"}");
        store.Add("recall", start.AddMinutes(2), "browser.exe", seconds: 60, estimated: true, metadata: "{\"is_idle\":true}");
        store.Add("recall", start.AddMinutes(3), "broken-metadata.exe", seconds: 60, estimated: true, metadata: "legacy text");
        store.Add("dictation", start, text: "Eski dikte kaydı");
        var summary = store.RecallSummary();
        check(summary.ActiveSeconds == 120 && summary.IdleSeconds == 120,
            "Archived idle flags (strings and booleans) are separated from active usage");
        check(summary.Captures == 1, "Screenshot count excludes legacy idle rows without images");
        check(store.UsageByDay().Sum(row => row.Seconds) == 120 && store.IdleUsageByDay().Sum(row => row.Seconds) == 120,
            "Daily archived usage agrees with active and idle totals");
        store.SetTags("editor.exe", ["Work"]);
        check(store.TagUsage().Single().Seconds == 60, "Archived idle rows do not inflate application tag totals");
        store.AddUsage("measured.exe", start, start.AddMinutes(1), 60);
        store.AddUsage("System Idle", start.AddMinutes(1), start.AddMinutes(2), 60);
        summary = store.RecallSummary();
        check(summary.ActiveSeconds == 120 && summary.IdleSeconds == 120,
            "Measured usage takes precedence when both recorders cover the same timestamp");
        check(store.UsageByDay().Sum(row => row.Seconds) == 120 && store.IdleUsageByDay().Sum(row => row.Seconds) == 120,
            "Daily totals avoid counting overlapping old and new recorders twice");
        summary = store.RecallSummary(start.AddMinutes(1), start.AddMinutes(3));
        check(summary.ActiveSeconds == 0 && summary.IdleSeconds == 120 && summary.Captures == 0,
            "Archive statistics respect date filters for measured and imported idle time");
        check(store.Search(new("dictation", "dikte")).Count == 1,
            "Archived dictation remains searchable independently of screenshot history");
    }
}
