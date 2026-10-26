using System.IO.Enumeration;
using WinForge.Core;

namespace WinForge.Modules.WindowControl;

[Flags]
internal enum ResizeEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

internal readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    internal int Right => X + Width;
    internal int Bottom => Y + Height;
    internal static WindowBounds From(RECT bounds) => new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
}

internal readonly record struct WindowResizeLimits(int MinimumWidth, int MinimumHeight, int MaximumWidth, int MaximumHeight);

internal static class WindowGeometry
{
    internal static WindowBounds Zoom(WindowBounds bounds, POINT point, int direction, int divisor, int minimumStep,
        int centerPercent, bool centerAxes, WindowResizeLimits limits)
    {
        divisor = Math.Max(1, divisor);
        var region = ResizeRegion(bounds, point, centerPercent, "Taşı");
        centerAxes |= region == ResizeEdges.None;
        var centerX = (region & (ResizeEdges.Left | ResizeEdges.Right)) == 0;
        var centerY = (region & (ResizeEdges.Top | ResizeEdges.Bottom)) == 0;
        var step = centerAxes && (centerX || centerY) ? 1 : Math.Max(1, minimumStep);
        var left = region.HasFlag(ResizeEdges.Right) ? Math.Max(step, bounds.Width / divisor) :
            centerX && centerAxes ? Math.Max(step, (point.X - bounds.X) / divisor) : 0;
        var right = region.HasFlag(ResizeEdges.Left) ? Math.Max(step, bounds.Width / divisor) :
            centerX && centerAxes ? Math.Max(step, (bounds.Right - point.X) / divisor) : 0;
        var top = region.HasFlag(ResizeEdges.Bottom) ? Math.Max(step, bounds.Height / divisor) :
            centerY && centerAxes ? Math.Max(step, (point.Y - bounds.Y) / divisor) : 0;
        var bottom = region.HasFlag(ResizeEdges.Top) ? Math.Max(step, bounds.Height / divisor) :
            centerY && centerAxes ? Math.Max(step, (bounds.Bottom - point.Y) / divisor) : 0;
        var sign = Math.Sign(direction);
        var requestedWidth = bounds.Width + sign * (left + right);
        var requestedHeight = bounds.Height + sign * (top + bottom);
        var width = Math.Clamp(requestedWidth, limits.MinimumWidth, Math.Max(limits.MinimumWidth, limits.MaximumWidth));
        var height = Math.Clamp(requestedHeight, limits.MinimumHeight, Math.Max(limits.MinimumHeight, limits.MaximumHeight));
        var x = left == 0 ? bounds.X : right == 0 ? bounds.Right - width : width == requestedWidth ? bounds.X - sign * left : bounds.X;
        var y = top == 0 ? bounds.Y : bottom == 0 ? bounds.Bottom - height : height == requestedHeight ? bounds.Y - sign * top : bounds.Y;
        return new(x, y, width, height);
    }

    internal static WindowBounds Drag(WindowBounds original, POINT start, POINT cursor, ResizeEdges edges, int minWidth, int minHeight,
        int maxWidth = int.MaxValue, int maxHeight = int.MaxValue)
    {
        var dx = cursor.X - start.X;
        var dy = cursor.Y - start.Y;
        if (edges == ResizeEdges.None) return original with { X = original.X + dx, Y = original.Y + dy };
        maxWidth = Math.Max(minWidth, maxWidth);
        maxHeight = Math.Max(minHeight, maxHeight);
        var symmetricX = edges.HasFlag(ResizeEdges.Left) && edges.HasFlag(ResizeEdges.Right);
        var symmetricY = edges.HasFlag(ResizeEdges.Top) && edges.HasFlag(ResizeEdges.Bottom);
        if (symmetricX || symmetricY)
        {
            var width = symmetricX ? Math.Clamp(original.Width + 2 * dx, minWidth, maxWidth) : original.Width;
            var height = symmetricY ? Math.Clamp(original.Height + 2 * dy, minHeight, maxHeight) : original.Height;
            return new(original.X + (original.Width - width) / 2, original.Y + (original.Height - height) / 2, width, height);
        }
        var left = edges.HasFlag(ResizeEdges.Left) ? original.Right - Math.Clamp(original.Width - dx, minWidth, maxWidth) : original.X;
        var top = edges.HasFlag(ResizeEdges.Top) ? original.Bottom - Math.Clamp(original.Height - dy, minHeight, maxHeight) : original.Y;
        var right = edges.HasFlag(ResizeEdges.Right) ? left + Math.Clamp(original.Width + dx, minWidth, maxWidth) : original.Right;
        var bottom = edges.HasFlag(ResizeEdges.Bottom) ? top + Math.Clamp(original.Height + dy, minHeight, maxHeight) : original.Bottom;
        return new(left, top, right - left, bottom - top);
    }

