using WinForge.Core;
using WinForge.History;

internal static class PortableDataChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        check(AppData.DirectoryPath == Path.Combine(AppContext.BaseDirectory, "userdata"),
            "User data is beside the executable");
        var originalWorkingDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            check(AppData.DirectoryPath == Path.Combine(AppContext.BaseDirectory, "userdata"),
                "Portable path is independent of the working directory");
        }
        finally { Environment.CurrentDirectory = originalWorkingDirectory; }

        Migration(root, check);
        var source = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "portable");
        Directory.CreateDirectory(Path.Combine(source, "History", "media"));
        Directory.CreateDirectory(Path.Combine(target, "History"));
        File.WriteAllText(Path.Combine(source, "History", "history.db"), "legacy");
        File.WriteAllText(Path.Combine(source, "History", "media", "image.webp"), "image");
        File.WriteAllText(Path.Combine(target, "History", "history.db"), "existing");
        AppData.CopyMissing(source, target);
        check(File.ReadAllText(Path.Combine(target, "History", "history.db")) == "existing", "Portable migration preserves existing files");
        check(File.ReadAllText(Path.Combine(target, "History", "media", "image.webp")) == "image", "Portable migration copies nested media");
        check(HistoryStore.DefaultDirectory == Path.Combine(AppData.DirectoryPath, "History"), "History uses portable data directory");

        var legacyHistory = Path.Combine(source, "History");
        var settings = new SettingsStore(Path.Combine(root, "settings.json"));
        settings.For("screen-history")["storage-directory"] = legacyHistory;
        PortableHistoryMigration.Migrate(settings, target, Path.Combine(root, "old-default"));
        var id = settings.For("screen-history")[PortableHistoryMigration.StorageIdKey];
        var migrated = PortableHistoryMigration.DirectoryFor(target, id);
        check(File.ReadAllText(Path.Combine(migrated, "history.db")) == "legacy" &&
            File.ReadAllText(Path.Combine(target, "History", "history.db")) == "existing",
            "Separate history databases are preserved during migration");
        check(File.Exists(Path.Combine(migrated, "media", "image.webp")) &&
            !settings.For("screen-history").ContainsKey("storage-directory"), "Custom history and media use a portable path");
        var moved = Path.Combine(root, "moved");
        Directory.Move(target, moved);
        check(File.Exists(Path.Combine(PortableHistoryMigration.DirectoryFor(moved, id), "history.db")),
            "Custom history remains accessible after moving the data directory");
    }

    private static void Migration(string root, Action<bool, string> check)
    {
        var previous = Path.Combine(root, "previous-data");
        var roaming = Path.Combine(root, "roaming");
        var destination = Path.Combine(root, "userdata");
        Directory.CreateDirectory(Path.Combine(previous, "WebView2"));
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(previous, ".legacy-migration-complete"), "1");
        File.WriteAllText(Path.Combine(previous, "settings.json"), "previous");
        File.WriteAllText(Path.Combine(previous, "appearance.json"), "previous-theme");
        File.WriteAllText(Path.Combine(previous, "WebView2", "Preferences"), "browser-preferences");
        File.WriteAllText(Path.Combine(roaming, "appearance.json"), "older-theme");
        File.WriteAllText(Path.Combine(roaming, "winforge.log"), "old-log");
        File.WriteAllText(Path.Combine(destination, "settings.json"), "current");

        AppData.MigrateLegacyData(destination, [previous, roaming, Path.Combine(root, "missing")]);
        check(File.ReadAllText(Path.Combine(destination, "settings.json")) == "current",
            "Userdata migration preserves current settings");
        check(File.ReadAllText(Path.Combine(destination, "appearance.json")) == "previous-theme",
            "Previous portable preferences take priority over AppData");
        check(File.ReadAllText(Path.Combine(destination, "WebView2", "Preferences")) == "browser-preferences" &&
            File.Exists(Path.Combine(destination, "winforge.log")),
            "Migration includes browser preferences and legacy logs");
        check(File.Exists(Path.Combine(previous, "settings.json")), "Migration preserves original data");

        File.Delete(Path.Combine(destination, "winforge.log"));
        AppData.MigrateLegacyData(destination, [previous, roaming]);
        check(!File.Exists(Path.Combine(destination, "winforge.log")),
            "Completed migration does not restore deliberately removed files");
    }
}
