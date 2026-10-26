using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class WindowTweaksModule : ITweakModule
{
    private const string Desktop = @"Control Panel\Desktop";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const uint SpiGetAnimation = 0x0048;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private IModuleContext? _context;
    private RegSnapshot? _menuDelay;
    private RegSnapshot? _taskbarAnimations;
    private RegSnapshot? _shake;
    private int? _originalAnimation;
    private readonly SystemParametersHandler _systemParameters;

    internal delegate bool SystemParametersHandler(uint action, uint parameter, IntPtr data, uint flags);

    public WindowTweaksModule() : this(SystemParametersInfo) { }

    internal WindowTweaksModule(SystemParametersHandler systemParameters) => _systemParameters = systemParameters;

    public string Id => "window-tweaks";
    public string Name => "Arayüz İnce Ayarları";
    public string Category => "Görünüm";
    public string Description => "Pencere animasyonlarını, menü gecikmesini ve Aero Shake'i kapatır.";
    public string? Warning => "Bazı etkilerin uygulanması için oturumu kapatıp açmak gerekebilir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "animations",
            Label = "Pencere animasyonlarını kapat",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "menu-delay",
            Label = "Menü açılma gecikmesi (ms)",
            Kind = SettingKind.Number,
            Default = 0,
            Min = 0,
            Max = 400
        },
        new SettingDescriptor
        {
            Key = "taskbar-animations",
            Label = "Görev çubuğu animasyonlarını kapat",
            Kind = SettingKind.Toggle,
            Default = true
        },
        new SettingDescriptor
        {
            Key = "shake",
            Label = "Aero Shake'i kapat",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "Pencereyi sallayınca diğer pencerelerin küçülmesini engeller."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        if (IsActive) return;
        _menuDelay = new RegSnapshot(Desktop, "MenuShowDelay");
        _taskbarAnimations = new RegSnapshot(Advanced, "TaskbarAnimations");
        _shake = new RegSnapshot(Advanced, "DisallowShaking");
        _originalAnimation = ReadAnimation();

        IsActive = true;
        Apply();
        _context?.Log.Info("Arayüz ince ayarları etkin.");
    }

    public void Deactivate()
    {
        if (_originalAnimation is { } originalAnimation)
        {
            WriteAnimation(originalAnimation);
            _originalAnimation = null;
        }
        _menuDelay?.Restore();
        _taskbarAnimations?.Restore();
        _shake?.Restore();
        _menuDelay = _taskbarAnimations = _shake = null;

        Reg.Broadcast("WindowMetrics", "ImmersiveColorSet");
        IsActive = false;
    }

    public void Apply()
    {
        if (!IsActive) return;

        var settings = _context!.Settings;
        WriteAnimation(settings.GetBool("animations", true) ? 0 : 1);
        Reg.WriteString(Desktop, "MenuShowDelay",
            Math.Clamp(settings.GetInt("menu-delay"), 0, 400).ToString());
        Reg.WriteDword(Advanced, "TaskbarAnimations", settings.GetBool("taskbar-animations", true) ? 0 : 1);
        Reg.WriteDword(Advanced, "DisallowShaking", settings.GetBool("shake", true) ? 1 : 0);

        Reg.Broadcast("WindowMetrics", "ImmersiveColorSet");
    }

    private int ReadAnimation()
    {
        var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AnimationInfo>());
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            Check(_systemParameters(SpiGetAnimation, info.Size, buffer, 0));
            return Marshal.PtrToStructure<AnimationInfo>(buffer).MinAnimate;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void WriteAnimation(int enabled)
    {
        var info = new AnimationInfo
        {
            Size = (uint)Marshal.SizeOf<AnimationInfo>(),
            MinAnimate = enabled
        };

        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AnimationInfo>());
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            Check(_systemParameters(SpiSetAnimation, info.Size, buffer, SpifUpdateIniFile | SpifSendChange));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Check(bool succeeded)
    {
        if (!succeeded) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows animasyon ayarı okunamadı veya uygulanamadı.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
}
