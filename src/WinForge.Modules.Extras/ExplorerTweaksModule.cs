using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ExplorerTweaksModule : ITweakModule
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";

    private IModuleContext? _context;
    private RegSnapshot[] _originals = Array.Empty<RegSnapshot>();

    public string Id => "explorer-tweaks";
    public string Name => "Gezgin İnce Ayarları";
    public string Category => "Gezgin";
    public string Description => "Dosya uzantıları, gizli dosyalar ve son kullanılanlar gibi Gezgin görünüm ayarları.";
    public string? Warning => "Bazı değişiklikler için Explorer'ı yeniden başlatmak gerekir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "extensions",
            Label = "Dosya uzantılarını göster",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "hidden",
            Label = "Gizli dosyaları göster",
            Kind = SettingKind.Toggle,
            Default = false
        },
        new SettingDescriptor
        {
            Key = "recent",
            Label = "Son kullanılan dosyaları göster",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Gezgin'in giriş sayfasındaki son kullanılan dosyaları gösterir."
        },
        new SettingDescriptor
        {
            Key = "frequent",
            Label = "Sık kullanılan klasörleri göster",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Gezgin'in giriş sayfasındaki sık kullanılan klasörleri gösterir."
        },
        new SettingDescriptor
        {
            Key = "compact",
            Label = "Kompakt mod",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Dosya ve klasör satırlarını daha sıkı aralıklarla gösterir."
        },
        new SettingDescriptor
        {
            Key = "full-path",
            Label = "Başlıkta tam yolu göster",
            Kind = SettingKind.Toggle,
            Default = false
        },
        new SettingDescriptor
        {
            Key = "checkboxes",
            Label = "Öğe seçim onay kutuları",
            Kind = SettingKind.Toggle,
            Default = false
        },
        new SettingDescriptor
        {
            Key = "separate",
            Label = "Klasörleri ayrı süreçte aç",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Bir klasör çökerse diğer Gezgin pencerelerini etkilemez; biraz daha fazla bellek kullanır."
        },
        new SettingDescriptor
        {
            Key = "desktop-icons",
            Label = "Masaüstü simgelerini göster",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Kapatınca masaüstündeki simgeler gizlenir; Explorer yeniden başlatılınca uygulanır."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _originals = new[]
        {
            new RegSnapshot(Advanced, "HideFileExt"),
            new RegSnapshot(Advanced, "Hidden"),
            new RegSnapshot(Advanced, "ShowRecent"),
            new RegSnapshot(Advanced, "ShowFrequent"),
            new RegSnapshot(Advanced, "UseCompactMode"),
            new RegSnapshot(CabinetState, "FullPath"),
            new RegSnapshot(Advanced, "AutoCheckBox"),
            new RegSnapshot(Advanced, "SeparateProcess"),
            new RegSnapshot(Advanced, "HideIcons")
        };

        IsActive = true;
        Apply();
        _context?.Log.Info("Gezgin ince ayarları etkin.");
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
        Reg.WriteDword(Advanced, "HideFileExt", settings.GetBool("extensions", true) ? 0 : 1);
        Reg.WriteDword(Advanced, "Hidden", settings.GetBool("hidden") ? 1 : 2);
        Reg.WriteDword(Advanced, "ShowRecent", settings.GetBool("recent", true) ? 1 : 0);
        Reg.WriteDword(Advanced, "ShowFrequent", settings.GetBool("frequent", true) ? 1 : 0);
        Reg.WriteDword(Advanced, "UseCompactMode", settings.GetBool("compact") ? 1 : 0);
        Reg.WriteDword(CabinetState, "FullPath", settings.GetBool("full-path") ? 1 : 0);
        Reg.WriteDword(Advanced, "AutoCheckBox", settings.GetBool("checkboxes") ? 1 : 0);
        Reg.WriteDword(Advanced, "SeparateProcess", settings.GetBool("separate") ? 1 : 0);
        Reg.WriteDword(Advanced, "HideIcons", settings.GetBool("desktop-icons", true) ? 0 : 1);
        Reg.Broadcast("ShellState");
    }
}
