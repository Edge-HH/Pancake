using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;
using Pancake.RichText;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 富文本悬浮岛：编辑作业文字时贴在控制窗旁，承载加粗、斜体、下划线、文字颜色与高光。
/// 与旧版一致，工具作用于“最近获得焦点的那个编辑器”的当前选区。
/// </summary>
public sealed partial class MainWindow
{
    private SubjectTileControl? _richTextTile;
    private RichTextEditor? _activeEditor;

    /// <summary>磁贴里的编辑器获得焦点时刷新悬浮岛内容并显示。</summary>
    private void OnEntryEditorFocused(SubjectTileControl tile, RichTextEditor editor)
    {
        if (!_isEditing) return;
        _richTextTile = tile;
        _activeEditor = editor;
        BuildRichTextIslandTools();
        RichTextIsland.IsVisible = true;
        ApplyIslandPlacement();
    }

    /// <summary>编辑态结束、切换到画笔或磁贴重建时收起悬浮岛。</summary>
    private void HideRichTextIsland()
    {
        _richTextTile = null;
        _activeEditor = null;
        RichTextIsland.IsVisible = false;
    }

    /// <summary>重建格式工具；每次换编辑器都重建，保证工具作用于当前编辑器。</summary>
    private void BuildRichTextIslandTools()
    {
        RichTextIslandItems.Children.Clear();
        if (_activeEditor is not { } editor) return;
        // 与旧版一致：字体排在第一位，其后是加粗、斜体、下划线与两种颜色。
        RichTextIslandItems.Children.Add(CreateIslandFontButton(editor));
        RichTextIslandItems.Children.Add(CreateIslandIconButton(nameof(FluentGlyphs.Bold), "加粗", () => editor.ApplyFormat(format => format.ToggleBold())));
        RichTextIslandItems.Children.Add(CreateIslandIconButton(nameof(FluentGlyphs.Italic), "斜体", () => editor.ApplyFormat(format => format.ToggleItalic())));
        RichTextIslandItems.Children.Add(CreateIslandIconButton(nameof(FluentGlyphs.Underline), "下划线", () => editor.ApplyFormat(format => format.ToggleUnderline())));
        RichTextIslandItems.Children.Add(CreateIslandColorButton(editor, highlight: false));
        RichTextIslandItems.Children.Add(CreateIslandColorButton(editor, highlight: true));
        // 工具每次重建都会生成新的按钮，重排后补上无字模式下的名称。
        ApplyIslandLabels();
    }

    private static Button CreateIslandIconButton(string symbol, string tooltip, Action click)
    {
        Button button = new()
        {
            MinWidth = 40,
            MinHeight = 40,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Content = new FluentIcon { Symbol = symbol, FontSize = 16 },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// 字体选择：可搜索的家族列表放在浮层里，竖版控制窗那一列宽度也放得下。
    /// 选中内置字体相当于「跟随默认」，不会把内置家族名写进富文本。
    /// </summary>
    private static Button CreateIslandFontButton(RichTextEditor editor)
    {
        int selectionStart = 0;
        int selectionLength = 0;
        Button button = CreateIslandIconButton(nameof(FluentGlyphs.TextFont), "字体", () =>
        {
            // 弹出浮层会抢走编辑器焦点，打开前先记住当前选区。
            selectionStart = editor.SelectionStart;
            selectionLength = editor.SelectionLength;
        });

        IReadOnlyList<string> families = FontService.SelectableFamilies();
        StackPanel panel = new() { Spacing = 6, Width = 260 };
        TextBox filter = new() { Watermark = "搜索字体", MaxLength = 40 };
        ListBox list = new() { Height = 260, ItemsSource = families };
        panel.Children.Add(filter);
        panel.Children.Add(list);
        Flyout flyout = new() { Content = panel };
        filter.TextChanged += (_, _) =>
        {
            string query = (filter.Text ?? string.Empty).Trim();
            list.ItemsSource = query.Length == 0
                ? families
                : families.Where(name => name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(80).ToArray();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string family) return;
            string? value = family.Equals(FontService.FamilyName, StringComparison.CurrentCultureIgnoreCase) ? null : family;
            editor.ApplyFormat(selectionStart, selectionLength, format => format with { FontFamily = value });
            flyout.Hide();
        };
        button.Flyout = flyout;
        return button;
    }

    /// <summary>
    /// 文字颜色与高光调色板。打开菜单前记录选区，点色块时按记录范围套用，
    /// 避免弹出菜单抢走编辑器焦点后选区丢失。
    /// </summary>
    private Button CreateIslandColorButton(RichTextEditor editor, bool highlight)
    {
        int selectionStart = 0;
        int selectionLength = 0;
        Button button = CreateIslandIconButton(
            highlight ? nameof(FluentGlyphs.Highlight) : nameof(FluentGlyphs.Color),
            highlight ? "高光颜色" : "文字颜色",
            () =>
            {
                // 点击按钮先记录选区：菜单打开后编辑器会失焦。
                selectionStart = editor.SelectionStart;
                selectionLength = editor.SelectionLength;
            });
        WrapPanel colors = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        void ApplyToSelection(Func<RichTextFormat, RichTextFormat> transform)
        {
            editor.ApplyFormat(selectionStart, selectionLength, transform);
            button.Flyout?.Hide();
        }

        if (highlight)
        {
            Button clear = CreateIslandSwatch("#F7F7F9", 30);
            ToolTip.SetTip(clear, "取消高光");
            clear.Click += (_, _) => ApplyToSelection(format => format with { Highlight = null });
            colors.Children.Add(clear);
        }

        IEnumerable<string> hexes = highlight
            ? new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA", "#4ADE80", "#F472B6" }.Select(ColorPalette.Resolve)
            : new[] { "#000000", "#FFFFFF" }.Concat(
                new[] { "#FBBF24", "#F87171", "#60A5FA", "#4ADE80", "#F472B6" }.Select(ColorPalette.Resolve));
        foreach (string hex in hexes)
        {
            Button swatch = CreateIslandSwatch(hex, 30);
            string captured = hex;
            swatch.Click += (_, _) =>
            {
                BoardColor color = BoardColor.Parse(captured, BoardColor.White);
                ApplyToSelection(format => highlight ? format with { Highlight = color } : format with { Foreground = color });
            };
            colors.Children.Add(swatch);
        }

        button.Flyout = new Flyout { Content = colors };
        return button;
    }

    private static Button CreateIslandSwatch(string hex, double size)
    {
        BoardColor color = BoardColor.Parse(hex, BoardColor.White);
        return new Button
        {
            Width = size,
            Height = size,
            Margin = new Thickness(3),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(color.ToColor()),
            BorderBrush = new SolidColorBrush(BoardColor.FromArgb(64, 255, 255, 255).ToColor()),
            BorderThickness = new Thickness(1)
        };
    }

    /// <summary>悬浮岛是否正在显示；供布局与验证读取。</summary>
    internal bool IsRichTextIslandVisible => RichTextIsland.IsVisible;
}
