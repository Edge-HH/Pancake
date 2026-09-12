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
    public static List<ExportTilePlacement> Arrange(IReadOnlyList<(double Width, double Height)> tiles,
        double width, double height, double titleHeight, double gap = 0, bool align = true)
    {
        if (tiles.Count == 0) return [];
        double availableHeight = height - titleHeight;
        if (availableHeight <= 0) throw new InvalidOperationException("标题过高，画布没有足够的磁贴空间。");
        // 先按原尺寸靠左上排列；仅在导出画布不足时等比缩放整个磁贴，文字和笔迹一起缩放。
        for (int percent = 100; percent >= 1; percent--)
        {
            double scale = percent / 100d;
            try
            {
                var layout = BoardLayout.Arrange(tiles.Select(t => (t.Width * scale, t.Height * scale)).ToList(),
                    width, availableHeight, gap, align);
                return layout.Select(p => new ExportTilePlacement
                { X = p.X, Y = p.Y + titleHeight, Width = p.Width, Height = p.Height, Scale = scale }).ToList();
            }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("画布无法容纳这些磁贴，请增大画布或减小间隔。");
    }

    public static bool Overlap(ExportTilePlacement a, ExportTilePlacement b) =>
        a.X < b.X + b.Width - .01 && a.X + a.Width > b.X + .01 && a.Y < b.Y + b.Height - .01 && a.Y + a.Height > b.Y + .01;

    public static bool InBounds(ExportTilePlacement tile, double width, double height, double top) =>
        tile.X >= 0 && tile.Y >= top && tile.X + tile.Width <= width + .01 && tile.Y + tile.Height <= height + .01;
}
