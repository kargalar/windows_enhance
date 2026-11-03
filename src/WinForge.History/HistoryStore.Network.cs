using System.Globalization;

namespace WinForge.History;

public sealed record NetworkUsageDayTotal(string Date, double Received, double Sent);

public sealed partial class HistoryStore
{
    public void AddNetworkUsage(DateTimeOffset start, DateTimeOffset end, long received, long sent)
    {
        if (end <= start || received < 0 || sent < 0 || (received == 0 && sent == 0)) return;
        using var connection = Open(); using var command = connection.CreateCommand();
        // Compact contiguous samples within one UTC minute without spanning recording gaps.
        command.CommandText = """
            UPDATE network_usage SET ended=$end,received=received+$received,sent=sent+$sent
            WHERE id=(SELECT max(id) FROM network_usage) AND ended=$start AND substr(started,1,16)=substr($start,1,16);
            INSERT INTO network_usage(started,ended,received,sent)
            SELECT $start,$end,$received,$sent WHERE changes()=0;
            """;
        Bind(command, ("$start", Timestamp(start)), ("$end", Timestamp(end)), ("$received", received), ("$sent", sent));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<NetworkUsageDayTotal> NetworkUsageByDay(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT started,ended,received,sent FROM network_usage
            WHERE ($from='' OR ended>$from) AND ($until='' OR started<$until) ORDER BY started
            """;
        Bind(command, ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } end ? Timestamp(end) : ""));
        using var reader = command.ExecuteReader();
        var totals = new Dictionary<DateTime, (double Received, double Sent)>();
        while (reader.Read())
        {
            var started = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            var ended = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            var seconds = (ended - started).TotalSeconds;
            if (seconds <= 0) continue;
            var cursor = from is { } lower && lower > started ? lower : started;
            var clippedEnd = until is { } upper && upper < ended ? upper : ended;
            while (cursor < clippedEnd)
            {
                var date = cursor.ToLocalTime().Date;
                var nextDate = date.AddDays(1);
                var nextDay = new DateTimeOffset(nextDate, TimeZoneInfo.Local.GetUtcOffset(nextDate));
                var segmentEnd = nextDay < clippedEnd ? nextDay : clippedEnd;
                var fraction = (segmentEnd - cursor).TotalSeconds / seconds;
                var value = totals.GetValueOrDefault(date);
                totals[date] = (value.Received + reader.GetInt64(2) * fraction, value.Sent + reader.GetInt64(3) * fraction);
                cursor = segmentEnd;
            }
        }
        return totals.OrderByDescending(row => row.Key)
            .Select(row => new NetworkUsageDayTotal(row.Key.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), row.Value.Received, row.Value.Sent)).ToArray();
    }
}
