using System.Globalization;
using System.Text;

namespace Pancake.RichText;

/// <summary>
/// RTF 读写。范围为旧版 RichEditBox 实际会写出的特性：
/// 字体表、颜色表、加粗、斜体、下划线、文字颜色、高光，以及段落与换行。
/// 其它控制字（样式表、页面设置、图片等）在读取时忽略，在写入时不生成。
/// </summary>
public static class RtfCodec
{
    /// <summary>随包字体在 RTF 中使用的标准家族名，与旧版保存的写法一致。</summary>
    public const string DefaultFontFamily = "HarmonyOS Sans SC";

    private const int DefaultFontSizeHalfPoints = 40; // 20pt，与旧版磁贴正文一致。

    /// <summary>
    /// 解析 RTF。<paramref name="fallbackPlainText"/> 是项目里同时保存的纯文本，
    /// 解析失败或 RTF 为空时使用；旧版误写入的尾部空段落也按纯文本边界去掉。
    /// </summary>
    public static RichTextDocument Parse(string? rtf, string? fallbackPlainText)
    {
        string fallback = fallbackPlainText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rtf) || !rtf.Contains("\\rtf", StringComparison.Ordinal))
        {
            return RichTextDocument.FromPlainText(fallback);
        }

        RichTextDocument document;
        try
        {
            document = new Parser(rtf).Parse();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException)
        {
            // 损坏的 RTF 不应让看板无法显示，退回纯文本。
            return RichTextDocument.FromPlainText(fallback);
        }

        // 纯文本是可信边界：RTF 以纯文本开头、只在尾部多出换行时，把多余段落裁掉。
        // 旧版本会把 RichEditBox 自动维护的段落标记写进 RTF，逐次保存会不断累积换行。
        while (!fallback.EndsWith('\n') &&
               document.Text.Length > fallback.Length &&
               document.Text.EndsWith('\n') &&
               document.Text.StartsWith(fallback, StringComparison.Ordinal))
        {
            document.Replace(document.Text.Length - 1, 1, string.Empty);
        }

        // 截断的 RTF 可能解析出空内容；此时纯文本才是可信来源，绝不能把内容显示成空白。
        if (document.Text.Length == 0 && fallback.Length > 0)
        {
            return RichTextDocument.FromPlainText(fallback);
        }

        return document;
    }

    /// <summary>把文档写成 RichEditBox 可以直接载入的 RTF。</summary>
    public static string Write(RichTextDocument document)
    {
        List<BoardColor> colors = [];
        int ColorIndex(BoardColor? color)
        {
            if (color is not { } value) return 0;
            int existing = colors.FindIndex(item => item.Equals(value));
            if (existing >= 0) return existing + 1;
            colors.Add(value);
            return colors.Count;
        }

        // 字体表按片段实际用到的家族收集：默认家族固定在 f0，其它家族依次编号。
        // 旧版只用单一字体表，按片段设置的字体保存后会丢失，这里补上。
        List<string> fonts = [DefaultFontFamily];
        int FontIndex(string? family)
        {
            string name = string.IsNullOrWhiteSpace(family) ? DefaultFontFamily : family;
            int existing = fonts.FindIndex(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) return existing;
            fonts.Add(name);
            return fonts.Count - 1;
        }

        StringBuilder runs = new();
        RichTextFormat previous = RichTextFormat.Default;
        bool first = true;
        foreach (RichTextSpan span in document.Spans)
        {
            if (span.Length <= 0) continue;
            RichTextFormat format = span.Format;
            int font = FontIndex(format.FontFamily);
            if (first || font != FontIndex(previous.FontFamily)) runs.Append(@"\f").Append(font);
            if (first || format.Bold != previous.Bold) runs.Append(format.Bold ? @"\b" : @"\b0");
            if (first || format.Italic != previous.Italic) runs.Append(format.Italic ? @"\i" : @"\i0");
            if (first || format.Underline != previous.Underline) runs.Append(format.Underline ? @"\ul" : @"\ulnone");
            int foreground = ColorIndex(format.Foreground);
            if (first || foreground != ColorIndex(previous.Foreground)) runs.Append(@"\cf").Append(foreground);
            int highlight = ColorIndex(format.Highlight);
            if (first || highlight != ColorIndex(previous.Highlight)) runs.Append(@"\highlight").Append(highlight);
            runs.Append(' ');
            AppendEscaped(runs, document.Text.AsSpan(span.Start, span.Length));
            previous = format;
            first = false;
        }

        StringBuilder result = new();
        result.Append(@"{\rtf1\ansi\ansicpg65001\deff0");
        result.Append(@"{\fonttbl");
        for (int index = 0; index < fonts.Count; index++)
        {
            // 中文优先用 134 代码页；非 ASCII 家族名按 RTF 规则转义。
            result.Append(@"{\f").Append(index).Append(@"\fnil\fcharset134 ");
            AppendEscaped(result, fonts[index].AsSpan());
            // 每个字体条目是独立的组：分号结束名字，右花括号结束条目。
            result.Append(";}");
        }

        result.Append('}');
        result.Append(@"{\colortbl ;");
        foreach (BoardColor color in colors)
        {
            result.Append(@"\red").Append(color.R).Append(@"\green").Append(color.G).Append(@"\blue").Append(color.B).Append(';');
        }

        result.Append('}');
        result.Append(@"\viewkind4\uc1\pard\f0\fs").Append(DefaultFontSizeHalfPoints).Append(' ');
        result.Append(runs);
        result.Append(@"\par}");
        return result.ToString();
    }

    /// <summary>非 ASCII 字符写成 \uN? 形式，保证在任何代码页下都不丢字。</summary>
    private static void AppendEscaped(StringBuilder builder, ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\\"); break;
                case '{': builder.Append(@"\{"); break;
                case '}': builder.Append(@"\}"); break;
                case '\n': builder.Append(@"\par "); break;
                case '\r': break;
                case '\t': builder.Append(@"\tab "); break;
                default:
                    if (c < 128) builder.Append(c);
                    else builder.Append(@"\u").Append((short)c).Append('?');
                    break;
            }
        }
    }

    private enum Destination
    {
        Body,
        ColorTable,
        FontTable,
        Ignored
    }

    private sealed class Parser(string source)
    {
        private readonly StringBuilder _text = new();
        private readonly List<RichTextSpan> _spans = [];
        // 颜色表第一项由 RTF 里的首个分号（自动色）填充，因此这里从空列表开始，
        // 保证 \cfN 的下标与写入时的 0 基偏移完全对应。
        private readonly List<BoardColor> _colors = [];
        private readonly Dictionary<int, string> _fonts = [];
        private readonly Stack<(RichTextFormat Format, Destination Destination, bool Skip)> _stack = new();
        private RichTextFormat _format = RichTextFormat.Default;
        private Destination _destination = Destination.Body;
        private bool _skip;
        private int _index;
        private int _unicodeSkipCount = 1;
        private int _pendingSkip;
        private int _red = -1;
        private int _green;
        private int _blue;
        private int _fontIndex;
        private readonly StringBuilder _fontName = new();

        public RichTextDocument Parse()
        {
            while (_index < source.Length) Read();
            return RichTextDocument.FromSpans(_text.ToString(), _spans);
        }

        private void Read()
        {
            char c = source[_index];
            switch (c)
            {
                case '{':
                    _stack.Push((_format, _destination, _skip));
                    _index++;
                    // \* 开头的目标在 RTF 里被定义为可忽略，直接跳过整组内容。
                    if (_index + 1 < source.Length && source[_index] == '\\' && source[_index + 1] == '*')
                    {
                        _skip = true;
                        _index += 2;
                    }

                    return;
                case '}':
                    if (_stack.Count > 0) (_format, _destination, _skip) = _stack.Pop();
                    _index++;
                    return;
                case '\\':
                    ReadControl();
                    return;
                default:
                    Append(c);
                    _index++;
                    return;
            }
        }

        private void ReadControl()
        {
            _index++;
            if (_index >= source.Length) return;
            char c = source[_index];
            if (!char.IsLetter(c))
            {
                _index++;
                switch (c)
                {
                    case '\\': Append('\\'); break;
                    case '{': Append('{'); break;
                    case '}': Append('}'); break;
                    case '~': Append('\u00A0'); break;
                    case '\'': ReadHexByte(); break;
                    default: break; // \-、\_ 等控制符号对正文没有影响。
                }

                return;
            }

            int start = _index;
            while (_index < source.Length && char.IsLetter(source[_index])) _index++;
            string word = source[start.._index];
            int? parameter = null;
            if (_index < source.Length && (source[_index] == '-' || char.IsDigit(source[_index])))
            {
                int sign = 1;
                if (source[_index] == '-')
                {
                    sign = -1;
                    _index++;
                }

                int digits = _index;
                while (_index < source.Length && char.IsDigit(source[_index])) _index++;
                if (_index > digits)
                {
                    parameter = sign * int.Parse(source.AsSpan(digits, _index - digits), CultureInfo.InvariantCulture);
                }
            }

            if (_index < source.Length && source[_index] == ' ') _index++;
            HandleWord(word, parameter);
        }

        /// <summary>\'hh 形式的字节；旧版只在 ASCII 范围外使用，这里按 Latin-1 还原。</summary>
        private void ReadHexByte()
        {
            int value = 0;
            for (int i = 0; i < 2; i++)
            {
                if (_index >= source.Length) return;
                int digit = HexValue(source[_index++]);
                if (digit < 0) return;
                value = value * 16 + digit;
            }

            Append((char)value);
        }

        private static int HexValue(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };

        private void HandleWord(string word, int? parameter)
        {
            if (_skip) return;
            switch (word)
            {
                case "colortbl": _destination = Destination.ColorTable; return;
                case "fonttbl": _destination = Destination.FontTable; return;
                case "cocoartf": _destination = Destination.Ignored; return;
                case "u":
                    int code = parameter ?? 0;
                    if (code < 0) code += 65536;
                    Append((char)code);
                    _pendingSkip = _unicodeSkipCount;
                    return;
                case "uc": _unicodeSkipCount = parameter ?? 1; return;
                case "par": case "line": Append('\n'); return;
                case "tab": Append('\t'); return;
            }

            if (_destination == Destination.ColorTable)
            {
                switch (word)
                {
                    case "red": _red = parameter ?? -1; break;
                    case "green": _green = parameter ?? 0; break;
                    case "blue": _blue = parameter ?? 0; break;
                }

                return;
            }

            if (_destination == Destination.FontTable)
            {
                if (word == "f") _fontIndex = parameter ?? 0;
                return;
            }

            if (_destination == Destination.Ignored) return;
            switch (word)
            {
                case "b": _format = _format with { Bold = parameter is not 0 }; break;
                case "i": _format = _format with { Italic = parameter is not 0 }; break;
                case "ul": _format = _format with { Underline = parameter is not 0 }; break;
                case "ulnone": _format = _format with { Underline = false }; break;
                case "cf":
                    _format = _format with { Foreground = parameter is > 0 && parameter < _colors.Count ? _colors[parameter.Value] : null };
                    break;
                case "highlight":
                    _format = _format with { Highlight = parameter is > 0 && parameter < _colors.Count ? _colors[parameter.Value] : null };
                    break;
                case "f":
                    _format = _format with
                    {
                        FontFamily = parameter is { } index && _fonts.TryGetValue(index, out string? family) ? family : _format.FontFamily
                    };
                    break;
                default: break; // 字号、上下标、行距等由界面决定，解析时忽略。
            }
        }

        /// <summary>按当前目标决定一个字符是正文、颜色表分隔符还是字体名。</summary>
        private void Append(char c)
        {
            if (_skip) return;
            if (_pendingSkip > 0)
            {
                // \uN 之后的 \ucN 个字符是给不支持 Unicode 的阅读器的回退内容，丢弃。
                _pendingSkip--;
                return;
            }

            switch (_destination)
            {
                case Destination.ColorTable:
                    if (c == ';') FlushColor();
                    return;
                case Destination.FontTable:
                    if (c == ';')
                    {
                        string name = _fontName.ToString().Trim();
                        if (name.Length > 0) _fonts[_fontIndex] = name;
                        _fontName.Clear();
                    }
                    else if (!char.IsControl(c))
                    {
                        _fontName.Append(c);
                    }

                    return;
                case Destination.Ignored:
                    return;
                default:
                    int spanStart = _text.Length;
                    _text.Append(c);
                    if (_spans.Count > 0 && _spans[^1].Format.Equals(_format) && _spans[^1].End == spanStart)
                    {
                        _spans[^1] = _spans[^1] with { Length = _spans[^1].Length + 1 };
                    }
                    else
                    {
                        _spans.Add(new RichTextSpan(spanStart, 1, _format));
                    }

                    return;
            }
        }

        private void FlushColor()
        {
            if (_red < 0)
            {
                // 颜色表的第一项是 "auto"，解析阶段用占位色，读取时按索引 0 视为自动色。
                _colors.Add(BoardColor.Black);
            }
            else
            {
                _colors.Add(BoardColor.FromRgb((byte)Math.Clamp(_red, 0, 255), (byte)Math.Clamp(_green, 0, 255), (byte)Math.Clamp(_blue, 0, 255)));
            }

            _red = -1;
            _green = 0;
            _blue = 0;
        }
    }
}
