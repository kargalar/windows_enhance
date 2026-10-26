using System.Globalization;
using System.Windows.Media;
using WinForge.Core;

namespace WinForge.Modules.AlwaysOnTop;

public sealed class AlwaysOnTopModule : ITweakModule
{
    private const long WsExTopmost = 0x00000008;
    private static readonly string[] ColorOptions = { "Mavi", "Yeşil", "Turuncu", "Pembe", "Kırmızı", "Beyaz" };

    private static readonly Dictionary<string, Color> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mavi"] = Color.FromRgb(76, 194, 255),
        ["Yeşil"] = Color.FromRgb(76, 217, 123),
        ["Turuncu"] = Color.FromRgb(255, 169, 76),
        ["Pembe"] = Color.FromRgb(255, 107, 214),
        ["Kırmızı"] = Color.FromRgb(255, 92, 92),
        ["Beyaz"] = Color.FromRgb(255, 255, 255)
    };

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private IModuleContext? _context;
    private IDisposable? _hotkey;
    private readonly Dictionary<IntPtr, bool> _pinned = new();
    private readonly Dictionary<IntPtr, BorderOverlay> _overlays = new();

    public string Id => "always-on-top";
    public string Name => "Her Zaman Üstte";
    public string Category => "Verimlilik";
    public string Description => "Alt+F ile aktif pencereyi diğerlerinin üstünde tut, aynı kısayol bırak.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "hotkey-enabled",
            Label = "Alt+F kısayolu etkin",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Bazı uygulamalar Alt+F'yi kendi menüsü için kullanır; kısayol onların önüne geçer."
        },
        new SettingDescriptor
        {
            Key = "border-enabled",
            Label = "Sabitlenen pencerede renkli kenarlık",
            Kind = SettingKind.Toggle,
            Default = true,
            Section = "Kenarlık"
        },
        new SettingDescriptor
        {
            Key = "border-color",
            Label = "Kenarlık rengi",
            Kind = SettingKind.Choice,
            Options = ColorOptions,
            Default = "Mavi",
            Section = "Kenarlık"
        },
        new SettingDescriptor
        {
            Key = "border-thickness",
            Label = "Kenarlık kalınlığı (px)",
            Kind = SettingKind.Number,
            Default = 2,
            Min = 1,
            Max = 10,
            Section = "Kenarlık"
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        RegisterHotkey();
    }

    public void Deactivate()
    {
        _hotkey?.Dispose();
        _hotkey = null;

        foreach (var overlay in _overlays.Values) overlay.Stop();
        _overlays.Clear();

        foreach (var (window, originalTopmost) in _pinned.ToArray())
        {
            if (!Native.IsWindow(window) || SetTopmost(window, originalTopmost)) _pinned.Remove(window);
        }
        if (_pinned.Count > 0)
            throw new InvalidOperationException("Bazı pencerelerin önceki sabitleme durumu geri yüklenemedi.");
        IsActive = false;
    }

    public void Apply()
    {
        if (!IsActive) return;

        _hotkey?.Dispose();
        _hotkey = null;
        RegisterHotkey();
        RefreshBorders();
    }

    private void RegisterHotkey()
    {
        var context = _context ?? throw new InvalidOperationException("Modül başlatılmadı.");
        if (!context.Settings.GetBool("hotkey-enabled", true)) return;

        _hotkey = context.Hotkeys.Register(HotkeyModifiers.Alt, 0x46 /* F */, ToggleForeground);
        context.Log.Info("Her Zaman Üstte kısayolu kaydedildi (Alt+F).");
    }

    private void ToggleForeground()
    {
        var context = _context;
        if (context is null) return;

        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero || !Native.IsWindow(window))
        {
            context.Notify("Aktif pencere bulunamadı");
            return;
        }

        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == (uint)Environment.ProcessId) return;
        if (IgnoredClasses.Contains(Native.ClassNameOf(window))) return;

        foreach (var stale in _pinned.Keys.Where(handle => !Native.IsWindow(handle)).ToArray()) _pinned.Remove(stale);
        foreach (var stale in _overlays.Keys.Where(handle => !Native.IsWindow(handle)).ToList())
        {
            _overlays[stale].Stop();
            _overlays.Remove(stale);
        }

        ToggleWindow(window);
    }

    private void ToggleWindow(IntPtr window)
    {
        var context = _context;
        if (context is null) return;
        if (!Native.IsWindow(window))
        {
            context.Notify("Pencere artık mevcut değil.");
            return;
        }
        var title = Native.TitleOf(window);
        var label = string.IsNullOrWhiteSpace(title) ? "Pencere" : title;

        if (_pinned.TryGetValue(window, out var originalTopmost))
        {
            if (!SetTopmost(window, originalTopmost))
            {
                context.Notify("Pencerenin önceki sabitleme durumu geri yüklenemedi.");
                return;
            }
            _pinned.Remove(window);
            RemoveOverlay(window);
            context.Notify($"Sabitleme kaldırıldı: {label}");
        }
        else
        {
            originalTopmost = (Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE).ToInt64() & WsExTopmost) != 0;
            if (!SetTopmost(window, true))
            {
                context.Notify("Pencere üste sabitlenemedi. Pencerenin izinlerini kontrol edin.");
                return;
            }
            _pinned.Add(window, originalTopmost);
            AddOverlay(window);
            context.Notify($"Üste sabitlendi: {label}");
        }
    }

    private void RefreshBorders()
    {
        foreach (var stale in _pinned.Keys.Where(handle => !Native.IsWindow(handle)).ToArray()) _pinned.Remove(stale);
        foreach (var stale in _overlays.Keys.Where(handle => !Native.IsWindow(handle)).ToList())
        {
            _overlays[stale].Stop();
            _overlays.Remove(stale);
        }

        if (!BorderEnabled)
        {
            foreach (var overlay in _overlays.Values) overlay.Stop();
            _overlays.Clear();
            return;
        }

        var (color, thickness) = BorderStyle;
        foreach (var overlay in _overlays.Values) overlay.SetStyle(color, thickness);

        foreach (var window in _pinned.Keys)
        {
            if (!_overlays.ContainsKey(window)) AddOverlay(window);
        }
    }

    private void AddOverlay(IntPtr window)
    {
        if (!BorderEnabled) return;
        if (_overlays.ContainsKey(window)) return;

        var (color, thickness) = BorderStyle;
        var overlay = new BorderOverlay(window, color, thickness);
        _overlays[window] = overlay;
        overlay.Start();
    }

    private void RemoveOverlay(IntPtr window)
    {
        if (!_overlays.Remove(window, out var overlay)) return;
        overlay.Stop();
    }

    private bool BorderEnabled => _context?.Settings.GetBool("border-enabled", true) ?? true;

    private (Color Color, double Thickness) BorderStyle
    {
        get
        {
            var settings = _context?.Settings;
            if (settings is null) return (Colors["Mavi"], 2);

            var name = settings.GetString("border-color", "Mavi");
            var color = Colors.TryGetValue(name, out var found) ? found : Colors["Mavi"];
            var thickness = Math.Clamp(settings.GetInt("border-thickness", 2), 1, 10);
            return (color, thickness);
        }
    }

    private static bool SetTopmost(IntPtr window, bool topmost)
    {
        return Native.SetWindowPos(
            window,
            topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
            0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}
