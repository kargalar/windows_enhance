using System.IO;
using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record KnownFolderDefinition(string Key, string Name, Guid Id);

internal interface IKnownFolderPaths
{
    string Read(Guid id);
    void Write(Guid id, string path);
}

internal sealed class WindowsKnownFolderPaths : IKnownFolderPaths
{
    private const uint DontVerify = 0x4000;

    public string Read(Guid id)
    {
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(in id, DontVerify, IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStringUni(pointer) ?? throw new IOException("Klasör yolu okunamadı."); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    public void Write(Guid id, string path) =>
        Marshal.ThrowExceptionForHR(SHSetKnownFolderPath(in id, 0, IntPtr.Zero, path));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(in Guid id, uint flags, IntPtr token, string path);
}

internal sealed class KnownFolderPathService(IKnownFolderPaths paths, IModuleSettings settings)
{
    internal static readonly IReadOnlyList<KnownFolderDefinition> Folders = Array.AsReadOnly<KnownFolderDefinition>(
    [
        new("desktop", "Masaüstü", new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")),
        new("documents", "Belgeler", new("FDD39AD0-238F-46AF-ADB4-6C85480369C7")),
        new("downloads", "İndirilenler", new("374DE290-123F-4565-9164-39C4925E467B")),
        new("pictures", "Resimler", new("33E28130-4E1E-4676-835A-98395C3BC3BB")),
        new("music", "Müzik", new("4BD8D571-6D19-48D3-BE97-422220080E43")),
        new("videos", "Videolar", new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"))
    ]);

    public string Read(string key) => paths.Read(Find(key).Id);
    public string Original(string key) => settings.GetString(BackupKey(Find(key).Key));

    public void Apply(string key, string destination)
    {
        var folder = Find(key);
        var target = Normalize(destination);
        var current = paths.Read(folder.Id);
        if (SamePath(current, target)) return;
        foreach (var other in Folders.Where(other => other.Key != key))
        {
            var otherPath = paths.Read(other.Id);
            if (Overlaps(target, otherPath))
                throw new IOException($"Hedef, {other.Name} klasörüyle aynı veya iç içe olamaz.");
        }
        if (File.Exists(target)) throw new IOException("Hedef bir dosya olamaz.");
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Önce hedef klasörü oluşturun veya Gözat ile mevcut bir klasör seçin.");
        // Save recovery information before changing Windows. Never overwrite the first backup.
        if (Original(key).Length == 0) settings.Set(BackupKey(key), current);
        paths.Write(folder.Id, target);
    }

    public void Restore(string key)
    {
        var original = Original(key);
        if (original.Length == 0) throw new InvalidOperationException("Bu klasör için kayıtlı önceki konum yok.");
        Apply(key, original);
    }

    internal static string Normalize(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.Length == 0 || expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            expanded.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment.EndsWith('.') || segment.EndsWith(' ')) ||
            !Path.IsPathFullyQualified(expanded) || expanded.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            expanded.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new FormatException("Tam bir klasör yolu girin. Örnek: D:\\Belgeler");
        var result = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        if (result.Length >= 260 || result.IndexOf(':', 2) >= 0 || result.Contains('*') || result.Contains('?') ||
            SamePath(result, Path.GetPathRoot(result) ?? ""))
            throw new FormatException("Sürücü kökü yerine geçerli bir alt klasör seçin (en fazla 259 karakter).");
        return result;
    }

    private static KnownFolderDefinition Find(string key) => Folders.FirstOrDefault(folder => folder.Key == key)
        ?? throw new ArgumentException("Bilinmeyen kullanıcı klasörü.");
    private static string BackupKey(string key) => "original-path-" + key;
    private static bool SamePath(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);
    private static bool Overlaps(string first, string second) => SamePath(first, second) ||
        first.StartsWith(Path.TrimEndingDirectorySeparator(second) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        second.StartsWith(Path.TrimEndingDirectorySeparator(first) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
