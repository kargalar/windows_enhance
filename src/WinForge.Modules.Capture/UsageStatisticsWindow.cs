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
    private string statusText = "";
    private string summaryText = "";

    public UsageStatisticsWindow(HistoryStore store, DateTimeOffset? from, DateTimeOffset? until, IFeatureUiService? featureUi = null)
    {
        this.store = store;
        fromText = from?.ToLocalTime().ToString("dd.MM.yyyy", TurkishCulture) ?? "";
        untilText = until?.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy", TurkishCulture) ?? "";

        Title = "Kullanım istatistikleri";
        Width = 1080;
        Height = 700;
        MinWidth = 820;
        MinHeight = 500;
        CaptureUi.Theme(this);

        uiSurface = featureUi?.CreateSurface("capture-usage-statistics")
            ?? throw new InvalidOperationException("The React usage statistics view is required.");
        uiSurface.MessageReceived += OnUiMessage;
        Content = uiSurface.Element;
        Loaded += (_, _) => Refresh();
        Closed += (_, _) => uiSurface.Dispose();
    }

    private DateTimeOffset? Start => ParseDate(fromText);
    private DateTimeOffset? End => ParseDate(untilText)?.AddDays(1);

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
            Refresh();
        }
        else if (command == "statistics.close")
        {
            Close();
        }
    }

    private void Refresh()
    {
        try
        {
            if (Start is { } start && End is { } end && end <= start)
                throw new ArgumentException("Bitiş tarihi başlangıçtan sonra olmalı.");

            var summary = store.RecallSummary(Start, End);
            summaryText = $"Etkin: {TimeSpan.FromSeconds(summary.ActiveSeconds):g}     Boşta: {TimeSpan.FromSeconds(summary.IdleSeconds):g}     " +
                $"Ekran görüntüsü: {summary.Captures:N0}     En çok kullanılan: {summary.MostUsedApp}";

            var appTotals = store.Usage(Start, End);
            var activeByDay = store.UsageByDay(Start, End);
            var idleByDay = store.IdleUsageByDay(Start, End);
            var dates = activeByDay.Select(row => row.Date).Concat(idleByDay.Select(row => row.Date)).Distinct(StringComparer.Ordinal);
            var dailyTotals = dates
                .Select(date => (date,
                    active: activeByDay.Where(row => row.Date == date).Sum(row => row.Seconds),
                    idle: idleByDay.Where(row => row.Date == date).Sum(row => row.Seconds)))
                .OrderBy(row => DateTime.ParseExact(row.date, "dd.MM.yyyy", TurkishCulture))
                .TakeLast(14)
                .ToArray();
            var contextTotals = store.ContextUsageByDay(Start, End);
            var tagTotals = store.TagUsage(Start, End);

            statusText = $"Aralık: {(Start?.ToLocalTime().ToString("dd.MM.yyyy") ?? "başlangıçtan beri")} – " +
                $"{(End?.AddDays(-1).ToLocalTime().ToString("dd.MM.yyyy") ?? "bugün dahil")} · " +
                "İstatistikler yerel kullanım zamanlarından hesaplanır.";

            uiSurface.SetState(new
            {
                from = fromText,
                until = untilText,
                status = statusText,
                summary = summaryText,
                applications = appTotals.Select(row => new
                {
                    app = row.App,
                    seconds = row.Seconds,
                    duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                    estimated = row.Estimated
                }).ToArray(),
                days = dailyTotals.Select(row => new { date = row.date, active = row.active, idle = row.idle }).ToArray(),
                dayApplications = activeByDay.Select(row => new
                {
                    date = row.Date,
                    app = row.App,
                    duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                    estimated = row.Estimated
                }).ToArray(),
                contexts = contextTotals.Select(row => new
                {
                    date = row.Date,
                    type = row.Type,
                    app = row.App,
                    context = row.Context,
                    duration = TimeSpan.FromSeconds(row.Seconds).ToString("g")
                }).ToArray(),
                tags = tagTotals.Select(row => new
                {
                    tag = row.Tag,
                    seconds = row.Seconds,
                    duration = TimeSpan.FromSeconds(row.Seconds).ToString("g"),
                    applications = row.ApplicationCount
                }).ToArray()
            });
        }
        catch (Exception error)
        {
            statusText = error.Message;
            uiSurface.SetState(new
            {
                from = fromText,
                until = untilText,
                status = statusText,
                summary = summaryText,
                applications = Array.Empty<object>(),
                days = Array.Empty<object>(),
                dayApplications = Array.Empty<object>(),
                contexts = Array.Empty<object>(),
                tags = Array.Empty<object>()
            });
        }
    }
}
