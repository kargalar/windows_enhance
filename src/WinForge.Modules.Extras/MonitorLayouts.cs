using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;

namespace WinForge.Modules.Extras;

/// <summary>Captures and applies display layouts directly through Windows, persisted in WinForge settings.</summary>
internal static class MonitorLayouts
{
    private const uint DmPosition = 0x00000020;
    private const uint DmPelsWidth = 0x00080000;
    private const uint DmPelsHeight = 0x00100000;
    private const uint CdsUpdateRegistry = 0x00000001;
    private const uint CdsSetPrimary = 0x00000010;
    private const uint CdsNoReset = 0x10000000;
    internal sealed record Display(string Name, bool Active, bool Primary, byte[] Mode);
    private static List<Display>? _previous;
    private static Action? _changed;
    private static readonly DispatcherTimer Timer = CreateTimer();
    internal static bool Pending => _previous is not null;
    internal static int Generation { get; private set; }
    internal static Action<Exception>? ErrorHandler { get; set; }
    private static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => { try { Revert(); } catch (Exception ex) { ErrorHandler?.Invoke(ex); } }; return timer;
    }
    private static byte[] NewMode() { var bytes = new byte[220]; BitConverter.GetBytes((ushort)220).CopyTo(bytes, 68); return bytes; }
    private static List<Display> Read()
    {
        var list = new List<Display>();
        for (uint index = 0; ; index++)
        {
            var device = new Device { Size = Marshal.SizeOf<Device>() };
            if (!EnumDisplayDevices(null, index, ref device, 0)) break;
            if ((device.Flags & 8) != 0) continue; // mirroring driver
            var mode = NewMode(); var active = (device.Flags & 1) != 0;
            if (!EnumDisplaySettings(device.Name, active ? -1 : -2, mode)) continue;
            list.Add(new(device.Name, active, (device.Flags & 4) != 0, mode));
        }
        return list;
    }
    internal static string Capture()
    {
        var json = JsonSerializer.Serialize(Read());
        Validate(json);
        return json;
    }
    internal static void Validate(string json)
    {
        var items = JsonSerializer.Deserialize<List<Display>>(json) ?? throw new FormatException("Monitör profili eksik.");
        if (items.Count is < 1 or > 32 || !items.Any(d => d.Active) || items.Count(d => d.Primary && d.Active) != 1 || items.Select(d => d.Name).Distinct().Count() != items.Count || items.Any(d => d.Mode.Length != 220 || !d.Name.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("Monitör profili geçersiz; en az bir etkin ve bir ana ekran gerekli.");
    }
    internal static void Apply(string json, Action changed)
    {
        Validate(json); if (Pending) throw new InvalidOperationException("Önce bekleyen monitör düzenini onaylayın veya geri alın.");
        var target = JsonSerializer.Deserialize<List<Display>>(json)!;
        var before = Read();
        if (target.Where(d => d.Active).Any(d => !before.Any(b => b.Name == d.Name))) throw new InvalidOperationException("Profildeki ekran bağlı değil veya Windows ekran kimliği değişmiş. Profili yeniden kaydedin.");
        _previous = before; _changed = changed; Generation++;
        try { Set(target, before); Timer.Start(); changed(); }
        catch { Revert(); throw; }
    }
    private static void Set(List<Display> target, List<Display> available)
    {
        var changes = available
            .Select(current => (Current: current, Target: target.FirstOrDefault(t => t.Name == current.Name) ?? current with { Active = false, Primary = false }))
            // EnumDisplaySettings also returns registry modes for disconnected
            // displays. Do not try to detach a display that is already inactive.
            .Where(change => change.Current.Active || change.Target.Active)
            .OrderByDescending(change => change.Target.Primary)
            .ToList();
        // Windows evaluates a multi-monitor topology only after every device's
        // settings have been staged. CDS_TEST per device can reject a valid
        // saved layout while the other displays are still in the old topology.
        foreach (var change in changes)
        {
            var d = change.Target;
            var detaching = !d.Active && change.Current.Active;
            var mode = detaching ? NewMode() : (byte[])d.Mode.Clone();
            if (detaching)
            {
                // Use a zeroed DEVMODE for a live detach so stale registry mode
                // fields cannot affect the operation. Windows' detach sample
                // specifies position and zero pixel dimensions.
                BitConverter.GetBytes(DmPosition | DmPelsWidth | DmPelsHeight).CopyTo(mode, 72);
            }
            var flags = CdsUpdateRegistry | CdsNoReset | (d.Primary && d.Active ? CdsSetPrimary : 0);
            var code = ChangeDisplaySettingsEx(d.Name, mode, IntPtr.Zero, flags, IntPtr.Zero);
            if (code != 0) throw new InvalidOperationException($"{d.Name}: ekran ayarlar\u0131 haz\u0131rlanamad\u0131 ({DescribeResult(code)}).");
        }
        var apply = ChangeDisplaySettingsEx(null, null, IntPtr.Zero, 0, IntPtr.Zero);
        if (apply != 0) throw new InvalidOperationException($"Ekran d\u00fczeni uygulanamad\u0131 ({DescribeResult(apply)}).");
    }
    private static string DescribeResult(int code) => code switch
    {
        -1 => "ekran s\u00fcr\u00fcc\u00fcs\u00fc modu uygulayamad\u0131 (DISP_CHANGE_FAILED, -1)",
        -2 => "istenen ekran modu desteklenmiyor (DISP_CHANGE_BADMODE, -2)",
        -3 => "ayarlar kaydedilemedi (DISP_CHANGE_NOTUPDATED, -3)",
        -4 => "de\u011fi\u015fiklik i\u00e7in Windows'un yeniden ba\u015flat\u0131lmas\u0131 gerekiyor (DISP_CHANGE_RESTART, -4)",
        -5 => "ge\u00e7ersiz ekran de\u011fi\u015ftirme bayraklar\u0131 (DISP_CHANGE_BADFLAGS, -5)",
        -6 => "ge\u00e7ersiz ekran parametresi (DISP_CHANGE_BADPARAM, -6)",
        -7 => "DualView nedeniyle ekran d\u00fczeni de\u011fi\u015ftirilemedi (DISP_CHANGE_BADDUALVIEW, -7)",
        _ => $"Windows ekran de\u011fi\u015fikli\u011fini reddetti ({code})"
    };
    internal static void Confirm() { Timer.Stop(); _previous = null; Generation++; var changed = _changed; _changed = null; changed?.Invoke(); }
    internal static void Revert()
    {
        Timer.Stop(); if (_previous is null) return;
        var before = _previous;
        Set(before, Read());
        _previous = null; Generation++; var changed = _changed; _changed = null; changed?.Invoke();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string? name, uint index, ref Device device, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string name, int mode, [In, Out] byte[] data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string? name, byte[]? mode, IntPtr window, uint flags, IntPtr parameter);
}
