using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;

namespace Pancake.Controls;

/// <summary>
/// 拖动把手的最小模板。
/// <para>
/// Thumb 是模板化控件：主题只为滚动条、滑条里的 Thumb 提供样式（选择器带父级限定），
/// 直接放在面板里的 Thumb 拿不到模板，既不渲染也**收不到指针**——
/// 磁贴拖动、八方向缩放、分隔条与自由布局组件的手势会因此整体失效。
/// </para>
/// 这里给把手一个只画背景的最小模板，命中区域与外观都不再依赖主题。
/// </summary>
internal static class ThumbVisuals
{
    /// <summary>给把手套上最小模板并返回它，便于在创建处链式调用。</summary>
    public static Thumb Apply(Thumb thumb)
    {
        thumb.Template = new FuncControlTemplate<Thumb>((owner, _) =>
        {
            Border border = new();
            // 用绑定而不是快照：把手后续调整背景、边框或圆角时外观同步变化。
            border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = owner });
            border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = owner });
            border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = owner });
            border.Bind(Border.CornerRadiusProperty, new Binding(nameof(TemplatedControl.CornerRadius)) { Source = owner });
            return border;
        });
        return thumb;
    }
}
