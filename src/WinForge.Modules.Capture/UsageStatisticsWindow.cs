using System.Globalization;
using System.Text.Json;
using System.Windows;
using WinForge.Core;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal sealed class UsageStatisticsWindow : Window
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private readonly HistoryStore store;
    private readonly IFeatureUiSurface uiSurface;
    private string fromText = "";
    private string untilText = "";
    private bool closed;
    private bool loading;
    private long refreshVersion;

    public UsageStatisticsWindow(HistoryStore store, DateTimeOffset? from, DateTimeOffset? until, IFeatureUiService? featureUi = null)
    {
        this.store = store;
        fromText = from?.ToLocalTime().ToString("dd.MM.yyyy", TurkishCulture) ?? "";
        untilText = until?.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy", TurkishCulture) ?? "";

        Title = "PC Geçmişi · Kullanım istatistikleri";
        Width = 1080;
        Height = 700;
        MinWidth = 820;
        MinHeight = 500;
        CaptureUi.Theme(this);

        uiSurface = featureUi?.CreateSurface("capture-usage-statistics")
            ?? throw new InvalidOperationException("The React usage statistics view is required.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Loaded += (_, _) => _ = RefreshAsync();
        Closed += (_, _) =>
        {
            closed = true;
            uiSurface.MessageReceived -= OnUiMessage;
            uiSurface.Dispose();
        };
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTime.TryParseExact(value.Trim(), "dd.MM.yyyy", TurkishCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Tarihi gg.aa.yyyy biçiminde girin.");
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private void OnUiMessage(string command, JsonElement payload)
    {
        if (command == "statistics.refresh")
        {
            fromText = payload.TryGetProperty("from", out var start) ? start.GetString() ?? "" : "";
            untilText = payload.TryGetProperty("until", out var end) ? end.GetString() ?? "" : "";
            _ = RefreshAsync();
        }
        else if (command == "statistics.close")
        {
            Close();
        }
    }

    private async Task RefreshAsync()
    {
        refreshVersion++;
        if (loading || closed) return;
        loading = true;
        try
        {
            while (!closed)
            {
                var version = refreshVersion;
                var from = fromText;
                var until = untilText;
                object state;
                try
                {
                    state = await Task.Run(() => ReadStatistics(from, until));
                }
                catch (Exception error)
                {
                    state = new { from, until, status = error.Message, summary = "", applications = Array.Empty<object>(),
                        network = Array.Empty<object>(), days = Array.Empty<object>(), dayApplications = Array.Empty<object>(),
                        contexts = Array.Empty<object>(), tags = Array.Empty<object>() };
                }
                if (closed) return;
                if (version != refreshVersion) continue;
                uiSurface.SetState(state);
                break;
            }
        }
        finally { loading = false; }
    }

    private object ReadStatistics(string from, string until)
    {
        var start = ParseDate(from);
        var end = ParseDate(until)?.AddDays(1);
        if (start >= end) throw new ArgumentException("Bitiş tarihi başlangıçtan sonra olmalı.");
        var (summary, appTotals) = store.RecallOverview(start, end);
        var summaryText = $"Etkin: {TimeSpan.FromSeconds(summary.ActiveSeconds):g}     Boşta: {TimeSpan.FromSeconds(summary.IdleSeconds):g}     " +
            $"Ekran görüntüsü: {summary.Captures:N0}     En çok kullanılan: {summary.MostUsedApp}";

        var activeByDay = store.UsageByDay(start, end);
        var idleByDay = store.IdleUsageByDay(start, end);
        var activeTotals = activeByDay.GroupBy(row => row.Date).ToDictionary(group => group.Key, group => group.Sum(row => row.Seconds));
        var idleTotals = idleByDay.GroupBy(row => row.Date).ToDictionary(group => group.Key, group => group.Sum(row => row.Seconds));
        var dailyTotals = activeTotals.Keys.Union(idleTotals.Keys)
            .OrderBy(date => DateTime.ParseExact(date, "dd.MM.yyyy", TurkishCulture))
            .TakeLast(14)
            .Select(date => new { date, active = activeTotals.GetValueOrDefault(date), idle = idleTotals.GetValueOrDefault(date) })
            .ToArray();

        return new
        {
            from,
            until,
            status = $"Aralık: {(start?.ToLocalTime().ToString("dd.MM.yyyy") ?? "başlangıçtan beri")} – " +
                $"{(end?.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy") ?? "bugün dahil")} · " +
                "İstatistikler yerel kullanım zamanlarından hesaplanır.",
            summary = summaryText,
            network = store.NetworkUsageByDay(start, end).Select(row => new { row.Date, row.Received, row.Sent }).ToArray(),
            applications = appTotals.Select(row => new
            {
                app = row.App, seconds = row.Seconds, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), estimated = row.Estimated
            }).ToArray(),
            days = dailyTotals,
            dayApplications = activeByDay.Select(row => new
            {
                date = row.Date, app = row.App, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), estimated = row.Estimated
            }).ToArray(),
            contexts = store.ContextUsageByDay(start, end).Select(row => new
            {
                date = row.Date, type = row.Type, app = row.App, context = row.Context, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g")
            }).ToArray(),
            tags = store.TagUsage(appTotals).Select(row => new
            {
                tag = row.Tag, seconds = row.Seconds, duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"), applications = row.ApplicationCount
            }).ToArray()
        };
    }
}
