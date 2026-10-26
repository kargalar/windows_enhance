using System.Globalization;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class ThemeSwitchModule : ITweakModule
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly string[] Targets = { "Uygulamalar + sistem", "Yalnızca uygulamalar", "Yalnızca sistem" };

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private Timer? _scheduleTimer;
    private RegSnapshot? _apps;
    private RegSnapshot? _system;
    private bool _light;
    private int _scheduleBusy;

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
        },
        new SettingDescriptor
        {
            Key = "schedule-enabled",
            Label = "Temayı saate göre değiştir",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Yerel saate göre açık ve koyu temayı otomatik uygular."
        },
        new SettingDescriptor
        {
            Key = "light-time",
            Label = "Açık tema saati (HH:mm)",
            Kind = SettingKind.Text,
            Default = "07:00",
            Description = "Bu saatten koyu tema saatine kadar açık tema kullanılır."
        },
        new SettingDescriptor
        {
            Key = "dark-time",
            Label = "Koyu tema saati (HH:mm)",
            Kind = SettingKind.Text,
            Default = "19:00",
            Description = "Açık tema saatinden önceki zaman aralığında koyu tema kullanılır."
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
        RestartSchedule();
    }

    public void Deactivate()
    {
        IsActive = false;
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;
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
        RestartSchedule();
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
        ApplyTheme(!_light);
        _context?.Notify(_light ? "Açık tema" : "Koyu tema");
    }

    private void RestartSchedule()
    {
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;
        if (!IsActive || !(_context?.Settings.GetBool("schedule-enabled", false) ?? false)) return;

        _scheduleTimer = new Timer(_ => ApplyScheduledTheme(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    private void ApplyScheduledTheme()
    {
        if (!IsActive || Interlocked.Exchange(ref _scheduleBusy, 1) != 0) return;
        try
        {
            var settings = _context?.Settings;
            if (settings is null || !settings.GetBool("schedule-enabled", false)) return;

            var lightStart = ReadScheduleTime(settings.GetString("light-time", "07:00"), new TimeOnly(7, 0), "light-time");
            var darkStart = ReadScheduleTime(settings.GetString("dark-time", "19:00"), new TimeOnly(19, 0), "dark-time");
            ApplyTheme(IsLightPeriod(TimeOnly.FromDateTime(DateTime.Now), lightStart, darkStart));
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Zamanlanmış tema uygulanamadı.", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _scheduleBusy, 0);
        }
    }

    private TimeOnly ReadScheduleTime(string value, TimeOnly fallback, string settingKey)
    {
        var time = ParseScheduleTime(value);
        if (time.HasValue) return time.Value;

        _context?.Log.Error($"{settingKey} değeri HH:mm biçiminde değil; {fallback:HH:mm} kullanılıyor.");
        return fallback;
    }

    internal static TimeOnly? ParseScheduleTime(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    private void ApplyTheme(bool light)
    {
        if (_light == light) return;

        var value = light ? 1 : 0;
        var target = _context?.Settings.GetString("target", Targets[0]) ?? Targets[0];

        if (!string.Equals(target, Targets[2], StringComparison.CurrentCultureIgnoreCase))
            Reg.WriteDword(Personalize, "AppsUseLightTheme", value);

        if (!string.Equals(target, Targets[1], StringComparison.CurrentCultureIgnoreCase))
            Reg.WriteDword(Personalize, "SystemUsesLightTheme", value);

        _light = light;
        Reg.Broadcast("ImmersiveColorSet");
    }

    internal static bool IsLightPeriod(TimeOnly now, TimeOnly lightStart, TimeOnly darkStart)
    {
        if (lightStart == darkStart) return true;
        return lightStart < darkStart
            ? now >= lightStart && now < darkStart
            : now >= lightStart || now < darkStart;
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
