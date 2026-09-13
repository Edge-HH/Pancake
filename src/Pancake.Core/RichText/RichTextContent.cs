using Pancake.Models;
using Pancake.Services;

namespace Pancake.RichText;

/// <summary>
/// 作业内容与富文本模型之间的转换。项目文件同时保存纯文本与 RTF，
/// 这里集中处理两者之间的映射，保证界面、导出与旧版存档读到同样的结果。
/// </summary>
public static class RichTextContent
{
    /// <summary>优先用 RTF 建立文档；RTF 缺失或损坏时退回纯文本。</summary>
    public static RichTextDocument ToDocument(HomeworkEntry entry) => RtfCodec.Parse(entry.RtfContent, entry.Content);

    /// <summary>把文档写回作业的纯文本与 RTF 字段，两者始终同步。</summary>
    public static void Save(HomeworkEntry entry, RichTextDocument document)
    {
        entry.Content = document.Text;
        // 未做任何格式设置时不写入 RTF，项目文件保持与旧版“仅文字”的写法一致。
        entry.RtfContent = document.Spans.All(span => span.Format == RichTextFormat.Default)
            ? string.Empty
            : document.ToRtf();
    }

    /// <summary>
    /// 计算实际显示用的文字颜色：null 表示跟随主题；旧版把默认白色写进了 RTF，
    /// 浅色主题下要显示为黑色，彩色文字保持原样。
    /// </summary>
    public static BoardColor? EffectiveForeground(RichTextFormat format, bool light)
    {
        if (format.Foreground is not { } color) return null;
        return light && DefaultContentColors.IsDefaultWhite(color.R, color.G, color.B)
            ? BoardColor.FromRgb(0, 0, 0)
            : color;
    }
}
