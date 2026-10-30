using System.Text;
using WinForge.FileTools.Engine;
using WinForge.Core;

namespace WinForge.ClipboardLine;

internal static class ClipboardFileExport
{
    private const int MaximumNameLength = 80;
    private const int MaximumCollisions = 10_000;

    internal static string[] Export(ClipboardLibrary library, IReadOnlyList<ClipItem> items, string directory)
    {
        var destination = Path.GetFullPath(directory);
        var outputs = new List<string>();
        foreach (var item in items)
        {
            var name = string.Concat(item.DisplayTitle.Take(MaximumNameLength).Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim(' ', '.');
            var extension = item.Kind is ClipKind.Image or ClipKind.Recording ? Path.GetExtension(item.Content) : ".txt";
            if (name.Length == 0 || !IsSafeName(destination, name + extension)) name = "Pano-" + item.Id.ToString("N");
            var written = false;
            for (var index = 0; index < MaximumCollisions; index++)
            {
                var filename = name + (index == 0 ? "" : $" ({index})") + extension;
                var path = SafePaths.ArchiveEntry(destination, filename);
                FileStream stream;
                try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { continue; }
                try
                {
                    using (stream)
                    {
                        if (item.Kind is ClipKind.Image or ClipKind.Recording)
                        {
                            using var source = new FileStream(library.MediaPath(item), FileMode.Open, FileAccess.Read, FileShare.Read);
                            source.CopyTo(stream);
                        }
                        else
                        {
                            var bytes = new UTF8Encoding(false).GetBytes(item.Content);
                            stream.Write(bytes);
                        }
                        stream.Flush(true);
                    }
                }
                catch { File.Delete(path); throw; }
                outputs.Add(path); written = true; break;
            }
            if (!written) throw new IOException("Dışa aktarım için kullanılabilir bir dosya adı bulunamadı.");
        }
        return outputs.ToArray();
    }

    private static bool IsSafeName(string directory, string name)
    {
        try { _ = SafePaths.ArchiveEntry(directory, name); return true; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or NotSupportedException or PathTooLongException) { return false; }
    }

    internal static void RemoveExpiredDragFiles(string libraryDirectory, DateTimeOffset now, ILogger? log = null)
    {
        var root = Path.GetFullPath(Path.Combine(libraryDirectory, "drag"));
        try
        {
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            foreach (var candidate in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var info = new DirectoryInfo(candidate);
                    if (!string.Equals(info.Parent?.FullName, root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(info.Name, "N", out _) || (info.Attributes & FileAttributes.ReparsePoint) != 0 || now - info.LastWriteTimeUtc < TimeSpan.FromDays(1)) continue;
                    foreach (var entry in info.EnumerateFileSystemInfos())
                    {
                        if (entry is not FileInfo file || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        file.Delete();
                    }
                    info.Delete();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log?.Info("Eski pano sürükleme dosyaları silinemedi: " + error.GetType().Name); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log?.Info("Pano sürükleme klasörü okunamadı: " + error.GetType().Name); }
    }
}
