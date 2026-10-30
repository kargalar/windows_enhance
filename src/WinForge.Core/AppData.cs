using System.IO;

namespace WinForge.Core;

/// <summary>Locations for portable application-owned data.</summary>
public static class AppData
{
    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "data");
    public static string TemporaryDirectory => Directory.CreateDirectory(Path.Combine(DirectoryPath, "temp")).FullName;

    public static void MigrateLegacyData()
    {
        Directory.CreateDirectory(DirectoryPath);
        var marker = Path.Combine(DirectoryPath, ".legacy-migration-complete");
        if (File.Exists(marker)) return;
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var source = Path.Combine(Environment.GetFolderPath(folder), "WinForge");
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source))
            {
                var target = Path.Combine(DirectoryPath, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target, false);
            }
            foreach (var directory in Directory.EnumerateDirectories(source))
                if (!string.Equals(Path.GetFileName(directory), "WebView2", StringComparison.OrdinalIgnoreCase))
                    CopyMissing(directory, Path.Combine(DirectoryPath, Path.GetFileName(directory)));
        }
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
