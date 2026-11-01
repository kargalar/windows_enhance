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
        new Accent("Yeşil", "#4CD97B"),
        new Accent("Kırmızı", "#FF6B6B")
    };

    public static IReadOnlyList<Tone> Tones { get; } = new[]
    {
        new Tone("Siyah", "#090A0C", "#101114", "#292A30", "#1D1E24",
                 "#F5F5F5", "#A0A1AA", "#191A1F", "#191A1F", "#202128"),
        new Tone("Grafit", "#15161A", "#1B1C21", "#35363E", "#2B2C33",
                 "#F5F5F5", "#A5A5AE", "#25262D", "#25262D", "#2B2C33")
    };

    public static string AccentName { get; private set; } = "Mavi";
    public static string ToneName { get; private set; } = "Siyah";
    public static bool Glow { get; private set; }
    public static double DetailWidth { get; private set; } = 460;

    private static string FilePath => Path.Combine(WinForge.Core.AppData.DirectoryPath, "appearance.json");

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
                ToneName = toneName == "Grafit" ? "Grafit" : "Siyah";

            if (root.TryGetProperty("glow", out var glow) &&
                (glow.ValueKind == JsonValueKind.True || glow.ValueKind == JsonValueKind.False))
                Glow = glow.GetBoolean();

            if (root.TryGetProperty("detailWidth", out var detailWidth) &&
                detailWidth.TryGetDouble(out var dw) &&
                dw >= 280 && dw <= 900)
                DetailWidth = dw;
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
                glow = Glow,
                detailWidth = Math.Round(DetailWidth, 1)
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

    public static void SetDetailWidth(double width)
    {
        if (width < 280 || width > 900) return;
        if (Math.Abs(DetailWidth - width) < 1.0) return;
        DetailWidth = width;
        Save();
    }

    public static void Reset()
    {
        AccentName = "Mavi";
        ToneName = "Siyah";
        Glow = false;
        DetailWidth = 460;
        Save();
    }

    public static void Apply()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var tone = Tones.FirstOrDefault(t => t.Name == ToneName) ?? Tones[0];
        var accentHex = Accents.FirstOrDefault(a => a.Name == AccentName)?.Hex ?? Accents[0].Hex;
        var accent = Parse(accentHex);
        var card = Parse(tone.Card);

        SetColor("BgBrush", Parse(tone.Bg));
        SetColor("CardBrush", card);
        SetColor("BorderBrush", Parse(tone.Border));
        SetColor("HoverBrush", Parse(tone.Hover));
        SetColor("TextBrush", Parse(tone.Text));
        SetColor("MutedBrush", Parse(tone.Muted));
        SetColor("PanelBrush", Parse(tone.Panel));
        SetColor("InputBrush", Parse(tone.Input));
        SetColor("InputTextBrush", Parse(tone.Text));
        SetColor("SelectedCardBrush", Parse(tone.Selected));
        SetColor("AccentBrush", accent);
        SetColor("AccentSoftBrush", Blend(accent, card, 0.16));
        SetColor("WarnBrush", Parse("#F2C66D"));
        SetColor("DangerBrush", Parse("#FF7373"));

        var border = Parse(tone.Border);
        var text = Parse(tone.Text);
        var input = Parse(tone.Input);
        SetColor("SwitchTrackBrush", Blend(text, border, 0.045));
        SetColor("SwitchTrackBorderBrush", Blend(text, border, 0.09));
        SetColor("SwitchThumbBrush", Blend(text, border, 0.9));
        SetColor("SwitchTrackOnBorderBrush", Blend(accent, Colors.White, 0.45));
        SetColor("SwitchThumbOnBrush", Parse(tone.Bg));
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
