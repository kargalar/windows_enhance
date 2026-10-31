using System.IO;

namespace WinForge.Core;

/// <summary>Locations for portable application-owned data.</summary>
public static class AppData
{
    public static string DirectoryPath => Storage.PortableDataPaths.DirectoryPath;
    public static string TemporaryDirectory => Storage.PortableDataPaths.TemporaryDirectory;

    public static void MigrateLegacyData()
    {
        MigrateLegacyData(DirectoryPath,
        [
            Path.Combine(AppContext.BaseDirectory, "data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinForge"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinForge")
        ]);
    }

    public static void MigrateLegacyData(string destination, IEnumerable<string> sources)
    {
        Directory.CreateDirectory(destination);
        var marker = Path.Combine(destination, ".userdata-migration-complete");
        if (File.Exists(marker)) return;
        foreach (var source in sources) CopyMissing(source, destination);
        File.WriteAllText(marker, "1");
    }

    public static void CopyMissing(string source, string destination)
    {
        if (!Directory.Exists(source) || string.Equals(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            CopyMissing(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
