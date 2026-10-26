using System.Windows;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class MouseSpotlightModule : ITweakModule
{
    private static readonly Color SpotlightColor = Color.FromRgb(76, 194, 255);
    private static readonly Color LeftClickColor = Color.FromRgb(255, 169, 76);
    private static readonly Color RightClickColor = Color.FromRgb(139, 140, 255);
    private static readonly string[] ClickColors = { "Turuncu", "Mor", "Camgöbeği", "Yeşil", "Kırmızı", "Beyaz" };
    private static readonly string[] CrosshairColors = { "Beyaz", "Sarı", "Camgöbeği" };

    private IModuleContext? _context;
    private GlobalKeyboardHook? _keyboard;
    private GlobalMouseHook? _mouse;
    private CursorRingOverlay? _ring;
    private CrosshairOverlay? _crosshair;
    private IDisposable? _crosshairHotkey;

    public string Id => "mouse-spotlight";
    public string Name => "Fare İşaretçisi";
    public string Category => "Fare";
    public string Description => "Çift Ctrl ile fareyi bulur, sol/sağ/orta/yan tıklamaları ayrı renk ve sürelerle vurgular, Win+Alt+. ile imleç artısını açar.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "double-ctrl",
            Label = "Çift Ctrl ile fareyi vurgula",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Ctrl tuşuna iki kez hızlıca basınca fare çevresinde halka belirir."
        },
        new SettingDescriptor
        {
            Key = "click",
            Label = "Tıklamalarda halka göster",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Sol tık turuncu, sağ tık mor halka ile gösterilir."
        },
        new SettingDescriptor
        {
            Key = "click-buttons",
            Label = "Vurgulanacak fare tuşları",
            Kind = SettingKind.Choice,
            Default = "left-right",
            Options = new[] { "left-right", "all" },
            OptionLabels = new Dictionary<string, string> { ["left-right"] = "Sol ve sağ", ["all"] = "Sol, sağ, orta ve yan tuşlar" },
            Description = "Yan tuşlar XBUTTON1 / XBUTTON2 olarak algılanır."
        },
        new SettingDescriptor { Key = "click-size", Label = "Tıklama halkası çapı (px)", Kind = SettingKind.Number, Default = 64, Min = 24, Max = 180 },
        new SettingDescriptor { Key = "click-duration", Label = "Tıklama vurgusu süresi (ms)", Kind = SettingKind.Number, Default = 500, Min = 100, Max = 2000 },
        new SettingDescriptor { Key = "click-color-left", Label = "Sol tık rengi", Kind = SettingKind.Choice, Default = "Turuncu", Options = ClickColors },
        new SettingDescriptor { Key = "click-color-right", Label = "Sağ tık rengi", Kind = SettingKind.Choice, Default = "Mor", Options = ClickColors },
        new SettingDescriptor { Key = "click-color-middle", Label = "Orta tık rengi", Kind = SettingKind.Choice, Default = "Camgöbeği", Options = ClickColors },
        new SettingDescriptor { Key = "click-color-x1", Label = "Yan tuş 1 rengi", Kind = SettingKind.Choice, Default = "Yeşil", Options = ClickColors },
        new SettingDescriptor { Key = "click-color-x2", Label = "Yan tuş 2 rengi", Kind = SettingKind.Choice, Default = "Kırmızı", Options = ClickColors },
        new SettingDescriptor
        {
            Key = "size",
            Label = "Halka boyutu (px)",
            Kind = SettingKind.Number,
            Default = 220,
            Min = 80,
            Max = 320
        },
        new SettingDescriptor
        {
            Key = "duration",
            Label = "Vurgu süresi (ms)",
            Kind = SettingKind.Number,
            Default = 1800,
            Min = 400,
            Max = 4000
        },
        new SettingDescriptor
        {
            Key = "crosshairs-enabled",
            Label = "İmleç artısını başlangıçta göster",
            Kind = SettingKind.Toggle,
            Default = false,
            Description = "Win+Alt+. kısayoluyla çalışma sırasında açıp kapatabilirsiniz."
        },
        new SettingDescriptor
        {
            Key = "crosshairs-color",
            Label = "İmleç artısı rengi",
            Kind = SettingKind.Choice,
            Options = CrosshairColors,
            Default = "Sarı"
        },
        new SettingDescriptor
        {
            Key = "crosshairs-opacity",
            Label = "İmleç artısı saydamlığı (%)",
            Kind = SettingKind.Number,
            Default = 75,
            Min = 10,
            Max = 100
        },
        new SettingDescriptor
        {
            Key = "crosshairs-center-radius",
            Label = "Merkez boşluğu (px)",
            Kind = SettingKind.Number,
            Default = 20,
            Min = 0,
            Max = 100
        },
        new SettingDescriptor
        {
            Key = "crosshairs-thickness",
            Label = "Çizgi kalınlığı (px)",
            Kind = SettingKind.Number,
            Default = 5,
            Min = 1,
            Max = 20
        },
        new SettingDescriptor
        {
            Key = "crosshairs-border-size",
            Label = "Kenar kalınlığı (px)",
            Kind = SettingKind.Number,
            Default = 2,
            Min = 0,
            Max = 10
        },
        new SettingDescriptor
        {
            Key = "crosshairs-fixed-length",
            Label = "Sabit çizgi uzunluğu (px)",
            Kind = SettingKind.Number,
            Default = 0,
            Min = 0,
            Max = 4000,
            Description = "0 değeri çizgileri imlecin bulunduğu monitörün kenarlarına kadar uzatır."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        try
        {
            _ring = new CursorRingOverlay();
            _crosshair = new CrosshairOverlay();
            RegisterCrosshairHotkey();

            _keyboard = new GlobalKeyboardHook();
            _keyboard.DoubleControlPressed += Spotlight;
            _keyboard.Install();

            _mouse = new GlobalMouseHook();
            _mouse.LeftButtonDown += point => Flash(point, GetClickColor("click-color-left", LeftClickColor));
            _mouse.RightButtonDown += point => Flash(point, GetClickColor("click-color-right", RightClickColor));
            _mouse.MiddleButtonDown += point => Flash(point, GetClickColor("click-color-middle", Colors.Cyan), "middle");
            _mouse.XButtonDown += (point, button) => Flash(point,
                GetClickColor(button == 1 ? "click-color-x1" : "click-color-x2", button == 1 ? Colors.LimeGreen : Colors.Red), "side");
            _mouse.Install();

            RefreshCrosshairs();
            IsActive = true;
            _context?.Log.Info("Fare işaretçisi etkin.");
        }
        catch
        {
            Deactivate();
            throw;
        }
    }

    public void Deactivate()
    {
        IsActive = false;

        _keyboard?.Dispose();
        _keyboard = null;

        _mouse?.Dispose();
        _mouse = null;

            _crosshairHotkey?.Dispose();
            _crosshairHotkey = null;

        _ring?.Stop();
        _ring = null;
            _crosshair?.Stop();
            _crosshair?.Close();
            _crosshair = null;
    }

    public void Apply()
    {
        if (IsActive) RefreshCrosshairs();
    }

    private void RegisterCrosshairHotkey()
    {
        try
        {
            _crosshairHotkey = _context?.Hotkeys.Register(HotkeyModifiers.Win | HotkeyModifiers.Alt, 0xBE, ToggleCrosshairs);
        }
        catch (InvalidOperationException ex)
        {
            _context?.Log.Error("İmleç artısı kısayolu kaydedilemedi (Win+Alt+.); diğer fare özellikleri kullanılabilir.", ex);
            _context?.Notify("Win+Alt+. başka bir uygulama tarafından kullanılıyor; imleç artısı kısayolu etkinleştirilemedi.");
        }
    }

    private void ToggleCrosshairs()
    {
        var settings = _context?.Settings;
        if (settings is null) return;
        settings.Set("crosshairs-enabled", !settings.GetBool("crosshairs-enabled"));
        RefreshCrosshairs();
    }

    private void RefreshCrosshairs()
    {
        var settings = _context?.Settings;
        var crosshair = _crosshair;
        if (settings is null || crosshair is null) return;
        if (!settings.GetBool("crosshairs-enabled"))
        {
            crosshair.Stop();
            return;
        }

        var color = settings.GetString("crosshairs-color", "Sarı") switch
        {
            "Beyaz" => Colors.White,
            "Camgöbeği" => Colors.Cyan,
            _ => Colors.Yellow
        };
        crosshair.Start(color,
            settings.GetInt("crosshairs-opacity", 75),
            settings.GetInt("crosshairs-center-radius", 20),
            settings.GetInt("crosshairs-thickness", 5),
            settings.GetInt("crosshairs-border-size", 2),
            settings.GetInt("crosshairs-fixed-length", 0));
    }

    private void Spotlight()
    {
        var settings = _context?.Settings;
        if (settings is null || !settings.GetBool("double-ctrl", true)) return;
        if (!Native.GetCursorPos(out var point)) return;

        var size = Math.Clamp(settings.GetInt("size", 220), 80, 320);
        var duration = Math.Clamp(settings.GetInt("duration", 1800), 400, 4000);
        var ring = _ring;
        if (ring is null) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
            ring.Show(point, SpotlightColor, size, duration, followCursor: true));
    }

    private void Flash(POINT point, Color color, string buttonGroup = "left-right")
    {
        var settings = _context?.Settings;
        if (settings is null || !settings.GetBool("click", true)) return;
        if (buttonGroup != "left-right" && settings.GetString("click-buttons", "left-right") != "all") return;

        var size = Math.Clamp(settings.GetInt("click-size", 64), 24, 180);
        var duration = Math.Clamp(settings.GetInt("click-duration", 500), 100, 2000);
        var ring = _ring;
        if (ring is null) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
            ring.Show(point, color, size, duration, followCursor: false));
    }

    private Color GetClickColor(string setting, Color fallback) => (_context?.Settings.GetString(setting, "") ?? "") switch
    {
        "Turuncu" => LeftClickColor,
        "Mor" => RightClickColor,
        "Camgöbeği" => Colors.Cyan,
        "Yeşil" => Colors.LimeGreen,
        "Kırmızı" => Colors.Red,
        "Beyaz" => Colors.White,
        _ => fallback
    };
}
