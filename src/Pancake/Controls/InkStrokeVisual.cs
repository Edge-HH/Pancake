using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 笔迹折线的统一外观：磁贴笔迹和仅时钟模式的全屏笔迹共用同一套颜色、线宽和线帽设置。
/// </summary>
internal static class InkStrokeVisual
{
    public static Polyline Create(InkStrokeData stroke)
    {
        Polyline line = new()
        {
            Stroke = new SolidColorBrush(BoardTheme.DisplayContentColor(stroke.Color).ToColor()),
            StrokeThickness = stroke.Thickness,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            RenderTransform = new ScaleTransform(stroke.TipScaleX, stroke.TipScaleY)
        };
        foreach (BoardPoint point in stroke.Points) line.Points.Add(new Point(point.X, point.Y));
        return line;
    }
}
