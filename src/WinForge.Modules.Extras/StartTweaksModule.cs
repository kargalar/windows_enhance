using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class StartTweaksModule : IModuleComponent
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private IModuleContext? _context;
    private RegSnapshot[] _originals = Array.Empty<RegSnapshot>();

    public string Id => "start-tweaks";
    public string Name => "Başlat Menüsü Temizliği";
    public string Category => "Başlat";
    public string Description => "Başlat menüsündeki önerileri, yeni uygulama bildirimlerini ve son kullanılanları gizler.";
    public string? Warning => "Değişiklikler Başlat menüsü yeniden açıldığında görünür.";
    public bool IsActive { get; private set; }
    public bool IsPersistent => true;

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "recommendations",
            Label = "Önerileri gizle",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "new-apps",
            Label = "Yeni uygulama bildirimlerini gizle",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "recent",
            Label = "Son kullanılan öğeleri gizle",
            Kind = SettingKind.Toggle,
            Default = true
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _originals = new[]
        {
            new RegSnapshot(Advanced, "Start_IrisRecommendations"),
            new RegSnapshot(Advanced, "Start_NotifyNewApps"),
            new RegSnapshot(Advanced, "Start_TrackDocs")
        };

        IsActive = true;
        Apply();
        _context?.Log.Info("Başlat menüsü temizliği etkin.");
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
        Reg.WriteDword(Advanced, "Start_IrisRecommendations", settings.GetBool("recommendations", true) ? 0 : 1);
        Reg.WriteDword(Advanced, "Start_NotifyNewApps", settings.GetBool("new-apps", true) ? 0 : 1);
        Reg.WriteDword(Advanced, "Start_TrackDocs", settings.GetBool("recent", true) ? 0 : 1);
        Reg.Broadcast("ShellState");
    }
}
