using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Services;
using Pancake.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace Pancake.Controls;

/// <summary>一条补全候选：正文、可选补充说明与可选的学科色点。</summary>
public sealed class AutofillEntry
{
    public required string Text { get; init; }
    public string? Detail { get; init; }
    public string? ColorHex { get; init; }
    public object? Payload { get; init; }
}

/// <summary>
/// 补全候选浮层。Popup 挂在窗口根面板上，因此不会被磁贴裁剪或缩放影响；
/// 候选行不参与焦点，鼠标与触摸点击都不会打断输入框的编辑状态。
/// </summary>
public sealed class AutofillPopup
{
    /// <summary>空标题时展示的推荐学科条数，浮层内部可滚动。</summary>
    public const int RecommendationLimit = 12;
    /// <summary>浮层一次最多显示几行候选，多出来的在浮层内滚动。</summary>
    public const int VisibleRowLimit = 5;
    /// <summary>浮层宽度范围：既要能一行放下常见学科名，也不能在窄窗口里溢出。</summary>
    public const double MinimumWidth = 240;
    public const double MaximumWidth = 520;

    private readonly Popup _popup = new() { IsLightDismissEnabled = false };
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly Border _frame;
    private readonly List<AutofillEntry> _entries = [];
    private readonly List<Border> _visuals = [];
    private Brush _rowTextBrush = BoardTheme.TextBrush;
    private bool _light;
    private bool _themed;
    private int _highlight = -1;
    private object? _owner;
    private Action<AutofillEntry>? _chosen;
    private FrameworkElement? _anchor;
    private Rect? _caret;

