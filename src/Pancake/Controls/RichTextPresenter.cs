using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Pancake.RichText;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 富文本显示控件：把平台无关的富文本模型渲染成带格式的行内文本。
/// 查看模式与图片导出共用同一套渲染规则，保证所见即所得。
/// </summary>
public sealed class RichTextPresenter : TextBlock
{
    public static readonly StyledProperty<RichTextDocument?> DocumentProperty =
        AvaloniaProperty.Register<RichTextPresenter, RichTextDocument?>(nameof(Document));

    public RichTextPresenter()
    {
        TextWrapping = TextWrapping.Wrap;
        FontSize = 20;
        Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor());
    }

    public RichTextDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty) Render();
    }

    /// <summary>按当前主题与文档片段重建行内文本。</summary>
    public void Render()
    {
        Inlines ??= [];
        Inlines.Clear();
        RichTextDocument? document = Document;
        if (document is null || document.Text.Length == 0) return;

        bool light = BoardTheme.IsLight;
        BoardColor themeText = BoardTheme.TextColor;
        foreach (RichTextSpan span in document.Spans)
        {
            if (span.Length <= 0) continue;
            BoardColor? foreground = RichTextContent.EffectiveForeground(span.Format, light);
            Run run = new(document.Text.Substring(span.Start, span.Length))
            {
                FontWeight = span.Format.Bold ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle = span.Format.Italic ? FontStyle.Italic : FontStyle.Normal,
                Foreground = new SolidColorBrush((foreground ?? themeText).ToColor())
            };
            // 字体按片段套用：模型里没写家族就跟随外层控件，写了但本机缺失时退回随包字体。
            if (FontService.ResolveRichTextFamily(span.Format.FontFamily) is { } family) run.FontFamily = family;
            if (span.Format.Underline)
            {
                run.TextDecorations = [new TextDecoration { Location = TextDecorationLocation.Underline }];
            }
            if (span.Format.Highlight is { } highlight) run.Background = new SolidColorBrush(highlight.ToColor());
            Inlines.Add(run);
        }
    }
}
