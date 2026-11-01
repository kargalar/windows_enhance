using System.Windows.Input;
using WinForge.Core;

namespace WinForge.Modules.Extras;

internal sealed record AutomationShortcut(uint Modifiers, int Key);
internal sealed record AutomationStep(string Kind, string Value = "", int Delay = 0);
internal sealed record AutomationCommand(AutomationShortcut Shortcut, bool Repeat, int Interval, IReadOnlyList<AutomationStep> Steps);

internal static class AutomationCommands
{
    internal const string Example = "# Kısayol = toggle|aralık (ms)|adımlar veya once|aralık|adımlar\nF1 = toggle|100|click left\nAlt+S = once|100|search google\n# Ctrl+Alt+T = once|100|text Merhaba; wait 500; key Enter";

    internal static List<AutomationCommand> Parse(string text)
    {
        if (text.Length > 65536) throw new FormatException("Komutlar en fazla 65536 karakter olabilir.");
        var result = new List<AutomationCommand>();
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            try
            {
                var separator = line.IndexOf('=');
                if (separator < 1) throw new FormatException("Kısayol = toggle|100|click left biçimini kullanın.");
                var shortcut = ParseShortcut(line[..separator]);
                if (result.Any(command => command.Shortcut == shortcut)) throw new FormatException("Kısayol birden fazla tanımlanmış.");
                var parts = line[(separator + 1)..].Split('|', 3, StringSplitOptions.TrimEntries);
                if (parts.Length != 3 || parts[0] is not ("toggle" or "once")) throw new FormatException("Çalışma türü toggle veya once olmalı.");
                if (!int.TryParse(parts[1], out var interval) || interval is < 10 or > 3600000)
                    throw new FormatException("Aralık 10–3600000 ms arasında olmalı.");
                var steps = new List<AutomationStep>();
                foreach (var item in parts[2].Split(';', StringSplitOptions.TrimEntries))
                {
                    var pair = item.Split(' ', 2, StringSplitOptions.TrimEntries);
                    var kind = pair[0].ToLowerInvariant();
                    var value = pair.Length == 2 ? pair[1] : "";
                    switch (kind)
                    {
                        case "search" when value == "google":
                            steps.Add(new(kind, value)); break;
                        case "click" when value is "left" or "right" or "middle":
                            steps.Add(new(kind, value)); break;
                        case "wait" when int.TryParse(value, out var delay) && delay is >= 10 and <= 3600000:
                            steps.Add(new(kind, Delay: delay)); break;
                        case "key":
                            var target = ParseShortcut(value);
                            if (target.Modifiers != 0) throw new FormatException("key adımı tek bir tuş alır (ör. Enter).");
                            steps.Add(new(kind, target.Key.ToString(System.Globalization.CultureInfo.InvariantCulture))); break;
                        case "text" when value.Length is > 0 and <= 2048:
                            steps.Add(new(kind, value)); break;
                        default: throw new FormatException("Geçersiz adım. click left/right/middle, wait 500, key Enter, text Merhaba veya search google kullanın.");
                    }
                }
                if (steps.Count is < 1 or > 100) throw new FormatException("Bir komut 1–100 adım içermeli.");
                if (parts[0] == "toggle" && steps.Any(step => step.Kind == "search"))
                    throw new FormatException("Google araması once türünde çalıştırılmalıdır.");
                result.Add(new(shortcut, parts[0] == "toggle", interval, steps));
                if (result.Count > 64) throw new FormatException("En fazla 64 komut tanımlanabilir.");
            }
            catch (FormatException ex) { throw new FormatException($"{lineNumber}. satır: {ex.Message}"); }
        }
        // Synthetic key presses must never trigger another automation (including themselves).
        if (result.Any(command => command.Steps.Any(step => step.Kind == "key" &&
                result.Any(other => other.Shortcut.Key == int.Parse(step.Value)))))
            throw new FormatException("key adımlarında tetikleyici olarak kullanılan bir tuş gönderilemez.");
        if (result.Any(command => command.Steps.Any(step => step.Kind == "search")) &&
            result.Any(command => command.Shortcut == new AutomationShortcut(HotkeyModifiers.Control, 0x43)))
            throw new FormatException("Google araması kopyalama için Ctrl+C kullanır; bu kısayolu tetikleyici olarak atamayın.");
        return result;
    }

    internal static AutomationShortcut ParseShortcut(string value)
    {
        // '+' is also a key: keep it distinct from the chord separator.
        value = value.Trim();
        if (value == "+" || value.EndsWith("++", StringComparison.Ordinal)) value = value[..^1] + "OemPlus";
        var parts = value.Trim().Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" => HotkeyModifiers.Win,
                _ => throw new FormatException($"Bilinmeyen değiştirici: {part}")
            };
            if ((modifiers & flag) != 0) throw new FormatException("Değiştirici tekrarlanamaz.");
            modifiers |= flag;
        }
        var name = parts[^1];
        name = name.ToLowerInvariant() switch
        {
            "-" or "minus" => "OemMinus",
            "+" or "=" or "equal" => "OemPlus",
            "," or "comma" => "OemComma",
            "." or "period" => "OemPeriod",
            ";" or "semicolon" => "OemSemicolon",
            "/" or "slash" => "OemQuestion",
            "'" or "quote" => "OemQuotes",
            "[" or "bracketleft" => "OemOpenBrackets",
            "]" or "bracketright" => "OemCloseBrackets",
            "\\" or "backslash" => "OemPipe",
            "`" or "backquote" => "OemTilde",
            "mediatracknext" => "MediaNextTrack",
            "mediatrackprevious" => "MediaPreviousTrack",
            "audiovolumeup" => "VolumeUp",
            "audiovolumedown" => "VolumeDown",
            "audiovolumemute" => "VolumeMute",
            _ => name
        };
        if (name.Length == 1 && char.IsAsciiDigit(name[0])) name = "D" + name;
        if (!Enum.TryParse<Key>(name, true, out var key) || !Enum.IsDefined(key) ||
            key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
            throw new FormatException($"Bilinmeyen tuş: {name}");
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is < 8 or > 254 or 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5 || key == Key.F12)
            throw new FormatException("Bu tuş kısayol olarak kullanılamaz (F12 Windows tarafından ayrılmıştır).");
        if ((modifiers & HotkeyModifiers.Win) != 0 && key == Key.L ||
            (modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && key == Key.Delete)
            throw new FormatException("Windows tarafından ayrılmış kısayol kullanılamaz.");
        return new(modifiers, vk);
    }
}

/// <summary>Dispatcher-owned execution state; no sleeping threads or outstanding input on cancellation.</summary>
internal sealed class AutomationRun(AutomationCommand command)
{
    private int _index;
    private long _due;
    internal bool Finished { get; private set; }

    internal void Advance(long now, bool blocked, Action<AutomationStep> execute)
    {
        if (Finished || blocked || now < _due) return;
        var step = command.Steps[_index++];
        if (step.Kind != "wait") execute(step);
        _due = now + (step.Kind == "wait" ? step.Delay : 0);
        if (_index != command.Steps.Count) return;
        if (!command.Repeat) { Finished = true; return; }
        _index = 0;
        _due += command.Interval;
    }
}
