using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinForge.Modules.Capture;

/// <summary>Marks use physical desktop pixels so overlays and exported crops agree at any DPI.</summary>
internal sealed class CaptureAnnotations
{
    private sealed record Mark(DrawingTool Tool, Color Color, double Width, List<Point> Points);
    private readonly List<Mark> marks = [];
    private Mark? active;
    public DrawingTool Tool { get; set; } = DrawingTool.Select;
    public Color Color { get; set; } = Colors.Crimson;
    public double Width { get; set; } = 4;
    public bool IsDrawing => active is not null;
    public bool HasMarks => marks.Count > 0 || active is not null;

    public void Begin(Point point) => active = new(Tool, Color, Width, [point]);
    public void Move(Point point)
    {
        if (active is null) return;
        if (active.Tool is DrawingTool.Freehand or DrawingTool.Highlight) active.Points.Add(point);
        else if (active.Points.Count == 1) active.Points.Add(point);
        else active.Points[1] = point;
    }
    public void Complete() { if (active is not null) marks.Add(active); active = null; }
    public void Cancel() => active = null;
    public void Undo() { Cancel(); if (marks.Count > 0) marks.RemoveAt(marks.Count - 1); }
    public void Clear() { active = null; marks.Clear(); }

    public DrawingImage Preview(CaptureArea area)
    {
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, area.Width, area.Height));
            Draw(drawing, area);
        }
        return new DrawingImage(group);
    }

    public BitmapSource Apply(BitmapSource image, CaptureArea area)
    {
        if (!HasMarks) return image;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            Draw(drawing, area);
        }
        var result = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }

    private void Draw(DrawingContext drawing, CaptureArea area)
    {
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, area.Width, area.Height)));
        drawing.PushTransform(new TranslateTransform(-area.X, -area.Y));
        foreach (var mark in marks.Concat(active is null ? [] : new[] { active }))
        {
            var brush = new SolidColorBrush(mark.Color);
            var pen = new Pen(brush, mark.Tool == DrawingTool.Highlight ? mark.Width * 5 : mark.Width)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var start = mark.Points[0]; var end = mark.Points[^1];
            if (mark.Tool == DrawingTool.Highlight) drawing.PushOpacity(.35);
            if (mark.Tool is DrawingTool.Freehand or DrawingTool.Highlight)
            {
                if (mark.Points.Count == 1) drawing.DrawEllipse(brush, null, start, pen.Thickness / 2, pen.Thickness / 2);
                else
                {
                    var path = new StreamGeometry();
                    using (var geometry = path.Open()) { geometry.BeginFigure(start, false, false); geometry.PolyLineTo(mark.Points.Skip(1).ToArray(), true, false); }
                    drawing.DrawGeometry(null, pen, path);
                }
            }
            else if (mark.Tool is DrawingTool.Rectangle or DrawingTool.Ellipse)
            {
                var rect = new Rect(start, end);
                if (mark.Tool == DrawingTool.Rectangle) drawing.DrawRectangle(null, pen, rect);
                else drawing.DrawEllipse(null, pen, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), rect.Width / 2, rect.Height / 2);
            }
            else if (mark.Tool == DrawingTool.Arrow)
            {
                drawing.DrawLine(pen, start, end);
                var direction = end - start;
                if (direction.Length > 1)
                {
                    var length = Math.Min(direction.Length / 2, Math.Max(12, mark.Width * 4));
                    direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
                    drawing.DrawLine(pen, end, end - direction * length + normal * length * .5);
                    drawing.DrawLine(pen, end, end - direction * length - normal * length * .5);
                }
            }
            if (mark.Tool == DrawingTool.Highlight) drawing.Pop();
        }
        drawing.Pop(); drawing.Pop();
    }
}
