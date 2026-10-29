using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class SearchTweaksModule : ITweakModule
{
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    private IModuleContext? _context;
    private RegSnapshot[] _originals = Array.Empty<RegSnapshot>();

    public string Id => "search-tweaks";
    public string Name => "Arama Temizliği";
    public string Category => "Arama";
    public string Description => "Windows aramasındaki web sonuçlarını, vurguları ve kutu önerilerini kapatır.";
    public string? Warning => "Değişiklikler arama kutusu yeniden açıldığında görünür.";
    public bool IsActive { get; private set; }
    public bool IsPersistent => true;

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "web",
            Label = "Web (Bing) sonuçlarını kapat",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "highlights",
            Label = "Arama vurgularını kapat",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "suggestions",
            Label = "Arama kutusu önerilerini kapat",
            Kind = SettingKind.Toggle,
            Default = true
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _originals = new[]
        {
            new RegSnapshot(Search, "BingSearchEnabled"),
            new RegSnapshot(Search, "SearchHighlightsEnabled"),
            new RegSnapshot(Search, "IsDynamicSearchBoxEnabled")
        };

        IsActive = true;
        Apply();
        _context?.Log.Info("Arama temizliği etkin.");
    }

    public void Deactivate()
    {
        IsActive = false;

        foreach (var original in _originals) original.Restore();
        _originals = Array.Empty<RegSnapshot>();
        Reg.Broadcast("ShellState");
    }

    public void Apply()
    {
        if (!IsActive) return;

        var settings = _context!.Settings;
        Reg.WriteDword(Search, "BingSearchEnabled", settings.GetBool("web", true) ? 0 : 1);
        Reg.WriteDword(Search, "SearchHighlightsEnabled", settings.GetBool("highlights", true) ? 0 : 1);
        Reg.WriteDword(Search, "IsDynamicSearchBoxEnabled", settings.GetBool("suggestions", true) ? 0 : 1);
        Reg.Broadcast("ShellState");
    }
}
