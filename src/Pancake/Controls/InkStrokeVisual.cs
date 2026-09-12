using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 笔迹折线的统一外观：磁贴笔迹和仅时钟模式的全屏笔迹共用同一套颜色、线宽和线帽设置，
/// 后续调整笔迹观感只需要改这里。
/// </summary>
internal static class InkStrokeVisual
{
    public static Polyline Create(InkStrokeData stroke) => new()
    {
        Stroke = new SolidColorBrush(BoardTheme.DisplayContentColor(stroke.Color)), StrokeThickness = stroke.Thickness,
        RenderTransform = new ScaleTransform { ScaleX = stroke.TipScaleX, ScaleY = stroke.TipScaleY },
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };
}
