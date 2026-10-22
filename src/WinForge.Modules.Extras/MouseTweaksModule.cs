using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

public sealed class MouseTweaksModule : ITweakModule
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpiGetDoubleClickTime = 0x0020;
    private const uint SpiSetDoubleClickTime = 0x0021;
    private const uint SpiGetWheelScrollLines = 0x0068;
    private const uint SpiSetWheelScrollLines = 0x0069;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    private IModuleContext? _context;
    private int[] _originalMouse = { 0, 0, 0 };
    private int _originalWheel = 3;
    private int _originalDoubleClick = 500;

    public string Id => "mouse-tweaks";
    public string Name => "Fare Ayarları";
    public string Category => "Fare";
    public string Description => "Fare hızlandırmasını kapatır; tekerlek kaydırma ve çift tık hızını ayarlar.";
    public string? Warning => null;
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; } = new[]
    {
        new SettingDescriptor
        {
            Key = "acceleration",
            Label = "Fare hızlandırmasını kapat",
            Kind = SettingKind.Toggle,
            Default = true,
            Description = "İşaretçinin hızı hareket mesafesiyle orantılı olur; oyunlarda daha öngörülebilir."
        },
        new SettingDescriptor
        {
            Key = "scroll-lines",
            Label = "Tekerlek kaydırma satırı",
            Kind = SettingKind.Number,
            Default = 3,
            Min = 1,
            Max = 10
        },
        new SettingDescriptor
        {
            Key = "double-click",
            Label = "Çift tık hızı (ms)",
            Kind = SettingKind.Number,
            Default = 500,
            Min = 200,
            Max = 900,
            Description = "Düşük değer daha hızlı çift tıklama gerektirir."
        }
    };

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        _originalMouse = ReadMouse();
        _originalWheel = ReadInt(SpiGetWheelScrollLines, 3);
        _originalDoubleClick = ReadInt(SpiGetDoubleClickTime, 500);

        var settings = _context!.Settings;
        if (settings.GetString("scroll-lines").Length == 0) settings.Set("scroll-lines", _originalWheel);
        if (settings.GetString("double-click").Length == 0) settings.Set("double-click", _originalDoubleClick);
        if (settings.GetString("acceleration").Length == 0)
            settings.Set("acceleration", _originalMouse[0] == 0 && _originalMouse[1] == 0 && _originalMouse[2] == 0);

        IsActive = true;
        Apply();
        _context?.Log.Info("Fare ayarları etkin.");
    }

    public void Deactivate()
    {
        IsActive = false;

        WriteMouse(_originalMouse);
        WriteInt(SpiSetWheelScrollLines, _originalWheel);
        WriteInt(SpiSetDoubleClickTime, _originalDoubleClick);
    }

    public void Apply()
    {
        if (!IsActive) return;

        var settings = _context!.Settings;
        WriteMouse(settings.GetBool("acceleration", true) ? new[] { 0, 0, 0 } : _originalMouse);
        WriteInt(SpiSetWheelScrollLines, Math.Clamp(settings.GetInt("scroll-lines", _originalWheel), 1, 10));
        WriteInt(SpiSetDoubleClickTime, Math.Clamp(settings.GetInt("double-click", _originalDoubleClick), 200, 900));
    }

    private static int[] ReadMouse()
    {
        var values = new int[3];
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            if (!SystemParametersInfo(SpiGetMouse, 0, handle.AddrOfPinnedObject(), 0)) return new[] { 0, 0, 0 };
            return values;
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteMouse(int[] values)
    {
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            SystemParametersInfo(SpiSetMouse, 0, handle.AddrOfPinnedObject(), SpifUpdateIniFile | SpifSendChange);
        }
        finally
        {
            handle.Free();
        }
    }

    private static int ReadInt(uint action, int fallback)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            if (!SystemParametersInfo(action, 0, buffer, 0)) return fallback;
            return Marshal.ReadInt32(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteInt(uint action, int value) =>
        SystemParametersInfo(action, (uint)value, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
}
