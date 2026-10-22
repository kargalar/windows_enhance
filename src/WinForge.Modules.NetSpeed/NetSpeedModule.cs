using WinForge.Core;

namespace WinForge.Modules.NetSpeed;

public sealed class NetSpeedModule : ITweakModule
{
    private IModuleContext? _context;
    private NetSpeedWindow? _window;

    public string Id => "net-speed";
    public string Name => "İnternet Hızı";
    public string Category => "Taskbar";
    public string Description => "Taskbar'ın hemen üstünde anlık indirme ve yükleme hızını gösterir.";
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
            Key = "right-offset",
            Label = "Sağdan uzaklık (px)",
            Kind = SettingKind.Number,
            Default = 190,
            Min = 0,
            Max = 900,
            Description = "Bildirim alanındaki ikonlarla çakışmayı önlemek için sağa doğru boşluk bırakır."
        },
        new SettingDescriptor
        {
            Key = "bottom-offset",
            Label = "Taskbar'dan boşluk (px)",
            Kind = SettingKind.Number,
            Default = 6,
            Min = 0,
            Max = 200,
            Description = "Gösterge taskbar ile birlikte hareket eder; bu değer taskbar ile arasındaki boşluktur."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _window = new NetSpeedWindow();
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
            settings.GetInt("right-offset", 190),
            settings.GetInt("bottom-offset", 6));
    }
}
