using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.RichText;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 富文本编辑器。底层由 <see cref="RichTextPresenter"/> 渲染带格式的文字，
/// 上层叠一个文字透明的 <see cref="TextBox"/>：光标、选区、输入法、剪贴板与撤销
/// 全部复用系统行为，编辑结果以最小差异写回富文本模型，保留其余片段的格式。
/// </summary>
public sealed class RichTextEditor : Grid
{
    private readonly RichTextPresenter _presenter = new();
    private readonly TextBox _input = new();
    private RichTextDocument _document = RichTextDocument.FromPlainText(string.Empty);
    private bool _syncing;
    private bool _isEditing = true;

    public RichTextEditor()
    {
        RowDefinitions = new RowDefinitions("Auto");
        _presenter.FontSize = FontSize;
        _presenter.Margin = new Thickness(0);
        _input.FontSize = FontSize;
        _input.FontFamily = FontService.DefaultFamily;
        _input.FontWeight = FontWeight.Normal;
        _input.AcceptsReturn = true;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.Background = new SolidColorBrush(BoardColor.Transparent.ToColor());
        _input.BorderThickness = new Thickness(0);
        _input.Padding = new Thickness(0);
        _input.MinHeight = 42;
        // 文字层负责显示，输入层只保留光标与选区，因此前景完全透明。
        _input.Foreground = new SolidColorBrush(Colors.Transparent);
        _input.CaretBrush = new SolidColorBrush(BoardTheme.TextColor.ToColor());
        _input.SelectionBrush = new SolidColorBrush(BoardColor.FromArgb(96, 96, 165, 250).ToColor());
        _input.TextChanged += (_, _) => SyncFromInput();
        // 光标移动到别的字体片段时，输入层要跟着换字体，否则光标与显示的文字会错位。
        _input.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.SelectionStartProperty ||
                args.Property == TextBox.SelectionEndProperty ||
                args.Property == TextBox.TextProperty)
            {
                RefreshInputFont();
            }
        };
        Children.Add(_presenter);
        Children.Add(_input);
    }

    /// <summary>编辑区的字号；显示层与输入层必须一致，否则光标会与文字错位。</summary>
    public double FontSize { get; init; } = 20;

    public RichTextDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? RichTextDocument.FromPlainText(string.Empty);
            _presenter.Document = _document;
            SyncToInput();
        }
    }

    /// <summary>编辑态下允许输入与选择；查看态只显示格式。</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            _isEditing = value;
            _input.IsReadOnly = !value;
            _input.IsHitTestVisible = value;
            _input.Cursor = value ? Avalonia.Input.Cursor.Default : null;
        }
    }

    /// <summary>内容变化回调，用于把结果写回作业条目。</summary>
    public Action? ContentChanged { get; set; }

    /// <summary>格式变化回调，用于刷新工具栏按钮状态。</summary>
    public Action? FormatChanged { get; set; }

    public int SelectionStart => _input.SelectionStart;

    public int SelectionLength => Math.Max(0, _input.SelectionEnd - _input.SelectionStart);

    /// <summary>内部输入框；自动填充等需要挂接输入事件的场景使用。</summary>
    public TextBox Input => _input;

    /// <summary>
    /// 对当前选区套用格式变换；没有选区时只影响之后输入的文字（沿用旧版行为）。
    /// </summary>
    public void ApplyFormat(Func<RichTextFormat, RichTextFormat> transform)
    {
        int start = SelectionStart;
        int length = SelectionLength;
        ApplyFormat(start, length, transform);
    }

    /// <summary>对指定范围套用格式；供弹出菜单使用，避免打开菜单时选区丢失。</summary>
    public void ApplyFormat(int start, int length, Func<RichTextFormat, RichTextFormat> transform)
    {
        start = Math.Clamp(start, 0, _document.Text.Length);
        length = Math.Clamp(length, 0, _document.Text.Length - start);
        if (length == 0)
        {
            // 无选区时把格式记在插入点，随后的输入会沿用该格式。
            _document.Replace(start, 0, string.Empty, transform(_document.GetFormatAt(Math.Max(0, start - 1))));
        }
        else
        {
            _document.Apply(start, length, transform);
        }

        // 文档是同一个实例，属性赋值不会触发变化通知，必须显式要求显示层重画，
        // 否则「套用格式」在界面上要等到下一次输入才生效。
        _presenter.Document = _document;
        _presenter.Render();
        ContentChanged?.Invoke();
        RefreshInputFont();
        FormatChanged?.Invoke();
    }

    /// <summary>当前选区的统一格式；混合选区返回 null。</summary>
    public RichTextFormat? SelectionFormat() =>
        _document.GetUniformFormat(SelectionStart, Math.Max(1, SelectionLength));

    /// <summary>主题切换后重画，保证浅色主题下默认文字为黑色。</summary>
    public void RefreshTheme()
    {
        _presenter.Render();
        _input.CaretBrush = new SolidColorBrush(BoardTheme.TextColor.ToColor());
    }

    private void SyncToInput()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            if (_input.Text != _document.Text) _input.Text = _document.Text;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 输入层只提供整段文字，这里求最小差异并写回模型，
    /// 这样一次插入或删除不会清掉其它片段的格式。
    /// </summary>
    private void SyncFromInput()
    {
        if (_syncing) return;
        string next = _input.Text ?? string.Empty;
        (int start, int removed, string inserted) = Diff(_document.Text, next);
        _syncing = true;
        try
        {
            _document.Replace(start, removed, inserted);
        }
        finally
        {
            _syncing = false;
        }

        _presenter.Document = _document;
        _presenter.Render();
        RefreshInputFont();
        ContentChanged?.Invoke();
    }

    /// <summary>
    /// 输入层的字体跟随光标所在片段。输入层文字是透明的，只负责光标与选区，
    /// 但它的字宽必须尽量与显示层一致，否则光标会落在渲染文字的旁边。
    /// 混合字体的段落仍会像加粗/斜体一样存在轻微偏差，这是「透明输入层」方案的固有取舍。
    /// </summary>
    private void RefreshInputFont()
    {
        int length = _document.Text.Length;
        int index = Math.Clamp(_input.SelectionStart, 0, Math.Max(0, length - 1));
        RichTextFormat format = length == 0 ? RichTextFormat.Default : _document.GetFormatAt(index);
        _input.FontFamily = FontService.ResolveRichTextFamily(format.FontFamily) ?? FontService.DefaultFamily;
    }

    /// <summary>求两段文字的最长公共前缀与后缀，得到一次编辑的最小改动范围。</summary>
    internal static (int Start, int Removed, string Inserted) Diff(string oldText, string newText)
    {
        int max = Math.Min(oldText.Length, newText.Length);
        int prefix = 0;
        while (prefix < max && oldText[prefix] == newText[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix &&
               oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;
        return (prefix, oldText.Length - prefix - suffix, newText.Substring(prefix, newText.Length - prefix - suffix));
    }
}
