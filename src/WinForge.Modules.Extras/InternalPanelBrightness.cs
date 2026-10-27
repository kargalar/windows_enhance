using System.Runtime.InteropServices;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record InternalPanelBrightness(string Id, string Name, byte Current, IReadOnlyList<byte> Levels);

/// <summary>Reads and adjusts built-in display panels through the Windows WMI monitor provider.</summary>
internal static class InternalPanelBrightnessService
{
    private const string NamespacePath = @"root\wmi";
    private const string BrightnessClass = "WmiMonitorBrightness";
    private const string BrightnessMethodsClass = "WmiMonitorBrightnessMethods";
    private const string InternalIdPrefix = "internal:";

    internal static IReadOnlyList<InternalPanelBrightness> Discover()
    {
        try
        {
            using var management = new WindowsManagement(NamespacePath);
            var results = management.Query($"SELECT InstanceName, CurrentBrightness, Level FROM {BrightnessClass} WHERE Active = TRUE",
                "InstanceName", "CurrentBrightness", "Level");
            var panels = new List<InternalPanelBrightness>();
            foreach (var item in results)
            {
                var instanceName = item["InstanceName"] as string;
                if (string.IsNullOrWhiteSpace(instanceName) || item["CurrentBrightness"] is null) continue;
                var current = Convert.ToByte(item["CurrentBrightness"], System.Globalization.CultureInfo.InvariantCulture);
                var levels = item["Level"] is Array values
                    ? values.Cast<object>().Select(value => Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture))
                        .Distinct().Order().ToArray()
                    : Array.Empty<byte>();
                if (levels.Length == 0) continue;
                panels.Add(new InternalPanelBrightness(InternalIdPrefix + instanceName,
                    $"Dahili ekran · {FormatDisplayName(instanceName)}", current, levels));
            }
            return panels;
        }
        catch (COMException) { return Array.Empty<InternalPanelBrightness>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<InternalPanelBrightness>(); }
        catch (PlatformNotSupportedException) { return Array.Empty<InternalPanelBrightness>(); }
    }

    internal static bool IsInternalId(string id) => id.StartsWith(InternalIdPrefix, StringComparison.OrdinalIgnoreCase);

    internal static byte MapPercentToLevel(int percent, IReadOnlyList<byte> levels)
    {
        if (levels.Count == 0) return 0;
        var minimum = levels.Min();
        var maximum = levels.Max();
        var requested = minimum + (maximum - minimum) * Math.Clamp(percent, 0, 100) / 100d;
        return levels.MinBy(level => Math.Abs(level - requested));
    }

    internal static int MapLevelToPercent(byte level, IReadOnlyList<byte> levels)
    {
        if (levels.Count == 0) return 0;
        var minimum = levels.Min();
        var maximum = levels.Max();
        if (maximum == minimum) return 0;
        return (int)Math.Round((Math.Clamp(level, minimum, maximum) - minimum) * 100d / (maximum - minimum));
    }

    internal static bool SetBrightness(InternalPanelBrightness panel, int percent, out int appliedPercent)
    {
        appliedPercent = 0;
        if (!IsInternalId(panel.Id) || panel.Levels.Count == 0) return false;
        var instanceName = panel.Id[InternalIdPrefix.Length..];
        try
        {
            var brightness = MapPercentToLevel(percent, panel.Levels);
            using var management = new WindowsManagement(NamespacePath);
            var results = management.Query($"SELECT InstanceName, __PATH FROM {BrightnessMethodsClass} WHERE Active = TRUE",
                "InstanceName", "__PATH");
            foreach (var item in results)
            {
                if (!string.Equals(item["InstanceName"] as string, instanceName, StringComparison.OrdinalIgnoreCase) ||
                    item["__PATH"] is not string path) continue;
                var result = management.Invoke(path, BrightnessMethodsClass, "WmiSetBrightness",
                    new Dictionary<string, object> { ["Timeout"] = 1u, ["Brightness"] = brightness });
                if (result != 0) return false;
                appliedPercent = MapLevelToPercent(brightness, panel.Levels);
                return true;
            }
        }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        catch (PlatformNotSupportedException) { }
        return false;
    }

    private static string FormatDisplayName(string instanceName) =>
        instanceName.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Windows paneli";
}
