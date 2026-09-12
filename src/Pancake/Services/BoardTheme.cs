using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Pancake.Services;

/// <summary>代码生成的控件在实际主题变化时重建，统一获取背景和默认前景。</summary>
public static class BoardTheme
{
    public static bool IsLight { get; set; }
    public static Color TextColor => TextColorFor(IsLight);
    public static SolidColorBrush TextBrush => new(TextColor);
    public static SolidColorBrush SurfaceBrush => new(SurfaceColorFor(IsLight));
    public static SolidColorBrush LineBrush => new(LineColorFor(IsLight));

    // 需要显式指定主题的场合（例如浮层要跟随锚点而不是全局状态）按参数取色，色值只在这里定义一次。
    public static Color TextColorFor(bool light) => light
        ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 247, 247, 249);
    public static Color SurfaceColorFor(bool light) => light
        ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 31, 31, 31);
    public static Color LineColorFor(bool light) => light
        ? Color.FromArgb(255, 215, 215, 215) : Color.FromArgb(255, 70, 70, 74);

    public static Color DisplayContentColor(Color color) => IsLight && DefaultContentColors.IsDefaultWhite(color.R, color.G, color.B)
        ? Color.FromArgb(color.A, 0, 0, 0) : color;
}
