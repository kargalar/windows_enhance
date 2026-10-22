using System.Windows;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class MouseSpotlightModule : ITweakModule
{
    private static readonly Color SpotlightColor = Color.FromRgb(76, 194, 255);
    private static readonly Color LeftClickColor = Color.FromRgb(255, 169, 76);
    private static readonly Color RightClickColor = Color.FromRgb(139, 140, 255);

    private IModuleContext? _context;
    private GlobalKeyboardHook? _keyboard;
    private GlobalMouseHook? _mouse;
    private CursorRingOverlay? _ring;

    public string Id => "mouse-spotlight";
    public string Name => "Fare İşaretçisi";
    public string Category => "Fare";
    public string Description => "Çift Ctrl ile fareyi bulur; tıklamalarda kısa halkalarla tıklama yerini gösterir.";
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
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        try
        {
            _ring = new CursorRingOverlay();

            _keyboard = new GlobalKeyboardHook();
            _keyboard.DoubleControlPressed += Spotlight;
            _keyboard.Install();

            _mouse = new GlobalMouseHook();
            _mouse.LeftButtonDown += point => Flash(point, LeftClickColor);
            _mouse.RightButtonDown += point => Flash(point, RightClickColor);
            _mouse.Install();

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

        _ring?.Stop();
        _ring = null;
    }

    public void Apply()
    {
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

    private void Flash(POINT point, Color color)
    {
        var settings = _context?.Settings;
        if (settings is null || !settings.GetBool("click", true)) return;

        var size = Math.Clamp(settings.GetInt("size", 220), 80, 320) / 2;
        var ring = _ring;
        if (ring is null) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
            ring.Show(point, color, size, 700, followCursor: false));
    }
}
