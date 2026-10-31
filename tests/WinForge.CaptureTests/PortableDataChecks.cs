using WinForge.Core;
using WinForge.History;

internal static class PortableDataChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
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
}
