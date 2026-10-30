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
    }
}
