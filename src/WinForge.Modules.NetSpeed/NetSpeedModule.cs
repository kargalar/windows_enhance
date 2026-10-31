using WinForge.Core;

namespace WinForge.Modules.NetSpeed;

public sealed class NetSpeedModule : ITweakModule
{
    private IModuleContext? _context;
    private NetSpeedWindow? _window;

    public string Id => "net-speed";
    public string Name => "İnternet Hızı";
    public string Category => "Taskbar";
    public string Description => "İndirme ve yükleme hızını görev çubuğunda, bildirim alanına hizalı gösterir.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "show-download",
            Label = "İndirme hızını göster",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "show-upload",
            Label = "Yükleme hızını göster",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "font-size",
            Label = "Yazı boyutu",
            Kind = SettingKind.Number,
            Default = 11,
            Min = 8,
            Max = 18
        },
        new SettingDescriptor
        {
            Key = "layout",
            Label = "Hız gösterimi",
            Kind = SettingKind.Choice,
            Options = new[] { NetSpeedWindow.SideBySide, NetSpeedWindow.Stacked },
            Default = NetSpeedWindow.SideBySide
        },
        new SettingDescriptor
        {
            Key = "position",
            Label = "Gösterim konumu",
            Kind = SettingKind.Choice,
            Options = new[] { NetSpeedWindow.AboveTaskbar, NetSpeedWindow.InsideTaskbar },
            Default = NetSpeedWindow.InsideTaskbar,
            Description = "Gösterge görev çubuğunun üstünde veya içinde, bildirim alanının solunda durur."
        },
        new SettingDescriptor
        {
            Key = "right-offset",
            Label = "Bildirim alanından uzaklık (px)",
            Kind = SettingKind.Number,
            Default = 10,
            Min = 0,
            Max = 200,
            Description = "Gösterge ile bildirim alanı ikonları arasındaki boşluk."
        },
        new SettingDescriptor
        {
            Key = "unit",
            Label = "Hız birimi",
            Kind = SettingKind.Choice,
            Options = new[] { "Otomatik", "MB/s", "Mbit/s" },
            Default = "Otomatik",
            Description = "Otomatik, hıza göre B/s, KB/s ve MB/s arasında geçiş yapar."
        },
        new SettingDescriptor
        {
            Key = "interval",
            Label = "Yenileme aralığı (ms)",
            Kind = SettingKind.Number,
            Default = 1000,
            Min = 250,
            Max = 3000
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _window = new NetSpeedWindow((_context as IFeatureUiContext)?.FeatureUi);
        _window.Start();
        Apply();

        IsActive = true;
        _context?.Log.Info("İnternet hızı göstergesi açıldı.");
    }

    public void Deactivate()
    {
        IsActive = false;
        _window?.Stop();
        _window = null;
    }

    public void Apply()
    {
        if (_window is null) return;

        var settings = _context!.Settings;
        _window.ApplyOptions(
            settings.GetInt("font-size", 11),
            settings.GetBool("show-download", true),
            settings.GetBool("show-upload", true),
            settings.GetInt("right-offset", 10),
            settings.GetString("unit", "Otomatik"),
            settings.GetInt("interval", 1000),
            settings.GetString("position", NetSpeedWindow.InsideTaskbar),
            settings.GetString("layout", NetSpeedWindow.SideBySide));
    }
}
