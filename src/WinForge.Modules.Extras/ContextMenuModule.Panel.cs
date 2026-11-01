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
        kind = "context-menu", groups = _rules.Select(r => r.Group).Where(g => g.Length > 0).Distinct().Order().ToArray(),
        entries = _entries.Select(entry =>
        {
            var rule = _rules.FirstOrDefault(r => r.Id == entry.Id);
            var icon = rule?.Icon is { Length: > 0 } custom ? custom : entry.Icon;
            return new { entry.Id, originalName = entry.Name, name = rule?.Name is { Length: > 0 } name ? name : entry.Name,
                entry.Source, visible = rule?.Visible ?? (entry.Clsid is null || !_context!.Settings.GetBool("hide-" + entry.Clsid.Trim('{', '}').ToLowerInvariant(), IsBlocked(entry.Clsid))),
                group = rule?.Group ?? "", icon = rule?.Icon ?? "", iconPreview = Preview(icon), hasOriginalIcon = entry.Icon.Length > 0 };
        }).ToArray()
    };
    public void HandlePanelCommand(string action, JsonElement payload)
    {
        if (action == "refresh") { _entries = ContextMenuCatalog.Read(); _previews.Clear(); PanelStateChanged?.Invoke(); return; }
        var id = payload.GetProperty("id").GetString() ?? "";
        var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new FormatException("Menü öğesi bulunamadı.");
        var previous = _rules.FirstOrDefault(r => r.Id == id) ?? new ContextMenuRule(id);
        ContextMenuRule next;
        if (action == "pick-icon")
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "Menü öğesi simgesi", Filter = "Simge ve görseller|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|Tüm dosyalar|*.*" };
            if (picker.ShowDialog() != true) return; next = previous with { Icon = picker.FileName };
        }
        else if (action == "save") next = payload.GetProperty("rule").Deserialize<ContextMenuRule>(ContextMenuCatalog.Json) ?? throw new FormatException("Menü ayarı eksik.");
        else if (action == "reset") next = new(id);
        else throw new FormatException("Bilinmeyen menü işlemi.");
        if (next.Id != id) throw new FormatException("Menü öğesi kimliği eşleşmiyor.");
        ContextMenuCatalog.ValidateRules([next]); if (next.Icon.Length > 0) _ = ContextMenuCatalog.ExportIcon(next.Icon);
        var rules = _rules.Where(r => r.Id != id).Append(next).ToList();
        if (IsActive) _runtime!.Apply(_entries, rules);
        if (entry.Clsid is not null)
        {
            _context!.Settings.Set("hide-" + entry.Clsid.Trim('{', '}').ToLowerInvariant(), !next.Visible);
            if (IsActive) { _originalBlocked.TryAdd(entry.Clsid, IsBlocked(entry.Clsid)); SetBlocked(entry.Clsid, !next.Visible); }
        }
        _rules = rules; _context!.Settings.Set("menu-rules", JsonSerializer.Serialize(_rules, ContextMenuCatalog.Json)); PanelStateChanged?.Invoke();
    }
}
