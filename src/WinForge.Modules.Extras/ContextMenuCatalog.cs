using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record ContextMenuEntry(string Id, string Name, string Source, string Match, string Icon, string? Clsid = null);
internal sealed record ContextMenuRule(string Id, string Name = "", bool Visible = true, string Group = "", string Icon = "");

internal static class ContextMenuCatalog
{
    internal const string RuntimeKey = @"Software\WinForge\ContextMenu\Runtime";
    internal const string CatalogKey = @"Software\WinForge\ContextMenu\Catalog";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Roots = [@"*", @"Directory", @"Directory\Background", @"Drive", @"Folder", @"AllFilesystemObjects", @"DesktopBackground"];
    internal static string Normalize(string text) => text.Split('\t')[0].Replace("&", "").Replace("…", "").Replace("...", "").Trim().ToLowerInvariant();
    internal static string DisplayName(string value)
    {
        if (!value.StartsWith('@')) return value.Replace("&", "").Trim();
        var buffer = new StringBuilder(2048);
        return SHLoadIndirectString(value, buffer, (uint)buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString().Replace("&", "").Trim() : "";
    }
    internal static bool IsOpaque(string value) => Guid.TryParse(value, out _) ||
        (value.Length >= 10 && !value.Any(char.IsWhiteSpace) && value.Any(char.IsDigit) && !value.Contains('.'));
    internal static string BestName(string raw, string? resource, string? description, string fallback)
    {
        foreach (var candidate in new[] { resource, raw, description })
        {
            var name = DisplayName(candidate ?? "");
            if (!string.IsNullOrWhiteSpace(name) && !IsOpaque(name)) return name;
        }
        return fallback;
    }
    internal static List<ContextMenuEntry> Read()
    {
        var entries = new Dictionary<string, ContextMenuEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name, icon) in new[]
        {
            ("properties", "Özellikler", "imageres.dll,-5366"), ("new-folder", "Yeni klasör", "shell32.dll,3"),
            ("new", "Yeni", "shell32.dll,3"), ("copy", "Kopyala", "shell32.dll,134"),
            ("cut", "Kes", "shell32.dll,133"), ("paste", "Yapıştır", "shell32.dll,135"),
            ("delete", "Sil", "shell32.dll,131"), ("rename", "Yeniden adlandır", ""), ("refresh", "Yenile", "")
        }) entries["builtin:" + id] = new("builtin:" + id, name, "Windows komutu", "builtin:" + id, icon);
        foreach (var root in Roots)
        {
            ReadVerbs(root, entries);
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var handlers = hive.OpenSubKey(@"Software\Classes\" + root + @"\shellex\ContextMenuHandlers");
                if (handlers is null) continue;
                foreach (var keyName in handlers.GetSubKeyNames())
                {
                    using var handler = handlers.OpenSubKey(keyName);
                    var clsid = handler?.GetValue("") as string ?? keyName;
                    if (!Guid.TryParse(clsid, out var guid)) continue;
                    clsid = guid.ToString("B");
                    var id = "extension:" + clsid;
                    if (entries.ContainsKey(id)) continue;
                    using var registration = Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid);
                    using var server = registration?.OpenSubKey("InprocServer32");
                    using var defaultIcon = registration?.OpenSubKey("DefaultIcon");
                    var dll = Environment.ExpandEnvironmentVariables(server?.GetValue("") as string ?? "").Trim('"');
                    string? description = null;
                    if (File.Exists(dll)) { try { description = FileVersionInfo.GetVersionInfo(dll).FileDescription; } catch { } }
                    var registeredName = registration?.GetValue("") as string;
                    var known = clsid.ToLowerInvariant() switch
                    {
                        "{d969a300-e7ff-11d0-a93b-00a0c90f2719}" => "Yeni",
                        "{09799afb-ad67-11d1-abcd-00c04fc30936}" => "Birlikte aç",
                        "{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}" => "Başlangıç ekranına sabitle",
                        _ => null
                    };
                    var name = known ?? BestName(registeredName ?? keyName, registration?.GetValue("LocalizedString") as string,
                        description, "Adsız kabuk uzantısı");
                    var icon = defaultIcon?.GetValue("") as string ?? (File.Exists(dll) ? dll + ",0" : "");
                    entries[id] = new(id, name, "Kabuk uzantısı", Normalize(name), icon, clsid);
                }
            }
        }
        using var catalog = Registry.CurrentUser.OpenSubKey(CatalogKey);
        if (catalog is not null)
        foreach (var id in catalog.GetSubKeyNames().Take(512))
        {
            using var row = catalog.OpenSubKey(id);
            var name = row?.GetValue("label") as string ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            var match = Normalize(name);
            // Native canonical IDs unify Windows items with their live names and icons.
            var canonical = row?.GetValue("canonical") as string;
            if (string.IsNullOrEmpty(canonical)) canonical = null;
            var key = !string.IsNullOrEmpty(canonical) ? canonical : "menu:" + match;
            entries[key] = new(key, name, "Gerçek menü öğesi", canonical ?? match, row?.GetValue("icon") as string ?? "");
        }
        return entries.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Source).ToList();
    }
    private static void ReadVerbs(string root, Dictionary<string, ContextMenuEntry> entries)
    {
        using var shell = Registry.ClassesRoot.OpenSubKey(root + @"\shell");
        if (shell is null) return;
        foreach (var keyName in shell.GetSubKeyNames())
        {
            if (keyName.StartsWith("WinForge.", StringComparison.OrdinalIgnoreCase)) continue;
            using var verb = shell.OpenSubKey(keyName);
            if (verb is null) continue;
            var name = BestName(verb.GetValue("") as string ?? keyName, verb.GetValue("MUIVerb") as string, null, "Menü komutu");
            var id = "verb:" + root + ":" + keyName;
            entries[id] = new(id, name, root + " · komut", Normalize(name), verb.GetValue("Icon") as string ?? "");
        }
    }
    internal static void ValidateRules(IEnumerable<ContextMenuRule> rules)
    {
        var ids = new HashSet<string>();
        foreach (var rule in rules)
        {
            if (!ids.Add(rule.Id) || rule.Id.Length > 1024 || rule.Name.Length > 120 || rule.Group.Length > 80 || rule.Icon.Length > 2048 ||
                rule.Name.Contains('\n') || rule.Group.IndexOfAny(['\r', '\n', '\t', ';']) >= 0)
                throw new FormatException("Geçersiz menü adı, grup veya simge.");
        }
    }
    internal static string? IconPreview(string spec)
    {
        try
        {
            var source = LoadIcon(spec);
            if (source is null) return null;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream(); encoder.Save(stream);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch { return null; }
    }
    internal static string ExportIcon(string spec)
    {
        if (string.IsNullOrEmpty(spec)) return "";
        var directory = Path.Combine(AppData.DirectoryPath, "ContextMenu", "Icons"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(spec))) + ".bmp");
        var source = LoadIcon(spec) ?? throw new FormatException("Simge okunamadı. ICO, PNG, JPG, BMP veya EXE/DLL simge kaynağı seçin.");
        var scaled = new TransformedBitmap(source, new ScaleTransform(20d / source.PixelWidth, 20d / source.PixelHeight));
        var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0)));
        using var output = File.Create(path); encoder.Save(output); return path;
    }
    private static BitmapSource? LoadIcon(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var path = Environment.ExpandEnvironmentVariables(spec.Trim().TrimStart('@'));
        var index = 0;
        var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out var parsed)) { index = parsed; path = path[..comma]; }
        path = path.Trim().Trim('"');
        if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), path);
        if (File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new FormatException("Simge dosyası en fazla 16 MB olabilir.");
            using var stream = File.OpenRead(path);
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bitmap.Freeze(); return bitmap;
        }
        if (SHDefExtractIcon(path, index, 0, out var icon, out var small, 32) != 0 || icon == IntPtr.Zero) return null;
        try { var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24)); bitmap.Freeze(); return bitmap; }
        finally { Native.DestroyIcon(icon); if (small != IntPtr.Zero) Native.DestroyIcon(small); }
    }
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint count, IntPtr reserved);
    [DllImport("shell32.dll", EntryPoint = "SHDefExtractIconW", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string path, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
}
