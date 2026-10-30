using System.Windows.Input;
using WinForge.Core;

namespace WinForge.Modules.Capture;

internal static class ModuleShortcut
{
    public static (uint Modifiers, uint Key) Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Kısayol boş olamaz.");
        var key = (Key)new KeyConverter().ConvertFromString(parts[^1])!;
        uint modifiers = 0;
        foreach (var modifier in parts[..^1]) modifiers |= modifier.ToLowerInvariant() switch
        {
            "shift" => HotkeyModifiers.Shift,
            "ctrl" or "control" => HotkeyModifiers.Control,
            "alt" => HotkeyModifiers.Alt,
            "win" => HotkeyModifiers.Win,
            _ => throw new ArgumentException("Geçersiz kısayol.")
        };
        if (key is Key.None or Key.System) throw new ArgumentException("Geçersiz kısayol tuşu.");
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
}
