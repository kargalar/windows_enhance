using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WinForge.History;

public sealed record ClipboardHistoryItem(long Id, long CopiedAt, string? Text, byte[]? Thumbnail, int Width, int Height);

public sealed record ClipboardHistoryContent(string? Text, byte[]? Png);

public sealed record ClipboardHistoryImageData(byte[] Png, byte[] Thumbnail, int Width, int Height);

public sealed class ClipboardHistoryStore(string path)
{
    public const int PageSize = 24;

    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA auto_vacuum = FULL;
            CREATE TABLE IF NOT EXISTS clips (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                copied_at INTEGER NOT NULL,
                text TEXT,
                png BLOB,
                thumbnail BLOB,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                CHECK (text IS NOT NULL OR png IS NOT NULL)
            );
            CREATE INDEX IF NOT EXISTS clips_by_time ON clips(copied_at);
            """;
        command.ExecuteNonQuery();
    }

    public void Add(long copiedAt, string? text, ClipboardHistoryImageData? image)
    {
        if (text is null && image is null) return;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clips (copied_at, text, png, thumbnail, width, height)
            VALUES ($copied, $text, $png, $thumbnail, $width, $height)
            """;
        command.Parameters.AddWithValue("$copied", copiedAt);
        command.Parameters.AddWithValue("$text", (object?)text ?? DBNull.Value);
        command.Parameters.AddWithValue("$png", (object?)image?.Png ?? DBNull.Value);
        command.Parameters.AddWithValue("$thumbnail", (object?)image?.Thumbnail ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", image?.Width ?? 0);
        command.Parameters.AddWithValue("$height", image?.Height ?? 0);
        command.ExecuteNonQuery();
    }

    public List<ClipboardHistoryItem> List(string query, string kind, int offset, long cutoff, string language, long? beforeId = null)
    {
        using var connection = Open();
        var compare = CultureInfo.GetCultureInfo(language).CompareInfo;
        connection.CreateFunction("contains_text",
            (string? text, string search) => text is not null && compare.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0, isDeterministic: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, copied_at, substr(text, 1, 320), thumbnail, width, height
            FROM clips
            WHERE copied_at >= $cutoff
              AND ($beforeId IS NULL OR id < $beforeId)
              AND ($kind <> 'text' OR text IS NOT NULL)
              AND ($kind <> 'image' OR thumbnail IS NOT NULL)
              AND ($query = '' OR contains_text(text, $query))
            ORDER BY id DESC LIMIT $limit OFFSET $offset
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$beforeId", (object?)beforeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", PageSize + 1);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var items = new List<ClipboardHistoryItem>(PageSize + 1);
        while (reader.Read())
        {
            var text = reader.IsDBNull(2) ? null : reader.GetString(2);
            var thumbnail = reader.IsDBNull(3) ? null : reader.GetFieldValue<byte[]>(3);
            items.Add(new(reader.GetInt64(0), reader.GetInt64(1), text, thumbnail, reader.GetInt32(4), reader.GetInt32(5)));
        }
        return items;
    }

    public ClipboardHistoryContent? Get(long id, long cutoff)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT text, png FROM clips WHERE id = $id AND copied_at >= $cutoff";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$cutoff", cutoff);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1))
            : null;
    }

    public int Expire(long cutoff)
    {
        if (cutoff == 0) return 0;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON; DELETE FROM clips WHERE copied_at < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
