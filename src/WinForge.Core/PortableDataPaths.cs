namespace WinForge.Storage;

// Shared with the platform-independent history assembly.
internal static class PortableDataPaths
{
    internal static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "userdata");
    internal static string TemporaryDirectory => Directory.CreateDirectory(Path.Combine(DirectoryPath, "temp")).FullName;
}
