using System.Text;

namespace WinForge.Modules.AltSnap;

// Preserve upstream comments, unknown options, custom shortcuts and zone layouts.
internal sealed class AltSnapConfiguration
{
    private readonly List<string> _lines;

    internal AltSnapConfiguration(string text) => _lines = text.Replace("\r\n", "\n").Split('\n').ToList();

    internal string Get(string section, string key, string fallback = "")
    {
        var index = Find(section, key);
        return index < 0 ? fallback : _lines[index][(_lines[index].IndexOf('=') + 1)..].Trim();
    }

    internal void Set(string section, string key, string value)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("AltSnap ayarı birden fazla satır içeremez.");
        var index = Find(section, key);
        if (index >= 0)
        {
            _lines[index] = key + "=" + value;
            return;
        }

        var header = _lines.FindIndex(line => IsSection(line, section));
        if (header < 0)
        {
            _lines.Add("[" + section + "]");
            _lines.Add(key + "=" + value);
        }
        else
        {
            _lines.Insert(header + 1, key + "=" + value);
        }
    }

    internal void Save(string path)
    {
        var temporary = path + ".tmp";
        // Windows profile APIs require a BOM for non-ANSI text (Turkish titles).
        File.WriteAllText(temporary, string.Join("\r\n", _lines), Encoding.Unicode);
        File.Move(temporary, path, overwrite: true);
    }

    private int Find(string section, string key)
    {
        var inside = false;
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index].Trim();
            if (line.StartsWith('[')) inside = IsSection(line, section);
            else if (inside && !line.StartsWith(';') && !line.StartsWith('#'))
            {
                var separator = line.IndexOf('=');
                if (separator >= 0 && line[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
        }
        return -1;
    }

    private static bool IsSection(string line, string section) =>
        line.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
}
