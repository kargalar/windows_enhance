using System.Security.Cryptography;
using System.Text;

namespace WinForge.Core;

/// <summary>Preserves separately configured history stores inside the portable data directory.</summary>
public static class PortableHistoryMigration
{
    public const string StorageIdKey = "portable-history-id";
    private const string LegacyStorageKey = "storage-directory";

    public static string DirectoryFor(string dataDirectory, string id)
    {
        if (id.Length == 0) return Path.Combine(dataDirectory, "History");
        if (id.Length != 64 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Geçmiş klasörü kimliği geçersiz.");
        return Path.Combine(dataDirectory, "History", "Imported", id);
    }

    public static void Migrate(SettingsStore settings, string dataDirectory, string legacyDefault)
    {
        var destination = DirectoryFor(dataDirectory, "");
        var changed = false;
        foreach (var values in settings.Values.Values)
        {
            if (!values.TryGetValue(LegacyStorageKey, out var configured)) continue;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var source = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!SamePath(source, legacyDefault) && !SamePath(source, destination))
                {
                    if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Önceki geçmiş klasörü bulunamadı: " + source);
                    var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())));
                    var target = DirectoryFor(dataDirectory, id);
                    if (Path.GetFullPath(target).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Geçmiş klasörü, taşınabilir veri klasörünü içeremez: " + source);
                    if (!Directory.Exists(target))
                    {
                        var parent = Path.GetDirectoryName(target)!;
                        Directory.CreateDirectory(parent);
                        var staging = Path.Combine(parent, ".migration-" + Guid.NewGuid().ToString("N"));
                        AppData.CopyMissing(source, staging);
                        Directory.Move(staging, target);
                    }
                    values[StorageIdKey] = id;
                }
            }
            values.Remove(LegacyStorageKey);
            changed = true;
        }
        if (changed) settings.Save();
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
