using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record DisplayVcpFeature(byte Code, uint Current, uint Maximum, IReadOnlyList<uint> SupportedValues, bool IsContinuous)
{
    public int Percent => Maximum == 0 ? 0 : (int)Math.Round(Math.Clamp(Current, 0, Maximum) * 100d / Maximum);
}

internal sealed record DisplayBrightnessMonitor(string Id, string Name, uint Minimum, uint Maximum, uint Current,
    bool SupportsBrightness, DisplayVcpFeature? Contrast = null, DisplayVcpFeature? Volume = null,
    DisplayVcpFeature? InputSource = null, DisplayVcpFeature? ColorPreset = null, DisplayVcpFeature? VcpBrightness = null,
    bool IsInternalPanel = false)
{
    public int Percent => SupportsBrightness
        ? BrightnessPercent.FromMonitorValue(Current, Minimum, Maximum)
        : VcpBrightness?.Percent ?? 0;
}

internal sealed class DisplaySettingsProfileMonitor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Brightness { get; set; }
    public int? Contrast { get; set; }
    public int? Volume { get; set; }
    public uint? ColorPreset { get; set; }

    public DisplaySettingsProfileMonitor() { }

    public DisplaySettingsProfileMonitor(string id, string name, int? brightness, int? contrast, int? volume, uint? colorPreset)
    {
        Id = id;
        Name = name;
        Brightness = brightness;
        Contrast = contrast;
        Volume = volume;
        ColorPreset = colorPreset;
    }
}

internal sealed class DisplaySettingsProfile
{
    public string Name { get; set; } = "";
    public List<DisplaySettingsProfileMonitor> Monitors { get; set; } = [];

    public DisplaySettingsProfile() { }
    public DisplaySettingsProfile(string name, IEnumerable<DisplaySettingsProfileMonitor> monitors)
    {
        Name = name;
        Monitors = monitors.ToList();
    }
}

internal static class DisplaySettingsProfiles
{
    internal static List<DisplaySettingsProfile> Load(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            var profiles = new List<DisplaySettingsProfile>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || !TryGetString(element, "Name", out var name) ||
                    string.IsNullOrWhiteSpace(name) || !element.TryGetProperty("Monitors", out var monitorsElement) ||
                    monitorsElement.ValueKind != JsonValueKind.Array) continue;

                var monitors = new List<DisplaySettingsProfileMonitor>();
                foreach (var monitor in monitorsElement.EnumerateArray())
                {
                    if (monitor.ValueKind != JsonValueKind.Object || !TryGetString(monitor, "Id", out var id) ||
                        !TryGetString(monitor, "Name", out var monitorName) || string.IsNullOrWhiteSpace(id)) continue;
                    monitors.Add(new DisplaySettingsProfileMonitor(id, monitorName,
                        TryGetNullableInt(monitor, "Brightness"), TryGetNullableInt(monitor, "Contrast"),
                        TryGetNullableInt(monitor, "Volume"), TryGetNullableUInt(monitor, "ColorPreset")));
                }

                profiles.Add(new DisplaySettingsProfile(name.Trim(), monitors));
            }
            return profiles;
        }
        catch (JsonException) { return []; }
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static int? TryGetNullableInt(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null ? null :
        property.TryGetInt32(out var value) ? value : null;

    private static uint? TryGetNullableUInt(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null ? null :
        property.TryGetUInt32(out var value) ? value : null;

    internal static DisplaySettingsProfile Capture(string name, IEnumerable<DisplayBrightnessMonitor> monitors) =>
        new(name, monitors.Select(monitor => new DisplaySettingsProfileMonitor(
            monitor.Id,
            monitor.Name,
            monitor.SupportsBrightness || monitor.VcpBrightness is not null ? monitor.Percent : null,
            monitor.Contrast is { IsContinuous: true } contrast ? contrast.Percent : null,
            monitor.Volume is { IsContinuous: true } volume ? volume.Percent : null,
            monitor.ColorPreset?.Current)).ToList());

    internal static bool Matches(DisplaySettingsProfileMonitor saved, DisplayBrightnessMonitor current) =>
        saved.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase) ||
        saved.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase);
}

internal static class MonitorCapabilitiesParser
{
    private static readonly HashSet<byte> RelevantCodes = [0x10, 0x12, 0x14, 0x60, 0x62];

    internal static IReadOnlyDictionary<byte, IReadOnlyList<uint>> Parse(string capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities)) return new Dictionary<byte, IReadOnlyList<uint>>();
        var section = FindVcpSection(capabilities);
        if (section is null) return new Dictionary<byte, IReadOnlyList<uint>>();

        var result = new Dictionary<byte, IReadOnlyList<uint>>();
        var index = 0;
        while (index < section.Length)
        {
            while (index < section.Length && char.IsWhiteSpace(section[index])) index++;
            var start = index;
            while (index < section.Length && Uri.IsHexDigit(section[index])) index++;
            if (start == index)
            {
                index++;
                continue;
            }

            if (!byte.TryParse(section.AsSpan(start, index - start), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var code)) continue;
            while (index < section.Length && char.IsWhiteSpace(section[index])) index++;
            var values = new List<uint>();
            if (index < section.Length && section[index] == '(')
            {
                var end = section.IndexOf(')', index + 1);
                if (end < 0) return result;
                foreach (Match match in Regex.Matches(section[(index + 1)..end], @"(?<![0-9A-Fa-f])[0-9A-Fa-f]{1,2}(?![0-9A-Fa-f])"))
                    if (uint.TryParse(match.Value, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var value)) values.Add(value);
                index = end + 1;
            }

            if (RelevantCodes.Contains(code)) result[code] = values;
        }

        return result;
    }

    internal static bool IsContinuousFeature(IReadOnlyCollection<uint> supportedValues) => supportedValues.Count == 0;

    private static string? FindVcpSection(string capabilities)
    {
        var start = capabilities.IndexOf("vcp(", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += 4;
        var depth = 1;
        for (var index = start; index < capabilities.Length; index++)
        {
            if (capabilities[index] == '(') depth++;
            else if (capabilities[index] == ')' && --depth == 0) return capabilities[start..index];
        }
        return null;
    }
}

internal static class BrightnessPercent
{
    internal static int FromMonitorValue(uint current, uint minimum, uint maximum)
    {
        if (maximum <= minimum) return 0;
        var clamped = Math.Clamp(current, minimum, maximum);
        return (int)Math.Round((clamped - minimum) * 100d / (maximum - minimum), MidpointRounding.AwayFromZero);
    }

    internal static uint ToMonitorValue(int percent, uint minimum, uint maximum)
    {
        if (maximum <= minimum) return minimum;
        var clampedPercent = Math.Clamp(percent, 0, 100);
        return (uint)Math.Round(minimum + (maximum - minimum) * (clampedPercent / 100d), MidpointRounding.AwayFromZero);
    }
}

internal static class DisplayBrightnessSchedule
{
    internal static bool TryParseTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out time);

    internal static bool TryGetTarget(DateTime localTime, string dayStartText, int dayBrightness,
        string nightStartText, int nightBrightness, out int brightness)
    {
        brightness = 0;
        if (!TryParseTime(dayStartText, out var dayStart) || !TryParseTime(nightStartText, out var nightStart)) return false;

        var isDay = dayStart == nightStart ||
                    (dayStart < nightStart
                        ? TimeOnly.FromDateTime(localTime) >= dayStart && TimeOnly.FromDateTime(localTime) < nightStart
                        : TimeOnly.FromDateTime(localTime) >= dayStart || TimeOnly.FromDateTime(localTime) < nightStart);
        brightness = Math.Clamp(isDay ? dayBrightness : nightBrightness, 0, 100);
        return true;
    }
}

