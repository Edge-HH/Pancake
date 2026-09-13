namespace Pancake.Services;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);
public readonly record struct SnapResult(double X, double Y, double? GuideX, double? GuideY);

/// <summary>看板与导出共用的逻辑坐标对齐；不依赖 UI，避免预览缩放影响吸附。</summary>
public static class BoardLayout
{
    /// <summary>不缩小内容的左上角装箱。对齐优先采用整行；无法容纳时再填补边缘空隙。</summary>
    public static List<LayoutRect> Arrange(IReadOnlyList<(double Width, double Height)> tiles,
        double width, double height, double gap = 0, bool align = true, double grid = 0)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("画布没有足够的磁贴空间。");
        gap = double.IsFinite(gap) ? Math.Max(0, gap) : 0;
        double Round(double value) => grid > 0 ? Math.Ceiling(value / grid) * grid : value;
        if (tiles.Any(t => !double.IsFinite(t.Width) || !double.IsFinite(t.Height) || t.Width <= 0 || t.Height <= 0))
            throw new InvalidOperationException("磁贴尺寸无效。");
        if (align)
        {
            List<LayoutRect> rows = [];
            double x = 0, y = 0, rowHeight = 0;
            foreach (var tile in tiles)
            {
                if (x > 0 && x + tile.Width > width) { x = 0; y = Round(y + rowHeight + gap); rowHeight = 0; }
                if (tile.Width > width || y + tile.Height > height) break;
                rows.Add(new(x, y, tile.Width, tile.Height));
                x = Round(x + tile.Width + gap); rowHeight = Math.Max(rowHeight, tile.Height);
            }
            if (rows.Count == tiles.Count) return rows;
        }
        List<LayoutRect> placed = [];
        LayoutRect[] result = new LayoutRect[tiles.Count];
        foreach (int i in Enumerable.Range(0, tiles.Count).OrderByDescending(i => tiles[i].Width * tiles[i].Height))
        {
            var tile = tiles[i];
            LayoutRect? found = null;
            foreach (double y in placed.Select(p => Round(p.Y + p.Height + gap)).Append(0).Distinct().Order())
            {
                foreach (double x in placed.Select(p => Round(p.X + p.Width + gap)).Append(0).Distinct().Order())
                {
                    LayoutRect candidate = new(x, y, tile.Width, tile.Height);
                    if (x + tile.Width > width + .001 || y + tile.Height > height + .001) continue;
                    if (placed.Any(p => x < p.X + p.Width + gap - .001 && x + tile.Width + gap > p.X + .001 &&
                        y < p.Y + p.Height + gap - .001 && y + tile.Height + gap > p.Y + .001)) continue;
                    found = candidate; break;
                }
                if (found is not null) break;
            }
            if (found is not { } rect) throw new InvalidOperationException("空间不足，无法完整显示所有磁贴；请扩大作业板、开启无限作业板或减小间隔。");
            placed.Add(rect); result[i] = rect;
        }
        return result.ToList();
    }

    // 只向上统一相差不超过 16 DIP 的尺寸，不能因对齐裁掉内容。
    public static List<(double Width, double Height)> AlignSizes(IReadOnlyList<(double Width, double Height)> sizes)
    {
        double[] widths = sizes.Select(s => s.Width).ToArray(), heights = sizes.Select(s => s.Height).ToArray();
        static void Align(double[] values)
        {
            var sorted = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
            for (int start = 0; start < sorted.Length;)
            {
                int end = start;
                while (end + 1 < sorted.Length && values[sorted[end + 1]] - values[sorted[start]] <= 16) end++;
                double size = values[sorted[end]];
                for (int j = start; j <= end; j++) values[sorted[j]] = size;
                start = end + 1;
            }
        }
        Align(widths); Align(heights);
        return widths.Select((w, i) => (w, heights[i])).ToList();
    }

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

}
