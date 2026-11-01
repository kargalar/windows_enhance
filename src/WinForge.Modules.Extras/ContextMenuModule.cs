using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed partial class ContextMenuModule : ITweakModule, IAppCommandModule
{
    private const string CustomPrefix = "WinForge.";
    private const string BlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    private static readonly string[] HandlerRoots =
    {
        @"Software\Classes\*\shellex\ContextMenuHandlers",
        @"Software\Classes\Directory\shellex\ContextMenuHandlers",
        @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers",
        @"Software\Classes\Drive\shellex\ContextMenuHandlers",
        @"Software\Classes\Folder\shellex\ContextMenuHandlers",
        @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers"
    };

    private static readonly (string Setting, string Root)[] CustomRoots =
    {
        ("desktop-items", @"Software\Classes\Directory\Background\shell"),
        ("folder-items", @"Software\Classes\Directory\shell"),
        ("file-items", @"Software\Classes\*\shell")
    };

    private readonly List<SettingDescriptor> _settings = new();
    private readonly List<Extension> _extensions = new();
    private readonly Dictionary<string, bool> _originalBlocked = new(StringComparer.OrdinalIgnoreCase);
    private IModuleContext? _context;

    public string Id => "context-menu";
    public string Name => "Sağ Tık Menüsü";
    public string Category => "Gezgin";
    public string Description => "Gezgin sağ tık menüsüne özel öğeler ekler ve kayıtlı kabuk uzantılarını gizler. Değişiklikler çoğunlukla anında görünür; görünmezse Explorer'ı yeniden başlatın.";
    public string? Warning => "Gruplar ve öğe simgeleri klasik Gezgin menüsünde / Daha fazla seçenek göster menüsünde uygulanır. Diğer uygulamaların menüleri etkilenmez. Kapatınca özgün menü geri gelir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context)
    {
        _context = context;
        BuildSettings();
        InitializePanel();
    }

    public void Activate()
    {
        _originalBlocked.Clear();
        foreach (var extension in _extensions)
            _originalBlocked[extension.Clsid] = IsBlocked(extension.Clsid);

        IsActive = true;
        Apply();
        _context?.Log.Info($"Sağ tık menüsü ayarları uygulandı ({_extensions.Count} uzantı).");
    }

    public void Deactivate()
    {
        _runtime?.Deactivate();
        ApplyPreparedActions([]);
        foreach (var (_, root) in CustomRoots)
            RemoveCustomItems(root);

        foreach (var (clsid, blocked) in _originalBlocked)
            SetBlocked(clsid, blocked);

        _originalBlocked.Clear();
        IsActive = false;
        Reg.Broadcast("ShellState");
    }

    public void Apply()
    {
        if (!IsActive) return;

        var settings = _context!.Settings;

        foreach (var (setting, root) in CustomRoots)
            ApplyCustomItems(root, settings.GetString(setting));

        foreach (var extension in _extensions)
            SetBlocked(extension.Clsid, settings.GetBool(extension.Key, extension.Blocked));

        ApplyPreparedActions(_rules);
        _runtime?.Apply(_entries, _rules, ReadGroupIcons(), _context!.Settings);

        Reg.Broadcast("ShellState");
    }

    private void ApplyCustomItems(string root, string text)
    {
        RemoveCustomItems(root);

        var entries = ParseItems(text);
        if (entries.Count == 0) return;

        for (var i = 0; i < entries.Count; i++)
        {
            var (label, command) = entries[i];
            using var item = Registry.CurrentUser.CreateSubKey($@"{root}\{CustomPrefix}{i}");
            if (item is null) continue;

            item.SetValue(null, label, RegistryValueKind.String);
            using var commandKey = item.CreateSubKey("command");
            commandKey?.SetValue(null, command, RegistryValueKind.String);
        }
    }

    private static List<(string Label, string Command)> ParseItems(string text)
    {
        var items = new List<(string, string)>();

        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = raw.IndexOf('|');
            if (separator <= 0) continue;

            var label = raw[..separator].Trim();
            var command = raw[(separator + 1)..].Trim();
            if (label.Length > 0 && command.Length > 0)
                items.Add((label, command));
        }

        return items;
    }

    private static void RemoveCustomItems(string root)
    {
        try
        {
            using var shell = Registry.CurrentUser.OpenSubKey(root, writable: true);
            if (shell is null) return;

            foreach (var name in shell.GetSubKeyNames())
            {
                // This module owns only WinForge.<index>; named verbs belong to other features.
                if (name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase) &&
                    name.Length > CustomPrefix.Length &&
                    name.AsSpan(CustomPrefix.Length).IndexOfAnyExceptInRange('0', '9') < 0)
                    shell.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }
        catch
        {
            // A locked shell key must not break the rest of the module.
        }
    }

    private void BuildSettings()
    {
        _settings.Clear();
        _extensions.Clear();

        _settings.Add(new SettingDescriptor
        {
            Key = "desktop-items",
            Label = "Masaüstü ve klasör arka planı",
            Kind = SettingKind.Text,
            Default = "",
            Section = "Özel öğeler",
            Description = "Başlık|Komut; Başlık2|Komut2 biçiminde yazın. Örnek: Not Defteri|notepad.exe"
        });
        _settings.Add(new SettingDescriptor
        {
            Key = "folder-items",
            Label = "Klasörler",
            Kind = SettingKind.Text,
            Default = "",
            Section = "Özel öğeler",
            Description = "Klasörlere sağ tıklanınca görünür. Dosya yolu için %1 kullanılabilir."
        });
        _settings.Add(new SettingDescriptor
        {
            Key = "file-items",
            Label = "Dosyalar",
            Kind = SettingKind.Text,
            Default = "",
            Section = "Özel öğeler",
            Description = "Dosyalara sağ tıklanınca görünür. Seçili dosya için %1 kullanılabilir."
        });

        var extensions = new Dictionary<string, Extension>(StringComparer.OrdinalIgnoreCase);
        var blocked = ReadBlocked();

        foreach (var root in HandlerRoots)
        {
            ReadHandlers(Registry.CurrentUser, root, extensions);
            ReadHandlers(Registry.LocalMachine, "SOFTWARE\\Classes" + root["Software\\Classes".Length..], extensions);
        }

        foreach (var clsid in blocked)
        {
            if (extensions.ContainsKey(clsid)) continue;
            extensions[clsid] = new Extension(clsid, ResolveName(clsid) ?? clsid, true);
        }

        foreach (var extension in extensions.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            extension.Blocked = blocked.Contains(extension.Clsid);
            _extensions.Add(extension);

        }
    }

    private static void ReadHandlers(RegistryKey hive, string root, Dictionary<string, Extension> extensions)
    {
        try
        {
            using var handlers = hive.OpenSubKey(root);
            if (handlers is null) return;

            foreach (var name in handlers.GetSubKeyNames())
            {
                using var handler = handlers.OpenSubKey(name);
                if (handler?.GetValue(null) is not string value) continue;

                var clsid = value.Trim();
                if (!clsid.StartsWith('{') || !clsid.EndsWith('}')) continue;
                if (extensions.ContainsKey(clsid)) continue;

                extensions[clsid] = new Extension(clsid, ResolveName(clsid) ?? name, false);
            }
        }
        catch
        {
            // Some hive/root combinations are not readable.
        }
    }

    private static string? ResolveName(string clsid)
    {
        foreach (var (hive, root) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Classes\CLSID\" + clsid),
                     (Registry.LocalMachine, @"SOFTWARE\Classes\CLSID\" + clsid)
                 })
        {
            try
            {
                using var key = hive.OpenSubKey(root);
                if (key?.GetValue(null) is string name && !string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch
            {
            }
        }

        return null;
    }

    private static HashSet<string> ReadBlocked()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BlockedKey);
            if (key is null) return result;

            foreach (var name in key.GetValueNames())
                result.Add(name);
        }
        catch
        {
        }

        return result;
    }

    private static bool IsBlocked(string clsid)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BlockedKey);
            return key?.GetValue(clsid) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetBlocked(string clsid, bool blocked)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(BlockedKey);
            if (key is null) return;

            if (blocked) key.SetValue(clsid, "", RegistryValueKind.String);
            else key.DeleteValue(clsid, throwOnMissingValue: false);
        }
        catch
        {
            // Explorer may reset permissions on the blocked list.
        }
    }

    private sealed class Extension
    {
        public Extension(string clsid, string name, bool blocked)
        {
            Clsid = clsid;
            Name = name;
            Blocked = blocked;
            Key = "hide-" + clsid.Trim('{', '}').ToLowerInvariant();
        }

        public string Clsid { get; }
        public string Name { get; }
        public string Key { get; }
        public bool Blocked { get; set; }
    }
}
