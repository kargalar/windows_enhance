using System.Text.Json;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class HostsFileModule : ITweakModule
{
    private IModuleContext? _context;
    private HostsFileEditorWindow? _window;
    private static readonly string[] EntryPlacementOptions = { "Üst (Windows varsayılanı)", "Dosyanın sonu" };

    public string Id => "hosts-file-editor";
    public string Name => "Hosts File Editor";
    public string Category => "Sistem";
    public string Description => "Windows hosts dosyasındaki IP-ad alan eşlemelerini görüntüler, filtreler, etkinleştirir, düzenler ve yönetici izniyle kaydeder.";
    public string? Warning => "Etkin hosts girdileri DNS çözümlemesini değiştirir. Kaydetmeden önce yedek alınır ve Windows yönetici onayı istenir.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public HostsFileModule()
    {
        Settings =
        [
            new SettingDescriptor
            {
                Key = "open-editor",
                Label = "Hosts dosyasını düzenle",
                Kind = SettingKind.Action,
                Description = "Mevcut girdileri filtreleyin, ekleyin, değiştirin veya devre dışı bırakın.",
                Execute = OpenEditor
            },
            new SettingDescriptor
            {
                Key = "entry-placement",
                Label = "Yeni girdilerin yeri",
                Kind = SettingKind.Choice,
                Options = EntryPlacementOptions,
                Default = EntryPlacementOptions[0],
                Description = "Üst seçeneği yeni girdileri Windows yorum başlığından sonra ekler; dosya sonu girdileri en alta koyar."
            }
        ];
    }

    public void Initialize(IModuleContext context) => _context = context;
    public void Activate() => IsActive = true;
    public void Deactivate()
    {
        IsActive = false;
        _window?.Close();
        _window = null;
    }
    public void Apply() { }

    private void OpenEditor()
    {
        try
        {
            var featureUi = (_context as IFeatureUiContext)?.FeatureUi
                ?? throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
            var placeNearHeader = _context?.Settings.GetString("entry-placement", EntryPlacementOptions[0]) == EntryPlacementOptions[0];
            _window ??= new HostsFileEditorWindow(new HostsFileStore(), placeNearHeader, featureUi);
            _window.SetEntryPlacement(placeNearHeader);
            _window.Show();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Hosts File Editor açılamadı.", exception);
            _context?.Notify("Hosts File Editor açılamadı: " + exception.Message);
        }
    }
}

internal sealed class HostsFileEditorWindow
{
    private readonly HostsFileStore _store;
    private readonly IFeatureUiWindow _window;
    private bool _insertNearHeader;
    private HostsFileSnapshot _snapshot;
    private bool _dirty;
    private string _filter = "";
    private string _status = "Hazır";
    private int? _selectedLine;
    private EntryDraft? _entryDraft;
    private bool _confirmDiscard;
    private bool _confirmRemove;
    private string _discardAction = "close";

    public HostsFileEditorWindow(HostsFileStore store, bool insertNearHeader, IFeatureUiService featureUi)
    {
        _store = store;
        _insertNearHeader = insertNearHeader;
        _snapshot = store.Load();
        _window = featureUi.Open("hosts-file-editor", "hosts-file-editor", "Hosts dosyası düzenleyicisi",
            new FeatureUiWindowOptions(1000, 700, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        PushState();
    }

    public void Show() => _window.Show();
    public void Close() => _window.Close();
    public void SetEntryPlacement(bool insertNearHeader) => _insertNearHeader = insertNearHeader;

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "hosts.filter": _filter = GetString(payload, "query"); PushState(); break;
            case "hosts.select": _selectedLine = GetInt(payload, "lineIndex"); PushState(); break;
            case "hosts.add": OpenEntryDraft(null); break;
            case "hosts.edit": EditEntry(); break;
            case "hosts.entry-update": UpdateEntryDraft(payload); break;
            case "hosts.entry-save": SaveEntry(); break;
            case "hosts.entry-cancel": _entryDraft = null; PushState(); break;
            case "hosts.toggle": ToggleEntry(); break;
            case "hosts.remove": _confirmRemove = true; PushState(); break;
            case "hosts.remove-confirm": ConfirmRemove(GetBool(payload, "confirm")); break;
            case "hosts.reload": Reload(); break;
            case "hosts.save": Save(); break;
            case "hosts.close": RequestClose(); break;
            case "hosts.discard-confirm": ConfirmDiscard(GetBool(payload, "confirm")); break;
        }
    }

    private void OpenEntryDraft(HostsFileEntry? entry)
    {
        _entryDraft = new EntryDraft(entry?.Address ?? "127.0.0.1", entry?.HostNames ?? "example.test",
            entry?.Comment ?? "", entry?.IsActive ?? true, entry?.LineIndex ?? -1, "");
        PushState();
    }

