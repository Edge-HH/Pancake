namespace Pancake.RichText;

/// <summary>
/// 一段文字的字符格式。颜色为 null 表示跟随主题，高光为 null 表示没有高光，
/// 字体为 null 表示使用随包内置字体。所有值都可比较，便于把相邻同样式的片段合并。
/// </summary>
public readonly record struct RichTextFormat(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    BoardColor? Foreground = null,
    BoardColor? Highlight = null,
    string? FontFamily = null)
{
    public static RichTextFormat Default { get; } = new();

    /// <summary>取反加粗/斜体/下划线，供工具栏按钮使用。</summary>
    public RichTextFormat ToggleBold() => this with { Bold = !Bold };

    public RichTextFormat ToggleItalic() => this with { Italic = !Italic };

    public RichTextFormat ToggleUnderline() => this with { Underline = !Underline };
}

/// <summary>文档中一段连续、格式相同的文字。</summary>
public readonly record struct RichTextSpan(int Start, int Length, RichTextFormat Format)
{
    public int End => Start + Length;

    public bool Contains(int index) => index >= Start && index < End;
}
