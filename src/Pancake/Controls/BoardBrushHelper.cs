using Avalonia.Media;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 核心层颜色到 Avalonia 画刷的统一转换入口。视图与代码生成的控件都从这里取色，
/// 保证主题切换时只有一处需要改动。
/// </summary>
public static class BoardBrushHelper
{
    public static Color ToColor(this BoardColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static SolidColorBrush ToBrush(this BoardColor color) => new(color.ToColor());

    public static SolidColorBrush BrushFromHex(string hex, BoardColor fallback) =>
        BoardColor.Parse(hex, fallback).ToBrush();

    /// <summary>按当前主题取默认前景色，供代码生成的控件使用。</summary>
    public static SolidColorBrush ThemeTextBrush() => BoardTheme.TextColor.ToBrush();

    public static SolidColorBrush ThemeSurfaceBrush() => BoardTheme.SurfaceColor.ToBrush();

    public static SolidColorBrush ThemeLineBrush() => BoardTheme.LineColor.ToBrush();
}
