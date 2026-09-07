namespace Pancake.Services;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);
public readonly record struct SnapResult(double X, double Y, double? GuideX, double? GuideY);

/// <summary>看板与导出共用的逻辑坐标对齐；不依赖 UI，避免预览缩放影响吸附。</summary>
public static class BoardLayout
{
    public static SnapResult Snap(LayoutRect moving, IEnumerable<LayoutRect> others,
        double width, double height, double top = 0, double tolerance = 8)
    {
        List<double> xs = [0, width / 2, width];
        List<double> ys = [top, top + (height - top) / 2, height];
        foreach (LayoutRect other in others)
        {
            xs.AddRange([other.X, other.X + other.Width / 2, other.X + other.Width]);
            ys.AddRange([other.Y, other.Y + other.Height / 2, other.Y + other.Height]);
        }
        static (double Value, double? Guide) Axis(double value, double size, double min, double max, List<double> targets, double threshold)
        {
            double best = threshold + .001;
            double result = Math.Clamp(value, min, Math.Max(min, max - size));
            double? guide = null;
            foreach (double target in targets)
            foreach (double offset in new[] { 0d, size / 2, size })
            {
                double next = target - offset, distance = Math.Abs(next - value);
                if (next < min || next + size > max + .001 || distance >= best) continue;
                best = distance; result = next; guide = target;
            }
            return (result, guide);
        }
        var x = Axis(moving.X, moving.Width, 0, width, xs, tolerance);
        var y = Axis(moving.Y, moving.Height, top, height, ys, tolerance);
        return new(x.Value, y.Value, x.Guide, y.Guide);
    }

    public static List<LayoutRect> ArrangeGrid(IReadOnlyList<(double Width, double Height)> tiles,
        double width, double height, double grid = 48, double minWidth = 280, double minHeight = 96)
    {
        if (tiles.Count == 0) return [];
        double columns = Math.Floor(width / grid), rows = Math.Floor(height / grid);
        double minColumns = Math.Ceiling(minWidth / grid), minRows = Math.Ceiling(minHeight / grid);
        if (columns < minColumns || rows < minRows)
            throw new InvalidOperationException("作业板空间不足，无法容纳最小网格磁贴。");
        // 优先保留原尺寸；必要时逐步缩小，位置及尺寸始终为整数网格。
        for (int percent = 100; percent >= 1; percent--)
        {
            var sizes = tiles.Select((tile, index) => (Index: index,
                W: Math.Max(minColumns, Math.Floor(tile.Width * percent / 100 / grid)),
                H: Math.Max(minRows, Math.Floor(tile.Height * percent / 100 / grid)))).ToList();
            List<LayoutRect> placed = [];
            LayoutRect[] result = new LayoutRect[tiles.Count];
            foreach (var tile in sizes.OrderByDescending(t => t.W * t.H).ThenBy(t => t.Index))
            {
                // 使用已放置磁贴的边缘作为候选点，填入空隙，避免均分单元格造成大块留白。
                var xs = placed.Select(p => p.X + p.Width).Append(0).Distinct().Order();
                var ys = placed.Select(p => p.Y + p.Height).Append(0).Distinct().Order();
                LayoutRect? slot = null;
                foreach (double y in ys)
                {
                    foreach (double x in xs)
                    {
                        LayoutRect candidate = new(x, y, tile.W, tile.H);
                        if (x + tile.W > columns || y + tile.H > rows || placed.Any(p => Overlaps(candidate, p))) continue;
                        slot = candidate; break;
                    }
                    if (slot is not null) break;
                }
                if (slot is not { } found) break;
                placed.Add(found);
                result[tile.Index] = new(found.X * grid, found.Y * grid, found.Width * grid, found.Height * grid);
            }
            if (placed.Count == tiles.Count) return result.ToList();
        }
        throw new InvalidOperationException("当前作业板空间不足，无法在最小磁贴尺寸下完成网格排列。");
    }

    private static bool Overlaps(LayoutRect a, LayoutRect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
}
