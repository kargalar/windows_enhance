using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ThemeSwitchModule : ITweakModule
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly string[] Targets = { "Uygulamalar + sistem", "Yalnızca uygulamalar", "Yalnızca sistem" };

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private RegSnapshot? _apps;
    private RegSnapshot? _system;
    private bool _light;

    public string Id => "theme-switch";
    public string Name => "Açık / Koyu Tema";
    public string Category => "Görünüm";
    public string Description => "Alt+Shift+L ile Windows'un uygulama ve sistem temasını tek tuşla değiştirir.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Alt+Shift+L kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "target",
            Label = "Tema hedefi",
            Kind = SettingKind.Choice,
            Options = Targets,
            Default = Targets[0]
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _apps = new RegSnapshot(Personalize, "AppsUseLightTheme");
        _system = new RegSnapshot(Personalize, "SystemUsesLightTheme");
        _light = ReadLight();

        IsActive = true;
        RegisterHotkey();
    }

    public void Deactivate()
    {
        IsActive = false;
        _hotkey?.Dispose();
        _hotkey = null;

        _apps?.Restore();
        _system?.Restore();
        _apps = null;
        _system = null;
        Reg.Broadcast("ImmersiveColorSet");
    }

    public void Apply()
    {
        if (!IsActive) return;

        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
        _light = ReadLight();
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;

        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x4C /* L */, Toggle);
        context.Log.Info("Tema değiştirme kısayolu kaydedildi (Alt+Shift+L).");
    }

    private void Toggle()
    {
        _light = !_light;
        var value = _light ? 1 : 0;
        var target = _context?.Settings.GetString("target", Targets[0]) ?? Targets[0];

        if (!string.Equals(target, Targets[2], StringComparison.CurrentCultureIgnoreCase))
            Reg.WriteDword(Personalize, "AppsUseLightTheme", value);

        if (!string.Equals(target, Targets[1], StringComparison.CurrentCultureIgnoreCase))
            Reg.WriteDword(Personalize, "SystemUsesLightTheme", value);

        Reg.Broadcast("ImmersiveColorSet");
        _context?.Notify(_light ? "Açık tema" : "Koyu tema");
    }

    private bool ReadLight()
    {
        var target = _context?.Settings.GetString("target", Targets[0]) ?? Targets[0];
        var key = string.Equals(target, Targets[2], StringComparison.CurrentCultureIgnoreCase)
            ? "SystemUsesLightTheme"
            : "AppsUseLightTheme";

        return (Reg.ReadDword(Personalize, key) ?? 1) == 1;
    }
}
