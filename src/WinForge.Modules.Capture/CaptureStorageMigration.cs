using System.Security.Cryptography;
using WinForge.History;

namespace WinForge.Modules.Capture;

internal static class CaptureStorageMigration
{
    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
    private static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    public static void History(HistoryStore source, string destination)
    {
        var from = Full(source.DirectoryPath); var to = Full(destination);
        if (Same(from, to)) return;
        if (to.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            from.StartsWith(to + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Kaynak ve hedef klasörler iç içe olamaz.");
        if (File.Exists(to) || (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()))
            throw new IOException("PC geçmişi için boş bir hedef klasör seçin; mevcut dosyaların üzerine yazılmaz.");
        var parent = Path.GetDirectoryName(to) ?? throw new IOException("Geçersiz hedef klasör.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".winforge-transfer-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using (var input = source.Open())
            using (var output = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(staging, "history.db"), Pooling = false }.ToString()))
            { output.Open(); input.BackupDatabase(output); }
            CopyTree(from, staging, true);
            if (Directory.Exists(to)) Directory.Delete(to);
            Directory.Move(staging, to);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static void CopyTree(string source, string target, bool root = false)
    {
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Bağlantı içeren geçmiş klasörü otomatik taşınamaz: " + entry.FullName);
            var destination = Path.Combine(target, entry.Name);
            if (entry is DirectoryInfo) { Directory.CreateDirectory(destination); CopyTree(entry.FullName, destination); }
            else
            {
                if (root && entry.Name is ("history.db" or "history.db-wal" or "history.db-shm")) continue;
                File.Copy(entry.FullName, destination);
            }
        }
    }

    public static void Images(HistoryStore store, string oldValue, string newValue)
    {
        var from = Full(string.IsNullOrWhiteSpace(oldValue) ? store.MediaDirectory : oldValue.Trim());
        var to = Full(string.IsNullOrWhiteSpace(newValue) ? store.MediaDirectory : newValue.Trim());
        if (Same(from, to) || !Directory.Exists(from)) return;
        IEnumerable<string> files;
        if (string.IsNullOrWhiteSpace(oldValue))
        {
            using var connection = store.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT media FROM items WHERE kind='capture' AND media<>''";
            using var reader = command.ExecuteReader(); var names = new List<string>();
            while (reader.Read()) { var name = reader.GetString(0); if (Path.GetFileName(name) == name) names.Add(Path.Combine(from, name)); }
            files = names.Where(File.Exists).ToArray();
        }
        else files = Directory.EnumerateFiles(from, "*.png").Where(path =>
        {
            var name = Path.GetFileNameWithoutExtension(path);
            return name.Length == 64 && name.All(Uri.IsHexDigit);
        }).ToArray();
        Directory.CreateDirectory(to);
        foreach (var file in files)
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(target))
            {
                using var a = File.OpenRead(file); using var b = File.OpenRead(target);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException("Hedefte farklı içerikli dosya var: " + target);
            }
            else File.Copy(file, target);
            if (string.IsNullOrWhiteSpace(newValue) && !string.IsNullOrWhiteSpace(oldValue))
            {
                using var connection = store.Open(); using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM items WHERE kind='capture' AND media=$media";
                command.Parameters.AddWithValue("$media", Path.GetFileName(target));
                if (Convert.ToInt64(command.ExecuteScalar()) == 0)
                    store.Add("capture", File.GetLastWriteTimeUtc(file), title: "Ta??nan ekran g?r?nt?s?", media: Path.GetFileName(target));
            }
        }
    }
}
