using System.Windows.Input;
using WinForge.FileTools;

namespace WinForge.ClipboardLine;

internal static class ClipboardKeymap
{
    internal static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["onizle"] = "Space", ["kopyala"] = "Enter", ["sabitle"] = "P", ["klasore-sabitle"] = "Shift+P", ["adlandir-sabitle"] = "Alt+P",
        ["raftan-kaldir"] = "S", ["ara"] = "Shift+S", ["geri-al"] = "Back", ["kutuphane"] = "L", ["cope-tasi"] = "Delete",
        ["tumunu-sec"] = "Ctrl+A", ["kopyala-acik"] = "Ctrl+C", ["eylemler"] = "Ctrl+K", ["arama"] = "Ctrl+F", ["duzenlemeyi-geri-al"] = "Ctrl+Z",
        ["yinele"] = "Ctrl+Y", ["kaydi-kirp"] = "M", ["sonraki-raf"] = "Tab", ["onceki-raf"] = "Shift+Tab",
        ["asagi-raf"] = "Down", ["yukari-raf"] = "Up", ["onceki-sayfa"] = "Alt+Left", ["sonraki-sayfa"] = "Alt+Right",
        ["ilk-oge"] = "Ctrl+Left", ["son-oge"] = "Ctrl+Right", ["sabit-filtre"] = "Ctrl+Shift+F",
        ["tur-tumu"] = "Ctrl+0", ["tur-gorsel"] = "Ctrl+1", ["tur-metin"] = "Ctrl+2", ["tur-baglanti"] = "Ctrl+3",
        ["raf-gorsel"] = "Shift+I", ["raf-metin"] = "Shift+T", ["raf-baglanti"] = "Shift+L",
        ["yapistir"] = "Ctrl+Enter", ["disa-aktar"] = "Ctrl+Shift+E", ["etiket-ekle"] = "Ctrl+Shift+T", ["baglanti-ac"] = "Ctrl+O", ["ocr-kopyala"] = "Ctrl+Shift+C"
    };

    internal static Key ParseWebKey(string key) => key switch
    {
        " " => Key.Space,
        "ArrowLeft" => Key.Left,
        "ArrowRight" => Key.Right,
        "ArrowUp" => Key.Up,
        "ArrowDown" => Key.Down,
        "Backspace" => Key.Back,
        "PageUp" => Key.PageUp,
        "PageDown" => Key.PageDown,
        "Home" => Key.Home,
        "End" => Key.End,
        _ when key.Length == 1 && char.IsDigit(key[0]) => Enum.Parse<Key>("D" + key),
        _ => Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.None
    };

    internal static IReadOnlyDictionary<string, string> ParseOverrides(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith('#')) continue;
            var pieces = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || !Defaults.ContainsKey(pieces[0]) || !result.TryAdd(pieces[0], pieces[1])) throw new ArgumentException("Her kısayolu eylem=tuş biçiminde bir kez yazın.");
        }
        Validate(result); return result;
    }

    internal static void Validate(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count > Defaults.Count || overrides.Keys.Any(key => !Defaults.ContainsKey(key))) throw new ArgumentException("Pano kısayolları geçersiz.");
        var used = new HashSet<(uint, uint)>();
        foreach (var entry in Defaults)
        {
            var value = overrides.GetValueOrDefault(entry.Key, entry.Value);
            if (value is null || value.Length > 80) throw new ArgumentException("Pano kısayolu geçersiz.");
            if (value.Length == 0) continue;
            var gesture = FeatureShortcut.Parse(value, requireModifier: false);
            if (!used.Add(gesture)) throw new ArgumentException($"{value} kısayolu birden fazla eyleme atanamaz.");
        }
    }

    internal static string? Match(KeyEventArgs args, IReadOnlyDictionary<string, string> overrides)
    {
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        return Match(key, Keyboard.Modifiers, overrides);
    }

    internal static string? Match(Key key, ModifierKeys keyboardModifiers, IReadOnlyDictionary<string, string> overrides)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var modifiers = (uint)keyboardModifiers;
        foreach (var entry in Defaults)
        {
            var value = overrides.GetValueOrDefault(entry.Key, entry.Value);
            if (value.Length > 0 && FeatureShortcut.Parse(value, requireModifier: false) == (modifiers, virtualKey)) return entry.Key;
        }
        bool UsesDefault(string action) => string.Equals(overrides.GetValueOrDefault(action, Defaults[action]), Defaults[action], StringComparison.OrdinalIgnoreCase);
        if (key == Key.P && keyboardModifiers == ModifierKeys.Control && UsesDefault("sabitle")) return "sabitle";
        if (key == Key.F && keyboardModifiers == ModifierKeys.None && UsesDefault("sabit-filtre")) return "sabit-filtre";
        return null;
    }
}
