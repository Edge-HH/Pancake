using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Windows.Foundation;

namespace Pancake;

/// <summary>
/// 控制窗周边的悬浮岛：编辑模式下把当前作业的富文本工具条（打开画笔时换成画笔控件）贴到控制窗左侧，
/// 竖版控制窗时贴到其上方；作业板缩放控件固定在右侧（竖版控制窗时下方），并且只在编辑模式出现，
/// 查看模式保留缩放结果与平移能力，但不占用屏幕。
/// 两块岛沿用控制窗的尺寸、圆角、背景与无字模式设置，视觉上属于同一套控制面板。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>作业板缩放范围与无限作业板的滚轮缩放保持一致。</summary>
    internal const double MinimumBoardZoom = 0.2;
    internal const double MaximumBoardZoom = 4;
    /// <summary>悬浮岛与控制窗共用的边框宽度，等高（竖版等宽）计算必须把它算进去。</summary>
    private const double IslandBorderThickness = 1;
    /// <summary>缩放档位：逐档增减比固定步长更容易停在常用比例上。</summary>
    private static readonly double[] BoardZoomSteps =
        [.2, .25, .33, .5, .67, .75, .8, .9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];

    private readonly List<(ButtonBase Button, UIElement Icon, TextBlock? Label)> _zoomIslandButtons = [];
    private readonly TranslateTransform _zoomIslandTranslation = new();
    private TextBlock? _zoomLevelText;
    private Slider? _zoomSlider;
    /// <summary>滑条回写缩放比例时抑制它自己的 ValueChanged，避免与档位按钮互相触发。</summary>
    private bool _syncingZoomSlider;
    private double _boardZoom = 1;
    private bool _applyingBoardZoom;
    /// <summary>布局刷新后要按新尺寸重新提交缩放，期间视口的中间状态不算用户改动。</summary>
    private bool _pendingBoardViewReset;
    private bool _wasInfiniteBoard;
    /// <summary>本次排布把浮岛放在控制窗上下（竖版控制窗）还是左右。</summary>
    private bool _islandVerticalBand;
    /// <summary>竖版控制窗为上下两块浮岛让位时临时挪动的纵向偏移；横版或收起浮岛时回到 0。</summary>
    private double _islandControlShift;
    /// <summary>竖版控制窗下承载字体选择框的图标按钮；横版时选择框回到工具条里内联显示。</summary>
    private AutoSuggestBox? _fontPickerHost;
    private Button? _fontPickerButton;
    private FluentIcon? _fontPickerIcon;
    private TextBlock? _fontPickerLabel;

    /// <summary>当前作业板缩放比例，供验证与设置页读取。</summary>
    internal double BoardZoom => _boardZoom;

    private void InitializeFloatingIslands()
    {
        _wasInfiniteBoard = _settings.InfiniteBoard;
        // 自动隐藏的飞出动效要带上缩放岛，否则控制窗飞走时它会留在原地。
        ZoomIsland.RenderTransform = _zoomIslandTranslation;
        // 控制窗尺寸会随缩放、无字模式和按钮增减变化，变化后浮岛要重新贴合。
        FloatingToolbar.SizeChanged += (_, _) => ApplyIslandPlacement();
        // Ctrl+滚轮与触摸捏合直接改 ZoomFactor，这里同步记录，刷新设置时不会退回旧比例。
        BoardScroller.ViewChanged += (_, _) =>
        {
            if (_applyingBoardZoom || _pendingBoardViewReset || Math.Abs(BoardScroller.ZoomFactor - _boardZoom) < .005) return;
            _boardZoom = Math.Clamp(BoardScroller.ZoomFactor, MinimumBoardZoom, MaximumBoardZoom);
            UpdateZoomLevelText();
        };
        BuildZoomIsland();
    }

    /// <summary>
    /// 缩放岛内容：缩小、当前比例（同时是恢复 100% 的入口）、连续缩放滑条、放大。
    /// 滑条与档位按钮共用同一个比例来源，任何一处改动都会同步另一处。
    /// </summary>
    private void BuildZoomIsland()
    {
        ZoomIslandItems.Children.Clear();
        _zoomIslandButtons.Clear();
        ZoomIslandItems.Children.Add(CreateZoomIslandButton(
            new FluentIcon { Symbol = nameof(FluentGlyphs.ZoomOut) }, "缩小作业板", () => StepBoardZoom(-1)));
        _zoomLevelText = new TextBlock { Text = "100%", HorizontalAlignment = HorizontalAlignment.Center };
        ZoomIslandItems.Children.Add(CreateZoomIslandButton(_zoomLevelText, "恢复 100%", () => SetBoardZoom(1)));
        _zoomSlider = new Slider
        {
            Minimum = Math.Round(MinimumBoardZoom * 100),
            Maximum = Math.Round(MaximumBoardZoom * 100),
            StepFrequency = 1,
            SmallChange = 1,
            LargeChange = 10,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(_zoomSlider, "拖动调整作业板比例");
        AutomationProperties.SetName(_zoomSlider, "作业板缩放比例");
        _zoomSlider.ValueChanged += (_, args) =>
        {
            if (_syncingZoomSlider) return;
            SetBoardZoom(args.NewValue / 100);
        };
        ZoomIslandItems.Children.Add(_zoomSlider);
        ZoomIslandItems.Children.Add(CreateZoomIslandButton(
            new FluentIcon { Symbol = nameof(FluentGlyphs.ZoomIn) }, "放大作业板", () => StepBoardZoom(1)));
        UpdateZoomLevelText();
    }

    /// <summary>悬浮岛按钮：图标加一行说明字，说明字始终排在图标下方（与控制窗竖版排布一致）。</summary>
    private Button CreateZoomIslandButton(UIElement icon, string name, Action click)
    {
        // 百分比按钮的文字本身就是内容，再补一行说明字只会重复。
        TextBlock? label = icon is TextBlock ? null : new TextBlock
        {
            Text = name,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed
        };
        StackPanel content = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        if (label is not null) content.Children.Add(label);
        Button button = new()
        {
            Style = (Style)Application.Current.Resources["BoardIconButtonStyle"],
            Content = content,
            BorderThickness = new Thickness(0)
        };
        button.Click += (_, _) => click();
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
        _zoomIslandButtons.Add((button, icon, label));
        return button;
    }

    /// <summary>磁贴只给图标按钮，这里补一行说明字；关闭无字模式后即可看到按钮名称。</summary>
    private static void DecorateRichTextToolbar(FrameworkElement toolbar)
    {
        if (toolbar is not Panel panel) return;
        foreach (UIElement child in panel.Children.ToList())
        {
            // 已经包裹过说明字的按钮内容是纵向容器，跳过以免重复叠加。
            if (child is not ButtonBase button || button.Content is not UIElement icon || icon is StackPanel) continue;
            if (ToolTipService.GetToolTip(button) is not string name || name.Length == 0) continue;
            TextBlock label = new()
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };
            StackPanel content = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(label);
            button.Content = null;
            button.Content = content;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(button, name);
        }
    }

    /// <summary>当前作业获得焦点：把它的工具条接入富文本悬浮岛。</summary>
    private void ShowRichTextToolbar(FrameworkElement toolbar)
    {
        DecorateRichTextToolbar(toolbar);
        RichTextToolbarHost.Content = toolbar;
        RefreshIslands();
    }

    /// <summary>作业失去焦点或结束编辑：撤下工具条并收起悬浮岛。</summary>
    private void HideRichTextToolbar(FrameworkElement toolbar)
    {
        if (!ReferenceEquals(RichTextToolbarHost.Content, toolbar)) return;
        RichTextToolbarHost.Content = null;
        RefreshIslands();
    }

    /// <summary>
    /// 悬浮岛的统一刷新：先决定是否需要出现，再确定排布方向，最后套用外观并重新贴合控制窗。
    /// 竖版控制窗默认把编辑工具排在控制窗上方、缩放控件排在下方；上下放不下时先挪动控制窗让位，
    /// 连同挪动都放不下才退回左右，避免浮岛被压成要在岛内翻页的一条。
    /// </summary>
    private void RefreshIslands()
    {
        if (RichTextIsland is null || ZoomIsland is null) return;
        UpdateRichTextIslandVisibility();
        UpdateZoomIslandVisibility();
        _islandVerticalBand = IsVerticalToolbar;
        ApplyIslandAppearance();
        if (_islandVerticalBand && !FitVerticalBand())
        {
            _islandVerticalBand = false;
            ApplyIslandAppearance();
        }
        // 横版排布（或竖版没有浮岛要放）时控制窗回到用户设置的位置。
        if (!_islandVerticalBand) ApplyIslandControlShift(0);
        ApplyIslandPlacement();
    }

    private bool IsVerticalToolbar => _settings.ToolbarPosition.StartsWith("Center");

    /// <summary>当前占用编辑槽位的浮岛：富文本工具条或画笔控件，笔模式与编辑模式互斥。</summary>
    private FrameworkElement? EditIsland => RichTextIsland.Visibility == Visibility.Visible ? RichTextIsland
        : GlobalInkToolbar.Visibility == Visibility.Visible ? GlobalInkToolbar
        : null;

    /// <summary>
    /// 按对齐方式与边距推算控制窗在窗口里的占位。这里不能用实测坐标：
    /// 切换控制窗位置时布局还没提交，实测结果仍是上一个位置，浮岛会停错地方。
    /// 自动隐藏的飞出动画只改渲染位移，也不会影响推算结果。
    /// </summary>
    private Rect ControlBounds()
    {
        double width = FloatingToolbar.ActualWidth, height = FloatingToolbar.ActualHeight;
        if (width < 1 || height < 1)
        {
            // 尺寸还没测量出来时按内容推算，保证首帧的浮岛位置就是对的。
            FloatingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = FloatingToolbar.DesiredSize.Width;
            height = FloatingToolbar.DesiredSize.Height;
        }
        double windowWidth = RootShell.ActualWidth, windowHeight = RootShell.ActualHeight;
        double insetX = Math.Max(0, _settings.ToolbarHorizontalInset), insetY = Math.Max(0, _settings.ToolbarVerticalInset);
        string position = _settings.ToolbarPosition;
        double left = position.EndsWith("Left") ? insetX
            : position.EndsWith("Right") ? windowWidth - insetX - width
            : (windowWidth - width) / 2;
        double top = position.StartsWith("Top") ? insetY
            : IsVerticalToolbar ? (windowHeight - height) / 2 + _islandControlShift
            : windowHeight - insetY - height;
        return new Rect(left, top, width, height);
    }

    /// <summary>
    /// 竖版控制窗上下要放下两块浮岛：编辑工具在上、缩放控件在下。
    /// 放得下时把控制窗上下挪到刚好放得下的位置，浮岛因此保持自然尺寸，
    /// 不会出现只能在岛内翻页的滚动条；富文本工具条即使挪动也放不下时退回左右排布，
    /// 画笔栏本身比窗口还高，保持上方排布并在岛内滚动。
    /// </summary>
    private bool FitVerticalBand()
    {
        // 控制窗还没完成首次布局时拿不到占位，先按竖版排布，等尺寸变化后再判定。
        if (RootShell.ActualHeight < 1 || FloatingToolbar.ActualHeight < 1) return true;
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double gap = Math.Round(8 * scale);
        double insetY = Math.Max(0, _settings.ToolbarVerticalInset);
        double windowHeight = RootShell.ActualHeight;
        double toolbarHeight = FloatingToolbar.ActualHeight;
        // 多留 1px：布局会做亚像素取整，正好卡满时浮岛会出现徒有其表的滚动条。
        double above = EditIsland is { } editIsland ? MeasureIsland(editIsland).Height + gap + 1 : 0;
        double below = ZoomIsland.Visibility == Visibility.Visible ? MeasureIsland(ZoomIsland).Height + gap + 1 : 0;
        // 缩放控件在下方必须完整放下，它本身不高；连控制窗都放不下说明窗口太小，退回左右排布。
        double largestTop = windowHeight - insetY - below - toolbarHeight;
        if (largestTop < insetY) return false;
        double smallestTop = insetY + above;
        // 富文本工具条必须完整显示，不能被压成岛内翻页的一条：上下都放不下就整块退回左右排布。
        // 画笔栏本身比窗口还高，允许在岛内滚动，因此仍然保持上方排布（见画笔栏的验收约定）。
        if (smallestTop > largestTop && ReferenceEquals(EditIsland, RichTextIsland)) return false;
        // 用户设置的是控制窗居中；这里只在需要让位时上下挪动最小距离。
        double centered = (windowHeight - toolbarHeight) / 2;
        // 上下都放得下就挪到刚好放下的位置；放不下时贴到最低位置，把上方空间都留给编辑工具。
        double top = smallestTop <= largestTop ? Math.Clamp(centered, smallestTop, largestTop) : largestTop;
        ApplyIslandControlShift(top - centered);
        return true;
    }

    /// <summary>
    /// 上下挪动竖版控制窗：上下边距一增一减，可用高度不变，只是整体挪位，
    /// 让上下两块浮岛都能按自然尺寸放下。
    /// </summary>
    private void ApplyIslandControlShift(double shift)
    {
        _islandControlShift = shift;
        if (FloatingToolbar is null) return;
        double x = _settings.ToolbarHorizontalInset, y = _settings.ToolbarVerticalInset;
        FloatingToolbar.Margin = new Thickness(x, y + shift, x, y - shift);
    }

    /// <summary>富文本岛只在编辑模式、非画笔模式、作业板可见且当前有作业获得焦点时出现。</summary>
    private void UpdateRichTextIslandVisibility()
    {
        bool drawing = _isEditing && GlobalPenButton.IsChecked == true;
        bool boardVisible = _settings.LayoutMode is "Split" or "Board" or "Free";
        bool visible = _isEditing && !drawing && boardVisible && RichTextToolbarHost.Content is not null;
        RichTextIsland.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 缩放岛只在编辑模式出现：查看模式不再占用屏幕，但缩放比例继续生效、看板也仍可缩放与平移，
    /// 否则放大过的看板在查看模式里无法拖动查看。仅时钟模式没有作业板，缩放岛同样隐藏。
    /// </summary>
    private void UpdateZoomIslandVisibility()
    {
        bool boardVisible = DisplayRoot.Visibility == Visibility.Visible && CurrentProject is not null &&
            _settings.LayoutMode is "Split" or "Board" or "Free";
        ZoomIsland.Visibility = boardVisible && _isEditing ? Visibility.Visible : Visibility.Collapsed;
        // 自动隐藏期间重新出现时直接沿用控制窗当前的显隐状态，避免先看到一块亮起的浮岛。
        ZoomIsland.Opacity = _toolbarHidden ? 0 : 1;
        ZoomIsland.IsHitTestVisible = !_toolbarHidden;
        // 作业板可见时始终允许缩放与平移；无限作业板本来就允许，这里保持原有行为。
        bool scrollable = boardVisible || _settings.InfiniteBoard;
        BoardScroller.ZoomMode = scrollable ? ZoomMode.Enabled : ZoomMode.Disabled;
        BoardScroller.HorizontalScrollMode = BoardScroller.VerticalScrollMode = scrollable ? ScrollMode.Enabled : ScrollMode.Disabled;
        BoardScroller.HorizontalScrollBarVisibility = BoardScroller.VerticalScrollBarVisibility =
            _settings.InfiniteBoard ? ScrollBarVisibility.Auto : scrollable ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    /// <summary>控制窗按钮内容尺寸：悬浮岛用同一尺寸，两块窗口因此等高（竖版控制窗时等宽）。</summary>
    private double ToolbarContentSize(double scale) => (_settings.ToolbarIconOnly ? 44 : 64) * scale;

    /// <summary>控制窗的高（竖版控制窗为宽）：内容尺寸加内边距与边框，悬浮岛必须与它一致。</summary>
    private static double ToolbarCrossSize(double contentSize, double padding) =>
        contentSize + padding * 2 + IslandBorderThickness * 2;

    /// <summary>悬浮岛外观：内边距、圆角与背景跟随控制窗；按钮尺寸与说明字跟随缩放和无字模式。</summary>
    private void ApplyIslandAppearance()
    {
        if (RichTextIsland is null || ZoomIsland is null || FloatingToolbar is null) return;
        bool vertical = _islandVerticalBand;
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double padding = 7 * scale;
        double contentSize = ToolbarContentSize(scale);
        double crossSize = ToolbarCrossSize(contentSize, padding);
        Brush background = CreateToolbarBackground();
        CornerRadius corner = new(_settings.ToolbarRadius);
        foreach (Border island in new[] { RichTextIsland, ZoomIsland })
        {
            island.Padding = new Thickness(padding);
            island.CornerRadius = corner;
            island.Background = background;
        }
        // 竖版控制窗较窄，浮岛改成与控制窗等宽、竖向排布；横版控制窗则等高、横向排布。
        RichTextIsland.Width = ZoomIsland.Width = vertical ? crossSize : double.NaN;
        RichTextIsland.Height = ZoomIsland.Height = vertical ? double.NaN : crossSize;
        RichTextToolbar.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        RichTextToolbar.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ZoomIslandScroll.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ZoomIslandScroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        // 缩放岛与控制窗同向排布：竖版控制窗时缩放按钮也竖向叠放，宽度才不会超出浮岛。
        ZoomIslandItems.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        ZoomIslandItems.Spacing = 4 * scale;
        foreach (var (button, icon, label) in _zoomIslandButtons)
            StyleIslandButton(button, icon, label, scale, padding, contentSize, vertical);
        // 滑条跟随控制窗排布：横版控制窗留出拖动长度，竖版控制窗改成同样竖排的滑条，
        // 这样窄窄的一列里也有足够的行程，不必靠水平方向那点宽度。
        if (_zoomSlider is not null)
        {
            _zoomSlider.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            _zoomSlider.Width = vertical ? contentSize : Math.Max(80, 100 * scale);
            _zoomSlider.Height = vertical ? Math.Max(59, 80 * scale) : Math.Min(contentSize, 32 * scale);
        }
        ApplyRichTextIslandAppearance(vertical, scale, padding, contentSize);
    }

    /// <summary>富文本岛内容随控制窗排布：横版并排、竖版竖排，字体选择框收缩到内容区内。</summary>
    private void ApplyRichTextIslandAppearance(bool vertical, double scale, double padding, double contentSize)
    {
        if (RichTextToolbarHost.Content is not StackPanel toolbar) return;
        toolbar.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        toolbar.Spacing = 4 * scale;
        // 竖版下字体选择框会在工具条与浮窗之间搬运，遍历副本以免修改集合时中断枚举。
        foreach (UIElement child in toolbar.Children.ToList())
        {
            if (child is AutoSuggestBox fontPicker)
            {
                ApplyFontPickerAppearance(toolbar, fontPicker, vertical, scale, padding, contentSize);
                continue;
            }
            if (child is Button fontButton && _fontPickerButton is not null && ReferenceEquals(fontButton, _fontPickerButton))
            {
                // 竖版下承载字体选择框的图标按钮，样式与其它浮岛按钮一致；
                // 横版时把选择框换回工具条内联显示。
                if (vertical) StyleIslandButton(fontButton, _fontPickerIcon!, _fontPickerLabel, scale, padding, contentSize, vertical);
                else if (_fontPickerHost is not null) ApplyFontPickerAppearance(toolbar, _fontPickerHost, false, scale, padding, contentSize);
                continue;
            }
            if (child is ButtonBase button && button.Content is StackPanel content)
            {
                UIElement? icon = content.Children.OfType<UIElement>().FirstOrDefault(element => element is not TextBlock);
                TextBlock? label = content.Children.OfType<TextBlock>().FirstOrDefault();
                if (icon is not null) StyleIslandButton(button, icon, label, scale, padding, contentSize, vertical);
            }
        }
    }

    /// <summary>
    /// 字体选择框竖版控制窗下一列按钮宽放不下完整输入框，改成字体图标按钮、点开后在浮窗里显示完整选择框；
    /// 横版控制窗恢复成内联输入框。两种形态共用同一个选择框实例，当前字体与候选词因此保持一致。
    /// </summary>
    private void ApplyFontPickerAppearance(StackPanel toolbar, AutoSuggestBox picker, bool vertical, double scale, double padding, double contentSize)
    {
        // 换到另一条作业时会带来新的选择框，旧的图标按钮随之废弃。
        if (!ReferenceEquals(_fontPickerHost, picker))
        {
            _fontPickerHost = picker;
            (_fontPickerButton, _fontPickerIcon, _fontPickerLabel) = CreateFontPickerButton(picker);
        }

        Button button = _fontPickerButton!;
        int index = toolbar.Children.IndexOf(picker);
        if (index < 0) index = toolbar.Children.IndexOf(button);
        if (index < 0) return;

        if (vertical)
        {
            if (!ReferenceEquals(toolbar.Children[index], button))
            {
                // 先摘下来再放进浮窗：同一个元素不能同时挂在两处。
                toolbar.Children.RemoveAt(index);
                if (button.Flyout is Flyout flyout) flyout.Content = picker;
                toolbar.Children.Insert(index, button);
            }
            picker.MinHeight = 0;
            picker.FontSize = 14 * scale;
            picker.Height = double.NaN;
            picker.Width = Math.Max(220, 240 * scale);
            picker.HorizontalAlignment = HorizontalAlignment.Stretch;
            picker.VerticalAlignment = VerticalAlignment.Center;
            StyleIslandButton(button, _fontPickerIcon!, _fontPickerLabel, scale, padding, contentSize, vertical);
            return;
        }

        if (!ReferenceEquals(toolbar.Children[index], picker))
        {
            if (button.Flyout is Flyout flyout) flyout.Content = null;
            toolbar.Children.RemoveAt(index);
            toolbar.Children.Insert(index, picker);
        }
        picker.MinHeight = 0;
        picker.FontSize = 14 * scale;
        picker.Height = Math.Min(32 * scale, contentSize);
        picker.Width = Math.Max(120, 180 * scale);
        picker.HorizontalAlignment = HorizontalAlignment.Left;
        picker.VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>字体图标按钮：与其它浮岛按钮一样是图标加一行说明字，内容由装饰逻辑统一排版。</summary>
    private static (Button Button, FluentIcon Icon, TextBlock Label) CreateFontPickerButton(AutoSuggestBox picker)
    {
        FluentIcon icon = new() { Symbol = nameof(FluentGlyphs.TextFont) };
        TextBlock label = new()
        {
            Text = "字体",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed
        };
        StackPanel content = new() { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(label);
        // 浮窗内容在切换形态时再搬运：元素不能同时挂在工具条和浮窗两处。
        Flyout flyout = new();
        // 选完字体或回车确认后收起浮窗，避免它挡住编辑区；内联形态下这两次隐藏没有副作用。
        picker.SuggestionChosen += (_, _) => flyout.Hide();
        picker.QuerySubmitted += (_, _) => flyout.Hide();
        Button button = new()
        {
            Style = (Style)Application.Current.Resources["BoardIconButtonStyle"],
            Content = content,
            BorderThickness = new Thickness(0),
            Flyout = flyout
        };
        ToolTipService.SetToolTip(button, "字体");
        AutomationProperties.SetName(button, "字体");
        return (button, icon, label);
    }

    /// <summary>
    /// 悬浮岛按钮统一样式：无字模式只显示图标，关闭后在图标的下一行显示说明字。
    /// 圆角按控制窗圆角扣除浮岛内边距计算，和主控制窗按钮保持同心。
    /// </summary>
    private void StyleIslandButton(ButtonBase button, UIElement icon, TextBlock? label, double scale, double padding, double contentSize, bool vertical)
    {
        double size = contentSize;
        button.Width = size;
        button.Height = size;
        button.MinWidth = button.MinHeight = 0;
        button.Padding = new Thickness(6 * scale);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, padding, size, size);
        if (icon is FluentIcon fluent) fluent.FontSize = 18 * scale;
        else if (icon is FontIcon font) font.FontSize = 15 * scale;
        else if (icon is TextBlock text) text.FontSize = 14 * scale;
        if (label is not null)
        {
            label.FontSize = 10 * scale;
            label.Visibility = _settings.ToolbarIconOnly ? Visibility.Collapsed : Visibility.Visible;
        }
        if (button.Content is not StackPanel content) return;
        content.Orientation = Orientation.Vertical;
        content.Spacing = 4 * scale;
    }

    /// <summary>
    /// 按控制窗实际占位摆放两块悬浮岛：默认编辑岛紧贴控制窗左侧、缩放岛在右侧，
    /// 竖版控制窗时对应改成上方与下方；编辑岛一侧放不下时会换到更宽松的另一侧，
    /// 缩放岛固定在右侧（竖版控制窗时下方）不换边；空间仍不足时收窄浮岛并在内部滚动，
    /// 避免压到相邻控件或跑出窗口。
    /// </summary>
    private void ApplyIslandPlacement()
    {
        if (RootShell is null || FloatingToolbar is null || ZoomIsland is null) return;
        double windowWidth = RootShell.ActualWidth, windowHeight = RootShell.ActualHeight;
        // 窗口还没量出尺寸时没有可用的坐标系，等尺寸变化时再排。
        if (windowWidth < 1 || windowHeight < 1) return;
        FrameworkElement? editIsland = EditIsland;
        bool zoomVisible = ZoomIsland.Visibility == Visibility.Visible;
        if (editIsland is null && !zoomVisible) return;

        bool vertical = _islandVerticalBand;
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double gap = Math.Round(8 * scale);
        double insetX = Math.Max(0, _settings.ToolbarHorizontalInset), insetY = Math.Max(0, _settings.ToolbarVerticalInset);
        Rect control = ControlBounds();
        Size editSize = editIsland is null ? default : MeasureIsland(editIsland);

        double beforeSlack = vertical ? control.Top - insetY : control.Left - insetX;
        double afterSlack = vertical ? windowHeight - insetY - control.Bottom : windowWidth - insetX - control.Right;
        // 缩放岛固定在控制窗之后（右侧；竖版控制窗时下方），空间不足时先缩短滑条：
        // 滑条是浮层里唯一能压缩的部件，缩短后整块浮层仍能完整显示，按钮不会被裁掉。
        if (zoomVisible) FitZoomSliderInto(Math.Max(0, afterSlack - gap), vertical, scale);
        Size zoomSize = zoomVisible ? MeasureIsland(ZoomIsland) : default;

        double editSpan = editIsland is null ? 0 : (vertical ? editSize.Height : editSize.Width) + gap;
        // 编辑岛默认占控制窗之前（左/上）一侧；该侧放不下而另一侧更宽松时换到另一侧。
        bool editBefore = editIsland is not null && !(editSpan > beforeSlack && afterSlack > beforeSlack);
        // 缩放岛固定占控制窗之后的一侧，不随空间换边。
        bool zoomBefore = false;

        List<(FrameworkElement Island, Size Size)> before = [], after = [];
        if (editIsland is not null) (editBefore ? before : after).Add((editIsland, editSize));
        if (zoomVisible) (zoomBefore ? before : after).Add((ZoomIsland, zoomSize));

        // 沿控制窗两侧依次排开：before 列表由近到远向一侧延伸，after 列表由近到远向另一侧延伸。
        void Place(List<(FrameworkElement Island, Size Size)> items, bool before, double start, double limit)
        {
            double cursor = start;
            // 内边距和边框无法压缩，摆位时按这个下限推进，浮岛再挤也不会压到相邻控件。
            double floor = 7 * scale * 2 + IslandBorderThickness * 2;
            foreach (var (island, size) in items)
            {
                double extent = vertical ? size.Height : size.Width;
                double available = Math.Max(0, before ? cursor - limit : limit - cursor);
                double placed = Math.Min(extent, available);
                double rendered = Math.Max(placed, floor);
                if (vertical)
                {
                    // 竖版控制窗较窄：浮岛与控制窗等宽，并顺同一条竖边对齐。
                    double cross = Math.Min(size.Width, Math.Max(0, control.Width));
                    PlaceIsland(island, control.Left + (control.Width - cross) / 2, before ? cursor - rendered : cursor, cross, placed);
                }
                else
                {
                    // 横版控制窗：浮岛与控制窗等高，并与控制窗的中线对齐。
                    double cross = Math.Min(size.Height, Math.Max(0, control.Height));
                    PlaceIsland(island, before ? cursor - rendered : cursor, control.Top + (control.Height - cross) / 2, placed, cross);
                }
                cursor = before ? cursor - rendered - gap : cursor + rendered + gap;
            }
        }
        Place(before, true, vertical ? control.Top - gap : control.Left - gap, vertical ? insetY : insetX);
        Place(after, false, vertical ? control.Bottom + gap : control.Right + gap,
            vertical ? windowHeight - insetY : windowWidth - insetX);
    }

    private static void PlaceIsland(FrameworkElement island, double left, double top, double maxWidth, double maxHeight)
    {
        island.HorizontalAlignment = HorizontalAlignment.Left;
        island.VerticalAlignment = VerticalAlignment.Top;
        island.Margin = new Thickness(Math.Max(0, left), Math.Max(0, top), 0, 0);
        // 放不下时收窄到可用空间并在岛内滚动，而不是压住相邻控件。
        island.MaxWidth = Math.Max(0, maxWidth);
        island.MaxHeight = Math.Max(0, maxHeight);
    }

    /// <summary>
    /// 空间紧张时优先缩短滑条。滑条是缩放岛里唯一可以变短的部件，
    /// 缩短后按钮与比例文字仍能完整显示，只有实在放不下才由摆位收窄整块浮层并在岛内滚动。
    /// </summary>
    private void FitZoomSliderInto(double slack, bool vertical, double scale)
    {
        if (_zoomSlider is null) return;
        double desired = vertical ? Math.Max(59, 80 * scale) : Math.Max(80, 100 * scale);
        double current = vertical ? _zoomSlider.Height : _zoomSlider.Width;
        // 首次排布可能早于外观应用，此时滑条还没有固定长度，按零处理后由下一次外观应用纠正。
        if (double.IsNaN(current)) current = 0;
        Size island = MeasureIsland(ZoomIsland);
        // 除滑条以外的部件与内边距：整岛自然尺寸减去滑条当前的占位。
        double others = (vertical ? island.Height - current : island.Width - current);
        double size = Math.Clamp(Math.Min(desired, slack - others), 48 * scale, desired);
        if (vertical)
        {
            if (Math.Abs(_zoomSlider.Height - size) > .5) _zoomSlider.Height = size;
        }
        else if (Math.Abs(_zoomSlider.Width - size) > .5)
        {
            _zoomSlider.Width = size;
        }
    }

    /// <summary>
    /// 测量浮岛的自然尺寸。DesiredSize 里含 Margin（上一轮排布留下的位置偏移），必须减掉，
    /// 否则浮岛会被当成越来越大的块，靠边与换边的判断都会失准；收窄限制也要先清掉。
    /// </summary>
    private static Size MeasureIsland(FrameworkElement island)
    {
        island.MaxWidth = double.PositiveInfinity;
        island.MaxHeight = double.PositiveInfinity;
        island.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Thickness margin = island.Margin;
        return new Size(
            Math.Max(0, island.DesiredSize.Width - margin.Left - margin.Right),
            Math.Max(0, island.DesiredSize.Height - margin.Top - margin.Bottom));
    }

    private void StepBoardZoom(int direction)
    {
        double target = direction > 0
            ? BoardZoomSteps.FirstOrDefault(step => step > _boardZoom + .001, MaximumBoardZoom)
            : BoardZoomSteps.LastOrDefault(step => step < _boardZoom - .001, MinimumBoardZoom);
        SetBoardZoom(target);
    }

    /// <summary>应用作业板缩放：比例交给作业板视口，缩放后仍能滚动到四周的磁贴。</summary>
    internal void SetBoardZoom(double factor)
    {
        _boardZoom = Math.Clamp(factor, MinimumBoardZoom, MaximumBoardZoom);
        UpdateZoomLevelText();
        _applyingBoardZoom = true;
        try
        {
            BoardScroller.ZoomMode = ZoomMode.Enabled;
            BoardScroller.ChangeView(null, null, (float)_boardZoom, true);
        }
        finally { _applyingBoardZoom = false; }
        UpdateBoardBounds();
    }

    private void UpdateZoomLevelText()
    {
        if (_zoomLevelText is not null) _zoomLevelText.Text = $"{Math.Round(_boardZoom * 100)}%";
        // 比例还可能来自档位按钮、Ctrl+滚轮或触摸捏合，滑条要跟着回到同一位置。
        if (_zoomSlider is null || Math.Abs(_zoomSlider.Value - _boardZoom * 100) < .01) return;
        _syncingZoomSlider = true;
        try { _zoomSlider.Value = _boardZoom * 100; }
        finally { _syncingZoomSlider = false; }
    }
}