internal sealed class DisplayBrightnessSession : IDisposable
{
    private readonly string _displayTopology = CurrentTopology();
    private static string CurrentTopology() => string.Join(";", DesktopDisplays.Enumerate()
        .OrderBy(display => display.DeviceName).Select(display => $"{display.DeviceName}:{display.Bounds.Left},{display.Bounds.Top},{display.Bounds.Right},{display.Bounds.Bottom}"));
    internal bool MatchesCurrentDisplays => _displayTopology == CurrentTopology();
    private readonly long _discoveredAt = Environment.TickCount64;
    internal bool NeedsBrightnessRediscovery => ShouldRediscoverBrightness(MatchesCurrentDisplays,
        Monitors.Count(monitor => monitor.SupportsBrightness || monitor.VcpBrightness is not null),
        DesktopDisplays.Enumerate().Count, Environment.TickCount64 - _discoveredAt);

    internal static bool ShouldRediscoverBrightness(bool topologyMatches, int supported, int connected, long ageMilliseconds) =>
        !topologyMatches || (supported < connected && ageMilliseconds >= 5000);
    private const uint MonitorDefaultToNearest = 2;
    private readonly List<PhysicalMonitor> _monitors;
    private readonly List<InternalPanelBrightness> _internalPanels;
    private bool _disposed;

    private DisplayBrightnessSession(List<PhysicalMonitor> monitors, IReadOnlyList<InternalPanelBrightness> internalPanels)
    {
        _monitors = monitors;
        _internalPanels = internalPanels.ToList();
    }

    public IReadOnlyList<DisplayBrightnessMonitor> Monitors => _monitors
        .Select(monitor => monitor.Info is null ? null : monitor.Info with
        {
            Current = monitor.Current,
            Contrast = monitor.Contrast?.Snapshot(),
            Volume = monitor.Volume?.Snapshot(),
            InputSource = monitor.InputSource?.Snapshot(),
            ColorPreset = monitor.ColorPreset?.Snapshot(),
            VcpBrightness = monitor.GenericBrightness?.Snapshot()
        })
        .Where(monitor => monitor is not null)
        .Cast<DisplayBrightnessMonitor>()
        .Concat(_internalPanels.Select(panel => new DisplayBrightnessMonitor(panel.Id, panel.Name,
            panel.Levels.Count == 0 ? 0u : panel.Levels.Min(),
            panel.Levels.Count == 0 ? 0u : panel.Levels.Max(), panel.Current, true, IsInternalPanel: true)))
        .ToArray();

    public static DisplayBrightnessSession Discover()
    {
        var monitors = new List<PhysicalMonitor>();
        MonitorEnumCallback callback = (handle, _, _, _) =>
        {
            var displayName = GetDisplayName(handle);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(handle, out var count) || count == 0) return true;

            var physical = new PhysicalMonitorInfo[count];
            if (!GetPhysicalMonitorsFromHMONITOR(handle, count, physical)) return true;

            for (var index = 0; index < physical.Length; index++)
            {
                var native = physical[index];
                if (native.Handle == IntPtr.Zero) continue;
                try
                {
                    var description = string.IsNullOrWhiteSpace(native.Description)
                        ? $"Fiziksel ekran {index + 1}"
                        : native.Description.Trim();
                    var brightnessSupported = TryReadBrightness(native.Handle, out var minimum, out var current, out var maximum);
                    var supportedCodes = GetCapabilities(native.Handle);
                    var genericBrightness = ReadFeature(native.Handle, 0x10, supportedCodes);
                    var contrast = ReadFeature(native.Handle, 0x12, supportedCodes);
                    var volume = ReadFeature(native.Handle, 0x62, supportedCodes);
                    var inputSource = ReadFeature(native.Handle, 0x60, supportedCodes);
                    var colorPreset = ReadFeature(native.Handle, 0x14, supportedCodes);
                    if (!brightnessSupported && genericBrightness is null && contrast is null && volume is null && inputSource is null && colorPreset is null)
                    {
                        DestroyPhysicalMonitor(native.Handle);
                        continue;
                    }

                    var monitor = new DisplayBrightnessMonitor($"{displayName}:{index}", $"{displayName} · {description}",
                        minimum, maximum, current, brightnessSupported, contrast?.Snapshot(), volume?.Snapshot(),
                        inputSource?.Snapshot(), colorPreset?.Snapshot(), genericBrightness?.Snapshot());
                    monitors.Add(new PhysicalMonitor(monitor, native.Handle, minimum, maximum, current,
                        genericBrightness, contrast, volume, inputSource, colorPreset));
                }
                catch
                {
                    DestroyPhysicalMonitor(native.Handle);
                }
            }

            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            foreach (var monitor in monitors) DestroyPhysicalMonitor(monitor.Handle);
            throw new InvalidOperationException("Ekranlar listelenemedi.");
        }

