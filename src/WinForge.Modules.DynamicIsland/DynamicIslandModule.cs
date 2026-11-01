using WinForge.Core;

namespace WinForge.Modules.DynamicIsland;

public sealed class DynamicIslandModule : ITweakModule
{
    private IModuleContext? _context;
    private IslandWindow? _window;
    private IDisposable? _hotkey;

    public string Id => "dynamic-island";
    public string Name => "Dynamic Island";
    public string Category => "Sistem";
    public string Description => "Medya, saat, takvim, odak sayacı, pano ve sistem olaylarını akıcı biçimde genişleyen bir kapsülde birleştirir. Üzerine gelerek açın; tekerlekle paneller arasında geçin.";
    public string? Warning => "Pano izleme isteğe bağlıdır; yalnızca son metin bellekte tutulur ve kapatınca silinir. Diğer ses panelleriyle birlikte kullanıldığında iki bildirim görebilirsiniz.";
    public bool IsActive { get; private set; }
    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public DynamicIslandModule()
    {
        Settings =
        [
            new() { Key = "preview", Label = "Adayı göster", Kind = SettingKind.Action, Execute = ShowIsland },
            new() { Key = "monitor", Label = "Ekran", Kind = SettingKind.Choice, Default = "primary", Options = ["primary", "cursor"], OptionLabels = new Dictionary<string, string> { ["primary"] = "Birincil ekran", ["cursor"] = "Fare imlecinin ekranı" }, Section = "Konum" },
            new() { Key = "position", Label = "Konum", Kind = SettingKind.Choice, Default = "top", Options = ["top", "bottom"], OptionLabels = new Dictionary<string, string> { ["top"] = "Üst orta", ["bottom"] = "Alt orta" }, Section = "Konum" },
            new() { Key = "offset", Label = "Ekran kenarından uzaklık", Kind = SettingKind.Number, Default = 12, Min = 0, Max = 100, Section = "Konum" },
            new() { Key = "scale", Label = "Boyut (%)", Kind = SettingKind.Number, Default = 100, Min = 80, Max = 140, Section = "Görünüm" },
            new() { Key = "theme", Label = "Kapsül teması", Kind = SettingKind.Choice, Default = "obsidian", Options = ["obsidian", "graphite", "evergreen"], OptionLabels = new Dictionary<string, string> { ["obsidian"] = "Obsidyen", ["graphite"] = "Grafit", ["evergreen"] = "Orman" }, Section = "Görünüm" },
            new() { Key = "motion", Label = "Animasyon", Kind = SettingKind.Choice, Default = "spring", Options = ["spring", "gentle", "off"], OptionLabels = new Dictionary<string, string> { ["spring"] = "Esnek", ["gentle"] = "Yumuşak", ["off"] = "Kapalı" }, Section = "Görünüm" },
            new() { Key = "hover", Label = "Üzerine gelince genişlet", Default = true, Section = "Davranış" },
            new() { Key = "fullscreen", Label = "Tam ekran uygulamalarda gizle", Default = true, Section = "Davranış" },
            new() { Key = "hotkey", Label = "Ctrl+Alt+D ile gizle / göster", Default = true, Section = "Davranış" },
            new() { Key = "duration", Label = "Bildirim süresi (saniye)", Kind = SettingKind.Number, Default = 4, Min = 2, Max = 15, Section = "Davranış" },
            new() { Key = "media", Label = "Medya ve parça değişimlerini göster", Default = true, Section = "İçerik" },
            new() { Key = "volume", Label = "Ses değişimlerini göster", Default = true, Section = "İçerik" },
            new() { Key = "power", Label = "Pil ve şarj bildirimleri", Default = true, Section = "İçerik" },
            new() { Key = "keys", Label = "Caps Lock / Num Lock bildirimleri", Default = true, Section = "İçerik" },
            new() { Key = "usb", Label = "Aygıt bağlantı bildirimleri", Default = true, Section = "İçerik" },
            new() { Key = "clipboard", Label = "Kopyalanan metni göster", Default = false, Description = "Hassas metinleri de gösterebilir. Metin diske yazılmaz; en fazla 500 karakter saklanır.", Section = "İçerik" },
            new() { Key = "downloads", Label = "İndirilenler klasöründeki yeni dosyaları göster", Default = true, Description = "Yeni oluşturulan veya son adına çevrilen dosyaları izler; tarayıcının indirme ilerlemesini okumaz.", Section = "İçerik" },
            new() { Key = "downloads-folder", Label = "İndirilenler klasörü", Kind = SettingKind.Text, Default = "", Description = "Boş bırakırsanız Windows İndirilenler klasörü kullanılır.", Section = "İçerik" },
            new() { Key = "focus-minutes", Label = "Odak süresi (dakika)", Kind = SettingKind.Number, Default = 25, Min = 1, Max = 120, Section = "Odak" },
        ];
    }

    public void Initialize(IModuleContext context) => _context = context;

    private void ShowIsland()
    {
        if (_window is null) throw new InvalidOperationException("Önce Dynamic Island modülünü etkinleştirin.");
        _window.Reveal();
    }

    public void Activate()
    {
        if (IsActive) return;
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        var ui = (context as IFeatureUiContext)?.FeatureUi ?? throw new InvalidOperationException("Ortak arayüz kullanılamıyor.");
        try
        {
            _window = new IslandWindow(context, ui);
            _window.Show();
            IsActive = true;
            Apply();
        }
        catch { Deactivate(); throw; }
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;
        _window?.Close();
        _window = null;
    }

    public void Apply()
    {
        if (!IsActive || _context is null) return;
        _hotkey?.Dispose();
        _hotkey = null;
        if (_context.Settings.GetBool("hotkey", true))
            _hotkey = _context.Hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat, 0x44, () => _window?.ToggleVisibility());
        _window?.ApplySettings();
    }
}
