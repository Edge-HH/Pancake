using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    // 页内预览的固定设计尺寸：经 Viewbox 等比缩放后，窄窗口也不会撑宽设置页。
    private const double PreviewSceneWidth = 640, PreviewSceneHeight = 300;
    // 右侧固定预览区的宽度范围与设置列的最小宽度：宽度不够时预览退回页内顶部，避免把设置控件挤到无法操作。
    private const double StickyPreviewMinWidth = 400, StickyPreviewMaxWidth = 720, MinimumSettingsColumnWidth = 440;
    // 浅色模式下给底图压一层白色提亮，深色图片在浅色设置页里不会再发闷。
    private const double TilePreviewLightOverlayOpacity = .55;

    private readonly Dictionary<string, Grid> _appearancePreviews = [];
    private string _selectedSettingsPage = "";
    private SubjectTileControl? _tileAppearancePreview;
    private Border? _tilePreviewInlineFrame;
    private Border? _tilePreviewStickyFrame;
    private Viewbox? _tilePreviewInlineViewbox;
    private Viewbox? _tilePreviewTileBox;
    private readonly Dictionary<string, (string Key, List<Action> Refresh)> _previewScenes = [];
    private List<Action>? _buildingPreviewRefreshers;

    private UIElement CreateAppearancePreview(string kind)
    {
        if (kind == "Tile") return CreateTileAppearancePreview();
        Grid scene = new() { Width = PreviewSceneWidth, Height = PreviewSceneHeight, IsHitTestVisible = false };
        _appearancePreviews.Add(kind, scene);
        // 固定设计坐标经 Viewbox 等比缩放，窄窗口也不会撑宽设置页。
        Border frame = new()
        {
            Name = kind + "AppearancePreview", MaxWidth = PreviewSceneWidth, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = scene }
        };
        // 边框随设置刷新使用当前主题；预览内容完全只读，不连接保存回调。
        frame.BorderBrush = BoardTheme.LineBrush;
        return frame;
    }

    /// <summary>
    /// 磁贴设置页的预览准备两个容器：宽窗口放进右侧固定区，向下滚动编辑选项时预览始终可见；
    /// 窄窗口回退到"标题大小"上方的页内位置。两个容器共用同一个场景与磁贴控件，
    /// 切换时只搬动已有元素，不重建富文本编辑器和背景资源。
    /// </summary>
    private UIElement CreateTileAppearancePreview()
    {
        Grid scene = new() { IsHitTestVisible = false };
        _appearancePreviews.Add("Tile", scene);
        _tilePreviewInlineViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = scene };
        _tilePreviewInlineFrame = new Border
        {
            Name = "TileAppearancePreview", MaxWidth = PreviewSceneWidth, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Child = _tilePreviewInlineViewbox
        };
        _tilePreviewStickyFrame = new Border
        {
            Name = "TileStickyAppearancePreview", HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12)
        };
        // 边框随设置刷新使用当前主题；预览内容完全只读，不连接保存回调。
        _tilePreviewInlineFrame.BorderBrush = _tilePreviewStickyFrame.BorderBrush = BoardTheme.LineBrush;
        SettingsPreviewHost.Children.Add(_tilePreviewStickyFrame);
        return _tilePreviewInlineFrame;
    }

    private void SettingsContentHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTilePreviewHosting();

    /// <summary>
    /// 按设置页当前的宽度决定磁贴预览挂在哪里。宽度足够时固定在右侧，
    /// 并让背景图铺满整块区域、磁贴按区域宽度放大；宽度不足时回到页内顶部。
    /// </summary>
    private void UpdateTilePreviewHosting()
    {
        if (_tilePreviewInlineFrame is null || _tilePreviewStickyFrame is null ||
            !_appearancePreviews.TryGetValue("Tile", out Grid? scene)) return;
        double available = SettingsContentHost.ActualWidth;
        double panelWidth = Math.Clamp(available * .52, StickyPreviewMinWidth, StickyPreviewMaxWidth);
        bool sticky = _selectedSettingsPage == "AppearanceTile" && SettingsRoot.Visibility == Visibility.Visible &&
            available - panelWidth >= MinimumSettingsColumnWidth;

        SettingsPreviewHost.Visibility = sticky ? Visibility.Visible : Visibility.Collapsed;
        SettingsPreviewHost.Width = panelWidth;
        _tilePreviewInlineFrame.Visibility = sticky ? Visibility.Collapsed : Visibility.Visible;
        // 铺满右侧区域时缩小留白，让磁贴尽量占满预览；页内预览保持原比例。
        if (_tilePreviewTileBox is not null) _tilePreviewTileBox.Margin = sticky ? new Thickness(16) : new Thickness(30);
        // 页内预览使用固定设计尺寸供 Viewbox 等比缩放；铺满右侧区域时把尺寸交回布局。
        scene.Width = sticky ? double.NaN : PreviewSceneWidth;
        scene.Height = sticky ? double.NaN : PreviewSceneHeight;

        // 页内预览多包一层 Viewbox 才能整体等比缩放，右侧区域则让场景直接铺满。
        if (sticky ? ReferenceEquals(scene.Parent, _tilePreviewStickyFrame) : ReferenceEquals(scene.Parent, _tilePreviewInlineViewbox)) return;
        if (scene.Parent is Viewbox inline) inline.Child = null;
        else if (scene.Parent is Border pinned) pinned.Child = null;
        if (sticky) _tilePreviewStickyFrame.Child = scene;
        else if (_tilePreviewInlineViewbox is not null) _tilePreviewInlineViewbox.Child = scene;
    }

    private void RefreshAppearancePreviews()
    {
        string[] kinds = _selectedSettingsPage switch
        {
            "AppearanceTile" => ["Tile"], "AppearanceBackground" => ["Shared", "Clock", "Board"],
            "AppearanceGrid" => ["Grid"], "AppearanceToolbar" => ["Toolbar"], "Layout" => ["Layout"], _ => []
        };
        foreach (string kind in kinds)
        {
            if (!_appearancePreviews.TryGetValue(kind, out Grid? scene)) continue;
            // 磁贴预览的边框可能在右侧固定区，也可能在设置页内，两处都要跟随主题刷新。
            if (kind == "Tile")
            {
                if (_tilePreviewInlineFrame is not null) _tilePreviewInlineFrame.BorderBrush = BoardTheme.LineBrush;
                if (_tilePreviewStickyFrame is not null) _tilePreviewStickyFrame.BorderBrush = BoardTheme.LineBrush;
            }
            else if (scene.Parent is Viewbox { Parent: Border frame }) frame.BorderBrush = BoardTheme.LineBrush;
            string key = kind switch
            {
                "Toolbar" => $"{BoardTheme.IsLight}|{RootShell.ActualWidth}|{RootShell.ActualHeight}|{_settings.ToolbarPosition}|{_settings.ToolbarScale}|{_settings.ToolbarIconOnly}|{_settings.ToolbarRadius}|{_settings.ToolbarGlass}|{_settings.ToolbarBlur}|{_settings.ToolbarHorizontalInset}|{_settings.ToolbarVerticalInset}",
                // 磁贴示例的主题色与高光色都取自当前色系，主题或色系变化必须重建示例才和真实磁贴一致。
                "Tile" => $"{BoardTheme.IsLight}|{ColorPalette.IsMacaron}",
                _ => $"{BoardTheme.IsLight}|{_settings.LayoutMode}"
            };
            // 只有网格页每次重建；布局页缓存真实磁贴控件，拖动时只走刷新器，不重建控件与背景。
            if (kind != "Grid" && _previewScenes.TryGetValue(kind, out var cached) && cached.Key == key)
            {
                scene.Background = BoardTheme.SurfaceBrush;
                foreach (var refresh in cached.Refresh) refresh();
                continue;
            }
            // 外观值更新只修改已有控件；重建整棵预览树会卸载播放器并丢失播放进度。
            _buildingPreviewRefreshers = [];
            _previewScenes[kind] = (key, _buildingPreviewRefreshers);
            scene.Children.Clear();
            scene.Background = BoardTheme.SurfaceBrush;
            switch (kind)
            {
                case "Tile":
                    // 背景图铺满整块预览；浅色模式额外压一层白色提亮，观感和浅色设置页一致。
                    scene.Children.Add(new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Settings/tile-preview-background.png")),
                        Stretch = Stretch.UniformToFill
                    });
                    scene.Children.Add(new Border
                    {
                        Name = "TilePreviewLightOverlay",
                        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
                        Opacity = BoardTheme.IsLight ? TilePreviewLightOverlayOpacity : 0,
                        IsHitTestVisible = false
                    });
                    _tileAppearancePreview = PreviewTile(CreateTileAppearanceSample());
                    _tilePreviewTileBox = new Viewbox { Stretch = Stretch.Uniform, Child = _tileAppearancePreview };
                    scene.Children.Add(_tilePreviewTileBox);
                    break;
                case "Shared":
                    AddPreviewBackground(scene, () => UseSharedBackground ? _settings.SharedBackground : new());
                    Grid split = new();
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    _buildingPreviewRefreshers.Add(() =>
                    {
                        split.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star);
                        split.ColumnDefinitions[1].Width = new GridLength(1 - Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star);
                    });
                    Grid clock = new(), board = new();
                    AddPreviewBackground(clock, () => _settings.ClockBackground, () => UseSharedBackground);
                    AddPreviewBackground(board, () => _settings.BoardBackground, () => UseSharedBackground);
                    clock.Children.Add(PreviewClock()); board.Children.Add(PreviewBoard());
                    split.Children.Add(clock); Grid.SetColumn(board, 1); split.Children.Add(board);
                    scene.Children.Add(split);
                    break;
                case "Clock":
                case "Board":
                    AddPreviewBackground(scene, () => UseSharedBackground ? _settings.SharedBackground : new());
                    AddPreviewBackground(scene, () => _settings.LayoutMode == "Free" ? new() :
                        kind == "Clock" ? _settings.ClockBackground : _settings.BoardBackground, () => UseSharedBackground);
                    scene.Children.Add(kind == "Clock" ? PreviewClock() : PreviewBoard());
                    break;
                case "Grid":
                    Grid comparison = new();
                    comparison.ColumnDefinitions.Add(new ColumnDefinition());
                    comparison.ColumnDefinitions.Add(new ColumnDefinition());
                    for (int column = 0; column < 2; column++)
                    {
                        Grid sample = new();
                        Canvas lines = new() { Width = 320, Height = 300,
                            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 320, 300) } };
                        DrawGrid(lines, GridAppearance.EffectiveStyle(_settings.GridStyle, column == 1, _settings.ShowGridWhileEditing), 0, 0, 320, 300);
                        sample.Children.Add(lines);
                        sample.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 4, 10, 4), Background = BoardTheme.SurfaceBrush,
                            Child = new TextBlock { Text = column == 0 ? "查看模式" : "编辑模式", Foreground = BoardTheme.TextBrush } });
                        Grid.SetColumn(sample, column); comparison.Children.Add(sample);
                    }
                    scene.Children.Add(comparison);
                    break;
                case "Toolbar":
                    scene.Children.Add(PreviewToolbar());
                    break;
                case "Layout":
                    // 底图沿用作业板区域背景（分屏时跟随跨区背景），预览与看板实际观感一致。
                    AddPreviewBackground(scene, () => UseSharedBackground ? _settings.SharedBackground : new());
                    AddPreviewBackground(scene, () => _settings.LayoutMode == "Free" ? new() : _settings.BoardBackground, () => UseSharedBackground);
                    scene.Children.Add(PreviewBoardLayout());
                    break;
            }
            _buildingPreviewRefreshers = null;
        }
        // 磁贴预览的位置取决于设置页当前宽度，刷新后重新决定挂在页内还是右侧固定区。
        UpdateTilePreviewHosting();
    }

    private void AddPreviewBackground(Grid target, Func<BackgroundSettings> settings, Func<bool>? shared = null)
    {
        BackgroundVisual background = new();
        void Refresh() => background.Apply(settings(), BoardTheme.SurfaceBrush, shared?.Invoke() ?? false);
        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
        target.Children.Add(background);
    }

    private SubjectTileControl PreviewTile(SubjectBoard? source)
    {
        // 使用真实磁贴控件和独立模型副本，富文本、附件、笔迹与标题样式和看板一致。
        SubjectBoard subject = source?.Clone() ?? new SubjectBoard { Name = "语文", TileWidth = 430, TileHeight = 220 };
        if (source is null) subject.Entries.Add(new HomeworkEntry { Content = "阅读课文，完成课后练习。" });
        SubjectTileControl tile = new(subject, _ => { }, _ => { }, _ => { }, _ => { }, _ => Task.CompletedTask, () => { });
        // 和看板磁贴一样带上数据上下文，预览可以取回控件内部的模型副本来排布。
        tile.DataContext = subject;
        tile.IsHitTestVisible = false;
        tile.Loaded += (_, _) => DisablePreviewTabStops(tile);
        tile.ApplyAppearance(_settings);
        _buildingPreviewRefreshers?.Add(() => tile.ApplyAppearance(_settings));
        return tile;
    }

    private static SubjectBoard CreateTileAppearanceSample()
    {
        // 固定示例只属于设置预览，不读取项目作业，也不接入保存回调。
        SubjectBoard sample = new()
        {
            // 三行正文比原来的两行更高，磁贴相应加高，示例内容不会被自己的滚动条裁掉。
            Name = "这是标题~", TileWidth = 560, TileHeight = 320,
            AccentHex = TileSampleAccentHex,
            // 与看板一致地按当前色系解析预设色，示例主题色和真实磁贴表现相同。
            AccentBrush = ViewModels.MainViewModel.BrushFromHex(
                ColorPalette.ResolveAccent(TileSampleAccentHex, false, ColorPalette.IsMacaron))
        };
        // 正文统一用默认字体、加粗、不斜体、白色、无下划线；"astra" 用当前色系的高光色选中。
        // RichEdit 会给 HYPERLINK 域强制画下划线且格式无法关闭，示例正文因此不再放超链接字段。
        string run = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 " + FontService.FamilyName + ";}}{\\colortbl ;" +
            TileSampleWhite + ";" + RtfColor(ColorPalette.Resolve(TileSampleHighlightHex)) + ";}\\f0\\fs36\\b\\ulnone\\cf1";
        sample.Entries.Add(new HomeworkEntry
        {
            Content = TileSampleFirstLine,
            RtfContent = run + TileSampleFirstLine + "\\par}"
        });
        sample.Entries.Add(new HomeworkEntry
        {
            Content = "Through hardships to the stars.",
            RtfContent = run + "Through hardships to the stars.\\par}"
        });
        sample.Entries.Add(new HomeworkEntry
        {
            Content = "Per ardua ad astra.",
            RtfContent = run + "Per ardua ad \\highlight2 astra\\highlight0.\\par}"
        });
        return sample;
    }

    // 设置预览示例的固定样式：预设黄色主题色、白色正文、预设高光色；预设色都跟随当前色系解析。
    private const string TileSampleAccentHex = "#FBBF24";
    private const string TileSampleHighlightHex = "#F472B6";
    private const string TileSampleFirstLine = "当有一天你不再纠结于答案 当我们又重逢于天涯或沧海";
    private const string TileSampleWhite = @"\red247\green247\blue249";

    private static string RtfColor(string hex)
    {
        Windows.UI.Color color = ViewModels.MainViewModel.BrushFromHex(hex).Color;
        return $"\\red{color.R}\\green{color.G}\\blue{color.B}";
    }

    private static void DisablePreviewTabStops(DependencyObject element)
    {
        // 预览中的只读编辑器和附件按钮不能占用设置页的键盘导航顺序。
        if (element is Control control) control.IsTabStop = false;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            DisablePreviewTabStops(VisualTreeHelper.GetChild(element, index));
    }

    private UIElement PreviewBoard()
    {
        double width = Math.Max(430, BoardCanvas.Width), height = Math.Max(300, BoardCanvas.Height);
        Canvas canvas = new() { Width = width, Height = height,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) } };
        foreach (SubjectBoard subject in ViewModel.Subjects)
        {
            SubjectTileControl tile = PreviewTile(subject);
            Canvas.SetLeft(tile, subject.X); Canvas.SetTop(tile, subject.Y); canvas.Children.Add(tile);
        }
        if (ViewModel.Subjects.Count == 0) canvas.Children.Add(PreviewTile(null));
        return new Viewbox { Margin = new Thickness(16), Stretch = Stretch.Uniform, Child = canvas };
    }

    private UIElement PreviewClock()
    {
        TextBlock LiveText(TextBlock source, double size)
        {
            TextBlock text = new() { FontSize = size, Foreground = BoardTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, FontWeight = source.FontWeight, FontFamily = source.FontFamily };
            text.SetBinding(TextBlock.TextProperty, new Binding { Source = source, Path = new PropertyPath("Text"), Mode = BindingMode.OneWay });
            return text;
        }
        StackPanel content = new() { Spacing = 18 };
        // 与看板同款结构：秒数右侧留空多少，左侧就补多少，时间数字才能落在中轴线上。
        Grid time = new() { ColumnSpacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock timeText = LiveText(MainTimeText, 112);
        TextBlock seconds = LiveText(SecondsText, 28); seconds.Margin = new Thickness(0, 17, 0, 0);
        Border mirror = new();
        mirror.SetBinding(FrameworkElement.WidthProperty, new Binding { Source = seconds, Path = new PropertyPath("ActualWidth"), Mode = BindingMode.OneWay });
        Grid.SetColumn(mirror, 0); Grid.SetColumn(timeText, 1); Grid.SetColumn(seconds, 2);
        time.Children.Add(mirror); time.Children.Add(timeText); time.Children.Add(seconds);
        content.Children.Add(time); content.Children.Add(LiveText(ClockDateText, 20));
        StackPanel components = new() { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center };
        components.Children.Add(LiveText(WeatherText, 14)); components.Children.Add(LiveText(NoiseText, 14));
        content.Children.Add(components);
        return new Viewbox { Margin = new Thickness(24), Stretch = Stretch.Uniform, Child = content };
    }

    /// <summary>
    /// 布局预览：真实磁贴控件按当前自动布局设置排在看板同款网格上，尺寸、位置和网格都走真实逻辑。
    /// 缩放只由看板视口决定，拖动自动排版间隔不会改变网格观感；网格与内容测量都会缓存，避免拖动时卡顿。
    /// </summary>
    private UIElement PreviewBoardLayout()
    {
        // 固定示例只属于设置预览，不读取也不修改项目作业。
        (string Name, string Accent, double Width, double Height, string[] Lines)[] samples =
        [
            ("语文", "#818CF8", 430, 320, ["背诵《赤壁赋》第二段", "完成课堂练习，整理作文素材。"]),
            ("数学", "#4ADE80", 430, 312, ["完成 P30 练习题", "复习二次函数公式", "订正昨天的错题。"]),
            ("英语", "#FBBF24", 300, 310, ["朗读课文三遍", "默写 Unit 5 单词。"]),
            ("物理", "#F87171", 300, 308, ["整理浮力实验报告", "预习下一节内容。"])
        ];
        List<SubjectTileControl> tiles = [];
        foreach (var sample in samples)
        {
            SubjectBoard subject = new()
            {
                Name = sample.Name, TileWidth = sample.Width, TileHeight = sample.Height,
                AccentBrush = ViewModels.MainViewModel.BrushFromHex(ColorPalette.ResolveAccent(sample.Accent, false, ColorPalette.IsMacaron))
            };
            foreach (string line in sample.Lines) subject.Entries.Add(new HomeworkEntry { Content = line });
            tiles.Add(PreviewTile(subject));
        }
        Canvas grid = new(), layer = new();
        foreach (SubjectTileControl tile in tiles) layer.Children.Add(tile);
        // 图层顺序与看板一致：网格在底，磁贴覆盖其上。
        Grid board = new();
        board.Children.Add(grid);
        board.Children.Add(layer);
        // 预览内框尺寸：画布按这个比例裁切看板，缩放固定，网格铺满整张预览。
        const double previewWidth = 640, previewHeight = 300;
        const double previewLeft = 10, previewTop = 36, previewRight = 10, previewBottom = 10;
        const double frameWidth = previewWidth - previewLeft - previewRight, frameHeight = previewHeight - previewTop - previewBottom;
        TextBlock gridLabel = new() { FontSize = 13, Foreground = BoardTheme.TextBrush };
        TextBlock gapLabel = new() { FontSize = 13, Foreground = BoardTheme.TextBrush };
        List<(double Width, double Height)> measured = [];
        string measuredKey = string.Empty, gridKey = string.Empty;
        void Refresh()
        {
            double gap = Math.Max(0, _settings.AutoLayoutGap);
            double gridSize = GridSize;
            // 与自动排列一致：只有开启网格吸附和自动对齐时，位置和尺寸才落在网格线上。
            double rounding = _settings.GridSnappingEnabled && _settings.AutoLayoutAlign ? gridSize : 0;
            // 内容尺寸只在影响测量的外观变化时重算；拖动滑动条不再逐帧测量富文本。
            string sizeKey = $"{_settings.AutoLayoutResize}|{_settings.TileTitleSize}|{BoardTheme.IsLight}";
            if (sizeKey != measuredKey)
            {
                measuredKey = sizeKey;
                measured.Clear();
                for (int index = 0; index < tiles.Count; index++)
                {
                    // 每次都从示例基准尺寸重新测量，连续刷新不会让磁贴越缩越小。
                    SubjectTileControl tile = tiles[index];
                    SubjectBoard subject = (SubjectBoard)tile.DataContext;
                    subject.TileWidth = samples[index].Width; subject.TileHeight = samples[index].Height;
                    tile.ApplyModelLayout();
                    measured.Add(tile.MeasureContentSize());
                }
            }
            // 关闭自动调整时使用示例原始尺寸，避免上一次测量结果残留。
            List<(double Width, double Height)> sizes = _settings.AutoLayoutResize
                ? [.. measured]
                : [.. samples.Select(sample => (sample.Width, sample.Height))];
            // 先统一相近尺寸、再按网格向上取整，和看板自动排列对尺寸的处理顺序一致。
            if (_settings.AutoLayoutResize && _settings.AutoLayoutAlign) sizes = BoardLayout.AlignSizes(sizes);
            if (_settings.AutoLayoutResize && rounding > 0)
                sizes = sizes.Select(size => (Math.Ceiling(size.Width / rounding) * rounding, Math.Ceiling(size.Height / rounding) * rounding)).ToList();
            // 与自动排列的兜底画布一致；无限作业板按内容继续扩展。
            double boardWidth = 1100, boardHeight = 780;
            if (_settings.InfiniteBoard)
            {
                boardWidth = Math.Max(boardWidth, sizes.Max(size => size.Width));
                boardHeight = Math.Max(boardHeight, sizes.Sum(size => size.Height + gap + rounding));
            }
            List<LayoutRect>? placements = null;
            // 网格和间隔偏大时按实际画布可能放不下；逐步放大虚拟画布兜底，保证预览始终有结果。
            for (double expansion = 1; placements is null && expansion <= 8; expansion *= 2)
            {
                try { placements = BoardLayout.Arrange(sizes, boardWidth * expansion, boardHeight * expansion, gap, _settings.AutoLayoutAlign, rounding); }
                catch (InvalidOperationException) { }
            }
            double stacked = 0;
            placements ??= sizes.Select(size => { LayoutRect rect = new(0, stacked, size.Width, size.Height); stacked += size.Height + gap; return rect; }).ToList();
            // 缩放只看看板视口：网格观感固定，拖动间隔不会让它变大变小；看板水平居中，内容仍从左上角开始排。
            double scale = Math.Min(frameWidth / boardWidth, frameHeight / boardHeight);
            double canvasWidth = Math.Max(1, frameWidth / scale), canvasHeight = Math.Max(1, frameHeight / scale);
            double originX = (canvasWidth - boardWidth) / 2, originY = (canvasHeight - boardHeight) / 2;
            for (int index = 0; index < tiles.Count; index++)
            {
                SubjectBoard subject = (SubjectBoard)tiles[index].DataContext;
                subject.TileWidth = placements[index].Width; subject.TileHeight = placements[index].Height;
                tiles[index].ApplyModelLayout();
                Canvas.SetLeft(tiles[index], originX + placements[index].X); Canvas.SetTop(tiles[index], originY + placements[index].Y);
            }
            // 网格只在尺寸或外观变化时重画：拖动间隔时网格保持原样，也不会逐帧创建大量图形。
            string appearance = $"{GridAppearance.EffectiveStyle(_settings.GridStyle, _isEditing, _settings.ShowGridWhileEditing)}|" +
                $"{gridSize}|{_settings.GridColor}|{_settings.GridLineThickness}|{_settings.GridDotColor}|{_settings.GridDotDiameter}";
            string nextGridKey = $"{appearance}|{canvasWidth:0.##}x{canvasHeight:0.##}|{originX:0.##}|{originY:0.##}";
            if (nextGridKey != gridKey)
            {
                gridKey = nextGridKey;
                board.Width = canvasWidth; board.Height = canvasHeight;
                grid.Width = layer.Width = canvasWidth; grid.Height = layer.Height = canvasHeight;
                // 网格和磁贴层各自持有独立的裁剪几何：同一个 Geometry 实例不能同时作为两个元素的 Clip。
                Windows.Foundation.Rect bounds = new(0, 0, canvasWidth, canvasHeight);
                grid.Clip = new RectangleGeometry { Rect = bounds };
                layer.Clip = new RectangleGeometry { Rect = bounds };
                // 网格从看板原点向两侧补齐，磁贴的吸附位置因此和看板完全一致。
                DrawGrid(grid, GridAppearance.EffectiveStyle(_settings.GridStyle, _isEditing, _settings.ShowGridWhileEditing),
                    originX - Math.Floor(originX / gridSize) * gridSize,
                    originY - Math.Floor(originY / gridSize) * gridSize,
                    canvasWidth, canvasHeight, scale);
            }
            gridLabel.Text = $"网格 {gridSize:0.#} px";
            gapLabel.Text = $"自动排列 · 间隔 {gap:0.#} px";
        }
        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
        // 标签不参与缩放：贴在预览内侧，始终使用真实字号。
        Grid preview = new() { Width = previewWidth, Height = previewHeight, IsHitTestVisible = false };
        preview.Children.Add(new Viewbox { Margin = new Thickness(previewLeft, previewTop, previewRight, previewBottom), Stretch = Stretch.Uniform, Child = board });
        preview.Children.Add(PreviewLayoutChip(gridLabel, HorizontalAlignment.Left));
        preview.Children.Add(PreviewLayoutChip(gapLabel, HorizontalAlignment.Right));
        return preview;
    }

    private static Border PreviewLayoutChip(TextBlock label, HorizontalAlignment alignment) => new()
    {
        HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 12, 12, 0),
        Padding = new Thickness(10, 4, 10, 4), CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1), BorderBrush = BoardTheme.LineBrush,
        Background = new SolidColorBrush(BoardTheme.IsLight
            ? Windows.UI.Color.FromArgb(255, 240, 240, 246) : Windows.UI.Color.FromArgb(255, 38, 38, 50)),
        Child = label
    };

    private UIElement CreateToolbarPositionPicker()
    {
        Grid screen = new() { Height = 210, Padding = new Thickness(12) };
        for (int index = 0; index < 3; index++)
        {
            screen.RowDefinitions.Add(new RowDefinition());
            screen.ColumnDefinitions.Add(new ColumnDefinition());
        }
        Border frame = new()
        {
            Name = "ToolbarPositionPicker", MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(8), CornerRadius = new CornerRadius(12), Child = screen
        };
        var positions = new[]
        {
            ("TopLeft", "左上", 0, 0), ("TopCenter", "上居中", 0, 1), ("TopRight", "右上", 0, 2),
            ("CenterLeft", "左居中（竖置）", 1, 0), ("CenterRight", "右居中（竖置）", 1, 2),
            ("BottomLeft", "左下", 2, 0), ("BottomCenter", "下居中", 2, 1), ("BottomRight", "右下", 2, 2)
        };
        foreach (var (position, label, row, column) in positions)
        {
            RadioButton button = new()
            {
                Name = "ToolbarPosition" + position, GroupName = "ToolbarPosition", Tag = position,
                MinWidth = 0, MinHeight = 40, Width = 40, Padding = new Thickness(0),
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : column == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = row == 0 ? VerticalAlignment.Top : row == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                IsChecked = _settings.ToolbarPosition == position
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Checked += (_, _) =>
            {
                if (_settings.ToolbarPosition == position) return;
                _settings.ToolbarPosition = position;
                SettingChanged();
            };
            Grid.SetRow(button, row); Grid.SetColumn(button, column); screen.Children.Add(button);
            _refreshSettingAvailability.Add(() => button.IsChecked = _settings.ToolbarPosition == position);
        }
        _refreshSettingAvailability.Add(() =>
        {
            frame.BorderBrush = BoardTheme.TextBrush;
            frame.Background = BoardTheme.SurfaceBrush;
        });
        return frame;
    }

    private UIElement PreviewToolbar()
    {
        string position = _settings.ToolbarPosition;
        bool vertical = position.StartsWith("Center");
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double toolbarWidth = ((vertical ? (_settings.ToolbarIconOnly ? 44 : 64) : (_settings.ToolbarIconOnly ? 44 : 88) * 3 + 8) + 14) * scale + 2;
        double toolbarHeight = (((_settings.ToolbarIconOnly ? 44 : 64) * (vertical ? 3 : 1)) + (vertical ? 8 : 0) + 14) * scale + 2;
        // 在实际窗口坐标中截取控制窗附近，保持预览宽高比，避免整窗缩小和留黑边。
        double cropHeight = Math.Max(300, Math.Max(toolbarHeight + 64, (toolbarWidth + 64) * 300 / 640));
        double cropWidth = cropHeight * 640 / 300;
        Grid viewport = new()
        {
            Width = Math.Max(cropWidth, Math.Max(RootShell.ActualWidth, toolbarWidth + _settings.ToolbarHorizontalInset * 2)),
            Height = Math.Max(cropHeight, Math.Max(RootShell.ActualHeight, toolbarHeight + _settings.ToolbarVerticalInset * 2))
        };
        BackgroundVisual background = new() { MediaStretchOverride = Stretch.UniformToFill };
        background.Apply(UseSharedBackground ? _settings.SharedBackground : _settings.BoardBackground, BoardTheme.SurfaceBrush);
        _buildingPreviewRefreshers?.Add(() => background.Apply(UseSharedBackground ? _settings.SharedBackground : _settings.BoardBackground, BoardTheme.SurfaceBrush));
        viewport.Children.Add(background);
        StackPanel buttons = new() { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4 * scale };
        foreach (var (glyph, label) in new[] { (FluentGlyphs.Edit, "编辑看板"), (FluentGlyphs.Settings, "设置"), (FluentGlyphs.FullScreen, "进入全屏") })
        {
            StackPanel content = new() { Orientation = Orientation.Vertical,
                Spacing = (vertical ? 2 : 4) * scale, HorizontalAlignment = HorizontalAlignment.Center };
            FluentIcon icon = new() { Glyph = glyph, FontSize = 18 * scale, Foreground = BoardTheme.TextBrush };
            TextBlock text = new() { Text = label, FontSize = (vertical ? 8 : 10) * scale, Foreground = BoardTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = _settings.ToolbarIconOnly ? Visibility.Collapsed : Visibility.Visible };
            bool labelFirst = !vertical && position.StartsWith("Top");
            if (labelFirst) content.Children.Add(text);
            content.Children.Add(icon);
            if (!labelFirst) content.Children.Add(text);
            double buttonWidth = (_settings.ToolbarIconOnly ? 44 : vertical ? 64 : 88) * scale;
            double buttonHeight = (_settings.ToolbarIconOnly ? 44 : 64) * scale;
            buttons.Children.Add(new Button { Width = buttonWidth,
                Height = buttonHeight, Padding = new Thickness(6 * scale),
                CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, 7 * scale, buttonWidth, buttonHeight),
                Style = SettingsButton.Style, Content = content, IsTabStop = false });
        }
        viewport.Children.Add(new Border
        {
            Name = "ToolbarPreviewFrame", Child = buttons, Padding = new Thickness(7 * scale), CornerRadius = new CornerRadius(_settings.ToolbarRadius),
            Background = CreateToolbarBackground(),
            BorderBrush = BoardTheme.LineBrush, BorderThickness = new Thickness(1),
            HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            Margin = new Thickness(_settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset, _settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset)
        });
        double centerX = position.EndsWith("Left") ? _settings.ToolbarHorizontalInset + toolbarWidth / 2
            : position.EndsWith("Right") ? viewport.Width - _settings.ToolbarHorizontalInset - toolbarWidth / 2 : viewport.Width / 2;
        double centerY = position.StartsWith("Top") ? _settings.ToolbarVerticalInset + toolbarHeight / 2
            : vertical ? viewport.Height / 2 : viewport.Height - _settings.ToolbarVerticalInset - toolbarHeight / 2;
        Canvas crop = new() { Width = cropWidth, Height = cropHeight,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, cropWidth, cropHeight) } };
        Canvas.SetLeft(viewport, -Math.Clamp(centerX - cropWidth / 2, 0, viewport.Width - cropWidth));
        Canvas.SetTop(viewport, -Math.Clamp(centerY - cropHeight / 2, 0, viewport.Height - cropHeight));
        crop.Children.Add(viewport);
        return new Viewbox { Stretch = Stretch.Uniform, Child = crop };
    }
}
