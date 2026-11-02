using System.Diagnostics;
using System.Text.Json;
using WinForge.History;

internal static class HistoryPerformanceChecks
{
    public static void Run(string root, string outputPath, Action<bool, string> check)
    {
        var results = new List<object>();
        var violations = new List<string>();
        var documentBody = string.Concat(Enumerable.Repeat("İstanbul çalışma notları pencere yönetimi dosya geçmişi günlük görevler ve metin araması. ", 32))[..1024];
        foreach (var records in new[] { 10_000, 100_000 })
        {
            var store = new HistoryStore(Path.Combine(root, records.ToString()));
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var seed = Stopwatch.StartNew();
            using (var connection = store.Open())
            using (var transaction = connection.BeginTransaction())
            {
                for (var i = 0; i < records; i++)
                    store.Add(i % 10 == 0 ? "dictation" : "recall", start.AddMinutes(i), "fixture-" + i % 10 + ".exe", "Document " + i,
                        "fixture document common " + (i % 97 == 0 ? "needle " : "ordinary ") + documentBody, connection: connection, transaction: transaction);
                transaction.Commit();
            }
            store.SetTags("fixture-1.exe", ["FixtureTag"]);
            var seedMilliseconds = seed.Elapsed.TotalMilliseconds;
            Console.WriteLine($"History benchmark: seeded {records} synthetic records in {seedMilliseconds:0} ms.");
            var cases = new (string Name, HistoryFilter Filter)[]
            {
                ("latest", new("recall")),
                ("fts-selective", new("recall", "needle")),
                ("fts-common", new("recall", "fixture")),
                ("fts-unicode", new("recall", "İstanbul")),
                ("app", new("recall", App: "fixture-1.exe")),
                ("tag", new("recall", Tag: "FixtureTag")),
                ("date-middle", new("recall", From: start.AddMinutes(records / 2), Until: start.AddMinutes(records / 2 + 1440))),
                ("deep-page", new("recall", Offset: records * 9 / 10 - 200))
            };
            var measurements = new List<object>();
            foreach (var (name, filter) in cases)
            {
                var watch = Stopwatch.StartNew();
                var first = store.Search(filter);
                var firstMilliseconds = watch.Elapsed.TotalMilliseconds;
                check(first.Count > 0 && first.Count <= 200 && first.All(item => item.Kind == "recall")
                    && first.Select(item => item.Id).Distinct().Count() == first.Count,
                    $"{records} {name}: results remain bounded, unique and module-isolated");
                check(first.Zip(first.Skip(1), (a, b) => a.Created > b.Created || a.Created == b.Created && a.Id > b.Id).All(valid => valid),
                    $"{records} {name}: reverse chronological ordering remains deterministic");
                var times = new List<double>();
                for (var i = 0; i < 30; i++)
                {
                    watch.Restart(); var rows = store.Search(filter); times.Add(watch.Elapsed.TotalMilliseconds);
                    if (rows.Count != first.Count || rows[0].Id != first[0].Id) throw new InvalidOperationException("History results changed during a read-only benchmark.");
                }
                times.Sort();
                var p95 = times[(int)Math.Ceiling(times.Count * .95) - 1];
                if (p95 > 200) violations.Add($"{records}/{name}: p95 {p95:0.00} ms exceeds 200 ms");
                var plan = store.ExplainSearch(filter);
                measurements.Add(new { name, firstMilliseconds, medianMilliseconds = times[times.Count / 2], p95Milliseconds = p95, maximumMilliseconds = times[^1], returned = first.Count, queryPlan = plan, samplesMilliseconds = times });
                Console.WriteLine($"History {records}/{name}: p95 {p95:0.00} ms, first {firstMilliseconds:0.00} ms.");
            }
            var opens = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var watch = Stopwatch.StartNew();
                using (var connection = store.Open())
                using (var command = connection.CreateCommand()) { command.CommandText = "SELECT 1"; _ = command.ExecuteScalar(); }
                opens.Add(watch.Elapsed.TotalMilliseconds);
            }
            opens.Sort();
            results.Add(new { records, minimumTextCharacters = documentBody.Length, seedMilliseconds, databaseBytes = new FileInfo(store.DatabasePath).Length,
                connectionMedianMilliseconds = opens[opens.Count / 2], connectionP95Milliseconds = opens[(int)Math.Ceiling(opens.Count * .95) - 1], queries = measurements });
        }
        var report = new { utc = DateTimeOffset.UtcNow, machine = BenchmarkMachineInfo.Read(),
            targetP95Milliseconds = 200, violations, results,
            note = "Synthetic data, actual HistoryStore.Search/Open SQL and parameter binding, 30 warm samples per case. No user history was read or modified. First timings are recorded separately; this is not a filesystem-cache cold-start claim." };
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        check(violations.Count == 0, "10k/100k history query p95 meets the 200 ms budget; report: " + outputPath + "; " + string.Join("; ", violations));
    }
}
