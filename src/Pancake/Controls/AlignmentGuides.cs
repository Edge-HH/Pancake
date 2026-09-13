using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 关闭网格吸附后拖动磁贴时显示的蓝色虚线，提示当前吸附到的中轴或相邻磁贴边缘。
/// </summary>
internal static class AlignmentGuides
{
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.FromRgb(0, 191, 255));

    public static void Draw(Canvas canvas, SnapResult snap, double width, double height, double thickness = 1)
    {
        canvas.Children.Clear();
        if (snap.GuideX is double x) Add(canvas, x, 0, x, height, thickness);
        if (snap.GuideY is double y) Add(canvas, 0, y, width, y, thickness);
    }

    private static void Add(Canvas canvas, double x1, double y1, double x2, double y2, double thickness) =>
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = GuideBrush,
            StrokeThickness = thickness,
            StrokeDashArray = new AvaloniaList<double> { 4, 3 }
        });
}
