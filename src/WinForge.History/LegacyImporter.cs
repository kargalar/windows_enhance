using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace WinForge.History;

public sealed record ImportSource(string Database, string Kind, string ImagesDirectory = "");
public sealed record ImportPreview(ImportSource Source, long Records, long Bytes, long MissingFiles, long ExistingRecords = 0);
public sealed record ImportProgress(long Completed, long Total, long Imported, long MissingFiles, string Message);

public sealed class LegacyImporter(HistoryStore destination)
{
    public static IReadOnlyList<ImportSource> Discover(string root, string kind)
    {
        if (!Directory.Exists(root)) return [];
        var name = kind == "recall" ? "recall.db" : "history.db";
        var result = new List<ImportSource>(); var folders = new Stack<string>(); folders.Push(Path.GetFullPath(root));
        while (folders.TryPop(out var folder))
        {
            try
            {
                var database = Path.Combine(folder, name);
                if (File.Exists(database)) result.Add(new(database, kind, ReadImagesDirectory(folder)));
                foreach (var child in Directory.EnumerateDirectories(folder))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0 && Path.GetFileName(child) is not (".git" or "obj" or "node_modules")) folders.Push(child);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(source => File.GetLastWriteTimeUtc(source.Database)).ToArray();
    }

    private static string ReadImagesDirectory(string folder)
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "config.json")));
            var value = config.RootElement.GetProperty("images_path").GetString();
            if (!string.IsNullOrWhiteSpace(value)) return Path.GetFullPath(value, folder);
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return Path.Combine(folder, "images");
    }

    public Task<ImportPreview> PreviewAsync(ImportSource source, CancellationToken token) => Task.Run(() =>
    {
        using var snapshot = Snapshot.Create(source.Database);
        using var command = snapshot.Connection.CreateCommand(); command.CommandText = "SELECT * FROM " + Table(source.Kind);
        using var reader = command.ExecuteReader(); long records = 0, bytes = 0, missing = 0, existing = 0;
        var keys = destination.ImportKeys();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested(); records++;
            var date = Get(reader, source.Kind == "recall" ? "timestamp" : "created_at");
            if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created) &&
                keys.Contains(source.Kind + ":" + HistoryStore.Timestamp(created) + ":" + Get(reader, "id"))) { existing++; continue; }
            foreach (var column in new[] { "image_path", "icon_path" })
            {
                var stored = Get(reader, column); if (stored.Length == 0) continue;
                var path = Resolve(source, stored, column == "icon_path");
                if (!seen.Add(path)) continue;
                if (File.Exists(path)) bytes += new FileInfo(path).Length; else missing++;
            }
        }
        return new ImportPreview(source, records, bytes, missing, existing);
    }, token);

    public async Task<ImportProgress> ImportAsync(ImportSource source, IProgress<ImportProgress>? progress, CancellationToken token)
    {
        using var snapshot = await Task.Run(() => Snapshot.Create(source.Database), token);
        using var count = snapshot.Connection.CreateCommand(); count.CommandText = "SELECT count(*) FROM " + Table(source.Kind);
        var total = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var command = snapshot.Connection.CreateCommand(); command.CommandText = "SELECT * FROM " + Table(source.Kind) + " ORDER BY id";
        using var reader = await command.ExecuteReaderAsync(token); long completed = 0, imported = 0, missing = 0;
        var keys = destination.ImportKeys();
        using var target = destination.Open();
        SqliteTransaction? transaction = null;
        var pendingMedia = new List<string>();
        try
        {
            while (await reader.ReadAsync(token))
            {
                token.ThrowIfCancellationRequested();
                var date = Get(reader, source.Kind == "recall" ? "timestamp" : "created_at");
                if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created))
                    throw new InvalidDataException("Kayıt tarihi okunamadı: " + Get(reader, "id"));
                var sourceKey = source.Kind + ":" + HistoryStore.Timestamp(created) + ":" + Get(reader, "id");
                if (!keys.Contains(sourceKey))
                {
                    string media = "", icon = "";
                    foreach (var column in new[] { "image_path", "icon_path" })
                    {
                        var stored = Get(reader, column); if (stored.Length == 0) continue;
                        var path = Resolve(source, stored, column == "icon_path");
                        var copied = await destination.ImportMediaAsync(path, token);
                        if (copied.Length > 0) pendingMedia.Add(copied);
                        if (copied.Length == 0) missing++;
                        if (column == "image_path") media = copied; else icon = copied;
                    }
                    _ = double.TryParse(Get(reader, "interval_at_capture"), CultureInfo.InvariantCulture, out var seconds);
                    if (source.Kind == "recall" && seconds <= 0) seconds = 60;
                    var metadata = JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName,
                        index => reader.IsDBNull(index) ? null : reader.GetValue(index).ToString()));
                    transaction ??= target.BeginTransaction();
                    var title = source.Kind == "dictation" ? (Get(reader, "model") == "1" ? "Large" : "Turbo") : Get(reader, "window_title");
                    var id = destination.Add(source.Kind, created, Get(reader, "app_name"), title,
                        Get(reader, source.Kind == "recall" ? "ocr_text" : "text"), media, seconds, source.Kind == "recall", sourceKey,
                        "done", icon, metadata, target, transaction);
                    if (id != 0) imported++;
                    keys.Add(sourceKey);
                }
                completed++;
                if (completed % 100 == 0 && transaction is not null)
                {
                    transaction.Commit(); transaction.Dispose(); transaction = null;
                    foreach (var name in pendingMedia) destination.ReleasePendingMedia(name); pendingMedia.Clear();
                }
                if (completed % 100 == 0 || completed == total) progress?.Report(new(completed, total, imported, missing, Path.GetFileName(source.Database)));
            }
            transaction?.Commit();
        }
        finally { transaction?.Dispose(); foreach (var name in pendingMedia) destination.ReleasePendingMedia(name); }
        if (source.Kind == "recall") ImportTags(snapshot.Connection);
        return new(completed, total, imported, missing, "Aktarım tamamlandı.");
    }
    private void ImportTags(SqliteConnection source)
    {
        using var exists = source.CreateCommand(); exists.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('tags','app_tags')";
        using var names = exists.ExecuteReader(); var tables = new HashSet<string>(); while (names.Read()) tables.Add(names.GetString(0)); names.Close();
        using var target = destination.Open(); using var transaction = target.BeginTransaction();
        foreach (var table in tables)
        {
            using var read = source.CreateCommand(); read.CommandText = "SELECT * FROM " + table; using var rows = read.ExecuteReader();
            while (rows.Read())
            {
                using var write = target.CreateCommand(); write.Transaction = transaction;
                write.CommandText = table == "tags" ? "INSERT OR IGNORE INTO tags VALUES($tag)" : "INSERT OR IGNORE INTO app_tags VALUES($app,$tag)";
                write.Parameters.AddWithValue("$tag", Get(rows, table == "tags" ? "name" : "tag_name"));
                if (table == "app_tags") write.Parameters.AddWithValue("$app", Get(rows, "app_name")); write.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }
    private static string Table(string kind) => kind switch { "recall" => "activities", "dictation" => "transcripts", _ => throw new ArgumentException("Bilinmeyen aktarım türü.") };
    private static string Get(SqliteDataReader reader, string column)
    {
        for (var i = 0; i < reader.FieldCount; i++) if (reader.GetName(i) == column) return reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "";
        return "";
    }
    private static string Resolve(ImportSource source, string stored, bool icon)
    {
        if (Path.IsPathFullyQualified(stored) && File.Exists(stored)) return stored;
        var folder = Path.GetDirectoryName(source.Database)!;
        var local = Path.GetFullPath(stored, folder); if (File.Exists(local)) return local;
        return Path.Combine(icon ? Path.Combine(folder, "icons") : source.ImagesDirectory, Path.GetFileName(stored));
    }

    private sealed class Snapshot : IDisposable
    {
        private readonly string path;
        public SqliteConnection Connection { get; }
        private Snapshot(string file, SqliteConnection connection) { path = file; Connection = connection; }
        public static Snapshot Create(string database)
        {
            var temporary = Path.Combine(Storage.PortableDataPaths.TemporaryDirectory, "WinForge-import-" + Guid.NewGuid().ToString("N") + ".db");
            var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
            try
            {
                using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(database), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                source.Open(); target.Open(); source.BackupDatabase(target); return new(temporary, target);
            }
            catch { target.Dispose(); File.Delete(temporary); throw; }
        }
        public void Dispose() { Connection.Dispose(); File.Delete(path); }
    }
}
