using System.Text.Json;

namespace WinForge.History;

public static class LegacySettings
{
    public static IReadOnlyDictionary<string, object?> Read(ImportSource source)
    {
        var file = Path.Combine(Path.GetDirectoryName(source.Database)!, source.Kind == "recall" ? "config.json" : "settings.json");
        var result = new Dictionary<string, object?>(); if (!File.Exists(file)) return result;
        using var document = JsonDocument.Parse(File.ReadAllText(file)); var root = document.RootElement;
        if (source.Kind == "recall")
        {
            if (root.TryGetProperty("screenshot_interval", out var interval) && interval.TryGetInt32(out var seconds) && seconds is >= 1 and <= 3600) result["interval"] = seconds;
            return result;
        }
        foreach (var (oldKey, key) in new[] { ("Language", "language"), ("Hotkey", "hotkey"), ("MicrophoneId", "microphone") })
            if (root.TryGetProperty(oldKey, out var value) && value.ValueKind == JsonValueKind.String) result[key] = value.GetString();
        if (root.TryGetProperty("SelectedModel", out var model) && model.TryGetInt32(out var selected) && selected is 0 or 1) result["model"] = selected;
        if (root.TryGetProperty("OutputMode", out var output) && output.TryGetInt32(out var mode)) result["output"] = mode == 1;
        if (root.TryGetProperty("ComputeMode", out var compute) && compute.TryGetInt32(out var engine)) result["cpu"] = engine == 1;
        return result;
    }
}
