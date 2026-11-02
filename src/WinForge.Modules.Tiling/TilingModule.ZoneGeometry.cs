using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WinForge.Core;
using RECT = WinForge.Core.RECT;

namespace WinForge.Modules.Tiling;

public partial class TilingModule
{
    internal static HashSet<int> FindZonesNearDividers(IReadOnlyList<RECT> zones, int columns, int rows,
        POINT cursor, int distance)
    {
        var hovered = FindZone(zones, cursor);
        if (hovered < 0) hovered = FindNearestZone(zones, cursor);
        var result = new HashSet<int>();
        if (hovered < 0 || columns <= 0 || rows <= 0) return result;

        var row = hovered / columns;
        var column = hovered % columns;
        var selectedRows = new HashSet<int> { row };
        var selectedColumns = new HashSet<int> { column };
        if (column + 1 < columns && IsNearVerticalDivider(zones[hovered], zones[hovered + 1], cursor.X, distance))
            selectedColumns.Add(column + 1);
        else if (column > 0 && IsNearVerticalDivider(zones[hovered - 1], zones[hovered], cursor.X, distance))
            selectedColumns.Add(column - 1);

        if (row + 1 < rows && IsNearHorizontalDivider(zones[hovered], zones[hovered + columns], cursor.Y, distance))
            selectedRows.Add(row + 1);
        else if (row > 0 && IsNearHorizontalDivider(zones[hovered - columns], zones[hovered], cursor.Y, distance))
            selectedRows.Add(row - 1);

        foreach (var selectedRow in selectedRows)
            foreach (var selectedColumn in selectedColumns)
            {
                var index = selectedRow * columns + selectedColumn;
                if (index >= 0 && index < zones.Count) result.Add(index);
            }
        return result;
    }

    private static bool IsNearVerticalDivider(RECT left, RECT right, int x, int distance)
    {
        if (Math.Min(left.Bottom, right.Bottom) <= Math.Max(left.Top, right.Top)) return false;
        var divider = (left.Right + right.Left) / 2;
        return Math.Abs(x - divider) <= distance;
    }

    private static bool IsNearHorizontalDivider(RECT top, RECT bottom, int y, int distance)
    {
        if (Math.Min(top.Right, bottom.Right) <= Math.Max(top.Left, bottom.Left)) return false;
        var divider = (top.Bottom + bottom.Top) / 2;
        return Math.Abs(y - divider) <= distance;
    }

    private static bool TryExtendZoneSelection(IReadOnlyList<RECT> zones, ISet<int> selection, int candidate, int gap)
    {
        if (candidate < 0 || candidate >= zones.Count || selection.Contains(candidate)) return false;
        if (!TryGetSpanBounds(zones, selection.ToHashSet(), gap, out var currentBounds) ||
            !selection.Any(index => AreZonesAdjacent(zones[index], zones[candidate], gap))) return false;

        var candidateBounds = zones[candidate];
        var extendedBounds = new RECT
        {
            Left = Math.Min(currentBounds.Left, candidateBounds.Left),
            Top = Math.Min(currentBounds.Top, candidateBounds.Top),
            Right = Math.Max(currentBounds.Right, candidateBounds.Right),
            Bottom = Math.Max(currentBounds.Bottom, candidateBounds.Bottom)
        };
        var expanded = Enumerable.Range(0, zones.Count).Where(index =>
        {
            var zone = zones[index];
            return zone.Left >= extendedBounds.Left && zone.Top >= extendedBounds.Top &&
                   zone.Right <= extendedBounds.Right && zone.Bottom <= extendedBounds.Bottom;
        }).ToHashSet();
        if (!expanded.Contains(candidate) || !selection.All(expanded.Contains)) return false;
        selection.Clear();
        foreach (var index in expanded) selection.Add(index);
        return true;
    }

    private static bool TryGetSpanBounds(IReadOnlyList<RECT> zones, IReadOnlySet<int> selection, int gap, out RECT bounds)
    {
        bounds = default;
        if (selection.Count == 0 || selection.Any(index => index < 0 || index >= zones.Count)) return false;
        var selected = selection.ToHashSet();
        var first = zones[selected.First()];
        bounds = selected.Skip(1).Select(index => zones[index]).Aggregate(first, (current, zone) => new RECT
        {
            Left = Math.Min(current.Left, zone.Left),
            Top = Math.Min(current.Top, zone.Top),
            Right = Math.Max(current.Right, zone.Right),
            Bottom = Math.Max(current.Bottom, zone.Bottom)
        });
        var spanBounds = bounds;

        var enclosedZones = Enumerable.Range(0, zones.Count).Where(index =>
        {
            var zone = zones[index];
            return zone.Left >= spanBounds.Left && zone.Top >= spanBounds.Top &&
                   zone.Right <= spanBounds.Right && zone.Bottom <= spanBounds.Bottom;
        }).ToHashSet();
        if (!selected.SetEquals(enclosedZones)) return false;

        var connected = new HashSet<int> { selected.First() };
        var pending = new Queue<int>(connected);
        while (pending.TryDequeue(out var current))
        {
            foreach (var candidate in selected)
                if (!connected.Contains(candidate) && AreZonesAdjacent(zones[current], zones[candidate], gap))
                {
                    connected.Add(candidate);
                    pending.Enqueue(candidate);
                }
        }
        return connected.Count == selected.Count;
    }

