using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Services;

namespace Pancake.Controls;

internal static class AlignmentGuides
{
    public static void Draw(Canvas canvas, SnapResult snap, double width, double height, double thickness = 1)
    {
        canvas.Children.Clear();
        void Add(double x1, double y1, double x2, double y2) => canvas.Children.Add(new Line
        {
            X1 = x1, X2 = x2, Y1 = y1, Y2 = y2,
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue),
            StrokeThickness = thickness, StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        if (snap.GuideX is double x) Add(x, 0, x, height);
        if (snap.GuideY is double y) Add(0, y, width, y);
    }
}
