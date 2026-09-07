using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pancake.Controls;

/// <summary>对后方实际内容进行高斯模糊，保留前景文字清晰度。</summary>
public sealed class BlurBackdropBrush(double amount) : XamlCompositionBrushBase
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
        using var factory = compositor.CreateEffectFactory(effect);
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
