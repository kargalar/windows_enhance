using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;

namespace WinForge.History;

public sealed record HistoryItem(long Id, string Kind, DateTimeOffset Created, string App, string Title,
    string Text, string Media, double Seconds, bool Estimated, string Tags, string OcrState);
public sealed record HistoryFilter(string Kind, string Query = "", string App = "", string Tag = "",
    DateTimeOffset? From = null, DateTimeOffset? Until = null, int Limit = 200, int Offset = 0);
public sealed record UsageTotal(string App, double Seconds, bool Estimated);

/// <summary>Each operation owns its connection; media is content addressed and shared safely.</summary>
public sealed class HistoryStore
{
    private const string ItemColumns = """
        id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, created TEXT NOT NULL, app TEXT NOT NULL DEFAULT '',
        title TEXT NOT NULL DEFAULT '', text TEXT NOT NULL DEFAULT '', media TEXT NOT NULL DEFAULT '',
        seconds REAL NOT NULL DEFAULT 0, estimated INTEGER NOT NULL DEFAULT 0,
        source TEXT UNIQUE, ocr_state TEXT NOT NULL DEFAULT 'done', icon TEXT NOT NULL DEFAULT '', metadata TEXT NOT NULL DEFAULT ''
        """;
    private static readonly object MediaGate = new();
    private static readonly Dictionary<string, int> PendingMedia = new(StringComparer.OrdinalIgnoreCase);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge", "History");
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
        command.CommandText = """
            SELECT i.id,i.kind,i.created,i.app,i.title,i.text,i.media,i.seconds,i.estimated,
            coalesce((SELECT group_concat(tag, ', ') FROM app_tags WHERE app=i.app),''),i.ocr_state
            FROM items i WHERE kind=$kind
            AND ($query='' OR i.id IN (SELECT rowid FROM search WHERE search MATCH $match))
            AND ($app='' OR i.app=$app)
            AND ($tag='' OR EXISTS(SELECT 1 FROM app_tags WHERE app=i.app AND tag=$tag))
            AND ($from='' OR created >= $from) AND ($until='' OR created < $until)
            ORDER BY created DESC,id DESC LIMIT $limit OFFSET $offset
            """;
        var query = filter.Query.Trim();
        Bind(command, ("$kind", filter.Kind), ("$query", query), ("$match", "\"" + query.Replace("\"", "\"\"") + "\""),
            ("$app", filter.App), ("$tag", filter.Tag), ("$from", filter.From is { } from ? Timestamp(from) : ""),
            ("$until", filter.Until is { } until ? Timestamp(until) : ""), ("$limit", Math.Clamp(filter.Limit, 1, 1000)), ("$offset", Math.Max(0, filter.Offset)));
        using var reader = command.ExecuteReader(); var result = new List<HistoryItem>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDouble(7), reader.GetBoolean(8), reader.GetString(9), reader.GetString(10)));
        return result;
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
        command.CommandText = """
            SELECT app,sum(seconds),estimated FROM (
            SELECT app,0 estimated,CASE WHEN ended<=started THEN seconds ELSE
              seconds*min(1,max(0,(julianday(CASE WHEN $until<>'' AND ended>$until THEN $until ELSE ended END)-
              julianday(CASE WHEN $from<>'' AND started<$from THEN $from ELSE started END)) /
              max(0.0000000001,julianday(ended)-julianday(started)))) END seconds
            FROM usage WHERE ($from='' OR ended>$from) AND ($until='' OR started<$until)
            UNION ALL SELECT app,1 estimated,seconds FROM items WHERE kind='recall' AND estimated=1
            AND ($from='' OR created >= $from) AND ($until='' OR created < $until))
            GROUP BY app,estimated ORDER BY sum(seconds) DESC
            """;
        Bind(command, ("$from", from is { } f ? Timestamp(f) : ""), ("$until", until is { } u ? Timestamp(u) : ""));
        using var reader = command.ExecuteReader(); var result = new List<UsageTotal>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetDouble(1), reader.GetBoolean(2))); return result;
    }
    public void Delete(string kind, long? id = null, DateTimeOffset? from = null, DateTimeOffset? until = null)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM items WHERE kind=$kind AND ($id IS NULL OR id=$id) AND ($from='' OR created >= $from) AND ($until='' OR created < $until)";
        Bind(command, ("$kind", kind), ("$id", (object?)id ?? DBNull.Value), ("$from", from is { } f ? Timestamp(f) : ""), ("$until", until is { } u ? Timestamp(u) : "")); command.ExecuteNonQuery();
        if (kind == "recall" && id is null)
        {
            command.CommandText = "DELETE FROM usage WHERE ($from='' OR started >= $from) AND ($until='' OR started < $until)"; command.ExecuteNonQuery();
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
