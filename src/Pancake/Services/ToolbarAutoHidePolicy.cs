namespace Pancake.Services;

/// <summary>不依赖界面的空闲判定与最近边框计算，坐标统一使用窗口内 DIP。</summary>
public static class ToolbarAutoHidePolicy
{
    public static double NormalizeDelay(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 1, 600) : 5;

    public static bool ShouldHide(bool enabled, bool viewing, bool interacting, double idleSeconds, double delay) =>
        enabled && viewing && !interacting && idleSeconds >= NormalizeDelay(delay);

    public static (double X, double Y) ExitOffset(double x, double y, double width, double height,
        double windowWidth, double windowHeight)
    {
        // 相同距离优先上下边框，让角落及底部居中的控制窗有稳定的飞出方向。
        var edges = new (double Distance, double X, double Y)[]
        {
            (windowHeight - y - height, 0, windowHeight - y + 1),
            (y, 0, -y - height - 1),
            (x, -x - width - 1, 0),
            (windowWidth - x - width, windowWidth - x + 1, 0)
        };
        var nearest = edges.MinBy(edge => edge.Distance);
        return (nearest.X, nearest.Y);
    }
}