    internal static WindowBounds RestoreUnderPointer(WindowBounds maximized, WindowBounds normal, POINT cursor)
    {
        var fraction = Math.Clamp((cursor.X - maximized.X) / (double)Math.Max(1, maximized.Width), 0, 1);
        return normal with
        {
            X = cursor.X - (int)Math.Round(normal.Width * fraction),
            Y = cursor.Y - Math.Clamp(cursor.Y - maximized.Y, 0, Math.Min(32, normal.Height - 1))
        };
    }

    internal static ResizeEdges Edges(WindowBounds bounds, POINT cursor) =>
        (cursor.X < bounds.X + bounds.Width / 2 ? ResizeEdges.Left : ResizeEdges.Right) |
        (cursor.Y < bounds.Y + bounds.Height / 2 ? ResizeEdges.Top : ResizeEdges.Bottom);

    internal static ResizeEdges ResizeRegion(WindowBounds bounds, POINT cursor, int centerPercent, string centerMode)
    {
        var centerWidth = bounds.Width * Math.Clamp(centerPercent, 0, 100) / 100;
        var centerHeight = bounds.Height * Math.Clamp(centerPercent, 0, 100) / 100;
        var left = bounds.X + (bounds.Width - centerWidth) / 2;
        var top = bounds.Y + (bounds.Height - centerHeight) / 2;
        var edges = ResizeEdges.None;
        if (cursor.X < left) edges |= ResizeEdges.Left;
        else if (cursor.X >= left + centerWidth) edges |= ResizeEdges.Right;
        if (cursor.Y < top) edges |= ResizeEdges.Top;
        else if (cursor.Y >= top + centerHeight) edges |= ResizeEdges.Bottom;
        if (edges != ResizeEdges.None) return edges;
        return centerMode switch
        {
            "Taşı" => ResizeEdges.None,
            "Sağ alt köşe" => ResizeEdges.Right | ResizeEdges.Bottom,
            "En yakın kenar" => NearestEdge(bounds, cursor),
            _ => ResizeEdges.Left | ResizeEdges.Top | ResizeEdges.Right | ResizeEdges.Bottom
        };
    }

    internal static ResizeEdges NearestEdge(WindowBounds bounds, POINT cursor)
    {
        var distances = new[] { (Math.Abs(cursor.X - bounds.X), ResizeEdges.Left), (Math.Abs(cursor.Y - bounds.Y), ResizeEdges.Top),
            (Math.Abs(bounds.Right - cursor.X), ResizeEdges.Right), (Math.Abs(bounds.Bottom - cursor.Y), ResizeEdges.Bottom) };
        return distances.MinBy(pair => pair.Item1).Item2;
    }

    internal static WindowBounds SideTarget(WindowBounds work, POINT point, int horizontalPercent, int verticalPercent, int gap)
    {
        var edge = NearestEdge(work, point);
        var splitX = work.Width * Math.Clamp(horizontalPercent, 1, 99) / 100;
        var splitY = work.Height * Math.Clamp(verticalPercent, 1, 99) / 100;
        var target = edge switch
        {
            ResizeEdges.Left => work with { Width = splitX },
            ResizeEdges.Right => work with { X = work.X + splitX, Width = work.Width - splitX },
            ResizeEdges.Top => work with { Height = splitY },
            _ => work with { Y = work.Y + splitY, Height = work.Height - splitY }
        };
        return Inset(target, gap);
    }

    internal static WindowBounds Inset(WindowBounds bounds, int gap)
    {
        var inset = Math.Clamp(gap, -100, Math.Max(0, (Math.Min(bounds.Width, bounds.Height) - 1) / 2));
        return new(bounds.X + inset, bounds.Y + inset, bounds.Width - 2 * inset, bounds.Height - 2 * inset);
    }

    internal static WindowBounds Constrain(WindowBounds bounds, WindowBounds work) => bounds with
    {
        X = Math.Clamp(bounds.X, work.X, Math.Max(work.X, work.Right - bounds.Width)),
        Y = Math.Clamp(bounds.Y, work.Y, Math.Max(work.Y, work.Bottom - bounds.Height))
    };

