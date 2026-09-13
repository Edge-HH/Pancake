using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 网格与点阵的唯一绘制入口：看板与设置页预览共用同一段绘制逻辑，
/// 预览因此和看板的网格大小、样式、颜色、粗细完全一致。
/// 缩小显示（预览）时线宽与点直径按 scale 反向放大，缩略图里仍能看清。
/// </summary>
internal static class GridRenderer
{
    /// <summary>网格线默认颜色；设置里没有有效颜色时使用。</summary>
    public static BoardColor DefaultLineColor { get; } = BoardColor.FromArgb(105, 86, 86, 92);

    /// <summary>点阵默认颜色。</summary>
    public static BoardColor DefaultDotColor { get; } = BoardColor.FromArgb(143, 86, 86, 92);

    /// <summary>
    /// 把网格或点阵画进 <paramref name="canvas"/>。startX/startY 是第一条线的位置，
    /// 默认等于 step（与看板一致：不画在画布边缘上）；预览可以整体平移网格让它与看板原点对齐。
    /// </summary>
    public static void Draw(
        Canvas canvas,
        string style,
        double step,
        double width,
        double height,
        BoardColor lineColor,
        double thickness,
        BoardColor dotColor,
        double dotDiameter,
        double startX = double.NaN,
        double startY = double.NaN,
        double scale = 1)
    {
        if (style == "None" || !double.IsFinite(step) || step <= 0 || width <= 0 || height <= 0) return;
        double firstX = double.IsFinite(startX) ? startX : step;
        double firstY = double.IsFinite(startY) ? startY : step;
        double compensation = Math.Max(scale, 1);

        if (style == "Dots")
        {
            double diameter = Math.Clamp(dotDiameter * compensation, 1, 24);
            IBrush brush = new SolidColorBrush(dotColor.ToColor());
            for (double y = firstY; y < height; y += step)
            {
                for (double x = firstX; x < width; x += step)
                {
                    Ellipse dot = new() { Width = diameter, Height = diameter, Fill = brush };
                    Canvas.SetLeft(dot, x - diameter / 2);
                    Canvas.SetTop(dot, y - diameter / 2);
                    canvas.Children.Add(dot);
                }
            }

            return;
        }

        IBrush lineBrush = new SolidColorBrush(lineColor.ToColor());
        double stroke = Math.Clamp(thickness * compensation, 0.5, 12);
        for (double x = firstX; x < width; x += step)
        {
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(x, 0),
                EndPoint = new Point(x, height),
                Stroke = lineBrush,
                StrokeThickness = stroke
            });
        }

        for (double y = firstY; y < height; y += step)
        {
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(width, y),
                Stroke = lineBrush,
                StrokeThickness = stroke
            });
        }
    }
}
