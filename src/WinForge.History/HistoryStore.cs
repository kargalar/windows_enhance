using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;

namespace WinForge.History;

public sealed record HistoryItem(long Id, string Kind, DateTimeOffset Created, string App, string Title,
    string Text, string Media, double Seconds, bool Estimated, string Tags, string OcrState, string Icon, string Metadata);
public sealed record HistoryFilter(string Kind, string Query = "", string App = "", string Tag = "",
    DateTimeOffset? From = null, DateTimeOffset? Until = null, int Limit = 200, int Offset = 0);
public sealed record UsageTotal(string App, double Seconds, bool Estimated);
public sealed record UsageDayTotal(string Date, string App, double Seconds, bool Estimated);
public sealed record TagUsageTotal(string Tag, double Seconds, int ApplicationCount);
public sealed record RecallUsageSummary(double ActiveSeconds, double IdleSeconds, long Captures, string MostUsedApp);
public sealed record ContextUsageTotal(string App, string Type, string Context, double Seconds);
public sealed record ContextUsageDayTotal(string Date, string App, string Type, string Context, double Seconds);

/// <summary>Each operation owns its connection; media is content addressed and shared safely.</summary>
public sealed class HistoryStore
{
    // Legacy idle rows retain the foreground app in their original metadata.
    // Prefer measured usage wherever the old and new recorders overlap.
    private const string ArchivedUsageSql = """
        SELECT CASE WHEN CASE WHEN json_valid(metadata) THEN
          lower(coalesce(cast(json_extract(metadata,'$.is_idle') AS TEXT),'')) IN ('1','true')
          ELSE 0 END THEN 'System Idle' ELSE app END app,
          created started,created ended,seconds,1 estimated
        FROM items WHERE kind='recall' AND estimated=1
        AND ($from='' OR created >= $from) AND ($until='' OR created < $until)
        AND NOT EXISTS(SELECT 1 FROM usage WHERE started<=items.created AND ended>items.created)
        """;
    private const string ItemColumns = """
        id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, created TEXT NOT NULL, app TEXT NOT NULL DEFAULT '',
        title TEXT NOT NULL DEFAULT '', text TEXT NOT NULL DEFAULT '', media TEXT NOT NULL DEFAULT '',
        seconds REAL NOT NULL DEFAULT 0, estimated INTEGER NOT NULL DEFAULT 0,
        source TEXT UNIQUE, ocr_state TEXT NOT NULL DEFAULT 'done', icon TEXT NOT NULL DEFAULT '', metadata TEXT NOT NULL DEFAULT ''
        """;
    private static readonly object MediaGate = new();
    private static readonly Dictionary<string, int> PendingMedia = new(StringComparer.OrdinalIgnoreCase);
    public static string DefaultDirectory => Path.Combine(Storage.PortableDataPaths.DirectoryPath, "History");
    public string DirectoryPath { get; }
    public string DatabasePath => Path.Combine(DirectoryPath, "history.db");
    public string MediaDirectory => Path.Combine(DirectoryPath, "media");
    public HistoryStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(MediaDirectory);
        using var connection = Open();
        var schema = $$"""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS items({{ItemColumns}});
            CREATE INDEX IF NOT EXISTS items_time ON items(kind,created);
            CREATE INDEX IF NOT EXISTS items_media ON items(media);
            CREATE INDEX IF NOT EXISTS items_ocr ON items(ocr_state,media);
            CREATE TABLE IF NOT EXISTS app_tags(app TEXT NOT NULL,tag TEXT NOT NULL,PRIMARY KEY(app,tag));
            CREATE TABLE IF NOT EXISTS tags(name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS usage(id INTEGER PRIMARY KEY,app TEXT NOT NULL,started TEXT NOT NULL,ended TEXT NOT NULL,seconds REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS usage_time ON usage(started,ended);
            CREATE TABLE IF NOT EXISTS context_usage(id INTEGER PRIMARY KEY,app TEXT NOT NULL,type TEXT NOT NULL,context TEXT NOT NULL,started TEXT NOT NULL,ended TEXT NOT NULL,seconds REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS context_usage_time ON context_usage(started,ended);
            CREATE INDEX IF NOT EXISTS context_usage_context ON context_usage(type,app,context);
            CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(app,title,text,content=items,content_rowid=id,tokenize='unicode61 remove_diacritics 2');
            CREATE TRIGGER IF NOT EXISTS item_insert AFTER INSERT ON items BEGIN
              INSERT INTO search(rowid,app,title,text) VALUES(new.id,new.app,new.title,new.text); END;
            CREATE TRIGGER IF NOT EXISTS item_delete AFTER DELETE ON items BEGIN
              INSERT INTO search(search,rowid,app,title,text) VALUES('delete',old.id,old.app,old.title,old.text); END;
            CREATE TRIGGER IF NOT EXISTS item_update AFTER UPDATE ON items BEGIN
              INSERT INTO search(search,rowid,app,title,text) VALUES('delete',old.id,old.app,old.title,old.text);
              INSERT INTO search(rowid,app,title,text) VALUES(new.id,new.app,new.title,new.text); END;
            """;
        Execute(connection, schema);
        using var definition = connection.CreateCommand(); definition.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='items'";
        if (!(definition.ExecuteScalar()?.ToString()?.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, $$"""
                DROP TRIGGER IF EXISTS item_insert; DROP TRIGGER IF EXISTS item_delete; DROP TRIGGER IF EXISTS item_update;
                CREATE TABLE items_rebuild({{ItemColumns}});
                INSERT INTO items_rebuild SELECT * FROM items;
                DROP TABLE items;
                ALTER TABLE items_rebuild RENAME TO items;
                """, transaction);
            Execute(connection, schema.Replace("PRAGMA journal_mode=WAL;", "", StringComparison.Ordinal), transaction);
            transaction.Commit();
        }
        using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) < 2)
            Execute(connection, """
                UPDATE OR IGNORE items SET source=kind||':'||created||':'||json_extract(metadata,'$.id')
                WHERE source IS NOT NULL AND json_valid(metadata) AND json_extract(metadata,'$.id') IS NOT NULL
                AND substr(source,1,length(kind)+1) <> kind||':';
                PRAGMA user_version=2;
                """);
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) < 3) Execute(connection, "PRAGMA user_version=3;");
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) < 4) Execute(connection, "PRAGMA user_version=4;");
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout=5000; PRAGMA secure_delete=ON;");
        return connection;
    }

    public static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    public long Add(string kind, DateTimeOffset created, string app = "", string title = "", string text = "", string media = "",
        double seconds = 0, bool estimated = false, string? source = null, string ocrState = "done", string icon = "", string metadata = "",
        SqliteConnection? connection = null, SqliteTransaction? transaction = null)
    {
        var ownsConnection = connection is null; connection ??= Open();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
            INSERT INTO items(kind,created,app,title,text,media,seconds,estimated,source,ocr_state,icon,metadata)
            VALUES($kind,$date,$app,$title,$text,$media,$seconds,$estimated,$source,$ocr,$icon,$metadata)
            ON CONFLICT(source) DO NOTHING RETURNING id;
            """;
            Bind(command, ("$kind", kind), ("$date", Timestamp(created)), ("$app", app), ("$title", title),
                ("$text", text), ("$media", media), ("$seconds", Math.Max(0, seconds)), ("$estimated", estimated ? 1 : 0),
                ("$source", (object?)source ?? DBNull.Value), ("$ocr", ocrState), ("$icon", icon), ("$metadata", metadata));
            var result = command.ExecuteScalar() is long id ? id : 0;
            if (transaction is null) { ReleasePendingMedia(media); ReleasePendingMedia(icon); }
            return result;
        }
        finally { if (ownsConnection) connection.Dispose(); }
    }

    public HashSet<string> ImportKeys()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT source FROM items WHERE source IS NOT NULL";
        using var reader = command.ExecuteReader(); var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) result.Add(reader.GetString(0)); return result;
    }

    public bool HasSource(string source)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM items WHERE source=$source";
        command.Parameters.AddWithValue("$source", source); return command.ExecuteScalar() is not null;
    }

    public IReadOnlyList<HistoryItem> Search(HistoryFilter filter)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        ConfigureSearch(command, filter);
        using var reader = command.ExecuteReader(); var result = new List<HistoryItem>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDouble(7), reader.GetBoolean(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12)));
        return result;
    }

    private const string SearchSql = """
            SELECT i.id,i.kind,i.created,i.app,i.title,i.text,i.media,i.seconds,i.estimated,
            coalesce((SELECT group_concat(tag, ', ') FROM app_tags WHERE app=i.app),''),i.ocr_state,i.icon,i.metadata
            FROM items i WHERE kind=$kind
            AND ($query='' OR i.id IN (SELECT rowid FROM search WHERE search MATCH $match))
            AND ($app='' OR i.app=$app)
            AND ($tag='' OR EXISTS(SELECT 1 FROM app_tags WHERE app=i.app AND tag=$tag))
            AND ($from='' OR created >= $from) AND ($until='' OR created < $until)
            ORDER BY created DESC,id DESC LIMIT $limit OFFSET $offset
            """;

    private static void ConfigureSearch(SqliteCommand command, HistoryFilter filter)
    {
        command.CommandText = SearchSql;
        var query = filter.Query.Trim();
        Bind(command, ("$kind", filter.Kind), ("$query", query), ("$match", "\"" + query.Replace("\"", "\"\"") + "\""),
            ("$app", filter.App), ("$tag", filter.Tag), ("$from", filter.From is { } from ? Timestamp(from) : ""),
            ("$until", filter.Until is { } until ? Timestamp(until) : ""), ("$limit", Math.Clamp(filter.Limit, 1, 1000)), ("$offset", Math.Max(0, filter.Offset)));
    }

    internal IReadOnlyList<string> ExplainSearch(HistoryFilter filter)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        ConfigureSearch(command, filter);
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        using var reader = command.ExecuteReader(); var steps = new List<string>();
        while (reader.Read()) steps.Add(reader.GetString(3));
        return steps;
    }

    public void CompleteOcr(long id, string text, bool failed = false)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE items SET text=$text,ocr_state=$state WHERE id=$id OR (kind='recall' AND ocr_state='pending' AND media<>'' AND media=(SELECT media FROM items WHERE id=$id))";
        Bind(command, ("$text", text), ("$state", failed ? "failed" : "done"), ("$id", id)); command.ExecuteNonQuery();
    }
    public IReadOnlyList<(long Id, string Media)> PendingOcr(int limit = 8)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT min(id),media FROM items WHERE ocr_state='pending' AND media<>'' GROUP BY media ORDER BY min(id) LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit); using var reader = command.ExecuteReader(); var rows = new List<(long, string)>();
        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1))); return rows;
    }
    public void RetryOcr() { using var connection = Open(); Execute(connection, "UPDATE items SET ocr_state='pending' WHERE kind='recall' AND ocr_state='failed'"); }

    public void SetTags(string app, IEnumerable<string> tags)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM app_tags WHERE app=$app"; command.Parameters.AddWithValue("$app", app); command.ExecuteNonQuery();
        foreach (var tag in tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            command.Parameters.Clear(); command.CommandText = "INSERT OR IGNORE INTO tags VALUES($tag); INSERT OR IGNORE INTO app_tags VALUES($app,$tag)";
            Bind(command, ("$app", app), ("$tag", tag)); command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public void AddUsage(string app, DateTimeOffset start, DateTimeOffset end, double seconds)
    {
        if (seconds <= 0) return;
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE usage SET ended=$end,seconds=seconds+$seconds
            WHERE id=(SELECT max(id) FROM usage) AND app=$app AND ended=$start;
            INSERT INTO usage(app,started,ended,seconds) SELECT $app,$start,$end,$seconds WHERE changes()=0;
            """;
        Bind(command, ("$app", app), ("$start", Timestamp(start)), ("$end", Timestamp(end)), ("$seconds", seconds)); command.ExecuteNonQuery();
    }
    public IReadOnlyList<UsageTotal> Usage(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $$"""
            SELECT app,sum(seconds),estimated FROM (
            SELECT app,0 estimated,CASE WHEN ended<=started THEN seconds ELSE
              seconds*min(1,max(0,(julianday(CASE WHEN $until<>'' AND ended>$until THEN $until ELSE ended END)-
              julianday(CASE WHEN $from<>'' AND started<$from THEN $from ELSE started END)) /
              max(0.0000000001,julianday(ended)-julianday(started)))) END seconds
            FROM usage WHERE app<>'System Idle' AND ($from='' OR ended>$from) AND ($until='' OR started<$until)
            UNION ALL SELECT app,1 estimated,seconds FROM ({{ArchivedUsageSql}}) WHERE app<>'System Idle')
            GROUP BY app,estimated ORDER BY sum(seconds) DESC
            """;
        Bind(command, ("$from", from is { } f ? Timestamp(f) : ""), ("$until", until is { } u ? Timestamp(u) : ""));
        using var reader = command.ExecuteReader(); var result = new List<UsageTotal>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetDouble(1), reader.GetBoolean(2))); return result;
    }
    public RecallUsageSummary RecallSummary(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        var appUsage = Usage(from, until);
        var active = appUsage.Sum(row => row.Seconds);
        var idle = SumUsage("System Idle", from, until);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM items WHERE kind='recall' AND media<>'' AND ($from='' OR created >= $from) AND ($until='' OR created < $until)";
        Bind(command, ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } end ? Timestamp(end) : ""));
        var captures = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        var mostUsed = appUsage.GroupBy(row => row.App).OrderByDescending(group => group.Sum(row => row.Seconds)).FirstOrDefault()?.Key ?? "—";
        return new(active, idle, captures, mostUsed);
    }
    private double SumUsage(string app, DateTimeOffset? from, DateTimeOffset? until)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $$"""
            SELECT coalesce(sum(seconds),0) FROM (
            SELECT CASE WHEN ended<=started THEN seconds ELSE seconds*min(1,max(0,
            julianday(CASE WHEN $until<>'' AND ended>$until THEN $until ELSE ended END)-
            julianday(CASE WHEN $from<>'' AND started<$from THEN $from ELSE started END))/
            max(0.0000000001,julianday(ended)-julianday(started))) END seconds
            FROM usage WHERE app=$app AND ($from='' OR ended>$from) AND ($until='' OR started<$until)
            UNION ALL SELECT seconds FROM ({{ArchivedUsageSql}}) WHERE app=$app)
            """;
        Bind(command, ("$app", app), ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } end ? Timestamp(end) : ""));
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
    public IReadOnlyList<TagUsageTotal> TagUsage(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        var appUsage = Usage(from, until).GroupBy(row => row.App, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Seconds), StringComparer.OrdinalIgnoreCase);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT tag,app FROM app_tags ORDER BY tag,app";
        using var reader = command.ExecuteReader(); var totals = new Dictionary<string, (double Seconds, HashSet<string> Apps)>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var tag = reader.GetString(0); var app = reader.GetString(1);
            if (!totals.TryGetValue(tag, out var value)) value = (0, new(StringComparer.OrdinalIgnoreCase));
            if (appUsage.TryGetValue(app, out var seconds)) value.Seconds += seconds;
            value.Apps.Add(app); totals[tag] = value;
        }
        return totals.Select(row => new TagUsageTotal(row.Key, row.Value.Seconds, row.Value.Apps.Count))
            .OrderByDescending(row => row.Seconds).ToArray();
    }
    public void AddContextUsage(string app, string type, string context, DateTimeOffset start, DateTimeOffset end, double seconds)
    {
        if (seconds <= 0 || string.IsNullOrWhiteSpace(context)) return;
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE context_usage SET ended=$end,seconds=seconds+$seconds
            WHERE id=(SELECT max(id) FROM context_usage) AND app=$app AND type=$type AND context=$context AND ended=$start;
            INSERT INTO context_usage(app,type,context,started,ended,seconds)
            SELECT $app,$type,$context,$start,$end,$seconds WHERE changes()=0;
            """;
        Bind(command, ("$app", app), ("$type", type), ("$context", context), ("$start", Timestamp(start)),
            ("$end", Timestamp(end)), ("$seconds", seconds)); command.ExecuteNonQuery();
    }
    public IReadOnlyList<ContextUsageTotal> ContextUsage(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT app,type,context,started,ended,seconds FROM context_usage
            WHERE ($from='' OR ended>$from) AND ($until='' OR started<$until)
            ORDER BY started DESC LIMIT 50000
            """;
        Bind(command, ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } endValue ? Timestamp(endValue) : ""));
        using var reader = command.ExecuteReader(); var totals = new Dictionary<(string App, string Type, string Context), double>();
        while (reader.Read())
        {
            var started = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            var ended = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
            var clippedStart = from is { } fromDate && started < fromDate ? fromDate : started;
            var clippedEnd = until is { } untilDate && ended > untilDate ? untilDate : ended;
            var span = (ended - started).TotalSeconds;
            if (clippedEnd <= clippedStart || span <= 0) continue;
            var seconds = reader.GetDouble(5) * (clippedEnd - clippedStart).TotalSeconds / span;
            var key = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
            totals[key] = totals.GetValueOrDefault(key) + seconds;
        }
        return totals.Select(row => new ContextUsageTotal(row.Key.App, row.Key.Type, row.Key.Context, row.Value))
            .OrderByDescending(row => row.Seconds).ToArray();
    }
    public IReadOnlyList<ContextUsageDayTotal> ContextUsageByDay(DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT app,type,context,started,ended,seconds FROM context_usage
            WHERE ($from='' OR ended>$from) AND ($until='' OR started<$until)
            ORDER BY started
            """;
        Bind(command, ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } endValue ? Timestamp(endValue) : ""));
        using var reader = command.ExecuteReader(); var totals = new Dictionary<(string Date, string App, string Type, string Context), double>();
        while (reader.Read())
        {
            var app = reader.GetString(0); var type = reader.GetString(1); var context = reader.GetString(2);
            var intervalStart = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            var intervalEnd = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture); var seconds = reader.GetDouble(5);
            var fullSpan = (intervalEnd - intervalStart).TotalSeconds;
            if (fullSpan <= 0) { Add(intervalStart.ToLocalTime().Date, seconds); continue; }
            var cursor = from is { } fromDate && intervalStart < fromDate ? fromDate : intervalStart;
            var clippedEnd = until is { } untilDate && intervalEnd > untilDate ? untilDate : intervalEnd;
            while (cursor < clippedEnd)
            {
                var local = cursor.ToLocalTime(); var nextLocalDay = new DateTimeOffset(local.Date.AddDays(1), local.Offset).ToUniversalTime();
                var segmentEnd = nextLocalDay < clippedEnd ? nextLocalDay : clippedEnd;
                Add(local.Date, seconds * (segmentEnd - cursor).TotalSeconds / fullSpan); cursor = segmentEnd;
            }
            void Add(DateTime date, double value)
            {
                var key = (date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")), app, type, context);
                totals[key] = totals.GetValueOrDefault(key) + value;
            }
        }
        return totals.Select(row => new ContextUsageDayTotal(row.Key.Date, row.Key.App, row.Key.Type, row.Key.Context, row.Value))
            .OrderByDescending(row => DateTime.ParseExact(row.Date, "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")))
            .ThenByDescending(row => row.Seconds).ToArray();
    }
    public IReadOnlyList<UsageDayTotal> UsageByDay(DateTimeOffset? from = null, DateTimeOffset? until = null) => UsageByDayCore(from, until, false);
    public IReadOnlyList<UsageDayTotal> IdleUsageByDay(DateTimeOffset? from = null, DateTimeOffset? until = null) => UsageByDayCore(from, until, true);
    private IReadOnlyList<UsageDayTotal> UsageByDayCore(DateTimeOffset? from, DateTimeOffset? until, bool idleOnly)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $$"""
            SELECT app,started,ended,seconds,0 estimated FROM usage
            WHERE (($idle=0 AND app<>'System Idle') OR ($idle=1 AND app='System Idle'))
            AND ($from='' OR ended>$from) AND ($until='' OR started<$until)
            UNION ALL SELECT app,started,ended,seconds,estimated FROM ({{ArchivedUsageSql}})
            WHERE (($idle=0 AND app<>'System Idle') OR ($idle=1 AND app='System Idle'))
            ORDER BY started
            """;
        Bind(command, ("$idle", idleOnly ? 1 : 0), ("$from", from is { } start ? Timestamp(start) : ""), ("$until", until is { } endValue ? Timestamp(endValue) : ""));
        using var reader = command.ExecuteReader(); var totals = new Dictionary<(string Date, string App, bool Estimated), double>();
        while (reader.Read())
        {
            var app = reader.GetString(0); var intervalStart = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
            var intervalEnd = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture); var seconds = reader.GetDouble(3);
            var estimated = reader.GetBoolean(4); var fullSpan = (intervalEnd - intervalStart).TotalSeconds;
            if (fullSpan <= 0) { Add(intervalStart.ToLocalTime().Date, seconds); continue; }
            var cursor = from is { } fromDate && intervalStart < fromDate ? fromDate : intervalStart;
            var end = until is { } untilDate && intervalEnd > untilDate ? untilDate : intervalEnd;
            while (cursor < end)
            {
                var local = cursor.ToLocalTime(); var nextLocalDay = new DateTimeOffset(local.Date.AddDays(1), local.Offset).ToUniversalTime();
                var segmentEnd = nextLocalDay < end ? nextLocalDay : end;
                Add(local.Date, seconds * (segmentEnd - cursor).TotalSeconds / fullSpan); cursor = segmentEnd;
            }
            void Add(DateTime date, double value)
            {
                var key = (date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")), app, estimated);
                totals[key] = totals.GetValueOrDefault(key) + value;
            }
        }
        return totals.Select(row => new UsageDayTotal(row.Key.Date, row.Key.App, row.Value, row.Key.Estimated))
            .OrderByDescending(row => DateTime.ParseExact(row.Date, "dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")))
            .ThenByDescending(row => row.Seconds).ToArray();
    }
    public void Delete(string kind, long? id = null, DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM items WHERE kind=$kind AND ($id IS NULL OR id=$id) AND ($from='' OR created >= $from) AND ($until='' OR created < $until)";
        Bind(command, ("$kind", kind), ("$id", (object?)id ?? DBNull.Value), ("$from", from is { } f ? Timestamp(f) : ""), ("$until", until is { } u ? Timestamp(u) : "")); command.ExecuteNonQuery();
        if (kind == "recall" && id is null)
        {
            command.CommandText = "DELETE FROM usage WHERE ($from='' OR ended > $from) AND ($until='' OR started < $until); DELETE FROM context_usage WHERE ($from='' OR ended > $from) AND ($until='' OR started < $until)";
            command.ExecuteNonQuery();
        }
        CollectUnusedMedia();
    }
    public int CollectUnusedMedia()
    {
        lock (MediaGate)
        {
            using var connection = Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT media FROM items UNION SELECT icon FROM items";
            using var reader = command.ExecuteReader(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) used.Add(reader.GetString(0));
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(MediaDirectory))
                if (!used.Contains(Path.GetFileName(file)) && !PendingMedia.ContainsKey(file) && !file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) { File.Delete(file); count++; }
            return count;
        }
    }
    public void ReserveMedia(string name)
    {
        if (name.Length == 0) return;
        var path = ResolveMedia(name); lock (MediaGate) PendingMedia[path] = PendingMedia.GetValueOrDefault(path) + 1;
    }
    public void ReleasePendingMedia(string name)
    {
        if (name.Length == 0) return;
        var path = ResolveMedia(name);
        lock (MediaGate) { if (PendingMedia.TryGetValue(path, out var count)) { if (count <= 1) PendingMedia.Remove(path); else PendingMedia[path] = count - 1; } }
    }
    public async Task<string> ImportMediaAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return "";
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        var name = hash + Path.GetExtension(path).ToLowerInvariant(); var destination = Path.Combine(MediaDirectory, name);
        ReserveMedia(name);
        if (File.Exists(destination)) return name;
        input.Position = 0; var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            File.Move(temporary, destination, true); return name;
        }
        catch { ReleasePendingMedia(name); throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string ResolveMedia(string name)
    {
        if (name.Length == 0 || Path.GetFileName(name) != name) throw new ArgumentException("Geçersiz medya adı.");
        return Path.Combine(MediaDirectory, name);
    }
    internal static void Bind(SqliteCommand command, params (string Name, object Value)[] values)
    { foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value); }
    internal static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery(); }
}