    private void EditEntry()
    {
        if (_selectedLine is not { } index) return;
        var entry = _snapshot.Document.Entries.FirstOrDefault(row => row.LineIndex == index);
        if (entry is not null) OpenEntryDraft(entry);
    }

    private void UpdateEntryDraft(JsonElement payload)
    {
        if (_entryDraft is null) return;
        _entryDraft = _entryDraft with
        {
            Address = payload.TryGetProperty("address", out var address) ? address.GetString() ?? "" : _entryDraft.Address,
            HostNames = payload.TryGetProperty("hostNames", out var hosts) ? hosts.GetString() ?? "" : _entryDraft.HostNames,
            Comment = payload.TryGetProperty("comment", out var comment) ? comment.GetString() ?? "" : _entryDraft.Comment,
            IsActive = payload.TryGetProperty("isActive", out var active) ? active.ValueKind == JsonValueKind.True : _entryDraft.IsActive
        };
        PushState();
    }

    private void SaveEntry()
    {
        if (_entryDraft is not { } draft) return;
        var entry = new HostsFileEntry(draft.Address.Trim(), draft.HostNames.Trim(), draft.Comment.Trim(), draft.IsActive, draft.LineIndex);
        try
        {
            HostsFileDocument.Validate(entry);
            if (draft.LineIndex < 0)
            {
                _snapshot.Document.Add(entry, _insertNearHeader);
                _selectedLine = _snapshot.Document.Entries.FirstOrDefault(row =>
                    row.Address == entry.Address && row.HostNames == entry.HostNames && row.Comment == entry.Comment)?.LineIndex;
            }
            else
            {
                _snapshot.Document.Update(entry);
                _selectedLine = entry.LineIndex;
            }
            _entryDraft = null;
            MarkDirty();
        }
        catch (Exception exception) { _entryDraft = draft with { Error = exception.Message }; PushState(); }
    }

    private void ToggleEntry()
    {
        if (_selectedLine is not { } lineIndex) return;
        try { _snapshot.Document.Toggle(lineIndex); MarkDirty(); }
        catch (Exception exception) { _status = "İşlem başarısız: " + exception.Message; PushState(); }
    }

    private void ConfirmRemove(bool confirmed)
    {
        _confirmRemove = false;
        if (!confirmed || _selectedLine is not { } lineIndex) { PushState(); return; }
        try { _snapshot.Document.Remove(lineIndex); _selectedLine = null; MarkDirty(); }
        catch (Exception exception) { _status = "Silme başarısız: " + exception.Message; PushState(); }
    }

    private void Save()
    {
        try
        {
            _store.Save(_snapshot.Document, _snapshot);
            _snapshot = _store.Load();
            _dirty = false;
            _status = "Kaydedildi · hosts dosyasının yedeği oluşturuldu";
            PushState();
        }
        catch (Exception exception) { _status = "Kaydetme başarısız: " + exception.Message; PushState(); }
    }

    private void Reload()
    {
        if (_dirty) { _confirmDiscard = true; _discardAction = "reload"; PushState(); return; }
        ReloadClean();
    }

    private void ReloadClean()
    {
        try
        {
            _snapshot = _store.Load();
            _dirty = false;
            _selectedLine = null;
            _status = "Hosts dosyası yeniden yüklendi";
            PushState();
        }
        catch (Exception exception) { _status = "Yeniden yükleme başarısız: " + exception.Message; PushState(); }
    }

    private void MarkDirty()
    {
        _dirty = true;
        _status = "Kaydedilmemiş değişiklikler";
        PushState();
    }

    private void RequestClose()
    {
        if (_dirty) { _confirmDiscard = true; _discardAction = "close"; PushState(); return; }
        _window.Hide();
    }

    private void ConfirmDiscard(bool confirmed)
    {
        _confirmDiscard = false;
        if (confirmed)
        {
            _dirty = false;
            _entryDraft = null;
            if (_discardAction == "reload") ReloadClean();
            else _window.Hide();
            return;
        }
        PushState();
    }

    private void PushState() => _window.SetState(new
    {
        filter = _filter,
        status = _status,
        dirty = _dirty,
        selectedLine = _selectedLine,
        canEdit = _selectedLine is not null,
        confirmDiscard = _confirmDiscard,
        discardAction = _discardAction,
        confirmRemove = _confirmRemove,
        entry = _entryDraft,
        rows = _snapshot.Document.Entries.Select(entry => new
        {
            entry.LineIndex, entry.Address, entry.HostNames, entry.Comment, entry.IsActive
        }).ToArray()
    });

    private static string GetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
    private static int? GetInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static bool GetBool(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed record EntryDraft(string Address, string HostNames, string Comment, bool IsActive, int LineIndex, string Error);
}
