using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WinForge.App;

public static class Appearance
{
    public sealed record Accent(string Name, string Hex);

    public sealed record Tone(
        string Name,
        string Bg,
        string Card,
        string Border,
        string Hover,
        string Text,
        string Muted,
        string Panel,
        string Input,
        string Selected);

    public static IReadOnlyList<Accent> Accents { get; } = new[]
    {
        new Accent("Mavi", "#75A7FF"),
        new Accent("Mor", "#8B8CFF"),
        new Accent("Yeşil", "#4CD97B"),
        new Accent("Turuncu", "#FFA94C"),
        new Accent("Pembe", "#FF6BD6"),
        new Accent("Kırmızı", "#FF6B6B")
    };

    public static IReadOnlyList<Tone> Tones { get; } = new[]
    {
        new Tone("Varsayılan", "#141518", "#1D1F23", "#34363D", "#26282E",
                 "#F2F3F5", "#AAADB7", "#24262B", "#191A1E", "#242C3B"),
        new Tone("Gece", "#050608", "#0C0F14", "#1A1F28", "#12161D",
                 "#E8EDF5", "#7E8797", "#0A0D12", "#070A0E", "#10141B"),
        new Tone("Grafit", "#101318", "#1A202B", "#2B3546", "#222B39",
                 "#F0F4FA", "#A4ADBD", "#161C26", "#11161F", "#1C2530"),
        new Tone("Açık", "#F5F6F8", "#FFFFFF", "#D7DAE1", "#F0F2F6",
                 "#20242C", "#596272", "#EEF0F4", "#FFFFFF", "#EDF2FC")
    };

    public static string AccentName { get; private set; } = "Mavi";
    public static string ToneName { get; private set; } = "Varsayılan";
    public static bool Glow { get; private set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinForge", "appearance.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;

            if (root.TryGetProperty("accent", out var accent) &&
                accent.GetString() is { } accentName &&
                Accents.Any(a => a.Name == accentName))
                AccentName = accentName;

            if (root.TryGetProperty("tone", out var tone) &&
                tone.GetString() is { } toneName &&
                Tones.Any(t => t.Name == toneName))
                ToneName = toneName;

            if (root.TryGetProperty("glow", out var glow) &&
                (glow.ValueKind == JsonValueKind.True || glow.ValueKind == JsonValueKind.False))
                Glow = glow.GetBoolean();
        }
        catch
        {
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new
            {
                accent = AccentName,
                tone = ToneName,
                glow = Glow
            }));
        }
        catch
        {
        }
    }

    public static void SetAccent(string name)
    {
        if (!Accents.Any(a => a.Name == name)) return;
        AccentName = name;
        Save();
    }

    public static void SetTone(string name)
    {
        if (!Tones.Any(t => t.Name == name)) return;
        ToneName = name;
        Save();
    }

    public static void SetGlow(bool enabled)
    {
        Glow = enabled;
        Save();
    }

    public static void Reset()
    {
        AccentName = "Mavi";
        ToneName = "Varsayılan";
        Glow = false;
        Save();
    }

    public static void Apply()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var tone = Tones.FirstOrDefault(t => t.Name == ToneName) ?? Tones[0];
        var accentHex = Accents.FirstOrDefault(a => a.Name == AccentName)?.Hex ?? Accents[0].Hex;
        var accent = Parse(accentHex);
        if (ToneName == "Açık") accent = Blend(accent, Colors.Black, 0.58);
        var card = Parse(tone.Card);

        SetColor("BgBrush", Parse(tone.Bg));
        SetColor("CardBrush", card);
        SetColor("BorderBrush", Parse(tone.Border));
        SetColor("HoverBrush", Parse(tone.Hover));
        SetColor("TextBrush", Parse(tone.Text));
        SetColor("MutedBrush", Parse(tone.Muted));
        SetColor("PanelBrush", Parse(tone.Panel));
        SetColor("InputBrush", Parse(tone.Input));
        SetColor("SelectedCardBrush", Parse(tone.Selected));
        SetColor("AccentBrush", accent);
        SetColor("AccentSoftBrush", Blend(accent, card, 0.16));
        SetColor("WarnBrush", ToneName == "Açık" ? Parse("#805000") : Parse("#FFC857"));
        SetColor("DangerBrush", ToneName == "Açık" ? Parse("#B42332") : Parse("#FF6B6B"));

        var border = Parse(tone.Border);
        var text = Parse(tone.Text);
        var input = Parse(tone.Input);
        SetColor("SwitchTrackBrush", Blend(text, border, 0.045));
        SetColor("SwitchTrackBorderBrush", Blend(text, border, 0.09));
        SetColor("SwitchThumbBrush", Blend(text, border, 0.9));
        SetColor("SwitchTrackOnBorderBrush", Blend(accent, Colors.White, 0.45));
        SetColor("SwitchThumbOnBrush", input);
        SetColor("PillBrush", Blend(card, Parse(tone.Bg), 0.5));
        SetColor("RingBrush", Blend(text, border, 0.06));
        SetColor("RingHoverBrush", Blend(text, border, 0.2));
        SetColor("SliderTrackBrush", Blend(text, border, 0.06));
        SetColor("SliderThumbRingBrush", input);
        SetColor("ScrollThumbBrush", Blend(text, border, 0.22));
    }

    public static Color AccentColor =>
        Parse(Accents.FirstOrDefault(a => a.Name == AccentName)?.Hex ?? Accents[0].Hex);

    private static void SetColor(string key, Color color)
    {
        if (Application.Current is { } app)
            app.Resources[key] = new SolidColorBrush(color);
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color Blend(Color accent, Color background, double amount) => Color.FromRgb(
        (byte)(accent.R * amount + background.R * (1 - amount)),
        (byte)(accent.G * amount + background.G * (1 - amount)),
        (byte)(accent.B * amount + background.B * (1 - amount)));
}
