namespace Pancake.Services;

/// <summary>组件布局的共同规则，后续组件只需注册稳定标识和视图。</summary>
public static class WidgetLayout
{
    public static void Move(RegionPlacement placement, double dx, double dy, double width, double height)
    {
        placement.X = Math.Clamp(placement.X + dx, 0, Math.Max(0, width - 40));
        placement.Y = Math.Clamp(placement.Y + dy, 0, Math.Max(0, height - 40));
    }
    public static void Resize(RegionPlacement placement, double dx, double dy, double width, double height)
    {
        placement.Width = Math.Clamp(placement.Width + dx, 80, Math.Max(80, width - placement.X));
        placement.Height = Math.Clamp(placement.Height + dy, 48, Math.Max(48, height - placement.Y));
    }
    public static string CompleteSplit(double ratio) => ratio <= .04 ? "Board" : ratio >= .96 ? "Clock" : "Split";
    public static Dictionary<string, RegionPlacement> Copy(IReadOnlyDictionary<string, RegionPlacement> placements) => placements.ToDictionary(p => p.Key,
        p => new RegionPlacement { X = p.Value.X, Y = p.Value.Y, Width = p.Value.Width, Height = p.Value.Height });
}