    internal static WindowBounds DoubleResizeTarget(WindowBounds original, WindowBounds work, ResizeEdges edges,
        int horizontalPercent, int verticalPercent, int gap, bool extend)
    {
        if (edges == ResizeEdges.None)
            return extend ? original with { X = work.X, Width = work.Width } : original with { Y = work.Y, Height = work.Height };
        if (extend)
        {
            var left = edges.HasFlag(ResizeEdges.Left) ? work.X + gap : original.X;
            var top = edges.HasFlag(ResizeEdges.Top) ? work.Y + gap : original.Y;
            var right = edges.HasFlag(ResizeEdges.Right) ? work.Right - gap : original.Right;
            var bottom = edges.HasFlag(ResizeEdges.Bottom) ? work.Bottom - gap : original.Bottom;
            return new(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }
        var splitX = work.X + work.Width * Math.Clamp(horizontalPercent, 1, 99) / 100;
        var splitY = work.Y + work.Height * Math.Clamp(verticalPercent, 1, 99) / 100;
        var x = edges.HasFlag(ResizeEdges.Right) ? splitX : work.X;
        var y = edges.HasFlag(ResizeEdges.Bottom) ? splitY : work.Y;
        var width = edges.HasFlag(ResizeEdges.Left) ? splitX - work.X : edges.HasFlag(ResizeEdges.Right) ? work.Right - splitX : work.Width;
        var height = edges.HasFlag(ResizeEdges.Top) ? splitY - work.Y : edges.HasFlag(ResizeEdges.Bottom) ? work.Bottom - splitY : work.Height;
        return Inset(new(x, y, width, height), gap);
    }

    internal static WindowBounds? StickyNeighbor(WindowBounds original, WindowBounds resized, WindowBounds neighbor, int gap, int threshold)
    {
        var result = neighbor;
        if (original.Y < neighbor.Bottom && original.Bottom > neighbor.Y)
        {
            if (Math.Abs(neighbor.X - original.Right - gap) <= threshold)
                result = result with { X = resized.Right + gap, Width = neighbor.Right - resized.Right - gap };
            else if (Math.Abs(original.X - neighbor.Right - gap) <= threshold)
                result = result with { Width = resized.X - gap - neighbor.X };
        }
        if (original.X < neighbor.Right && original.Right > neighbor.X)
        {
            if (Math.Abs(neighbor.Y - original.Bottom - gap) <= threshold)
                result = result with { Y = resized.Bottom + gap, Height = neighbor.Bottom - resized.Bottom - gap };
            else if (Math.Abs(original.Y - neighbor.Bottom - gap) <= threshold)
                result = result with { Height = resized.Y - gap - neighbor.Y };
        }
        return result != neighbor ? result : null;
    }

    internal static WindowBounds? EdgeTarget(WindowBounds work, POINT point, int threshold, int horizontalPercent, int verticalPercent, int gap)
    {
        if (threshold <= 0) return null;
        var left = point.X <= work.X + threshold;
        var right = point.X >= work.Right - threshold;
        var top = point.Y <= work.Y + threshold;
        var bottom = point.Y >= work.Bottom - threshold;
        if (!left && !right && !top && !bottom) return null;
        if (!left && !right) return top ? work : null;
        var splitX = work.X + work.Width * Math.Clamp(horizontalPercent, 1, 99) / 100;
        var splitY = work.Y + work.Height * Math.Clamp(verticalPercent, 1, 99) / 100;
        var x = left ? work.X : splitX;
        var y = top ? work.Y : bottom ? splitY : work.Y;
        var width = left ? splitX - work.X : work.Right - splitX;
        var height = top ? splitY - work.Y : bottom ? work.Bottom - splitY : work.Height;
        return Inset(new(x, y, width, height), gap);
    }

    internal static WindowBounds Magnet(WindowBounds moving, IEnumerable<WindowBounds> targets, int distance, int gap, bool includeInner, bool includeOuter = true)
    {
        if (distance <= 0) return moving;
        var bestX = distance + 1;
        var bestY = distance + 1;
        var dx = 0;
        var dy = 0;
        foreach (var target in targets)
        {
            if (moving.Y < target.Bottom && moving.Bottom > target.Y)
            {
                if (includeOuter)
                {
                    Consider(target.X - gap - moving.Right, ref bestX, ref dx);
                    Consider(target.Right + gap - moving.X, ref bestX, ref dx);
                }
                if (includeInner)
                {
                    Consider(target.X - moving.X, ref bestX, ref dx);
                    Consider(target.Right - moving.Right, ref bestX, ref dx);
                }
            }
            if (moving.X < target.Right && moving.Right > target.X)
            {
                if (includeOuter)
                {
                    Consider(target.Y - gap - moving.Bottom, ref bestY, ref dy);
                    Consider(target.Bottom + gap - moving.Y, ref bestY, ref dy);
                }
                if (includeInner)
                {
                    Consider(target.Y - moving.Y, ref bestY, ref dy);
                    Consider(target.Bottom - moving.Bottom, ref bestY, ref dy);
                }
            }
        }
        return moving with { X = moving.X + dx, Y = moving.Y + dy };
    }

    private static void Consider(int delta, ref int bestDistance, ref int offset)
    {
        var distance = Math.Abs(delta);
        if (distance >= bestDistance) return;
        bestDistance = distance;
        offset = delta;
    }

    internal static bool Matches(string pattern, string value) =>
        FileSystemName.MatchesSimpleExpression(pattern.Trim(), value, ignoreCase: true);

    internal static bool IsExcluded(string processPatterns, string windowPatterns, string processName, string title, string className)
    {
        if (processPatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(pattern => Matches(pattern, processName))) return true;
        return windowPatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(pattern =>
        {
            var parts = pattern.Split('|', 2);
            return Matches(parts[0].Length == 0 ? "*" : parts[0], title) &&
                (parts.Length == 1 || Matches(parts[1].Length == 0 ? "*" : parts[1], className));
        });
    }
}
