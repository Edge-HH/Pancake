using Pancake;
using Pancake.RichText;

int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}

// 1. 纯文本往返：没有 RTF 时按纯文本建立文档。
RichTextDocument plain = RichTextDocument.FromPlainText("完成 P30 练习题");
Check(plain.Text == "完成 P30 练习题", "纯文本文档应保留原文");
Check(plain.Spans.Count == 1 && plain.Spans[0].Length == plain.Text.Length, "纯文本文档应只有一段");
Check(plain.Spans[0].Format == RichTextFormat.Default, "纯文本文档应使用默认格式");

// 2. 加粗、斜体、下划线在写出后仍能读回。
RichTextDocument formatted = RichTextDocument.FromPlainText("加粗斜体下划线");
formatted.Apply(0, 2, format => format.ToggleBold());
formatted.Apply(2, 2, format => format.ToggleItalic());
formatted.Apply(4, 3, format => format.ToggleUnderline());
RichTextDocument reloaded = RtfCodec.Parse(formatted.ToRtf(), formatted.Text);
Check(reloaded.Text == formatted.Text, "格式往返不应改变文字");
Check(reloaded.GetFormatAt(0).Bold && !reloaded.GetFormatAt(2).Bold, "加粗范围应在往返后保留");
Check(reloaded.GetFormatAt(2).Italic && !reloaded.GetFormatAt(0).Italic, "斜体范围应在往返后保留");
Check(reloaded.GetFormatAt(4).Underline && !reloaded.GetFormatAt(0).Underline, "下划线范围应在往返后保留");

// 2b. 按片段设置的字体（含非 ASCII 家族名）在往返后保留：字体表要写全，且分开编号。
RichTextDocument fonted = RichTextDocument.FromPlainText("默认字体与指定字体");
fonted.Apply(0, 4, format => format with { FontFamily = "阿里妈妈东方大楷" });
fonted.Apply(4, 6, format => format with { FontFamily = "Arial" });
RichTextDocument fontedReload = RtfCodec.Parse(fonted.ToRtf(), fonted.Text);
Check(fontedReload.GetFormatAt(0).FontFamily == "阿里妈妈东方大楷", "非 ASCII 字体名应在往返后保留");
Check(fontedReload.GetFormatAt(4).FontFamily == "Arial", "第二个字体应在往返后保留");
Check(fontedReload.GetFormatAt(4).FontFamily != fontedReload.GetFormatAt(0).FontFamily, "不同片段应使用不同字体");

// 3. 文字颜色与高光往返。
BoardColor red = BoardColor.FromRgb(239, 68, 68);
BoardColor yellow = BoardColor.FromRgb(250, 204, 21);
RichTextDocument colored = RichTextDocument.FromPlainText("红色高光普通");
colored.Apply(0, 2, format => format with { Foreground = red });
colored.Apply(2, 2, format => format with { Highlight = yellow });
RichTextDocument coloredReload = RtfCodec.Parse(colored.ToRtf(), colored.Text);
Check(coloredReload.GetFormatAt(0).Foreground == red, "文字颜色应在往返后保留");
Check(coloredReload.GetFormatAt(2).Highlight == yellow, "高光颜色应在往返后保留");
Check(coloredReload.GetFormatAt(4).Foreground is null && coloredReload.GetFormatAt(4).Highlight is null, "未设置颜色的部分不应被染色");

// 4. 解析 WinUI RichEditBox 实际写出的 RTF：字体表、颜色表、Unicode 转义与段落标记。
const string winUiRtf = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat{\fonttbl{\f0\fnil\fcharset134 Microsoft YaHei;}}"
    + @"{\colortbl ;\red255\green255\blue255;\red239\green68\blue68;}"
    + @"\viewkind4\uc1 \pard\f0\fs20 \u23436?\u25104?\b P30\b0  \u32451?\u20064?\par }";
RichTextDocument parsed = RtfCodec.Parse(winUiRtf, "完成P30 练习");
Check(parsed.Text == "完成P30 练习", "应解析出与纯文本一致的内容并去掉尾部段落，实际为 " + parsed.Text.Replace("\n", "\\n"));
Check(parsed.Spans.Any(span => span.Format.Bold), "应解析出加粗片段");

// 5. 旧版误写入的尾部空段落按纯文本边界裁掉。
const string trailingRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}}{\colortbl ;\red0\green0\blue0;}\uc1\pard\f0\fs20 \u20320?\u22909?\par\par\par}";
RichTextDocument trimmed = RtfCodec.Parse(trailingRtf, "你好");
Check(trimmed.Text == "你好", "尾部多余段落应被裁掉，实际为 " + trimmed.Text.Replace("\n", "\\n"));

// 6. 工具栏切换：整段加粗后再取消。
RichTextDocument toggled = RichTextDocument.FromPlainText("开关");
toggled.Apply(0, toggled.Text.Length, format => format.ToggleBold());
Check(toggled.GetUniformFormat(0, toggled.Text.Length)?.Bold == true, "整段切换加粗后应统一为加粗");
toggled.Apply(0, toggled.Text.Length, format => format.ToggleBold());
Check(toggled.GetUniformFormat(0, toggled.Text.Length)?.Bold == false, "再次切换应恢复为不加粗");

// 7. 混合选区的格式查询返回 null，界面据此不误标任何按钮。
RichTextDocument mixed = RichTextDocument.FromPlainText("前中后");
mixed.Apply(1, 1, format => format.ToggleItalic());
Check(mixed.GetUniformFormat(0, 3) is null, "混合选区不应返回单一格式");
Check(mixed.GetUniformFormat(0, 1)?.Italic == false, "单一选区应返回该段格式");

// 8. 编辑操作：插入、删除与格式延续。
RichTextDocument edited = RichTextDocument.FromPlainText("开头结尾");
edited.Apply(0, 2, format => format.ToggleBold());
edited.Replace(2, 0, "中间");
Check(edited.Text == "开头中间结尾", "插入后文字应正确");
Check(edited.GetFormatAt(2).Bold, "插入位置应延续前一字符的格式");
edited.Replace(2, 2, string.Empty);
Check(edited.Text == "开头结尾", "删除后文字应正确");
Check(edited.Spans.All(span => span.Length > 0), "删除后不应留下空片段");
Check(edited.Spans.Sum(span => span.Length) == edited.Text.Length, "片段总长度应等于文字长度");

// 9. 空文档与损坏输入。
Check(RichTextDocument.FromPlainText("").Text.Length == 0, "空文本应建立空文档");
Check(RtfCodec.Parse("这不是 RTF", "兜底").Text == "兜底", "非 RTF 输入应回退到纯文本");
Check(RtfCodec.Parse(@"{\rtf1\ansi \b", "兜底").Text == "兜底", "截断的 RTF 应回退到纯文本");

Console.WriteLine($"PASS: {checks} rich-text model, RTF round-trip, migration and editing checks.");
