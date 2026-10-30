using System.Text.Json;
using System.Windows;

namespace WinForge.ClipboardLine;

/// <summary>Restricted, data-only representation of editable image marks.</summary>
internal sealed record AnnotationState
{
    public string Kind { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Opacity { get; init; } = 1;
    public string Stroke { get; init; } = "#FF000000";
    public string Fill { get; init; } = "#00000000";
    public double StrokeWidth { get; init; } = 1;
    public Point[] Points { get; init; } = [];
    public string Text { get; init; } = "";
    public string TextColor { get; init; } = "#FF000000";
    public double FontSize { get; init; } = 24;
    public double CornerRadius { get; init; }
    public double Padding { get; init; }
    public byte[] Image { get; init; } = [];

    internal static AnnotationState[] Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        if (json.Length > 30_000_000) throw new InvalidDataException("İşaretleme verisi çok büyük.");
        var marks = JsonSerializer.Deserialize<AnnotationState[]>(json) ?? throw new InvalidDataException("İşaretleme verisi geçersiz.");
        if (marks.Length > 500) throw new InvalidDataException("Çok fazla işaretleme var.");
        foreach (var mark in marks)
        {
            if (mark is null || mark.Kind is not ("polyline" or "rectangle" or "ellipse" or "text" or "image") ||
                !Finite(mark.X, mark.Y, mark.Width, mark.Height, mark.Opacity, mark.StrokeWidth, mark.FontSize, mark.CornerRadius, mark.Padding) ||
                mark.Width is < 0 or > 100_000 || mark.Height is < 0 or > 100_000 || mark.Opacity is < 0 or > 1 || mark.StrokeWidth is < 0 or > 500 ||
                mark.FontSize is < 1 or > 500 || mark.Points is null || mark.Points.Length > 100_000 || mark.Points.Any(point => !Finite(point.X, point.Y)) ||
                mark.Text is null || mark.Text.Length > 4096 || mark.Image is null || mark.Image.Length > 16_000_000 ||
                mark.Stroke is null || mark.Fill is null || mark.TextColor is null || mark.Stroke.Length > 32 || mark.Fill.Length > 32 || mark.TextColor.Length > 32)
                throw new InvalidDataException("İşaretleme verisi geçersiz.");
        }
        return marks;
    }
    internal static string Serialize(IEnumerable<AnnotationState> marks) => JsonSerializer.Serialize(marks);
    internal static string Translate(string json, double x, double y) => Serialize(Parse(json).Select(mark => mark with { X = mark.X - x, Y = mark.Y - y }));
    private static bool Finite(params double[] values) => values.All(value => double.IsFinite(value) && Math.Abs(value) <= 1_000_000);
}
