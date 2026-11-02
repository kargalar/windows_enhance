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
