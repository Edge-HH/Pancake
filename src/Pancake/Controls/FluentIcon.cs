using Avalonia;
using Avalonia.Controls;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 使用随包 Fluent System Icons 字体的图标控件。字形码位与语义名的对应关系放在核心层，
/// 保证界面与契约测试引用同一份定义。
/// </summary>
public sealed class FluentIcon : TextBlock
{
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<FluentIcon, string>(nameof(Symbol), nameof(FluentGlyphs.Info));

    public FluentIcon()
    {
        FontFamily = FontService.IconFamily;
        Glyph = FluentGlyphs.Info;
    }

    public string Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>TextBlock 没有 Glyph 属性，这里用 Text 承载字形码位并保持接口语义。</summary>
    public string Glyph
    {
        get => Text ?? string.Empty;
        set => Text = value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SymbolProperty)
        {
            Glyph = FluentGlyphs.Resolve(change.GetNewValue<string>() ?? nameof(FluentGlyphs.Info));
        }
    }
}
