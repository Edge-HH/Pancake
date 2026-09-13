using Microsoft.UI.Xaml.Media;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>控制窗、磁贴和预览共用颜色合成规则，不降低前景或模糊的透明度。</summary>
internal static class SurfaceBackground
{
    internal static Brush Create(string color, double opacity, bool cleared, bool glass, double blur)
    {
        var tint = GridAppearance.ParseColor(color, BoardTheme.SurfaceBrush.Color);
        double alpha = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 0.8;
        tint.A = cleared ? (byte)0 : (byte)Math.Round(tint.A * alpha);
        return glass
            ? new BlurBackdropBrush(blur, tint) { FallbackColor = tint }
            : new SolidColorBrush(tint);
    }
}
