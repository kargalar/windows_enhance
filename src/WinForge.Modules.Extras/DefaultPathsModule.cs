using System.Text.Json;
using Microsoft.Win32;

namespace WinForge.Modules.Extras;

public sealed class DefaultPathsModule : DefaultSettingsModule
{
    private sealed record FolderRow(string Key, string Name, string Current, string Original, string Error);
    private sealed record PendingChange(string Key, string Name, string From, string Target, bool Restore);
    private readonly Dictionary<string, string> _drafts = new();
    private FolderRow[] _rows = [];
    private PendingChange? _pending;
    private KnownFolderPathService Service => new(new WindowsKnownFolderPaths(), Context.Settings);

    public override string Id => "default-paths";
    public override string Name => "Varsayılan Yollar";
    public override string Description => "Belgeler, İndirilenler ve diğer kullanıcı klasörlerinin konumlarını düzenler; yeni uygulama depolama ayarlarını açar.";
    public override string? Warning => "Konum değişikliği kalıcıdır; modülü kapatmak geri almaz. Mevcut dosyalar taşınmaz. OneDrive veya kurum tarafından yönetilen klasörlerde önce eşitleme/yönlendirme ayarlarını kontrol edin.";

    protected override void Refresh()
    {
        _pending = null;
        _rows = KnownFolderPathService.Folders.Select(folder =>
        {
            try { return new FolderRow(folder.Key, folder.Name, Service.Read(folder.Key), Service.Original(folder.Key), ""); }
            catch (Exception exception) { return new FolderRow(folder.Key, folder.Name, "", "", exception.Message); }
        }).ToArray();
        _drafts.Clear();
        foreach (var row in _rows) _drafts[row.Key] = row.Current;
    }

    protected override void OnMessage(string command, JsonElement payload)
    {
        if (command == "paths.storage") { OpenSettings("ms-settings:savelocations"); return; }
        if (command == "paths.cancel") { _pending = null; return; }
        if (command == "paths.confirm")
        {
            var pending = _pending ?? throw new InvalidOperationException("Onay bekleyen konum değişikliği yok.");
            _pending = null;
            if (!string.Equals(Service.Read(pending.Key), pending.From, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Klasör konumu başka bir uygulamada değişti. Yenileyip tekrar deneyin.");
            if (pending.Restore) Service.Restore(pending.Key);
            else Service.Apply(pending.Key, pending.Target);
            Refresh();
            Status = pending.Name + " konumu kaydedildi. Dosyalar eski konumlarında kaldı. Açık uygulamaları yeniden başlatmanız gerekebilir.";
            return;
        }
        var key = Text(payload, "key");
        var row = _rows.FirstOrDefault(row => row.Key == key) ?? throw new ArgumentException("Bilinmeyen klasör.");
        if (row.Error.Length > 0) throw new InvalidOperationException(row.Error);
        switch (command)
        {
            case "paths.edit": _drafts[key] = Text(payload, "path"); break;
            case "paths.browse":
                var dialog = new OpenFolderDialog { Title = row.Name + " için klasör seçin", Multiselect = false };
                if (dialog.ShowDialog() == true) _drafts[key] = dialog.FolderName;
                break;
            case "paths.apply":
            case "paths.restore":
                var restore = command == "paths.restore";
                var target = KnownFolderPathService.Normalize(restore ? Service.Original(key) : _drafts[key]);
                _pending = new PendingChange(key, row.Name, Service.Read(key), target, restore);
                break;
        }
    }

    protected override void PushState() => Window?.SetState(new
    {
        status = Status,
        rows = _rows.Select(row => new { row.Key, row.Name, row.Current, row.Original, row.Error, draft = _drafts.GetValueOrDefault(row.Key, row.Current) }),
        pending = _pending
    });
}