    public AutofillPopup()
    {
        // 长候选列表在浮层内部滚动，一次最多五行，滚动到窗口外也不会顶到磁贴。
        _scroll = new ScrollViewer
        {
            Content = _rows,
            MaxHeight = 320,
            // 透明背景让行间空隙也落在滚动容器上，触屏滑动不会在缝隙处失效。
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            // 触屏拖动需要滚动容器自己处理平移，候选行因此只用 Tapped 选中，不吞掉指针按下。
            ManipulationMode = ManipulationModes.System,
            // 候选窗不接收焦点：点击后按键仍归输入框，且关闭时不会把焦点留在浮层里。
            IsTabStop = false
        };
        _frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            MinWidth = MinimumWidth,
            MaxWidth = MaximumWidth,
            Child = _scroll,
            IsTabStop = false
        };
        // 浮层是代码创建的控件，不会跟随主题资源引用自动换色，构造后先按当前主题取一次色。
        ApplyTheme(BoardTheme.IsLight ? ElementTheme.Light : ElementTheme.Dark);
        // 万一焦点落进浮层（触摸或辅助功能），按键也由浮层自己处理。
        _frame.KeyDown += (_, args) => HandleKey(args);
        _popup.Child = _frame;
    }

    public bool IsOpen => _popup.IsOpen;

    public int Count => _entries.Count;

    public AutofillEntry? Highlighted => _highlight >= 0 && _highlight < _entries.Count ? _entries[_highlight] : null;

    /// <summary>浮层当前是否由指定输入框打开；共用同一个浮层时必须先判断归属。</summary>
    public bool IsOwnedBy(object owner) => IsOpen && ReferenceEquals(_owner, owner);

    /// <summary>把浮层挂到窗口根面板；Popup 自身不参与排版，只按偏移量绘制。</summary>
    public void AttachTo(Panel host)
    {
        host.Children.Add(_popup);
        if (host is Grid grid) Grid.SetRowSpan(_popup, Math.Max(1, grid.RowDefinitions.Count));
    }

    /// <param name="preserveHighlight">
    /// 输入法组合期间候选会随着组合文本反复刷新，列表变化时保留用户已经移动过的选中项，
    /// 避免选中跳回第一条；普通输入（组合已结束）仍然回到最佳匹配。
    /// </param>
    public void Show(object owner, FrameworkElement anchor, IReadOnlyList<AutofillEntry> entries, Action<AutofillEntry> chosen, Rect? caret = null, bool preserveHighlight = false)
    {
        if (entries.Count == 0 || anchor.XamlRoot is null)
        {
            Hide();
            return;
        }

        // 浮层可能跨主题复用，每次显示都按锚点当前实际主题重新取色。
        ApplyTheme(anchor.ActualTheme);

        // 相同来源、相同候选时只重新定位，避免每次按键都重建列表而闪烁。
        if (IsOpen && ReferenceEquals(_owner, owner) && SameEntries(entries))
        {
            _chosen = chosen;
            ApplyHighlight();
            Place(anchor, caret);
            return;
        }

        _owner = owner;
        _chosen = chosen;
        _anchor = anchor;
        _caret = caret;
        string? keep = preserveHighlight ? Highlighted?.Text : null;
        _entries.Clear();
        _entries.AddRange(entries);
        BuildRows();
        _highlight = keep is null ? -1 : _entries.FindIndex(entry => entry.Text == keep);
        if (_highlight < 0) _highlight = 0;
        ApplyHighlight();
        _popup.XamlRoot ??= anchor.XamlRoot;
        _popup.IsOpen = true;
        Place(anchor, caret);
    }

    private bool SameEntries(IReadOnlyList<AutofillEntry> entries)
    {
        if (_entries.Count != entries.Count) return false;
        for (int index = 0; index < entries.Count; index++)
            if (!ReferenceEquals(_entries[index].Payload, entries[index].Payload)) return false;
        return true;
    }

    public void Hide()
    {
        if (!_popup.IsOpen) return;
        _popup.IsOpen = false;
        _entries.Clear();
        _visuals.Clear();
        _rows.Children.Clear();
        _highlight = -1;
        _owner = null;
        _chosen = null;
        _anchor = null;
        _caret = null;
    }

    public void HideIfOwnedBy(object owner)
    {
        if (IsOwnedBy(owner)) Hide();
    }

    /// <summary>看板滚动后把浮层重新贴回输入位置；锚点已被移除时直接收起。</summary>
    public void Reposition()
    {
        if (!IsOpen) return;
        if (_anchor is null || _anchor.XamlRoot is null)
        {
            Hide();
            return;
        }

        // 浮层显示期间切换主题时也要跟上，避免深色底配浅色字或反过来。
        ApplyTheme(_anchor.ActualTheme);
        Place(_anchor, _caret);
    }

    public void MoveHighlight(int delta)
    {
        if (_entries.Count == 0) return;
        _highlight = (_highlight + delta + _entries.Count) % _entries.Count;
        ApplyHighlight();
    }

    public bool ChooseHighlighted()
    {
        if (Highlighted is not { } entry) return false;
        Choose(entry);
        return true;
    }

    public void Choose(AutofillEntry entry)
    {
        Action<AutofillEntry>? chosen = _chosen;
        Hide();
        chosen?.Invoke(entry);
    }

    /// <summary>浮层自身的键盘处理，与窗口根面板共用同一套动作。</summary>
    public bool HandleKey(KeyRoutedEventArgs args)
    {
        if (!IsOpen) return false;
        switch (args.Key)
        {
            case VirtualKey.Up:
                MoveHighlight(-1);
                break;
            case VirtualKey.Down:
                MoveHighlight(1);
                break;
            case VirtualKey.Tab:
            case VirtualKey.Enter:
                if (!ChooseHighlighted()) return false;
                break;
            case VirtualKey.Escape:
                Hide();
                break;
            default:
                return false;
        }

        args.Handled = true;
        return true;
    }

    private void BuildRows()
    {
        _rows.Children.Clear();
        _visuals.Clear();
        foreach (AutofillEntry entry in _entries)
        {
            Grid content = new() { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (entry.ColorHex is { Length: > 0 } hex)
            {
                Ellipse dot = new()
                {
                    Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center,
                    Fill = MainViewModel.BrushFromHex(ColorPalette.Resolve(hex))
                };
                content.Children.Add(dot);
            }

            TextBlock text = new()
            {
                Text = entry.Text,
                FontSize = 15,
                Foreground = _rowTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            if (!string.IsNullOrEmpty(entry.Detail))
            {
                TextBlock detail = new()
                {
                    Text = entry.Detail,
                    FontSize = 12,
                    Opacity = .7,
                    Foreground = _rowTextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(detail, 2);
                content.Children.Add(detail);
            }

            Border row = new()
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = content,
                Tag = entry
            };
            int index = _visuals.Count;
            row.PointerEntered += (_, _) =>
            {
                _highlight = index;
                ApplyHighlight();
            };
            // 用 Tapped 而不是 PointerPressed 选中：按下就选中会让触屏拖动被当成点击，
            // 也没法把平移交给滚动容器，候选列表在触屏上就滑不动。
            row.Tapped += (_, args) =>
            {
                args.Handled = true;
                Choose(entry);
            };
            _visuals.Add(row);
            _rows.Children.Add(row);
        }

        LimitVisibleRows();
    }

    /// <summary>
    /// 按真实行高限制浮层高度：一次最多露出 VisibleRowLimit 行，其余在浮层内部滚动。
    /// 行高由字号和内边距决定，这里量真实行而不是写死数字，改字体或缩放后同样成立。
    /// </summary>
    private void LimitVisibleRows()
    {
        _rows.Measure(new Size(MaximumWidth, double.PositiveInfinity));
        int count = Math.Min(VisibleRowLimit, _rows.Children.Count);
        double height = Math.Max(0, count - 1) * _rows.Spacing;
        for (int index = 0; index < count; index++)
        {
            height += _rows.Children[index].DesiredSize.Height;
        }

        _scroll.MaxHeight = Math.Max(1, height);
    }

    private void ApplyHighlight()
    {
        for (int index = 0; index < _visuals.Count; index++)
        {
            _visuals[index].Background = new SolidColorBrush(index == _highlight ? HighlightColor() : Microsoft.UI.Colors.Transparent);
        }
    }

    /// <summary>
    /// 按实际主题解析浮层配色。代码创建的控件不会随主题资源引用自动换色，
    /// 因此主题变化后必须重新取色，否则浅色模式会保留深色背景而文字已经变黑。
    /// </summary>
    private void ApplyTheme(ElementTheme theme)
    {
        bool light = theme == ElementTheme.Light;
        if (_themed && _light == light) return;
        _themed = true;
        _light = light;
        _frame.Background = ThemedBrush("BoardSurfaceElevatedBrush", light, BoardTheme.SurfaceColorFor(light));
        _frame.BorderBrush = ThemedBrush("BoardLineBrush", light, BoardTheme.LineColorFor(light));
        _rowTextBrush = ThemedBrush("BoardTextBrush", light, BoardTheme.TextColorFor(light));
        // 候选行已经生成时同步换色，避免主题切换后新旧颜色混在一起。
        foreach (Border row in _visuals)
        {
            if (row.Child is Grid content)
            {
                foreach (TextBlock text in content.Children.OfType<TextBlock>())
                {
                    text.Foreground = _rowTextBrush;
                }
            }
        }
    }

    private void Place(FrameworkElement anchor, Rect? caret)
    {
        if (_popup.Parent is not UIElement parent || anchor.XamlRoot is null) return;
        Rect bounds = new(0, 0, Math.Max(1, anchor.ActualWidth), Math.Max(1, anchor.ActualHeight));
        if (caret is { } caretRect && caretRect.Width >= 0 && caretRect.Height > 0)
            bounds = new Rect(caretRect.X, caretRect.Y, Math.Max(1, caretRect.Width), caretRect.Height);
        Point origin = anchor.TransformToVisual(parent).TransformPoint(new Point(bounds.X, bounds.Y));

        _frame.Measure(new Size(MaximumWidth, double.PositiveInfinity));
        double parentWidth = parent is FrameworkElement element ? element.ActualWidth : MaximumWidth;
        double parentHeight = parent is FrameworkElement host ? host.ActualHeight : 0;
        // 候选名较长时放宽到 MaxWidth；窗口比浮层还窄时再收窄到可用宽度，避免被裁掉。
        double width = Math.Clamp(_frame.DesiredSize.Width, MinimumWidth, MaximumWidth);
        width = Math.Min(width, Math.Max(MinimumWidth, parentWidth - 16));
        double height = Math.Max(1, _frame.DesiredSize.Height);
        double x = Math.Clamp(origin.X, 8, Math.Max(8, parentWidth - width - 8));
        double below = origin.Y + bounds.Height + 6;
        double above = origin.Y - height - 6;
        double y = below + height > parentHeight - 8 && above >= 8 ? above : below;
        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = Math.Max(8, Math.Min(y, Math.Max(8, parentHeight - height - 8)));
    }

    private Windows.UI.Color HighlightColor() => _light
        ? Windows.UI.Color.FromArgb(255, 232, 232, 251)
        : Windows.UI.Color.FromArgb(255, 42, 43, 88);

    /// <summary>主题字典里的画笔本身随主题切换，必须显式指定要取哪套，不能依赖控件自身的资源引用。</summary>
    private static Brush ThemedBrush(string key, bool light, Windows.UI.Color fallback)
    {
        return LookupThemedBrush(Application.Current.Resources, key, light ? "Light" : "Dark")
            ?? new SolidColorBrush(fallback);
    }

    // 主题字典位于合并的资源字典里（Themes/ThemeResources.xaml），必须递归查找才能按主题取到画笔。
    private static Brush? LookupThemedBrush(ResourceDictionary dictionary, string key, string themeKey)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out object? value) &&
            value is ResourceDictionary themes &&
            themes.TryGetValue(key, out object? entry) &&
            entry is Brush brush)
        {
            return brush;
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (LookupThemedBrush(merged, key, themeKey) is { } found) return found;
        }

        return null;
    }
}
