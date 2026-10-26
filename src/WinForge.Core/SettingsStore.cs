using System.Globalization;
using System.Text.Json;

namespace WinForge.Core;

public sealed class SettingsStore
{
    private sealed class Persisted
    {
        public List<string> Enabled { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new();
    }

    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }
    public HashSet<string> Enabled { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    public Dictionary<string, string> For(string moduleId)
    {
        lock (_gate)
        {
            if (!Values.TryGetValue(moduleId, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Values[moduleId] = values;
            }

            return values;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            var data = JsonSerializer.Deserialize<Persisted>(File.ReadAllText(FilePath), JsonOptions);
            if (data is null) return;

            foreach (var id in data.Enabled ?? [])
                if (!string.IsNullOrWhiteSpace(id)) Enabled.Add(id);
            foreach (var (id, values) in data.Values ?? [])
            {
                if (values is null) continue;
                var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in values) target[key] = value ?? "";
                Values[id] = target;
            }

            MigrateWindowControl();
        }
        catch
        {
            // Corrupt settings must never prevent the app from starting.
        }
    }

    private void MigrateWindowControl()
    {
        const string legacyModule = "altsnap";
        const string currentModule = "window-control";
        if (Enabled.Remove(legacyModule)) Enabled.Add(currentModule);
        if (Values.Remove(legacyModule, out var legacy))
        {
            var current = For(currentModule);
            foreach (var (key, value) in legacy)
                if (!key.StartsWith("__native-", StringComparison.OrdinalIgnoreCase)) current.TryAdd(key, value);
        }
        Enabled.Remove("windhawk-taskbar");
        Values.Remove("windhawk-taskbar");
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var data = new Persisted
                {
                    Enabled = Enabled.ToList(),
                    Values = Values.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value))
                };

                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
                File.Move(tmp, FilePath, true);
            }
            catch
            {
                // Settings are best effort; never crash over them.
            }
        }
    }
}

internal sealed class ModuleSettings : IModuleSettings
{
    private readonly Dictionary<string, string> _values;
    private readonly Action _save;

    public ModuleSettings(Dictionary<string, string> values, Action save)
    {
        _values = values;
        _save = save;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        if (!_values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (bool.TryParse(raw, out var parsed)) return parsed;
        if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "0" || raw.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (!_values.TryGetValue(key, out var raw)) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    public string GetString(string key, string fallback = "")
    {
        return _values.TryGetValue(key, out var raw) ? raw : fallback;
    }

    public void Set(string key, object? value)
    {
        _values[key] = value switch
        {
            null => "",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        _save();
    }
}
