using System.Text.Json;
using System.Windows;

namespace WinForge.Modules.Extras;

public sealed class DefaultAppsModule : DefaultSettingsModule
{
    private const int PageSize = 100;
    private string[] _extensions = [];
    private string _query = "";
    private FileAssociationRow[] _rows = [];
    private int _count;
    public override string Id => "default-apps";
    public override string Name => "Varsayılan Programlar";
    public override string Description => "Dosya uzantılarının geçerli programlarını görüntüler ve Windows üzerinden varsayılan açılış uygulamasını seçmenizi sağlar.";
    public override string? Warning => "Program seçimi Windows Ayarları içinde tamamlanır. Değişiklikler kalıcıdır; modülü kapatmak eşleştirmeleri geri almaz.";

    protected override void Refresh()
    {
        _extensions = FileAssociationService.ReadExtensions();
        Filter();
    }

    private void Filter()
    {
        var matches = _extensions.Where(extension => extension.Contains(_query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (_query.Trim().Length > 0)
        {
            try
            {
                var exact = FileAssociationService.NormalizeExtension(_query);
                matches.RemoveAll(extension => string.Equals(extension, exact, StringComparison.OrdinalIgnoreCase));
                matches.Insert(0, exact);
            }
            catch (FormatException) { /* Partial search text is also supported. */ }
        }
        _count = matches.Count;
        _rows = matches.Take(PageSize).Select(FileAssociationService.Read).ToArray();
    }

    protected override void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "apps.filter": _query = Text(payload, "query"); Filter(); break;
            case "apps.settings": OpenSettings("ms-settings:defaultapps"); break;
            case "apps.choose":
                var extension = FileAssociationService.NormalizeExtension(Text(payload, "extension"));
                Clipboard.SetText(extension);
                OpenSettings("ms-settings:defaultapps");
                Status = extension + " panoya kopyalandı. Windows Ayarları’ndaki dosya türü aramasına yapıştırın, programı seçip varsayılan olarak ayarlayın. Sonra buradan Yenile’ye basın.";
                break;
        }
    }

    protected override void PushState() => PublishState(new { status = Status, query = _query, count = _count, rows = _rows });
}