    private static bool AreZonesAdjacent(RECT first, RECT second, int gap)
    {
        var verticalOverlap = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top);
        var horizontalOverlap = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);
        var horizontalDistance = Math.Min(Math.Abs(first.Right - second.Left), Math.Abs(second.Right - first.Left));
        var verticalDistance = Math.Min(Math.Abs(first.Bottom - second.Top), Math.Abs(second.Bottom - first.Top));
        return (horizontalDistance <= gap && verticalOverlap > 0) || (verticalDistance <= gap && horizontalOverlap > 0);
    }

    internal static Dictionary<string, (int Columns, int Rows)> ParseMonitorGridProfiles(string value)
    {
        var profiles = new Dictionary<string, (int Columns, int Rows)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !IsValidMonitorName(pair[0])) continue;
            var dimensions = pair[1].Split('x', 2, StringSplitOptions.TrimEntries);
            if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out var columns) || !int.TryParse(dimensions[1], out var rows)) continue;
            profiles[pair[0]] = (Math.Clamp(columns, 1, 6), Math.Clamp(rows, 1, 4));
        }
        return profiles;
    }

    private static bool IsValidMonitorName(string value) =>
        value.Length is > 0 and <= 32 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private List<RECT> CreateZones(RECT workArea)
    {
        if (_zoneLayout == 3) return CreateGridZones(workArea, _zoneColumns, _zoneRows, _gap);
        var zones = new List<RECT>();
        var width = workArea.Right - workArea.Left;
        var height = workArea.Bottom - workArea.Top;

        if (_zoneLayout == 2)
        {
            var widths = SplitSizes(width, 2, _gap);
            var heights = SplitSizes(height, 2, _gap);
            var top = workArea.Top;
            for (var row = 0; row < 2; row++)
            {
                var left = workArea.Left;
                for (var column = 0; column < 2; column++)
                {
                    zones.Add(new RECT
                    {
                        Left = left,
                        Top = top,
                        Right = left + widths[column],
                        Bottom = top + heights[row]
                    });
                    left += widths[column] + _gap;
                }
                top += heights[row] + _gap;
            }
            return zones;
        }

        var count = _zoneLayout == 1 ? 3 : 2;
        var columnWidths = SplitSizes(width, count, _gap);
        var x = workArea.Left;
        foreach (var columnWidth in columnWidths)
        {
            zones.Add(new RECT
            {
                Left = x,
                Top = workArea.Top,
                Right = x + columnWidth,
                Bottom = workArea.Bottom
            });
            x += columnWidth + _gap;
        }

        return zones;
    }

    private static List<RECT> CreateGridZones(RECT workArea, int columns, int rows, int gap)
    {
        columns = Math.Clamp(columns, 1, 6);
        rows = Math.Clamp(rows, 1, 4);
        gap = Math.Clamp(gap, 0, 48);
        var width = Math.Max(1, workArea.Right - workArea.Left);
        var height = Math.Max(1, workArea.Bottom - workArea.Top);
        columns = Math.Min(columns, width);
        rows = Math.Min(rows, height);
        var horizontalGap = columns == 1 ? 0 : Math.Min(gap, Math.Max(0, (width - columns) / (columns - 1)));
        var verticalGap = rows == 1 ? 0 : Math.Min(gap, Math.Max(0, (height - rows) / (rows - 1)));
        var widths = SplitSizes(width, columns, horizontalGap);
        var heights = SplitSizes(height, rows, verticalGap);
        var zones = new List<RECT>(columns * rows);
        var top = workArea.Top;
        for (var row = 0; row < rows; row++)
        {
            var left = workArea.Left;
            for (var column = 0; column < columns; column++)
            {
                zones.Add(new RECT
                {
                    Left = left,
                    Top = top,
                    Right = left + widths[column],
                    Bottom = top + heights[row]
                });
                left += widths[column] + horizontalGap;
            }
            top += heights[row] + verticalGap;
        }
        return zones;
    }

    private static int FindZone(IReadOnlyList<RECT> zones, POINT point)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            if (point.X >= zone.Left && point.X < zone.Right && point.Y >= zone.Top && point.Y < zone.Bottom)
                return index;
        }

        return -1;
    }

    private static int FindNearestZone(IReadOnlyList<RECT> zones, POINT point)
    {
        if (zones.Count == 0) return -1;
        var nearest = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < zones.Count; index++)
        {
            var zone = zones[index];
            var deltaX = (zone.Left + zone.Right) / 2.0 - point.X;
            var deltaY = (zone.Top + zone.Bottom) / 2.0 - point.Y;
            var distance = deltaX * deltaX + deltaY * deltaY;
            if (distance >= bestDistance) continue;
            nearest = index;
            bestDistance = distance;
        }
        return nearest;
    }

    private static int FindAdjacentZone(IReadOnlyList<RECT> zones, int current, int direction)
    {
        if (current < 0 || current >= zones.Count || direction is not (1 or 2 or 4 or 8)) return current;
        var origin = zones[current];
        var originX = (origin.Left + origin.Right) / 2.0;
        var originY = (origin.Top + origin.Bottom) / 2.0;
        var bestIndex = current;
        var bestScore = double.MaxValue;
        for (var index = 0; index < zones.Count; index++)
        {
            if (index == current) continue;
            var candidate = zones[index];
            var deltaX = (candidate.Left + candidate.Right) / 2.0 - originX;
            var deltaY = (candidate.Top + candidate.Bottom) / 2.0 - originY;
            var primary = direction switch { 1 => -deltaX, 2 => -deltaY, 4 => deltaX, _ => deltaY };
            if (primary <= 0) continue;
            var secondary = direction is 1 or 4 ? Math.Abs(deltaY) : Math.Abs(deltaX);
            var score = primary + secondary * 1.5;
            if (score >= bestScore) continue;
            bestIndex = index;
            bestScore = score;
        }
        return bestIndex;
    }
}
