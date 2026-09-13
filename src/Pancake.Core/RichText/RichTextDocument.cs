using System.Text;

namespace Pancake.RichText;

/// <summary>
/// 与界面无关的富文本模型：一份纯文本以及覆盖全文、互不重叠的格式片段。
/// 旧版本保存的 RTF 与纯文本都能转换成它，界面与导出共用同一个模型。
/// </summary>
public sealed class RichTextDocument
{
    private readonly List<RichTextSpan> _spans = [];

    private RichTextDocument(string text) => Text = text;

    public string Text { get; private set; }

    public IReadOnlyList<RichTextSpan> Spans => _spans;

    public bool IsEmpty => Text.Length == 0;

    /// <summary>用纯文本建立文档，可指定统一格式。</summary>
    public static RichTextDocument FromPlainText(string? text, RichTextFormat? format = null)
    {
        RichTextDocument document = new(text ?? string.Empty);
        if (document.Text.Length > 0)
        {
            document._spans.Add(new RichTextSpan(0, document.Text.Length, format ?? RichTextFormat.Default));
        }

        return document;
    }

    /// <summary>用若干片段建立文档；片段不必覆盖全文，空缺部分使用默认格式。</summary>
    public static RichTextDocument FromSpans(string text, IEnumerable<RichTextSpan> spans)
    {
        RichTextDocument document = new(text);
        foreach (RichTextSpan span in spans)
        {
            if (span.Length <= 0 || span.Start < 0 || span.End > text.Length) continue;
            document._spans.Add(span);
        }

        document.Normalize();
        return document;
    }

    /// <summary>按字符下标取格式；越界时返回默认格式。</summary>
    public RichTextFormat GetFormatAt(int index)
    {
        foreach (RichTextSpan span in _spans)
        {
            if (span.Contains(index)) return span.Format;
        }

        return RichTextFormat.Default;
    }

    /// <summary>整段替换文本，用于编辑器输入；保留范围外格式。</summary>
    public void Replace(int start, int length, string replacement, RichTextFormat? format = null)
    {
        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);
        replacement ??= string.Empty;
        RichTextFormat inserted = format ?? (length > 0 ? GetFormatAt(start) : GetFormatAt(Math.Max(0, start - 1)));
        List<RichTextSpan> rebuilt = [];
        foreach (RichTextSpan span in _spans)
        {
            int end = span.End;
            if (end <= start) rebuilt.Add(span);
            else if (span.Start >= start + length) rebuilt.Add(span with { Start = span.Start - length + replacement.Length });
            else
            {
                if (span.Start < start) rebuilt.Add(span with { Length = start - span.Start });
                if (end > start + length) rebuilt.Add(span with { Start = start + replacement.Length, Length = end - (start + length) });
            }
        }

        if (replacement.Length > 0) rebuilt.Add(new RichTextSpan(start, replacement.Length, inserted));
        Text = string.Concat(Text.AsSpan(0, start), replacement, Text.AsSpan(start + length));
        _spans.Clear();
        _spans.AddRange(rebuilt);
        Normalize();
    }

    /// <summary>
    /// 把 [start, start+length) 范围改成 <paramref name="transform"/> 计算出的格式。
    /// 长度为 0 时表示“之后输入的文字使用该格式”，不改动已有内容。
    /// </summary>
    public void Apply(int start, int length, Func<RichTextFormat, RichTextFormat> transform)
    {
        start = Math.Clamp(start, 0, Text.Length);
        length = Math.Clamp(length, 0, Text.Length - start);
        if (length == 0) return;
        Split at = new(start, start + length);
        List<RichTextSpan> rebuilt = [];
        foreach (RichTextSpan span in _spans)
        {
            if (span.End <= at.Start || span.Start >= at.End)
            {
                rebuilt.Add(span);
                continue;
            }

            if (span.Start < at.Start) rebuilt.Add(span with { Length = at.Start - span.Start });
            int coveredStart = Math.Max(span.Start, at.Start);
            int coveredEnd = Math.Min(span.End, at.End);
            if (span.Start < coveredStart) { /* 已在上面补齐前缀 */ }
            if (span.End > coveredEnd) rebuilt.Add(new RichTextSpan(coveredEnd, span.End - coveredEnd, span.Format));
            rebuilt.Add(new RichTextSpan(coveredStart, coveredEnd - coveredStart, transform(span.Format)));
        }

        _spans.Clear();
        _spans.AddRange(rebuilt);
        Normalize();
    }

    /// <summary>范围是否全部为同一格式；用于工具栏按钮的选中状态。</summary>
    public RichTextFormat? GetUniformFormat(int start, int length)
    {
        if (length <= 0) return GetFormatAt(Math.Clamp(start, 0, Math.Max(0, Text.Length - 1)));
        RichTextFormat? format = null;
        foreach (RichTextSpan span in _spans)
        {
            if (span.End <= start || span.Start >= start + length) continue;
            if (format is null) format = span.Format;
            else if (!format.Value.Equals(span.Format)) return null;
        }

        return format ?? RichTextFormat.Default;
    }

    /// <summary>把片段补全到覆盖全文并按顺序合并同样式邻居。</summary>
    public void Normalize()
    {
        _spans.Sort((left, right) => left.Start.CompareTo(right.Start));
        List<RichTextSpan> merged = [];
        int cursor = 0;
        foreach (RichTextSpan span in _spans)
        {
            if (span.Length <= 0) continue;
            if (span.Start > cursor) merged.Add(new RichTextSpan(cursor, span.Start - cursor, RichTextFormat.Default));
            if (span.Start < cursor)
            {
                // 片段重叠时以先出现的为准，裁掉重叠部分。
                int overlapEnd = cursor;
                if (span.End <= overlapEnd) continue;
                merged.Add(new RichTextSpan(overlapEnd, span.End - overlapEnd, span.Format));
            }
            else
            {
                merged.Add(span);
            }

            cursor = Math.Max(cursor, span.End);
        }

        if (cursor < Text.Length) merged.Add(new RichTextSpan(cursor, Text.Length - cursor, RichTextFormat.Default));

        List<RichTextSpan> result = [];
        foreach (RichTextSpan span in merged)
        {
            if (result.Count > 0 && result[^1].Format.Equals(span.Format) && result[^1].End == span.Start)
            {
                result[^1] = result[^1] with { Length = result[^1].Length + span.Length };
            }
            else
            {
                result.Add(span);
            }
        }

        _spans.Clear();
        _spans.AddRange(result);
    }

    public string ToPlainText() => Text;

    public string ToRtf() => RtfCodec.Write(this);

    /// <summary>用于调试与测试：每段格式的简要描述。</summary>
    public override string ToString()
    {
        StringBuilder builder = new();
        foreach (RichTextSpan span in _spans)
        {
            builder.Append('[').Append(span.Start).Append('+').Append(span.Length).Append("] ");
            if (span.Format.Bold) builder.Append("B");
            if (span.Format.Italic) builder.Append("I");
            if (span.Format.Underline) builder.Append("U");
            if (span.Format.Foreground is { } fg) builder.Append(fg.ToHex());
            if (span.Format.Highlight is { } hl) builder.Append("hl").Append(hl.ToHex());
            builder.Append(' ').Append(Text.Substring(span.Start, Math.Min(span.Length, 20))).AppendLine();
        }

        return builder.ToString();
    }

    private readonly record struct Split(int Start, int End);
}
