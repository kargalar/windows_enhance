using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class TrayIconsModule : ITweakModule
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    private readonly List<SettingDescriptor> _settings = new();
    private readonly List<TrayGroup> _groups = new();
    private readonly Dictionary<string, object?> _originals = new(StringComparer.OrdinalIgnoreCase);
    private IModuleContext? _context;

    public string Id => "tray-icons";
    public string Name => "Sistem Tepsisi Simgeleri";
    public string Category => "Taskbar";
    public string Description => "Bildirim alanında hangi uygulama simgelerinin görüneceğini tek tek seçer. Değişiklikler anında uygulanır.";
    public string? Warning => "Windows, bir uygulama tepsi simgesini yeniden kaydettiğinde seçimi sıfırlayabilir; WinForge açılışta seçimleri yeniden uygular.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings => _settings;

    public void Initialize(IModuleContext context)
    {
        _context = context;
        BuildGroups();
    }

    public void Activate()
    {
        _originals.Clear();
        foreach (var group in _groups)
        {
            foreach (var subKey in group.SubKeys)
                _originals[subKey] = Reg.ReadValue($@"{SettingsKey}\{subKey}", "IsPromoted");
        }

        IsActive = true;
        Apply();
        _context?.Log.Info($"Tepsi simgesi ayarları uygulandı ({_groups.Count} uygulama).");
    }

    public void Deactivate()
    {
        foreach (var (subKey, value) in _originals)
        {
            var path = $@"{SettingsKey}\{subKey}";
            if (value is int promoted) Reg.WriteDword(path, "IsPromoted", promoted);
            else Reg.DeleteValue(path, "IsPromoted");
        }

        _originals.Clear();
        IsActive = false;
        Reg.Broadcast("TraySettings");
    }

    public void Apply()
    {
        if (!IsActive) return;

        var changed = false;
        foreach (var group in _groups)
        {
            var promoted = _context!.Settings.GetBool(group.Key, group.Promoted);
            foreach (var subKey in group.SubKeys)
            {
                if (Reg.ReadValue($@"{SettingsKey}\{subKey}", "IsPromoted") is int current &&
                    (current == 1) == promoted)
                    continue;

                Reg.WriteDword($@"{SettingsKey}\{subKey}", "IsPromoted", promoted ? 1 : 0);
                changed = true;
            }
        }

        if (changed) Reg.Broadcast("TraySettings");
    }

    private void BuildGroups()
    {
        _settings.Clear();
        _groups.Clear();

        Dictionary<string, TrayGroup> groups;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (root is null) return;

            groups = new Dictionary<string, TrayGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subKeyName);
                if (sub?.GetValue("ExecutablePath") is not string executable ||
                    string.IsNullOrWhiteSpace(executable))
                    continue;

                var tooltip = sub.GetValue("InitialTooltip") as string ?? "";
                var label = string.IsNullOrWhiteSpace(tooltip)
                    ? Path.GetFileNameWithoutExtension(executable)
                    : tooltip.Trim();

                if (string.IsNullOrWhiteSpace(label)) label = executable;

                var key = KeyFor(Path.GetFileName(executable), tooltip);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new TrayGroup(key, label, executable);
                    groups[key] = group;
                }

                group.SubKeys.Add(subKeyName);
                group.Promoted |= sub.GetValue("IsPromoted") is int promoted && promoted == 1;
            }
        }
        catch (Exception ex)
        {
            _context?.Log.Error("Tepsi simgesi ayarları okunamadı.", ex);
            return;
        }

        foreach (var group in groups.Values.OrderBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            _groups.Add(group);
            _settings.Add(new SettingDescriptor
            {
                Key = group.Key,
                Label = group.Label,
                Kind = SettingKind.Toggle,
                Default = group.Promoted,
                Section = "Bildirim alanı",
                Description = Path.GetFileName(group.Executable)
            });
        }
    }

    private static string KeyFor(string fileName, string tooltip)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fileName.ToLowerInvariant() + "\n" + tooltip));
        return "tray-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private sealed class TrayGroup
    {
        public TrayGroup(string key, string label, string executable)
        {
            Key = key;
            Label = label;
            Executable = executable;
        }

        public string Key { get; }
        public string Label { get; }
        public string Executable { get; }
        public List<string> SubKeys { get; } = new();
        public bool Promoted { get; set; }
    }
}
