using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Services;
using Pancake.ViewModels;

namespace Pancake.Controls;

/// <summary>背景、模糊和前景分层，跨区时只替换底图，区域模糊仍独立生效。</summary>
public sealed class BackgroundVisual : Grid
{
    public BackgroundVisual() { IsHitTestVisible = false; }

    public void Apply(BackgroundSettings style, Brush fallback, bool shared = false)
    {
        Children.Clear();
        Background = shared || (style.Glass && string.IsNullOrWhiteSpace(style.Color)) ? null : string.IsNullOrWhiteSpace(style.Color) ? fallback : SafeColor(style.Color, fallback);
        if (!shared && File.Exists(style.ImagePath))
            Children.Add(new Image { Source = new BitmapImage(new Uri(style.ImagePath)), Stretch = style.ImageMode switch
            { "Stretch" => Stretch.Fill, "Fit" => Stretch.Uniform, _ => Stretch.UniformToFill } });
        if (style.Glass)
        {
            Children.Add(new Border { Background = new BlurBackdropBrush(style.Blur) });
            Children.Add(new Border { Background = new SolidColorBrush(BoardTheme.IsLight
                ? Windows.UI.Color.FromArgb(36, 255, 255, 255) : Windows.UI.Color.FromArgb(36, 0, 0, 0)) });
        }
    }

    private static Brush SafeColor(string value, Brush fallback)
    {
        try { return MainViewModel.BrushFromHex(value); }
        catch { return fallback; }
    }
}
