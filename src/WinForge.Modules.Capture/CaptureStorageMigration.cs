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
        var fromHistory = string.IsNullOrWhiteSpace(oldValue);
        var toHistory = string.IsNullOrWhiteSpace(newValue);
        var defaultFolder = Path.Combine(store.MediaDirectory, HistoryStore.CaptureImages);
        var from = Full(fromHistory ? defaultFolder : oldValue.Trim());
        var to = Full(toHistory ? defaultFolder : newValue.Trim());
        if (Same(from, to)) return;
        var files = new List<(string Source, string Relative)>();
        if (fromHistory)
        {
            using var connection = store.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT media FROM items WHERE kind='capture' AND media<>''";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase)) continue;
                var imagePrefix = HistoryStore.CaptureImages + "/";
                var relative = name.StartsWith(imagePrefix, StringComparison.Ordinal)
                    ? name[imagePrefix.Length..].Replace('/', Path.DirectorySeparatorChar) : Path.GetFileName(name);
                var source = store.ResolveMedia(name);
                if (File.Exists(source)) files.Add((source, relative));
            }
        }
        else
        {
            AddExports(from, "");
            var fullscreen = new DirectoryInfo(Path.Combine(from, HistoryStore.FullscreenFolderName));
            if (fullscreen.Exists && (fullscreen.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                AddExports(fullscreen.FullName, fullscreen.Name);
                foreach (var application in fullscreen.EnumerateDirectories())
                    if ((application.Attributes & FileAttributes.ReparsePoint) == 0 && HistoryStore.IsValidMediaSegment(application.Name))
                        AddExports(application.FullName, Path.Combine(fullscreen.Name, application.Name));
            }
        }

        foreach (var (file, relative) in files)
        {
            var target = Path.Combine(to, relative);
            if (Same(file, target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                using var a = File.OpenRead(file); using var b = File.OpenRead(target);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b))) throw new IOException("Hedefte farklı içerikli dosya var: " + target);
            }
            else File.Copy(file, target);
            if (toHistory)
            {
                var name = HistoryStore.CaptureImages + "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
                var legacyName = Path.GetFileName(relative) == relative ? relative : name;
                using var connection = store.Open(); using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM items WHERE kind='capture' AND media IN ($media, $legacy)";
                command.Parameters.AddWithValue("$media", name);
                command.Parameters.AddWithValue("$legacy", legacyName);
                if (Convert.ToInt64(command.ExecuteScalar()) == 0)
                    store.Add("capture", File.GetLastWriteTimeUtc(file), title: "Taşınan ekran görüntüsü", media: name);
            }
        }

        void AddExports(string folder, string relativeFolder)
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if ((name.Length == 64 && name.All(Uri.IsHexDigit)) || CaptureImageFile.IsCaptureFilename(Path.GetFileName(file)))
                    files.Add((file, Path.Combine(relativeFolder, Path.GetFileName(file))));
            }
        }
    }
}
