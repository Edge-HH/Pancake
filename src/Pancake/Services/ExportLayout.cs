namespace Pancake.Services;

public sealed class ExportTilePlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Scale { get; set; }
}

/// <summary>独立于窗口和 DPI 的导出排版；网格留白和标题区域以画布逻辑坐标计算。</summary>
public static class ExportLayout
{
    public static List<ExportTilePlacement> Arrange(IReadOnlyList<(double Width, double Height)> tiles, double width, double height, double titleHeight)
    {
        if (tiles.Count == 0) return [];
        double margin = Math.Min(width, height) * .025;
        double gap = margin * .6;
        double availableWidth = width - 2 * margin;
        double availableHeight = height - 2 * margin - titleHeight;
        if (availableHeight <= 0) throw new InvalidOperationException("标题过高，画布没有足够的磁贴空间。");
        List<ExportTilePlacement> best = [];
        double bestScale = 0;
        for (int columns = 1; columns <= tiles.Count; columns++)
        {
            int rows = (tiles.Count + columns - 1) / columns;
            double cellWidth = (availableWidth - (columns - 1) * gap) / columns;
            double cellHeight = (availableHeight - (rows - 1) * gap) / rows;
            double scale = tiles.Min(t => Math.Min(cellWidth / t.Width, cellHeight / t.Height));
            if (scale <= bestScale) continue;
            bestScale = scale;
            best = tiles.Select((t, i) => new ExportTilePlacement
            {
                X = margin + i % columns * (cellWidth + gap),
                Y = margin + titleHeight + i / columns * (cellHeight + gap),
                Width = t.Width * scale, Height = t.Height * scale, Scale = scale
            }).ToList();
        }
        if (best.Count == 0) throw new InvalidOperationException("画布无法容纳这些磁贴。");
        return best;
    }

    public static bool Overlap(ExportTilePlacement a, ExportTilePlacement b) =>
        a.X < b.X + b.Width - .01 && a.X + a.Width > b.X + .01 && a.Y < b.Y + b.Height - .01 && a.Y + a.Height > b.Y + .01;

    public static bool InBounds(ExportTilePlacement tile, double width, double height, double top) =>
        tile.X >= 0 && tile.Y >= top && tile.X + tile.Width <= width + .01 && tile.Y + tile.Height <= height + .01;
}
