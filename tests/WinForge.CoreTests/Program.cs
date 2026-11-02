using System.Text.Json;
using System.IO;
using WinForge.Core;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinForge.CoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var trace = Path.Combine(root, "metrics.jsonl");
            Environment.SetEnvironmentVariable("WINFORGE_PERF_PATH", trace);
            var measurement = PerformanceTrace.Measure("test.duration", "core");
            measurement.Dispose();
            measurement.Dispose();
            PerformanceTrace.Flush();
            var entries = File.ReadAllLines(trace);
            Check(entries.Length == 1, "Timing scopes emit once even when disposed twice");
            using (var entry = JsonDocument.Parse(entries[0]))
            {
                Check(entry.RootElement.GetProperty("Milliseconds").GetDouble() >= 0, "Timing is monotonic and nonnegative");
                Check(!entry.RootElement.TryGetProperty("Settings", out _), "Trace contains timing metadata only");
            }
            for (var i = 0; i < 10000; i++) PerformanceTrace.Record("test.burst", 1);
            Check(PerformanceTrace.DroppedEntries > 0, "Trace backpressure counts dropped entries instead of growing without bound");
            PerformanceTrace.Flush();
            Check(File.ReadAllLines(trace).Length <= 4097, "Trace burst respects the queue budget");

            var path = Path.Combine(root, "settings.json");
            var settings = new SettingsStore(path);
            settings.For("test")["name"] = "Türkçe";
            settings.Enabled.Add("test");
            settings.Save();
            var reopened = new SettingsStore(path);
            Check(reopened.Enabled.Contains("test") && reopened.For("test")["name"] == "Türkçe", "Settings survive reopening with Unicode and enabled state");
            Check(!File.Exists(path + ".tmp"), "Successful persistence leaves no temporary settings file");
            SettingsPersistenceChecks.Run(root, Check);
            LoggingChecks.Run(root, Check);
            NetworkPlacementChecks.Run(Check);
            InputObserverChecks.Run(Check);
            JsonStateDeltaChecks.Run(Check);
            Console.WriteLine($"PASS: {checks} core checks.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            PerformanceTrace.Flush();
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinForge.CoreTests"));
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == expected) Directory.Delete(root, true);
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
    }
}
