namespace Pancake.Services;

/// <summary>代码生成的控件在实际主题变化时重建，统一获取背景和默认前景。</summary>
public static class BoardTheme
{
    public static bool IsLight { get; set; }
    public static BoardColor TextColor => TextColorFor(IsLight);
    public static BoardColor SurfaceColor => SurfaceColorFor(IsLight);
    public static BoardColor LineColor => LineColorFor(IsLight);

    // 需要显式指定主题的场合（例如浮层要跟随锚点而不是全局状态）按参数取色，色值只在这里定义一次。
    public static BoardColor TextColorFor(bool light) => light
        ? BoardColor.FromRgb(0, 0, 0) : BoardColor.FromRgb(247, 247, 249);
    public static BoardColor SurfaceColorFor(bool light) => light
        ? BoardColor.FromRgb(255, 255, 255) : BoardColor.FromRgb(31, 31, 31);
    public static BoardColor LineColorFor(bool light) => light
        ? BoardColor.FromRgb(215, 215, 215) : BoardColor.FromRgb(70, 70, 74);

    public static BoardColor DisplayContentColor(BoardColor color) =>
        IsLight && DefaultContentColors.IsDefaultWhite(color.R, color.G, color.B)
            ? new BoardColor(color.A, 0, 0, 0) : color;
}
