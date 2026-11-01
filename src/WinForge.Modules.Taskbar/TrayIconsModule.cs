using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using WinForge.Core;

namespace WinForge.Modules.Taskbar;

internal sealed class TrayIconsModule : ITweakModule
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    private const string PromotionValue = "IsPromoted";

    private readonly List<SettingDescriptor> _settings = new();
    private readonly List<TrayGroup> _groups = new();
    private readonly Dictionary<string, PromotionSnapshot> _originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EntryIdentity> _identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlySet<string>? _includedSubKeys;
    private IModuleContext? _context;

    internal TrayIconsModule() { }

    internal TrayIconsModule(IReadOnlySet<string> includedSubKeys)
    {
        ArgumentNullException.ThrowIfNull(includedSubKeys);
        _includedSubKeys = new HashSet<string>(includedSubKeys, StringComparer.OrdinalIgnoreCase);
    }

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
        if (IsActive) return;
        if (_context is null) throw new InvalidOperationException("Tepsi simgesi modülü henüz başlatılmadı.");
        _originals.Clear();
        foreach (var group in _groups)
        {
            foreach (var subKey in group.SubKeys)
            {
                using var key = OpenMatchingEntry(subKey, writable: false);
                if (key is not null) _originals[subKey] = PromotionSnapshot.Read(key);
            }
        }

        IsActive = true;
        try { Apply(); }
        catch
        {
            Deactivate();
            throw;
        }
        _context?.Log.Info($"Tepsi simgesi ayarları uygulandı ({_groups.Count} uygulama).");
    }

    public void Deactivate()
    {
        if (!IsActive && _originals.Count == 0) return;
        var failures = new List<Exception>();
        foreach (var (subKey, snapshot) in _originals)
        {
            try
            {
                using var key = OpenMatchingEntry(subKey, writable: true);
                if (key is not null) snapshot.Restore(key);
            }
            catch (Exception exception) { failures.Add(exception); }
        }

        SettingsChangeBroadcaster.Broadcast("TraySettings");
        if (failures.Count != 0) throw new AggregateException("Tepsi simgesi tercihleri tamamen geri yüklenemedi.", failures);
        _originals.Clear();
        IsActive = false;
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
                using var key = OpenMatchingEntry(subKey, writable: true);
                if (key is null || !_originals.ContainsKey(subKey)) continue;
                if (key.GetValue(PromotionValue) is int current &&
                    (current == 1) == promoted)
                    continue;

                key.SetValue(PromotionValue, promoted ? 1 : 0, RegistryValueKind.DWord);
                changed = true;
            }
        }

        if (changed) SettingsChangeBroadcaster.Broadcast("TraySettings");
    }

    private void BuildGroups()
    {
        _settings.Clear();
        _groups.Clear();
        _identities.Clear();

        Dictionary<string, TrayGroup> groups;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (root is null) return;

            groups = new Dictionary<string, TrayGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                if (_includedSubKeys is not null && !_includedSubKeys.Contains(subKeyName)) continue;
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
                _identities[subKeyName] = EntryIdentity.Read(sub);
                group.Promoted |= sub.GetValue(PromotionValue) is int promoted && promoted == 1;
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

    private RegistryKey? OpenMatchingEntry(string subKey, bool writable)
    {
        var key = Registry.CurrentUser.OpenSubKey($@"{SettingsKey}\{subKey}", writable);
        if (key is null) return null;
        if (_identities.TryGetValue(subKey, out var identity) && identity == EntryIdentity.Read(key)) return key;
        key.Dispose();
        return null;
    }

    private sealed record EntryIdentity(string Executable, string Tooltip, string Guid)
    {
        internal static EntryIdentity Read(RegistryKey key)
        {
            var guid = key.GetValue("IconGuid") switch
            {
                byte[] bytes when bytes.Length == 16 => new System.Guid(bytes).ToString("D"),
                string text when System.Guid.TryParse(text, out var value) => value.ToString("D"),
                object value => value.ToString() ?? "",
                _ => ""
            };
            return new((key.GetValue("ExecutablePath") as string ?? "").ToUpperInvariant(),
                key.GetValue("InitialTooltip") as string ?? "", guid);
        }
    }

    private sealed record PromotionSnapshot(bool Present, object? Value, RegistryValueKind Kind)
    {
        internal static PromotionSnapshot Read(RegistryKey key)
        {
            var present = key.GetValueNames().Contains(PromotionValue, StringComparer.OrdinalIgnoreCase);
            return new(present, present ? key.GetValue(PromotionValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null,
                present ? key.GetValueKind(PromotionValue) : RegistryValueKind.Unknown);
        }

        internal void Restore(RegistryKey key)
        {
            if (Present) key.SetValue(PromotionValue, Value!, Kind);
            else key.DeleteValue(PromotionValue, throwOnMissingValue: false);
        }
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
