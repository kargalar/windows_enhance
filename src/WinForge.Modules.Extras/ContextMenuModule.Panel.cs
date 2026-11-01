using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed partial class ContextMenuModule : IModulePanel
{
    private List<ContextMenuEntry> _entries = new();
    private List<ContextMenuRule> _rules = new();
    private readonly Dictionary<string, string?> _previews = new();
    private ContextMenuRuntime? _runtime;
    public event Action? PanelStateChanged;
    private void InitializePanel()
    {
        _entries = ContextMenuCatalog.Read();
        _rules = JsonSerializer.Deserialize<List<ContextMenuRule>>(_context!.Settings.GetString("menu-rules", "[]"), ContextMenuCatalog.Json) ?? [];
        ContextMenuCatalog.ValidateRules(_rules); _runtime = new(_context.Log);
    }
    private string? Preview(string icon)
    {
        if (!_previews.TryGetValue(icon, out var result)) _previews[icon] = result = ContextMenuCatalog.IconPreview(icon);
        return result;
    }
    public object GetPanelState() => new
    {
        kind = "context-menu", groups = ReadGroups(),
        cleanSeparators = _context!.Settings.GetBool("clean-separators", true),
        showSeparators = _context.Settings.GetBool("show-separators", true),
        unquotedCopyPath = _context.Settings.GetBool("unquoted-copy-path", true),
        groupIcons = ReadGroupIcons().ToDictionary(pair => pair.Key, pair => new { icon = pair.Value, iconPreview = Preview(pair.Value) }),
        entries = _entries.Select(entry =>
        {
            var rule = _rules.FirstOrDefault(r => r.Id == entry.Id);
            var icon = rule?.Icon is { Length: > 0 } custom ? custom : entry.Icon;
            return new { entry.Id, originalName = entry.Name, name = rule?.Name is { Length: > 0 } name ? name : entry.Name,
                entry.Source, identity = entry.Match, isSeparator = entry.Match.StartsWith("separator:"), scope = ContextMenuCatalog.Scope(entry), contexts = rule?.Contexts, configured = rule is not null,
                separatorBefore = rule?.SeparatorBefore ?? false, separatorAfter = rule?.SeparatorAfter ?? false,
                visible = rule?.Visible ?? (!entry.Id.StartsWith("prepared:") && (entry.Clsid is null || !_context!.Settings.GetBool("hide-" + entry.Clsid.Trim('{', '}').ToLowerInvariant(), IsBlocked(entry.Clsid)))),
                group = rule?.Group ?? "", icon = rule?.Icon ?? "", iconPreview = Preview(icon), hasOriginalIcon = entry.Icon.Length > 0 };
        }).ToArray()
    };
    public void HandlePanelCommand(string action, JsonElement payload)
    {
        if (action == "save-options")
        {
            foreach (var (property, key) in new[] { ("cleanSeparators", "clean-separators"), ("showSeparators", "show-separators"), ("unquotedCopyPath", "unquoted-copy-path") })
                if (payload.TryGetProperty(property, out var value)) _context!.Settings.Set(key, value.GetBoolean());
            if (IsActive) _runtime!.Apply(_entries, _rules, ReadGroupIcons(), _context!.Settings);
            PanelStateChanged?.Invoke(); return;
        }
        if (action == "refresh") { _entries = ContextMenuCatalog.Read(); _previews.Clear(); PanelStateChanged?.Invoke(); return; }
        if (action is "pick-group-icon" or "clear-group-icon")
        {
            var name = payload.GetProperty("name").GetString() ?? "";
            if (!ReadGroups().Contains(name)) throw new FormatException("Grup bulunamadı.");
            var icons = ReadGroupIcons();
            if (action == "pick-group-icon")
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "Grup simgesi", Filter = "Simge ve görseller|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|Tüm dosyalar|*.*" };
                if (picker.ShowDialog() != true) return;
                _ = ContextMenuCatalog.ExportIcon(picker.FileName);
                icons[name] = picker.FileName;
            }
            else icons.Remove(name);
            if (IsActive) _runtime!.Apply(_entries, _rules, icons, _context!.Settings);
            _context!.Settings.Set("menu-group-icons", JsonSerializer.Serialize(icons, ContextMenuCatalog.Json));
            PanelStateChanged?.Invoke(); return;
        }
        if (action == "save-group" || action == "delete-group")
        {
            var name = payload.GetProperty("name").GetString()?.Trim() ?? "";
            if (name.Length == 0) throw new FormatException("Grup adı gerekli.");
            ContextMenuCatalog.ValidateRules([new("group", Group: name)]);
            var ids = action == "delete-group" ? [] : payload.GetProperty("ids").Deserialize<string[]>(ContextMenuCatalog.Json) ?? [];
            var previousName = payload.TryGetProperty("previousName", out var oldName) ? oldName.GetString() ?? "" : name;
            if (previousName != name && ReadGroups().Contains(name)) throw new FormatException("Bu grup adı zaten kullanılıyor.");
            if (ids.Any(id => !_entries.Any(e => e.Id == id && !e.Match.StartsWith("separator:")))) throw new FormatException("Menü öğesi bulunamadı veya ayırıcı çizgi gruba eklenemez.");
            var rules = _rules.Select(r => r.Group == name || r.Group == previousName ? r with { Group = "" } : r).ToList();
            foreach (var id in ids)
            {
                var rule = rules.FirstOrDefault(r => r.Id == id) ?? new ContextMenuRule(id, Visible: !id.StartsWith("prepared:"));
                rules.RemoveAll(r => r.Id == id); rules.Add(rule with { Group = name });
            }
            var groups = ReadGroups().Where(g => g != name && g != previousName).ToList();
            if (action == "save-group") groups.Add(name);
            var icons = ReadGroupIcons();
            var icon = payload.TryGetProperty("icon", out var source) ? source.GetString() ?? "" : icons.GetValueOrDefault(previousName, "");
            ContextMenuCatalog.ValidateRules([new("group", Group: name, Icon: icon)]);
            if (action == "save-group" && icon.Length > 0) _ = ContextMenuCatalog.ExportIcon(icon);
            icons.Remove(previousName); icons.Remove(name);
            if (action == "save-group" && icon.Length > 0) icons[name] = icon;
            if (IsActive) { ApplyPreparedActions(rules); _runtime!.Apply(_entries, rules, icons, _context!.Settings); }
            _context!.Settings.Set("menu-group-icons", JsonSerializer.Serialize(icons, ContextMenuCatalog.Json));
            _rules = rules;
            _context!.Settings.Set("menu-groups", JsonSerializer.Serialize(groups, ContextMenuCatalog.Json));
            _context.Settings.Set("menu-rules", JsonSerializer.Serialize(rules, ContextMenuCatalog.Json));
            PanelStateChanged?.Invoke(); return;
        }
        if (action == "reset-item")
        {
            var ids = payload.GetProperty("ids").Deserialize<string[]>(ContextMenuCatalog.Json) ?? [];
            foreach (var itemId in ids)
                HandlePanelCommand("reset", JsonSerializer.SerializeToElement(new { id = itemId }, ContextMenuCatalog.Json));
            return;
        }
        if (action is "save-item" or "save-items")
        {
            var updates = action == "save-items" ? payload.GetProperty("items").EnumerateArray().ToArray() : [payload];
            var replacements = new Dictionary<string, ContextMenuRule>();
            foreach (var update in updates)
            {
                var ids = update.GetProperty("ids").Deserialize<string[]>(ContextMenuCatalog.Json) ?? [];
                var rule = update.GetProperty("rule").Deserialize<ContextMenuRule>(ContextMenuCatalog.Json) ?? throw new FormatException("Menü ayarı eksik.");
                if (ids.Length == 0 || ids.Any(id => !_entries.Any(e => e.Id == id))) throw new FormatException("Menü öğesi bulunamadı.");
                foreach (var id in ids) replacements[id] = rule with { Id = id, Group = _rules.FirstOrDefault(r => r.Id == id)?.Group ?? "" };
            }
            ContextMenuCatalog.ValidateRules(replacements.Values.ToList());
            foreach (var icon in replacements.Values.Select(r => r.Icon).Where(icon => icon.Length > 0).Distinct()) _ = ContextMenuCatalog.ExportIcon(icon);
            var rules = _rules.Where(r => !replacements.ContainsKey(r.Id)).Concat(replacements.Values).ToList();
            if (IsActive) { ApplyPreparedActions(rules); _runtime!.Apply(_entries, rules, ReadGroupIcons(), _context!.Settings); }
            foreach (var entry in _entries.Where(e => replacements.ContainsKey(e.Id) && e.Clsid is not null))
            {
                var rule = replacements[entry.Id];
                var hidden = !(rule.Contexts?.Values.Any(v => v) ?? rule.Visible);
                _context!.Settings.Set("hide-" + entry.Clsid!.Trim('{', '}').ToLowerInvariant(), hidden);
                if (IsActive) { _originalBlocked.TryAdd(entry.Clsid, IsBlocked(entry.Clsid)); SetBlocked(entry.Clsid, hidden); }
            }
            _rules = rules; _context!.Settings.Set("menu-rules", JsonSerializer.Serialize(rules, ContextMenuCatalog.Json));
            PanelStateChanged?.Invoke(); return;
        }
        HandleEntryCommand(action, payload);
    }
    private void HandleEntryCommand(string action, JsonElement payload)
    {
        var id = payload.GetProperty("id").GetString() ?? "";
        var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new FormatException("Menü öğesi bulunamadı.");
        var previous = _rules.FirstOrDefault(r => r.Id == id) ?? new ContextMenuRule(id, Visible: !id.StartsWith("prepared:"));
        ContextMenuRule next;
        if (action == "pick-icon")
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Menü öğesi simgesi", Filter = "Simge ve görseller|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|Tüm dosyalar|*.*" };
            if (picker.ShowDialog() != true) return; next = previous with { Icon = picker.FileName };
            if (payload.TryGetProperty("ids", out var itemIds))
            {
                HandlePanelCommand("save-item", JsonSerializer.SerializeToElement(new { ids = itemIds.Deserialize<string[]>(), rule = next }, ContextMenuCatalog.Json));
                return;
            }
        }
        else if (action == "save") next = payload.GetProperty("rule").Deserialize<ContextMenuRule>(ContextMenuCatalog.Json) ?? throw new FormatException("Menü ayarı eksik.");
        else if (action == "reset") next = new(id, Visible: !id.StartsWith("prepared:"));
        else throw new FormatException("Bilinmeyen menü işlemi.");
        if (next.Id != id) throw new FormatException("Menü öğesi kimliği eşleşmiyor.");
        ContextMenuCatalog.ValidateRules([next]); if (next.Icon.Length > 0) _ = ContextMenuCatalog.ExportIcon(next.Icon);
        var rules = _rules.Where(r => r.Id != id).Append(next).ToList();
        if (IsActive) { ApplyPreparedActions(rules); _runtime!.Apply(_entries, rules, ReadGroupIcons(), _context!.Settings); }
        if (entry.Clsid is not null)
        {
            _context!.Settings.Set("hide-" + entry.Clsid.Trim('{', '}').ToLowerInvariant(), !next.Visible);
            if (IsActive) { _originalBlocked.TryAdd(entry.Clsid, IsBlocked(entry.Clsid)); SetBlocked(entry.Clsid, !next.Visible); }
        }
        _rules = rules; _context!.Settings.Set("menu-rules", JsonSerializer.Serialize(_rules, ContextMenuCatalog.Json)); PanelStateChanged?.Invoke();
    }
    private Dictionary<string, string> ReadGroupIcons() => JsonSerializer.Deserialize<Dictionary<string, string>>(
        _context!.Settings.GetString("menu-group-icons", "{}"), ContextMenuCatalog.Json) ?? new();
    private string[] ReadGroups() => (JsonSerializer.Deserialize<string[]>(_context!.Settings.GetString("menu-groups", "[]"), ContextMenuCatalog.Json) ?? [])
        .Concat(_rules.Select(r => r.Group)).Where(g => g.Length > 0).Distinct().Order().ToArray();
}