        return new DisplayBrightnessSession(monitors, InternalPanelBrightnessService.Discover());
    }

    public bool SetBrightness(string id, int percent, out int appliedPercent)
    {
        ThrowIfDisposed();
        if (InternalPanelBrightnessService.IsInternalId(id))
        {
            var index = _internalPanels.FindIndex(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                appliedPercent = 0;
                return false;
            }
            var panel = _internalPanels[index];
            if (!InternalPanelBrightnessService.SetBrightness(panel, percent, out appliedPercent)) return false;
            _internalPanels[index] = panel with { Current = InternalPanelBrightnessService.MapPercentToLevel(percent, panel.Levels) };
            return true;
        }
        var monitor = FindPhysicalMonitor(id);
        if (monitor is null)
        {
            appliedPercent = 0;
            return false;
        }

        if (monitor.Info?.SupportsBrightness != true)
        {
            if (monitor.GenericBrightness is null)
            {
                appliedPercent = 0;
                return false;
            }
            var genericValue = (uint)Math.Round(Math.Clamp(percent, 0, 100) * monitor.GenericBrightness.Maximum / 100d);
            if (!SetVCPFeature(monitor.Handle, 0x10, genericValue))
            {
                Thread.Sleep(40);
                if (!SetVCPFeature(monitor.Handle, 0x10, genericValue))
                {
                    appliedPercent = monitor.GenericBrightness.Percent;
                    return false;
                }
            }
            monitor.GenericBrightness.CurrentValue = genericValue;
            appliedPercent = monitor.GenericBrightness.Percent;
            return true;
        }

        var value = BrightnessPercent.ToMonitorValue(percent, monitor.Minimum, monitor.Maximum);
        if (!SetMonitorBrightness(monitor.Handle, value))
        {
            Thread.Sleep(40);
            if (!SetMonitorBrightness(monitor.Handle, value))
            {
                var vcpValue = (uint)Math.Round(Math.Clamp(percent, 0, 100) * (monitor.Maximum > 0 ? monitor.Maximum : 100u) / 100d);
                if (SetVCPFeature(monitor.Handle, 0x10, vcpValue))
                {
                    if (monitor.GenericBrightness is not null) monitor.GenericBrightness.CurrentValue = vcpValue;
                    monitor.Current = value;
                    appliedPercent = BrightnessPercent.FromMonitorValue(value, monitor.Minimum, monitor.Maximum);
                    return true;
                }

                appliedPercent = BrightnessPercent.FromMonitorValue(monitor.Current, monitor.Minimum, monitor.Maximum);
                return false;
            }
        }

        monitor.Current = value;
        appliedPercent = BrightnessPercent.FromMonitorValue(value, monitor.Minimum, monitor.Maximum);
        return true;
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightnessForDevice(string deviceName, int delta)
    {
        ThrowIfDisposed();
        return AdjustBrightness(FindMonitorsForDevice(Monitors, deviceName), delta);
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightnessForAllDisplays(int delta)
    {
        ThrowIfDisposed();
        return AdjustBrightness(Monitors, delta);
    }

    private (int Supported, int Applied, int Failed, int? Brightness) AdjustBrightness(
        IEnumerable<DisplayBrightnessMonitor> monitors, int delta)
    {
        return ApplyBrightnessToMonitors(monitors, null, delta);
    }

    internal (int Supported, int Applied, int Failed, int? Brightness) SetBrightnessForAllDisplays(int percent) =>
        ApplyBrightnessToMonitors(Monitors, percent, 0);

    private (int Supported, int Applied, int Failed, int? Brightness) ApplyBrightnessToMonitors(
        IEnumerable<DisplayBrightnessMonitor> monitors, int? percent, int delta)
    {
        ThrowIfDisposed();
        return ApplyBrightnessToMonitors(monitors, percent, delta, (id, target) =>
            SetBrightness(id, target, out var applied) ? applied : null);
    }

    // One shared slider must send one shared target, including monitors already at a limit.
    internal static (int Supported, int Applied, int Failed, int? Brightness) ApplyBrightnessToMonitors(
        IEnumerable<DisplayBrightnessMonitor> monitors, int? percent, int delta, Func<string, int, int?> setBrightness)
    {
        var targets = monitors.Where(monitor => monitor.SupportsBrightness || monitor.VcpBrightness is not null).ToArray();
        if (targets.Length == 0) return (0, 0, 0, null);
        var target = Math.Clamp(percent ?? (int)Math.Round(targets.Average(monitor => monitor.Percent)) + delta, 0, 100);
        var applied = 0;
        var total = 0;
        // Some drivers share a DDC/CI transport; serialize writes to avoid dropping a sibling display's command.
        foreach (var monitor in targets)
        {
            try
            {
                if (setBrightness(monitor.Id, target) is not { } actual) continue;
                applied++;
                total += actual;
            }
            catch { /* Continue applying to the other monitors and report this display as failed. */ }
        }
        return (targets.Length, applied, targets.Length - applied,
            applied == 0 ? null : (int)Math.Round(total / (double)applied));
    }

    internal static IReadOnlyList<DisplayBrightnessMonitor> FindMonitorsForDevice(
        IEnumerable<DisplayBrightnessMonitor> monitors, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return Array.Empty<DisplayBrightnessMonitor>();
        var prefix = deviceName.Trim() + ":";
        return monitors.Where(monitor => monitor.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    internal static bool TryGetCursorDisplayDeviceName(out string deviceName)
    {
        deviceName = string.Empty;
        if (!GetCursorPos(out var cursor)) return false;
        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || string.IsNullOrWhiteSpace(info.DeviceName)) return false;
        deviceName = info.DeviceName;
        return true;
    }

    public bool SetFeature(string id, byte code, uint value, out uint appliedValue)
    {
        ThrowIfDisposed();
        var monitor = FindPhysicalMonitor(id);
        if (monitor is null)
        {
            appliedValue = 0;
            return false;
        }

        var feature = monitor.GetFeature(code);
        if (feature is null || !feature.IsValueSupported(value) || !SetVCPFeature(monitor.Handle, code, value))
        {
            if (feature is not null && feature.IsValueSupported(value))
            {
                Thread.Sleep(40);
                if (SetVCPFeature(monitor.Handle, code, value))
                {
                    feature.CurrentValue = value;
                    appliedValue = value;
                    return true;
                }
            }
            appliedValue = feature?.CurrentValue ?? 0;
            return false;
        }

        feature.CurrentValue = value;
        appliedValue = value;
        return true;
    }

    private PhysicalMonitor? FindPhysicalMonitor(string id) => _monitors.FirstOrDefault(monitor =>
        string.Equals(monitor.Info?.Id, id, StringComparison.OrdinalIgnoreCase));

    private static bool TryReadBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetMonitorBrightness(monitor, out minimum, out current, out maximum) && maximum > minimum)
                return true;
            if (attempt < 2) Thread.Sleep(40);
        }
        minimum = current = maximum = 0;
        return false;
    }

    private static IReadOnlyDictionary<byte, IReadOnlyList<uint>> GetCapabilities(IntPtr monitor)
    {
        if (!GetCapabilitiesStringLength(monitor, out var length) || length is < 2 or > 64 * 1024) return new Dictionary<byte, IReadOnlyList<uint>>();
        var capabilities = new StringBuilder((int)length);
        return CapabilitiesRequestAndCapabilitiesReply(monitor, capabilities, length)
            ? MonitorCapabilitiesParser.Parse(capabilities.ToString())
            : new Dictionary<byte, IReadOnlyList<uint>>();
    }

    private static MutableVcpFeature? ReadFeature(IntPtr monitor, byte code, IReadOnlyDictionary<byte, IReadOnlyList<uint>> supportedCodes)
    {
        if (!supportedCodes.TryGetValue(code, out var supportedValues))
        {
            if (code != 0x10) return null;
            supportedValues = Array.Empty<uint>();
        }
        uint current = 0, maximum = 0;
        var read = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetVCPFeatureAndVCPFeatureReply(monitor, code, out _, out current, out maximum)) { read = true; break; }
            if (attempt < 2) Thread.Sleep(80);
        }
        if (!read) return null;
        var isContinuous = MonitorCapabilitiesParser.IsContinuousFeature(supportedValues);
        if (isContinuous && maximum == 0) return null;
        if (!isContinuous && supportedValues.Count == 0) return null;
        return new MutableVcpFeature(code, current, isContinuous ? maximum : 0, supportedValues, isContinuous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var monitor in _monitors) DestroyPhysicalMonitor(monitor.Handle);
        _monitors.Clear();
    }

    private static string GetDisplayName(IntPtr monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        return GetMonitorInfo(monitor, ref info) && !string.IsNullOrWhiteSpace(info.DeviceName)
            ? info.DeviceName.TrimEnd('\0')
            : "Ekran";
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DisplayBrightnessSession));
    }

    private sealed class PhysicalMonitor(DisplayBrightnessMonitor info, IntPtr handle, uint minimum, uint maximum, uint current,
        MutableVcpFeature? genericBrightness, MutableVcpFeature? contrast, MutableVcpFeature? volume,
        MutableVcpFeature? inputSource, MutableVcpFeature? colorPreset)
    {
        public DisplayBrightnessMonitor? Info { get; } = info;
        public IntPtr Handle { get; } = handle;
        public uint Minimum { get; } = minimum;
        public uint Maximum { get; } = maximum;
        public uint Current { get; set; } = current;
        public MutableVcpFeature? GenericBrightness { get; } = genericBrightness;
        public MutableVcpFeature? Contrast { get; } = contrast;
        public MutableVcpFeature? Volume { get; } = volume;
        public MutableVcpFeature? InputSource { get; } = inputSource;
        public MutableVcpFeature? ColorPreset { get; } = colorPreset;
        public MutableVcpFeature? GetFeature(byte code) => code switch
        {
            0x10 => GenericBrightness,
            0x12 => Contrast,
            0x14 => ColorPreset,
            0x60 => InputSource,
            0x62 => Volume,
            _ => null
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    private sealed class MutableVcpFeature(byte code, uint currentValue, uint maximum,
        IReadOnlyList<uint> supportedValues, bool isContinuous)
    {
        public byte Code { get; } = code;
        public uint CurrentValue { get; set; } = currentValue;
        public uint Maximum { get; } = maximum;
        public IReadOnlyList<uint> SupportedValues { get; } = supportedValues;
        public bool IsContinuous { get; } = isContinuous;
        public int Percent => Maximum == 0 ? 0 : (int)Math.Round(Math.Clamp(CurrentValue, 0, Maximum) * 100d / Maximum);
        public bool IsValueSupported(uint value) => IsContinuous ? value <= Maximum : SupportedValues.Contains(value);
        public DisplayVcpFeature Snapshot() => new(Code, CurrentValue, Maximum, SupportedValues, IsContinuous);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitorInfo
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    private delegate bool MonitorEnumCallback(IntPtr monitor, IntPtr deviceContext, IntPtr rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clipRect, MonitorEnumCallback callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("Dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitorInfo[] physicalMonitors);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);

    [DllImport("Dxva2.dll", EntryPoint = "CapabilitiesRequestAndCapabilitiesReply", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint codeType, out uint currentValue, out uint maximumValue);

    [DllImport("Dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint newValue);

    [DllImport("Dxva2.dll", EntryPoint = "DestroyPhysicalMonitor", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}

public sealed class DisplayBrightnessModule : ITweakModule
{
    private const string DefaultDimBrightnessHotkey = "Ctrl+Alt+F11";
    private const string DefaultBrightenBrightnessHotkey = "Ctrl+Alt+F12";
    private IModuleContext? _context;
    private DisplayBrightnessWindow? _window;
    private DispatcherTimer? _scheduleTimer;
    private IDisposable? _dimBrightnessHotkey;
    private IDisposable? _brightenBrightnessHotkey;
    private GlobalMouseHook? _brightnessMouseHook;
    private readonly SemaphoreSlim _brightnessAdjustmentGate = new(1, 1);
    private readonly object _hotkeySessionSync = new();
    private DisplayBrightnessSession? _hotkeyBrightnessSession;
    private Task<DisplayBrightnessSession>? _hotkeyBrightnessDiscovery;
    private int? _lastScheduledBrightness;
    private bool _scheduleApplying;

    public string Id => "display-brightness";
    public string Name => "Power Display · Ekran Denetimi";
    public string Category => "Sistem";
    public string Description => "Dahili panel parlaklığını Windows WMI ile, harici ekran parlaklığı, kontrast, monitör sesi, giriş kaynağı ve renk ön ayarlarını DDC/CI ile yönetin.";
    public string? Warning => "Harici ekranlarda monitör menüsünden DDC/CI açık olmalıdır. Yalnızca yetenek bildirgesinde sunulan VCP kontrolleri gösterilir; üretici DDC/CI uyumsuzluğu bazı donanım sonuçlarını etkileyebilir. Renk ön ayarı monitör renklerini değiştirebilir.";
    public bool IsActive { get; private set; }

    public IReadOnlyList<SettingDescriptor> Settings { get; }

    public DisplayBrightnessModule()
    {
        Settings = new[]
        {
            new SettingDescriptor
            {
                Key = "open-panel",
                Label = "Power Display panelini aç",
                Kind = SettingKind.Action,
                Description = "Uyumlu harici ekranların desteklenen DDC/CI denetimlerini gösterir.",
                Execute = OpenPanel
            },
            new SettingDescriptor
            {
                Key = "brightness-schedule-enabled",
                Label = "Zamanlanmış parlaklık etkin",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Gündüz ve gece başlangıç saatlerinde desteklenen ekran parlaklığını panel kapalıyken de uygular."
            },
            new SettingDescriptor
            {
                Key = "day-start-time",
                Label = "Gündüz başlangıcı (SS:dd)",
                Kind = SettingKind.Text,
                Default = "07:00",
                Description = "Yerel saatle gündüz parlaklık döneminin başlangıcı."
            },
            new SettingDescriptor
            {
                Key = "day-brightness",
                Label = "Gündüz parlaklığı (%)",
                Kind = SettingKind.Number,
                Default = 75,
                Min = 0,
                Max = 100
            },
            new SettingDescriptor
            {
                Key = "night-start-time",
                Label = "Gece başlangıcı (SS:dd)",
                Kind = SettingKind.Text,
                Default = "20:00",
                Description = "Yerel saatle gece parlaklık döneminin başlangıcı."
            },
            new SettingDescriptor
            {
                Key = "night-brightness",
                Label = "Gece parlaklığı (%)",
                Kind = SettingKind.Number,
                Default = 35,
                Min = 0,
                Max = 100
            },
            new SettingDescriptor
            {
                Key = "brightness-hotkeys-enabled",
                Label = "İmleçteki ekran için parlaklık kısayolları",
                Kind = SettingKind.Toggle,
                Default = false,
                Description = "Ctrl+Alt+F11 parlaklığı azaltır, Ctrl+Alt+F12 artırır. Yalnızca imlecin bulunduğu ekran değiştirilir."
            },
            new SettingDescriptor
            {
                Key = "brightness-hotkey-step",
                Label = "Kısayol adımı (%)",
                Kind = SettingKind.Number,
                Default = 5,
                Min = 1,
                Max = 20,
                Description = "Her kısayol basışında uygulanacak parlaklık değişimi."
            },
            new SettingDescriptor
            {
                Key = "brightness-dim-hotkey",
                Label = "Parlaklığı azaltma kısayolu",
                Kind = SettingKind.Text,
                Default = DefaultDimBrightnessHotkey,
                Description = "Örnek: Ctrl+Alt+F11 veya Ctrl+Alt+MouseWheelDown. Ctrl, Alt, Shift ya da Win kullanın."
            },
            new SettingDescriptor
            {
                Key = "brightness-brighten-hotkey",
                Label = "Parlaklığı artırma kısayolu",
                Kind = SettingKind.Text,
                Default = DefaultBrightenBrightnessHotkey,
                Description = "Örnek: Ctrl+Alt+F12 veya Ctrl+Alt+MouseWheelUp. Azaltma kısayolundan farklı olmalıdır."
            }
        };
    }

    public void Initialize(IModuleContext context) => _context = context;

    public void Activate()
    {
        IsActive = true;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        RegisterBrightnessHotkeys();
        StartHotkeyBrightnessDiscovery();
        UpdateScheduleTimer();
    }

    public void Deactivate()
    {
        IsActive = false;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _scheduleTimer?.Stop();
        _scheduleTimer = null;
        _lastScheduledBrightness = null;
        ReleaseBrightnessHotkeys();
        DisposeHotkeyBrightnessSessionIfIdle();
        _window?.Close();
        _window = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => DisposeHotkeyBrightnessSessionIfIdle();

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            DisposeHotkeyBrightnessSessionIfIdle();
    }

    public void Apply()
    {
        _lastScheduledBrightness = null;
        if (IsActive)
        {
            RegisterBrightnessHotkeys();
            UpdateScheduleTimer();
        }
    }

    private void RegisterBrightnessHotkeys()
    {
        ReleaseBrightnessHotkeys();
        if (_context?.Settings.GetBool("brightness-hotkeys-enabled", false) != true) return;

        try
        {
            var dimHotkey = ParseBrightnessHotkey(_context.Settings.GetString("brightness-dim-hotkey", DefaultDimBrightnessHotkey));
            var brightenHotkey = ParseBrightnessHotkey(_context.Settings.GetString("brightness-brighten-hotkey", DefaultBrightenBrightnessHotkey));
            if (dimHotkey == brightenHotkey)
                throw new FormatException("Parlaklığı artırma ve azaltma kısayolları farklı olmalıdır.");

            RegisterBrightnessShortcut(dimHotkey, -1);
            RegisterBrightnessShortcut(brightenHotkey, 1);
            if (dimHotkey.WheelDirection != 0 || brightenHotkey.WheelDirection != 0)
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                _brightnessMouseHook = new GlobalMouseHook
                {
                    EventFilter = input => IsActive && _context is not null &&
                        HandleBrightnessWheel(input, GetPressedHotkeyModifiers(), dimHotkey, brightenHotkey,
                            direction => dispatcher.BeginInvoke(new Action(() => _ = AdjustBrightnessAsync(direction))))
                };
                _brightnessMouseHook.Install();
            }
        }
        catch (Exception exception)
        {
            ReleaseBrightnessHotkeys();
            _context.Log.Error("Power Display parlaklık kısayolları kaydedilemedi.", exception);
            _context.Notify($"Power Display parlaklık kısayolları kaydedilemedi: {exception.Message}");
        }
    }

    private void RegisterBrightnessShortcut((uint Modifiers, uint VirtualKey, int WheelDirection) shortcut, int direction)
    {
        if (shortcut.WheelDirection != 0) return;
        var registration = _context!.Hotkeys.Register(shortcut.Modifiers, shortcut.VirtualKey,
            () => _ = AdjustBrightnessAsync(direction));
        if (direction < 0) _dimBrightnessHotkey = registration;
        else _brightenBrightnessHotkey = registration;
    }

    internal static bool HandleBrightnessWheel(GlobalMouseHook.Input input, uint modifiers,
        (uint Modifiers, uint VirtualKey, int WheelDirection) dimShortcut,
        (uint Modifiers, uint VirtualKey, int WheelDirection) brightenShortcut, Action<int> adjust)
    {
        if (HotkeyService.IsCapturing || input.Message != Native.WM_MOUSEWHEEL || input.WheelDelta == 0) return false;
        var wheelDirection = Math.Sign(input.WheelDelta);

        if (dimShortcut.WheelDirection == wheelDirection && dimShortcut.Modifiers == modifiers)
            adjust(-1);
        else if (brightenShortcut.WheelDirection == wheelDirection && brightenShortcut.Modifiers == modifiers)
            adjust(1);
        else return false;
        return true;
    }

    private static uint GetPressedHotkeyModifiers()
    {
        uint modifiers = 0;
        if (IsKeyDown(0x11)) modifiers |= HotkeyModifiers.Control;
        if (IsKeyDown(0x12)) modifiers |= HotkeyModifiers.Alt;
        if (IsKeyDown(0x10)) modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal static (uint Modifiers, uint VirtualKey, int WheelDirection) ParseBrightnessHotkey(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Kısayolu Ctrl+Alt+F11 biçiminde yazın.");

        uint modifiers = 0;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => throw new FormatException($"'{parts[index]}' tanınan bir kısayol değiştiricisi değil.")
            };
            if ((modifiers & modifier) != 0)
                throw new FormatException("Kısayolda aynı değiştiriciyi birden fazla yazmayın.");
            modifiers |= modifier;
        }

        var wheelDirection = parts[^1].ToLowerInvariant() switch
        {
            "mousewheelup" or "wheelup" => 1,
            "mousewheeldown" or "wheeldown" => -1,
            _ => 0
        };
        if (wheelDirection != 0) return (modifiers, 0, wheelDirection);

        if (!Enum.TryParse<Key>(parts[^1], true, out var key) ||
            key is Key.None or Key.DeadCharProcessed or Key.ImeProcessed or Key.System or Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new FormatException($"'{parts[^1]}' geçerli bir işlem tuşu değil.");

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is < 1 or > 254)
            throw new FormatException($"'{parts[^1]}' eşlenebilir bir tuş değil.");
        return (modifiers, (uint)virtualKey, 0);
    }

    private void ReleaseBrightnessHotkeys()
    {
        _dimBrightnessHotkey?.Dispose();
        _dimBrightnessHotkey = null;
        _brightenBrightnessHotkey?.Dispose();
        _brightenBrightnessHotkey = null;
        _brightnessMouseHook?.Dispose();
        _brightnessMouseHook = null;
    }

    private async Task AdjustBrightnessAsync(int direction)
    {
        if (!IsActive || _context is null) return;
        var step = Math.Clamp(_context.Settings.GetInt("brightness-hotkey-step", 5), 1, 20);
        await _brightnessAdjustmentGate.WaitAsync();
        try
        {
            if (!IsActive || _context is null) return;
            var result = await Task.Run(() =>
            {
                var session = _hotkeyBrightnessSession;
                if (session is not null && session.NeedsBrightnessRediscovery)
                {
                    session.Dispose();
                    session = _hotkeyBrightnessSession = null;
                }
                if (session is null)
                {
                    Task<DisplayBrightnessSession> discovery;
                    lock (_hotkeySessionSync)
                        discovery = _hotkeyBrightnessDiscovery ??= Task.Run(DisplayBrightnessSession.Discover);
                    session = discovery.GetAwaiter().GetResult();
                    if (session.NeedsBrightnessRediscovery)
                    {
                        session.Dispose();
                        session = DisplayBrightnessSession.Discover();
                    }
                    _hotkeyBrightnessSession = session;
                    lock (_hotkeySessionSync)
                        if (ReferenceEquals(_hotkeyBrightnessDiscovery, discovery)) _hotkeyBrightnessDiscovery = null;
                }
                var outcome = session.AdjustBrightnessForAllDisplays(step * direction);
                if (outcome.Failed > 0 || outcome.Supported == 0)
                {
                    session.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                return outcome;
            });
            if (!IsActive) return;
            if (result.Supported == 0)
                _context.Notify("Bağlı ekranlarda desteklenen bir parlaklık denetimi bulunamadı.");
            else if (result.Failed > 0)
                _context.Notify($"Ekran parlaklığı: {result.Applied}/{result.Supported} ekrana uygulandı; {result.Failed} hata.");
            ShowBrightnessOutcome(result);
        }
        catch (Exception exception)
        {
            _context.Log.Error("Power Display kısayoluyla parlaklık değiştirilemedi.", exception);
            _context.Notify("Ekran parlaklığı değiştirilemedi: " + exception.Message);
        }
        finally
        {
            if (!IsActive)
                Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            _brightnessAdjustmentGate.Release();
        }
    }

    private int _overlayBrightnessRequest;

    private async Task SetOverlayBrightnessAsync(int percent)
    {
        var request = Interlocked.Increment(ref _overlayBrightnessRequest);
        await _brightnessAdjustmentGate.WaitAsync();
        try
        {
            if (!IsActive || request != Volatile.Read(ref _overlayBrightnessRequest)) return;
            var result = await Task.Run(() =>
            {
                if (_hotkeyBrightnessSession is { } cached && cached.NeedsBrightnessRediscovery)
                {
                    cached.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                var session = _hotkeyBrightnessSession ??= DisplayBrightnessSession.Discover();
                var outcome = session.SetBrightnessForAllDisplays(percent);
                if (outcome.Failed > 0 || outcome.Supported == 0)
                {
                    session.Dispose();
                    _hotkeyBrightnessSession = null;
                }
                return outcome;
            });
            if (request == Volatile.Read(ref _overlayBrightnessRequest)) ShowBrightnessOutcome(result);
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Gösterge üzerinden parlaklık değiştirilemedi.", exception);
        }
        finally
        {
            if (!IsActive) Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            _brightnessAdjustmentGate.Release();
        }
    }

    private void ShowBrightnessOutcome((int Supported, int Applied, int Failed, int? Brightness) result)
    {
        if (!IsActive || _context is not IDisplayBrightnessOverlayContext overlay) return;
        var connected = DesktopDisplays.Enumerate().Count;
        var status = result.Supported == 1 && connected == 1 && result.Failed == 0 ? null
            : $"{result.Applied}/{Math.Max(connected, result.Supported)} monitöre uygulandı";
        if (result.Failed > 0) status += $" · {result.Failed} hata";
        if (result.Supported > 0 && result.Supported < connected)
            status += $" · {connected - result.Supported} monitörde denetim bulunamadı";
        if (result.Supported == 0) status = "Parlaklık denetimi desteklenmiyor";
        overlay.ShowDisplayBrightness(result.Brightness ?? 0, "all-displays",
            percent => _ = SetOverlayBrightnessAsync(percent), status);
    }

    private void DisposeHotkeyBrightnessSessionIfIdle()
    {
        if (!_brightnessAdjustmentGate.Wait(0)) return;
        try
        {
            Interlocked.Exchange(ref _hotkeyBrightnessSession, null)?.Dispose();
            Task<DisplayBrightnessSession>? discovery;
            lock (_hotkeySessionSync)
            {
                discovery = _hotkeyBrightnessDiscovery;
                _hotkeyBrightnessDiscovery = null;
            }
            if (discovery is not null) _ = DisposeDiscoveredSessionAsync(discovery);
        }
        finally { _brightnessAdjustmentGate.Release(); }
    }

    private void StartHotkeyBrightnessDiscovery()
    {
        lock (_hotkeySessionSync)
            _hotkeyBrightnessDiscovery ??= Task.Run(DisplayBrightnessSession.Discover);
    }

    private static async Task DisposeDiscoveredSessionAsync(Task<DisplayBrightnessSession> discovery)
    {
        try { (await discovery.ConfigureAwait(false)).Dispose(); }
        catch { }
    }

    private void UpdateScheduleTimer()
    {
        var settings = _context?.Settings;
        if (!IsActive || settings?.GetBool("brightness-schedule-enabled", false) != true)
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            return;
        }

        if (!DisplayBrightnessSchedule.TryGetTarget(DateTime.Now,
                settings.GetString("day-start-time", "07:00"), settings.GetInt("day-brightness", 75),
                settings.GetString("night-start-time", "20:00"), settings.GetInt("night-brightness", 35), out _))
        {
            _scheduleTimer?.Stop();
            _scheduleTimer = null;
            _ = ApplyScheduledBrightnessAsync();
            return;
        }

        if (_scheduleTimer is null)
        {
            _scheduleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _scheduleTimer.Tick += async (_, _) => await ApplyScheduledBrightnessAsync();
        }
        if (!_scheduleTimer.IsEnabled) _scheduleTimer.Start();
        _ = ApplyScheduledBrightnessAsync();
    }

    private async Task ApplyScheduledBrightnessAsync()
    {
        var settings = _context?.Settings;
        if (!IsActive || _scheduleApplying || settings?.GetBool("brightness-schedule-enabled", false) != true) return;
        if (!DisplayBrightnessSchedule.TryGetTarget(DateTime.Now,
                settings.GetString("day-start-time", "07:00"), settings.GetInt("day-brightness", 75),
                settings.GetString("night-start-time", "20:00"), settings.GetInt("night-brightness", 35), out var target))
        {
            _context?.Log.Error("Power Display zamanlanmış parlaklığı uygulanmadı: saatler SS:dd biçiminde olmalıdır.");
            return;
        }
        if (_lastScheduledBrightness == target) return;

        _scheduleApplying = true;
        try
        {
            var result = await Task.Run(() =>
            {
                using var session = DisplayBrightnessSession.Discover();
                var supported = 0;
                var applied = 0;
                var failed = 0;
                foreach (var monitor in session.Monitors)
                {
                    if (!monitor.SupportsBrightness && monitor.VcpBrightness is null) continue;
                    supported++;
                    if (session.SetBrightness(monitor.Id, target, out _)) applied++;
                    else failed++;
                }
                return (supported, applied, failed);
            });

            if (!IsActive) return;
            if (result.applied > 0 || result.supported == 0) _lastScheduledBrightness = target;
            if (result.failed > 0)
                _context?.Log.Error($"Power Display zamanlanmış parlaklığı: {result.applied}/{result.supported} ekran uygulandı; {result.failed} hata.");
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Power Display zamanlanmış parlaklığı uygulanamadı.", exception);
        }
        finally
        {
            _scheduleApplying = false;
        }
    }

    private void OpenPanel()
    {
        try
        {
            var featureUi = (_context as IFeatureUiContext)?.FeatureUi
                ?? throw new InvalidOperationException("React arayüz altyapısı kullanılamıyor.");
            if (_window is null || !_window.IsVisible)
                _window = new DisplayBrightnessWindow(_context?.Settings, featureUi);
            else
                _window.Show();
        }
        catch (Exception exception)
        {
            _context?.Log.Error("Ekran parlaklığı paneli açılamadı.", exception);
            _context?.Notify("Ekran parlaklığı paneli açılamadı: " + exception.Message);
        }
    }
}

internal sealed class DisplayBrightnessWindow
{
    private readonly Dictionary<string, int> _pendingBrightness = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, byte Code), uint> _pendingVcpFeatures = new();
    private readonly DispatcherTimer _applyTimer;
    private readonly IModuleSettings? _settings;
    private readonly IFeatureUiWindow _window;
    private List<DisplaySettingsProfile> _profiles;
    private DisplayBrightnessSession? _session;
    private bool _linkLevels = true;
    private bool _isClosed;
    private int _refreshGeneration;
    private string _status = "Ekran bilgileri yükleniyor…";
    private string _profileName = "Yeni profil";
    private string _selectedProfileName = "";
    private PendingColorPreset? _confirmColor;

    public DisplayBrightnessWindow(IModuleSettings? settings, IFeatureUiService featureUi)
    {
        _settings = settings;
        _profiles = DisplaySettingsProfiles.Load(settings?.GetString("profiles", "[]") ?? "[]");
        _window = featureUi.Open("display-brightness-" + Guid.NewGuid().ToString("N"), "display-brightness",
            "Power Display · Ekran Denetimi",
            new FeatureUiWindowOptions(920, 760, ShowInTaskbar: true, Resizable: true));
        _window.MessageReceived += OnMessage;
        _applyTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
        _applyTimer.Tick += (_, _) => { FlushPendingChanges(); PushState(); };
        PushState();
        Refresh();
    }

    public void Close()
    {
        if (_isClosed) return;
        _isClosed = true;
        _refreshGeneration++;
        FlushPendingChanges();
        _applyTimer.Stop();
        _session?.Dispose();
        _session = null;
        _window.Close();
    }

    public bool IsVisible => _window.IsVisible;
    public void Show() => _window.Show();

    private void OnMessage(string command, JsonElement payload)
    {
        switch (command)
        {
            case "display.close": Close(); break;
            case "display.refresh": Refresh(); break;
            case "display.link-levels": _linkLevels = TryGetBool(payload, "enabled"); PushState(); break;
            case "display.profile-name": _profileName = TryGetString(payload, "name"); PushState(); break;
            case "display.profile-select": SelectProfile(TryGetString(payload, "name")); break;
            case "display.profile-save": SaveProfile(); break;
            case "display.profile-apply": ApplyProfile(); break;
            case "display.profile-delete": DeleteProfile(); break;
            case "display.slider": UpdateSlider(payload); break;
            case "display.selector": SelectFeature(payload); break;
            case "display.color-confirm": ConfirmColorPreset(TryGetBool(payload, "confirm")); break;
        }
    }

    private void SelectProfile(string name)
    {
        _selectedProfileName = name;
        if (!string.IsNullOrEmpty(name)) _profileName = name;
        PushState();
    }

    private void UpdateSlider(JsonElement payload)
    {
        var id = TryGetString(payload, "id");
        var codeValue = TryGetInt(payload, "code");
        var percent = Math.Clamp(TryGetInt(payload, "value"), 0, 100);
        if (string.IsNullOrEmpty(id) || codeValue is < 0 or > 255) return;
        var code = (byte)codeValue;
        if (code == 0x10)
        {
            ScheduleBrightness(id, percent);
            if (_linkLevels)
            {
                foreach (var sibling in _session?.Monitors.Where(monitor => monitor.Id != id &&
                             (monitor.SupportsBrightness || monitor.VcpBrightness is not null)) ?? [])
                    ScheduleBrightness(sibling.Id, percent);
            }
        }
        else if (GetFeature(id, code) is { } feature)
            ScheduleFeature(id, code, (uint)Math.Round(percent * feature.Maximum / 100d, MidpointRounding.AwayFromZero));
        PushState();
    }

    private void SelectFeature(JsonElement payload)
    {
        var id = TryGetString(payload, "id");
        var codeValue = TryGetInt(payload, "code");
        var value = TryGetUInt(payload, "value");
        if (string.IsNullOrEmpty(id) || codeValue is < 0 or > 255 || value is null) return;
        var code = (byte)codeValue;
        if (code == 0x14)
        {
            _confirmColor = new PendingColorPreset(id, value.Value);
            PushState();
            return;
        }
        ScheduleFeature(id, code, value.Value);
        PushState();
    }

    private void ConfirmColorPreset(bool confirmed)
    {
        if (_confirmColor is { } selection && confirmed) ScheduleFeature(selection.Id, 0x14, selection.Value);
        _confirmColor = null;
        PushState();
    }

    private void SaveProfile()
    {
        FlushPendingChanges();
        var name = _profileName.Trim();
        if (name.Length is < 1 or > 48) { _status = "Profil adı 1–48 karakter arasında olmalıdır."; PushState(); return; }
        if (_session is null || _session.Monitors.Count == 0) { _status = "Profil kaydetmek için önce uyumlu bir ekran bulunmalıdır."; PushState(); return; }
        var profile = DisplaySettingsProfiles.Capture(name, _session.Monitors);
        var existing = _profiles.FindIndex(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _profiles[existing] = profile; else _profiles.Add(profile);
        PersistProfiles();
        _profileName = name;
        _selectedProfileName = name;
        _status = $"‘{name}’ profili {_session.Monitors.Count} ekranın desteklenen denetimleriyle kaydedildi.";
        PushState();
    }

    private void ApplyProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName) || _session is null)
        { _status = "Önce uygulanacak bir profil seçin."; PushState(); return; }
        var profile = _profiles.FirstOrDefault(item => item.Name.Equals(_selectedProfileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return;
        FlushPendingChanges();
        var missingMonitors = 0;
        var unsupportedValues = 0;
        var requested = 0;
        foreach (var saved in profile.Monitors)
        {
            var monitor = _session.Monitors.FirstOrDefault(current => DisplaySettingsProfiles.Matches(saved, current));
            if (monitor is null) { missingMonitors++; continue; }
            if (saved.Brightness is { } brightness && (monitor.SupportsBrightness || monitor.VcpBrightness is not null))
            { ScheduleBrightness(monitor.Id, brightness); requested++; }
            else if (saved.Brightness is not null) unsupportedValues++;
            ScheduleProfileFeature(monitor, 0x12, saved.Contrast is { } contrast ? (uint)contrast : null, ref requested, ref unsupportedValues, percent: true);
            ScheduleProfileFeature(monitor, 0x62, saved.Volume is { } volume ? (uint)volume : null, ref requested, ref unsupportedValues, percent: true);
            ScheduleProfileFeature(monitor, 0x14, saved.ColorPreset, ref requested, ref unsupportedValues, percent: false);
        }
        var (applied, failed) = FlushPendingChanges();
        _status = $"‘{profile.Name}’ profili: {applied}/{requested} denetim uygulandı; {failed} hata, {missingMonitors} ekran bulunamadı, {unsupportedValues} denetim desteklenmiyor.";
        PushState();
    }

    private void ScheduleProfileFeature(DisplayBrightnessMonitor monitor, byte code, uint? savedValue,
        ref int requested, ref int unsupported, bool percent)
    {
        if (savedValue is null) return;
        var feature = code switch { 0x12 => monitor.Contrast, 0x62 => monitor.Volume, 0x14 => monitor.ColorPreset, _ => null };
        if (feature is null || (percent && !feature.IsContinuous) || (!percent && !feature.SupportedValues.Contains(savedValue.Value)))
        { unsupported++; return; }
        var value = percent
            ? (uint)Math.Round(Math.Clamp(savedValue.Value, 0, 100) * feature.Maximum / 100d, MidpointRounding.AwayFromZero)
            : savedValue.Value;
        ScheduleFeature(monitor.Id, code, value);
        requested++;
    }

    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(_selectedProfileName)) return;
        var deleted = _selectedProfileName;
        _profiles.RemoveAll(profile => profile.Name.Equals(deleted, StringComparison.OrdinalIgnoreCase));
        PersistProfiles();
        _profileName = "";
        _selectedProfileName = "";
        _status = $"‘{deleted}’ profili silindi.";
        PushState();
    }

    private void PersistProfiles() => _settings?.Set("profiles", JsonSerializer.Serialize(_profiles));

    private void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        FlushPendingChanges();
        _session?.Dispose();
        _session = null;
        _status = "Ekranların DDC/CI yetenekleri okunuyor…";
        var generation = ++_refreshGeneration;
        PushState();
        try
        {
            var session = await Task.Run(DisplayBrightnessSession.Discover);
            if (_isClosed || generation != _refreshGeneration) { session.Dispose(); return; }
            _session = session;
            var monitors = session.Monitors;
            if (monitors.Count == 0)
                _status = "Denetlenebilir harici ekran veya Windows WMI parlaklık desteği olan dahili panel bulunamadı. Harici ekran için DDC/CI'yi açıp yenileyin.";
            else
            {
                var externalCount = monitors.Count(monitor => !monitor.IsInternalPanel);
                var internalCount = monitors.Count - externalCount;
                _status = $"{monitors.Count} ekran bulundu: {externalCount} harici DDC/CI ekranı, {internalCount} dahili panel. Yalnızca kullanılabilir denetimler gösterilir.";
            }
            PushState();
        }
        catch (Exception exception)
        {
            if (_isClosed || generation != _refreshGeneration) return;
            _status = "Ekranlar okunamadı: " + exception.Message;
            PushState();
        }
    }

    private DisplayVcpFeature? GetFeature(string monitorId, byte code) => _session?.Monitors
        .FirstOrDefault(monitor => monitor.Id == monitorId) is { } monitor
        ? code switch { 0x12 => monitor.Contrast, 0x14 => monitor.ColorPreset, 0x60 => monitor.InputSource, 0x62 => monitor.Volume, _ => null }
        : null;

    private bool ApplyBrightness(string id, int percent)
    {
        if (_session is null) return false;
        try
        {
            if (!_session.SetBrightness(id, percent, out _))
            {
                _status = InternalPanelBrightnessService.IsInternalId(id)
                    ? "Dahili panel parlaklığı değiştirilemedi. Windows WMI sağlayıcısı ve panel desteğini kontrol edin."
                    : "Bu ekranda parlaklık değiştirilemedi. DDC/CI desteğini ve monitör bağlantısını kontrol edin.";
                return false;
            }
            return true;
        }
        catch (Exception exception) { _status = "Parlaklık uygulanamadı: " + exception.Message; return false; }
    }

    private void ScheduleBrightness(string id, int percent)
    {
        _pendingBrightness[id] = percent;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ScheduleFeature(string id, byte code, uint value)
    {
        _pendingVcpFeatures[(id, code)] = value;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private (int Succeeded, int Failed) FlushPendingChanges()
    {
        _applyTimer.Stop();
        if (_session is null || (_pendingBrightness.Count == 0 && _pendingVcpFeatures.Count == 0)) return (0, 0);
        var succeeded = 0;
        var failed = 0;
        var brightnessRequested = _pendingBrightness.Count;
        foreach (var (id, percent) in _pendingBrightness.ToArray())
        {
            if (ApplyBrightness(id, percent)) succeeded++; else failed++;
        }
        if (brightnessRequested > 0)
            _status = $"Parlaklık: {succeeded}/{brightnessRequested} monitöre uygulandı" +
                (failed > 0 ? $" · {failed} hata. DDC/CI desteğini ve bağlantıyı kontrol edin." : ".");
        _pendingBrightness.Clear();
        foreach (var ((id, code), value) in _pendingVcpFeatures.ToArray())
        {
            try
            {
                if (!_session.SetFeature(id, code, value, out _))
                { _status = $"DDC/CI 0x{code:X2} denetimi uygulanamadı. Monitör bağlantısını ve özellik desteğini kontrol edin."; failed++; }
                else succeeded++;
            }
            catch (Exception exception) { _status = $"DDC/CI 0x{code:X2} denetimi uygulanamadı: {exception.Message}"; failed++; }
        }
        _pendingVcpFeatures.Clear();
        return (succeeded, failed);
    }

    private void PushState() => _window.SetState(new
    {
        status = _status,
        linkLevels = _linkLevels,
        profileName = _profileName,
        selectedProfileName = _selectedProfileName,
        profiles = _profiles.Select(profile => profile.Name).ToArray(),
        confirmColorPreset = _confirmColor is null ? null : new
        {
            id = _confirmColor.Id,
            value = _confirmColor.Value,
            monitorName = _session?.Monitors.FirstOrDefault(monitor => monitor.Id == _confirmColor.Id)?.Name ?? "monitör",
            label = _session?.Monitors.FirstOrDefault(monitor => monitor.Id == _confirmColor.Id)?.ColorPreset?.SupportedValues.FirstOrDefault(value => value == _confirmColor.Value).ToString("X2")
        },
        monitors = _session?.Monitors.Select(monitor => new
        {
            id = monitor.Id,
            name = monitor.Name,
            isInternalPanel = monitor.IsInternalPanel,
            controls = BuildControls(monitor)
        }).ToArray() ?? []
    });

    private object[] BuildControls(DisplayBrightnessMonitor monitor)
    {
        var controls = new List<object>();
        if (monitor.SupportsBrightness || monitor.VcpBrightness is not null)
            controls.Add(new { code = (int)0x10, label = "Parlaklık", type = "slider", value = _pendingBrightness.GetValueOrDefault(monitor.Id, monitor.Percent) });
        AddSliderControl(controls, monitor, 0x12, "Kontrast", monitor.Contrast);
        AddSliderControl(controls, monitor, 0x62, "Monitör sesi", monitor.Volume);
        AddSelectorControl(controls, monitor.Id, 0x60, "Giriş kaynağı", monitor.InputSource);
        AddSelectorControl(controls, monitor.Id, 0x14, "Renk ön ayarı", monitor.ColorPreset);
        if (!monitor.IsInternalPanel)
        {
            var codes = new[] { monitor.SupportsBrightness || monitor.VcpBrightness is not null ? (byte?)0x10 : null,
                monitor.Contrast is not null ? (byte?)0x12 : null, monitor.ColorPreset is not null ? (byte?)0x14 : null,
                monitor.InputSource is not null ? (byte?)0x60 : null, monitor.Volume is not null ? (byte?)0x62 : null }
                .Where(code => code.HasValue).Select(code => code!.Value).Distinct().Order();
            controls.Add(new { code = -1, label = "DDC/CI VCP", type = "info", value = string.Join(" · ", codes.Select(code => $"0x{code:X2}")) });
        }
        return controls.ToArray();
    }

    private void AddSliderControl(List<object> controls, DisplayBrightnessMonitor monitor, byte code, string label, DisplayVcpFeature? feature)
    {
        if (feature is not { IsContinuous: true }) return;
        var currentValue = _pendingVcpFeatures.GetValueOrDefault((monitor.Id, code), feature.Current);
        var percent = feature.Maximum == 0 ? 0 : (int)Math.Round(currentValue * 100d / feature.Maximum, MidpointRounding.AwayFromZero);
        controls.Add(new { code = (int)code, label, type = "slider", value = Math.Clamp(percent, 0, 100) });
    }

    private void AddSelectorControl(List<object> controls, string id, byte code, string label, DisplayVcpFeature? feature)
    {
        if (feature is not { IsContinuous: false, SupportedValues.Count: > 0 }) return;
        var current = _pendingVcpFeatures.GetValueOrDefault((id, code), feature.Current);
        controls.Add(new { code = (int)code, label, type = "select", value = current,
            options = feature.SupportedValues.Distinct().Order().Select(value => new { value, label = $"{label} · 0x{value:X2}" }).ToArray() });
    }

    private sealed record PendingColorPreset(string Id, uint Value);

    private static string TryGetString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";
    private static int TryGetInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static uint? TryGetUInt(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.TryGetUInt32(out var number) ? number : null;
    private static bool TryGetBool(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
