using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinForge.ClipboardLine;

namespace WinForge.FileTools.UI;

public sealed partial class ToolEditor
{
    internal string SaveAnnotations()
    {
        var marks = new List<AnnotationState>();
        foreach (var element in drawing.Children.Cast<FrameworkElement>().Skip(1))
        {
            var x = Canvas.GetLeft(element); var y = Canvas.GetTop(element);
            var transform = element.RenderTransform as TranslateTransform;
            var state = new AnnotationState
            {
                X = (double.IsFinite(x) ? x : 0) + (transform?.X ?? 0), Y = (double.IsFinite(y) ? y : 0) + (transform?.Y ?? 0),
                Width = double.IsFinite(element.Width) ? element.Width : 0, Height = double.IsFinite(element.Height) ? element.Height : 0, Opacity = element.Opacity
            };
            state = element switch
            {
                Polyline line => state with { Kind = "polyline", Stroke = BrushColor(line.Stroke), StrokeWidth = line.StrokeThickness, Points = line.Points.ToArray() },
                Rectangle rectangle => state with { Kind = "rectangle", Stroke = BrushColor(rectangle.Stroke), Fill = BrushColor(rectangle.Fill), StrokeWidth = rectangle.StrokeThickness },
                Ellipse ellipse => state with { Kind = "ellipse", Stroke = BrushColor(ellipse.Stroke), Fill = BrushColor(ellipse.Fill), StrokeWidth = ellipse.StrokeThickness },
                Border { Child: TextBlock text } border => state with { Kind = "text", Fill = BrushColor(border.Background), Text = text.Text, TextColor = BrushColor(text.Foreground), FontSize = text.FontSize, CornerRadius = border.CornerRadius.TopLeft, Padding = border.Padding.Left },
                Image { Source: BitmapSource image } => state with { Kind = "image", Image = ImageEngine.PngBytes(image) },
                _ => throw new InvalidDataException("İşaretleme türü kaydedilemiyor.")
            };
            marks.Add(state);
        }
        var json = AnnotationState.Serialize(marks);
        _ = AnnotationState.Parse(json);
        return json;
    }

    internal void LoadAnnotations(string json)
    {
        foreach (var mark in AnnotationState.Parse(json))
        {
            FrameworkElement element = mark.Kind switch
            {
                "polyline" => new Polyline { Stroke = Brush(mark.Stroke), StrokeThickness = mark.StrokeWidth, StrokeLineJoin = PenLineJoin.Round, Points = new PointCollection(mark.Points) },
                "rectangle" => new Rectangle { Stroke = Brush(mark.Stroke), Fill = Brush(mark.Fill), StrokeThickness = mark.StrokeWidth },
                "ellipse" => new Ellipse { Stroke = Brush(mark.Stroke), Fill = Brush(mark.Fill), StrokeThickness = mark.StrokeWidth },
                "text" => new Border { Background = Brush(mark.Fill), CornerRadius = new CornerRadius(mark.CornerRadius), Padding = new Thickness(mark.Padding), Child = new TextBlock { Text = mark.Text, Foreground = Brush(mark.TextColor), FontSize = mark.FontSize, TextWrapping = TextWrapping.Wrap } },
                "image" => new Image { Source = DecodeImage(mark.Image), Stretch = Stretch.Fill },
                _ => throw new InvalidDataException("İşaretleme türü geçersiz.")
            };
            element.IsHitTestVisible = false; element.Opacity = mark.Opacity;
            if (mark.Kind != "polyline") { element.Width = mark.Width; element.Height = mark.Height; }
            Canvas.SetLeft(element, mark.X); Canvas.SetTop(element, mark.Y); drawing.Children.Add(element);
        }
    }

    private static string BrushColor(Brush? brush) => (brush as SolidColorBrush)?.Color.ToString() ?? "#00000000";
    private static Brush Brush(string value) => ImageEngine.ColorBrush(value);
    private static BitmapSource DecodeImage(byte[] bytes)
    {
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("İşaretleme görseli geçersiz.");
        var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width == 0 || height == 0 || (ulong)width * height > 100_000_000) throw new InvalidDataException("İşaretleme görseli çok büyük.");
        return ImageEngine.FromPng(bytes);
    }
}
