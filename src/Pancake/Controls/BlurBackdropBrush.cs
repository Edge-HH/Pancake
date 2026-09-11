using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pancake.Controls;

/// <summary>对后方实际内容进行高斯模糊，保留前景文字清晰度。</summary>
public sealed class BlurBackdropBrush(double amount, Windows.UI.Color? tint = null) : XamlCompositionBrushBase
{
    private CompositionBackdropBrush? _backdrop;
    protected override void OnConnected()
    {
        if (CompositionBrush is not null) return;
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        using var effect = new GaussianBlurEffect
        {
            BlurAmount = (float)Math.Clamp(amount, 0, 100),
            BorderMode = EffectBorderMode.Hard,
            Source = new CompositionEffectSourceParameter("backdrop")
        };
        // 颜色覆盖在模糊结果上；清除颜色时仍保留完整的模糊源。
        using var tintEffect = new ColorSourceEffect { Color = tint ?? Microsoft.UI.Colors.Transparent };
        using var composite = new CompositeEffect
        {
            Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver,
            Sources = { effect, tintEffect }
        };
        using var factory = tint.HasValue
            ? compositor.CreateEffectFactory(composite)
            : compositor.CreateEffectFactory(effect);
        var brush = factory.CreateBrush();
        _backdrop = compositor.CreateBackdropBrush();
        brush.SetSourceParameter("backdrop", _backdrop);
        CompositionBrush = brush;
    }
    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _backdrop?.Dispose();
        _backdrop = null;
    }
}
