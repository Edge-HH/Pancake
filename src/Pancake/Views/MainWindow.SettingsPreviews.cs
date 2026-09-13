using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pancake.Controls;
using Pancake.Models;
using Pancake.RichText;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 设置页的实时外观预览：用真实控件（磁贴、背景层、网格、控制窗）搭出只读场景，
/// 改动设置后立即刷新，观感与看板一致。对应 2.x 设置页里的预览区块。
/// </summary>
public sealed partial class MainWindow
{
    // 预览的固定设计尺寸：经 Viewbox 等比缩放，窄窗口也不会撑宽设置页。
    private const double PreviewSceneWidth = 640, PreviewSceneHeight = 300;
    // 浅色模式下给磁贴示例的底图压一层白色提亮，深色图片不会显得发闷。
    private const double TilePreviewLightOverlayOpacity = .55;
    // 磁贴示例的固定样式：预设黄色主题色、白色正文、预设高光色，都跟随当前色系解析。
    private const string TileSampleAccentHex = "#FBBF24";
    private const string TileSampleHighlightHex = "#F472B6";
    private const string TileSampleFirstLine = "当有一天你不再纠结于答案 当我们又重逢于天涯或沧海";

    private readonly Dictionary<string, Grid> _appearancePreviews = [];
    private readonly Dictionary<string, (string Key, List<Action> Refresh)> _previewScenes = [];
    private List<Action>? _buildingPreviewRefreshers;
    // 磁贴预览有一套固定设计尺寸的场景，可以在页内位置与右侧固定区之间整体搬运。
    private Viewbox? _tilePreviewInlineViewbox;
    private Border? _tilePreviewInlineFrame;

    /// <summary>
    /// 创建一块预览。预览完全只读：不接保存回调、不参与键盘导航，
    /// 也不占用设置页的输入焦点。
    /// </summary>
    private Control CreateAppearancePreview(string kind)
    {
        if (kind == "Tile") return CreateTileAppearancePreview();
        Grid scene = new() { Width = PreviewSceneWidth, Height = PreviewSceneHeight, IsHitTestVisible = false };
        _appearancePreviews[kind] = scene;
        // 设置页每次切换都会重建页面，这里必须丢弃上一块场景的缓存，
        // 否则新场景会因为缓存命中而一直空着。
        _previewScenes.Remove(kind);
        return new Border
        {
            MaxWidth = PreviewSceneWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BorderBrush = BoardTheme.LineColor.ToBrush(),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = scene }
        };
    }

    /// <summary>
    /// 磁贴外观页的预览准备两个容器：宽窗口放进右侧固定区，向下滚动改选项时预览始终可见；
    /// 窄窗口退回「标题大小」上方的页内位置。两个容器共用同一个场景，切换时只搬运场景，
    /// 不重建示例磁贴与背景资源（与旧版一致）。
    /// </summary>
    private Control CreateTileAppearancePreview()
    {
        Grid scene = new() { Width = PreviewSceneWidth, Height = PreviewSceneHeight, IsHitTestVisible = false };
        _appearancePreviews["Tile"] = scene;
        _previewScenes.Remove("Tile");
        _tilePreviewInlineViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = scene };
        _tilePreviewInlineFrame = new Border
        {
            MaxWidth = PreviewSceneWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            BorderBrush = BoardTheme.LineColor.ToBrush(),
            Child = _tilePreviewInlineViewbox
        };
        SettingsPreviewHost.Child = null;
        return _tilePreviewInlineFrame;
    }

    /// <summary>
    /// 按设置页当前可用宽度决定磁贴预览挂在哪里：够宽时固定在右侧固定区并铺满整块区域，
    /// 宽度不足时回到页内位置。切换时只搬动场景，保留示例磁贴与背景资源。
    /// </summary>
    private void UpdateTilePreviewHosting()
    {
        if (_tilePreviewInlineFrame is null || _tilePreviewInlineViewbox is null) return;
        if (!_appearancePreviews.TryGetValue("Tile", out Grid? scene)) return;
        // 用「设置页总宽 - 左侧导航列」而不是滚动区宽度：固定区出现后滚动区会变窄，
        // 用滚动区宽度判定会形成“显示固定区→内容变窄→判定不成立→收起固定区”的抖动。
        double navigation = SettingsRoot.ColumnDefinitions[0].ActualWidth;
        if (navigation < 1) navigation = 240;
        double available = SettingsRoot.Bounds.Width - navigation;
        if (available < 1) return;
        double panelWidth = Math.Clamp(available * 0.52, 400, 720);
        bool sticky = _settingsSection == "AppearanceTile" && SettingsRoot.IsVisible &&
                      available - panelWidth >= 440;

        SettingsPreviewHost.IsVisible = sticky;
        SettingsPreviewHost.Width = panelWidth;
        _tilePreviewInlineFrame.IsVisible = !sticky;
        // 页内预览用固定设计尺寸交给 Viewbox 等比缩放；铺满右侧固定区时把尺寸交回布局。
        scene.Width = sticky ? double.NaN : PreviewSceneWidth;
        scene.Height = sticky ? double.NaN : PreviewSceneHeight;
        scene.Margin = sticky ? new Thickness(16) : default;

        // 已经挂在目标容器里就不重复搬运：搬动会卸载播放器并丢失背景播放进度。
        if (sticky)
        {
            if (ReferenceEquals(scene.Parent, SettingsPreviewHost)) return;
            if (scene.Parent is Viewbox inline) inline.Child = null;
            SettingsPreviewHost.Child = scene;
            return;
        }

        if (ReferenceEquals(scene.Parent, _tilePreviewInlineViewbox)) return;
        if (scene.Parent is Border host) host.Child = null;
        _tilePreviewInlineViewbox.Child = scene;
    }

    /// <summary>
    /// 刷新当前设置分类用到的预览。外观值变化只改已有控件（走刷新器），
    /// 只有主题、色系、布局模式或控制窗停靠这类会改变场景结构的变化才重建，
    /// 避免拖动滑块时反复重建富文本与背景资源。
    /// </summary>
    private void RefreshAppearancePreviews()
    {
        if (!_isLoaded) return;
        string[] kinds = _settingsSection switch
        {
            "AppearanceTile" => ["Tile"],
            "AppearanceBackground" => ["Shared", "Clock", "Board"],
            "AppearanceGrid" => ["Grid"],
            "AppearanceToolbar" => ["Toolbar"],
            "Layout" => ["Layout"],
            _ => []
        };
        foreach (string kind in kinds)
        {
            if (!_appearancePreviews.TryGetValue(kind, out Grid? scene)) continue;
            if (scene.Parent is Viewbox { Parent: Border frame }) frame.BorderBrush = BoardTheme.LineColor.ToBrush();
            string key = kind switch
            {
                "Toolbar" =>
                    $"{BoardTheme.IsLight}|{Bounds.Width:0}|{Bounds.Height:0}|{Settings.ToolbarPosition}|{Settings.ToolbarScale}|" +
                    $"{Settings.ToolbarIconOnly}|{Settings.ToolbarRadius}|{Settings.ToolbarGlass}|{Settings.ToolbarBlur}|" +
                    $"{Settings.ToolbarHorizontalInset}|{Settings.ToolbarVerticalInset}",
                // 磁贴示例的主题色与高光色都取自当前色系，主题或色系变化必须重建示例。
                "Tile" => $"{BoardTheme.IsLight}|{ColorPalette.IsMacaron}",
                _ => $"{BoardTheme.IsLight}|{Settings.LayoutMode}"
            };
            if (kind != "Grid" && _previewScenes.TryGetValue(kind, out (string Key, List<Action> Refresh) cached) &&
                cached.Key == key)
            {
                scene.Background = BoardTheme.SurfaceColor.ToBrush();
                foreach (Action refresh in cached.Refresh) refresh();
                continue;
            }

            _buildingPreviewRefreshers = [];
            _previewScenes[kind] = (key, _buildingPreviewRefreshers);
            scene.Children.Clear();
            scene.Background = BoardTheme.SurfaceColor.ToBrush();
            switch (kind)
            {
                case "Tile":
                    BuildTilePreview(scene);
                    break;
                case "Shared":
                    BuildSharedPreview(scene);
                    break;
                case "Clock":
                case "Board":
                    BuildRegionPreview(scene, kind);
                    break;
                case "Grid":
                    BuildGridPreview(scene);
                    break;
                case "Toolbar":
                    BuildToolbarPreview(scene);
                    break;
                case "Layout":
                    BuildLayoutPreview(scene);
                    break;
            }

            _buildingPreviewRefreshers = null;
        }

        // 磁贴预览挂在页内还是右侧固定区取决于设置页当前宽度，刷新后重新决定。
        UpdateTilePreviewHosting();
    }

    /// <summary>磁贴预览：底图 + 固定示例磁贴，用于观察标题字号、背景与毛玻璃。</summary>
    private void BuildTilePreview(Grid scene)
    {
        scene.Children.Add(new Image { Source = LoadPreviewBackground(), Stretch = Stretch.UniformToFill });
        scene.Children.Add(new Border
        {
            Background = new SolidColorBrush(Colors.White),
            Opacity = BoardTheme.IsLight ? TilePreviewLightOverlayOpacity : 0
        });

        SubjectTileControl sample = PreviewTile(CreateTileAppearanceSample());
        void Refresh()
        {
            sample.ApplyBackground(Settings.TileBackground);
            sample.ApplyTitleSize(Settings.TileTitleSize);
        }

        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
        scene.Children.Add(new Viewbox { Margin = new Thickness(30), Stretch = Stretch.Uniform, Child = sample });
    }

    /// <summary>跨区底图预览：一张连续底图上叠出分屏比例、时钟与作业板两块区域。</summary>
    private void BuildSharedPreview(Grid scene)
    {
        AddPreviewBackground(scene, () => Settings.SharedBackgroundEnabled && Settings.LayoutMode == "Split"
            ? Settings.SharedBackground
            : new BackgroundSettings());
        Grid split = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };
        void RefreshSplit()
        {
            double ratio = Math.Clamp(Settings.SplitRatio, .1, .9);
            split.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
            split.ColumnDefinitions[1].Width = new GridLength(1 - ratio, GridUnitType.Star);
        }

        RefreshSplit();
        _buildingPreviewRefreshers?.Add(RefreshSplit);
        Grid clock = new(), board = new();
        AddPreviewBackground(clock, () => Settings.ClockBackground);
        AddPreviewBackground(board, () => Settings.BoardBackground);
        clock.Children.Add(PreviewClock());
        board.Children.Add(PreviewBoard());
        split.Children.Add(clock);
        Grid.SetColumn(board, 1);
        split.Children.Add(board);
        scene.Children.Add(split);
    }

    /// <summary>时钟区或作业板区域的底图预览：跨区背景在下、区域底图在上。</summary>
    private void BuildRegionPreview(Grid scene, string kind)
    {
        AddPreviewBackground(scene, () => Settings.SharedBackgroundEnabled && Settings.LayoutMode == "Split"
            ? Settings.SharedBackground
            : new BackgroundSettings());
        AddPreviewBackground(scene, () => Settings.LayoutMode == "Free"
            ? new BackgroundSettings()
            : kind == "Clock" ? Settings.ClockBackground : Settings.BoardBackground);
        scene.Children.Add(kind == "Clock" ? PreviewClock() : PreviewBoard());
    }

    /// <summary>网格预览：左边查看模式、右边编辑模式，样式与看板完全同源。</summary>
    private void BuildGridPreview(Grid scene)
    {
        Grid comparison = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };
        for (int column = 0; column < 2; column++)
        {
            Grid sample = new();
            Canvas lines = new()
            {
                Width = 320,
                Height = PreviewSceneHeight,
                ClipToBounds = true
            };
            GridRenderer.Draw(
                lines,
                GridAppearance.EffectiveStyle(Settings.GridStyle, column == 1, Settings.ShowGridWhileEditing),
                GridSize,
                320,
                PreviewSceneHeight,
                GridAppearance.ParseColor(Settings.GridColor, GridRenderer.DefaultLineColor),
                Settings.GridLineThickness,
                GridAppearance.ParseColor(Settings.GridDotColor, GridRenderer.DefaultDotColor),
                Settings.GridDotDiameter);
            sample.Children.Add(lines);
            sample.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(10, 4),
                CornerRadius = new CornerRadius(6),
                Background = BoardTheme.SurfaceColor.ToBrush(),
                Child = new TextBlock
                {
                    Text = column == 0 ? "查看模式" : "编辑模式",
                    FontSize = 13,
                    Foreground = BoardTheme.TextColor.ToBrush()
                }
            });
            Grid.SetColumn(sample, column);
            comparison.Children.Add(sample);
        }

        scene.Children.Add(comparison);
    }

    /// <summary>
    /// 布局预览：四个示例磁贴按真实的自动布局设置排在看板同款网格上，
    /// 尺寸走和自动排列完全相同的内容测量与收紧逻辑。
    /// </summary>
    private void BuildLayoutPreview(Grid scene)
    {
        (string Name, string Accent, double Width, double Height, string[] Lines)[] samples =
        [
            ("语文", "#818CF8", 430, 320, ["背诵《赤壁赋》第二段", "完成课堂练习，整理作文素材。"]),
            ("数学", "#4ADE80", 430, 312, ["完成 P30 练习题", "复习二次函数公式", "订正昨天的错题。"]),
            ("英语", "#FBBF24", 300, 310, ["朗读课文三遍", "默写 Unit 5 单词。"]),
            ("物理", "#F87171", 300, 308, ["整理浮力实验报告", "预习下一节内容。"])
        ];
        List<SubjectTileControl> tiles = [];
        foreach ((string name, string accent, double width, double height, string[] lines) in samples)
        {
            SubjectBoard subject = new()
            {
                Name = name,
                TileWidth = width,
                TileHeight = height,
                AccentHex = ColorPalette.ResolveAccent(accent, false, ColorPalette.IsMacaron),
                IsAccentExplicit = true
            };
            foreach (string line in lines) subject.Entries.Add(new HomeworkEntry { Content = line });
            tiles.Add(PreviewTile(subject));
        }

        Canvas grid = new(), layer = new();
        foreach (SubjectTileControl tile in tiles) layer.Children.Add(tile);
        // 图层顺序与看板一致：网格在底，磁贴覆盖其上。
        Grid board = new();
        board.Children.Add(grid);
        board.Children.Add(layer);

        // 预览内框尺寸：画布按这个比例裁切看板，缩放固定，网格铺满整张预览。
        const double previewWidth = PreviewSceneWidth, previewHeight = PreviewSceneHeight;
        const double previewLeft = 10, previewTop = 36, previewRight = 10, previewBottom = 10;
        const double frameWidth = previewWidth - previewLeft - previewRight;
        const double frameHeight = previewHeight - previewTop - previewBottom;
        TextBlock gridLabel = new() { FontSize = 13, Foreground = BoardTheme.TextColor.ToBrush() };
        TextBlock gapLabel = new() { FontSize = 13, Foreground = BoardTheme.TextColor.ToBrush() };
        List<(double Width, double Height)> measured = [];
        string measuredKey = string.Empty, gridKey = string.Empty;

        void Refresh()
        {
            double gap = Math.Max(0, Settings.AutoLayoutGap);
            double gridSize = GridSize;
            // 与自动排列一致：只有开启网格吸附和自动对齐时，位置和尺寸才落在网格线上。
            double rounding = Settings.GridSnappingEnabled && Settings.AutoLayoutAlign ? gridSize : 0;
            // 内容尺寸只在影响测量的外观变化时重算；拖动滑条不逐帧测量富文本。
            string sizeKey = $"{Settings.AutoLayoutResize}|{Settings.TileTitleSize}|{BoardTheme.IsLight}";
            if (sizeKey != measuredKey)
            {
                measuredKey = sizeKey;
                measured.Clear();
                for (int index = 0; index < tiles.Count; index++)
                {
                    // 每次都从示例基准尺寸重新测量，连续刷新不会让磁贴越缩越小。
                    SubjectBoard subject = (SubjectBoard)tiles[index].DataContext!;
                    subject.TileWidth = samples[index].Width;
                    subject.TileHeight = samples[index].Height;
                    tiles[index].ApplyModelLayout();
                    measured.Add(tiles[index].MeasureContentSize());
                }
            }

            List<(double Width, double Height)> sizes = Settings.AutoLayoutResize
                ? [.. measured]
                : [.. samples.Select(sample => (sample.Width, sample.Height))];
            if (Settings.AutoLayoutResize && Settings.AutoLayoutAlign) sizes = BoardLayout.AlignSizes(sizes);
            if (Settings.AutoLayoutResize && rounding > 0)
            {
                sizes = sizes
                    .Select(size => (Math.Ceiling(size.Width / rounding) * rounding, Math.Ceiling(size.Height / rounding) * rounding))
                    .ToList();
            }

            double boardWidth = 1100, boardHeight = 780;
            if (Settings.InfiniteBoard)
            {
                boardWidth = Math.Max(boardWidth, sizes.Max(size => size.Width));
                boardHeight = Math.Max(boardHeight, sizes.Sum(size => size.Height + gap + rounding));
            }

            List<LayoutRect>? placements = null;
            // 网格和间隔偏大时真实画布可能放不下；逐步放大虚拟画布兜底，保证预览始终有结果。
            for (double expansion = 1; placements is null && expansion <= 8; expansion *= 2)
            {
                try
                {
                    placements = BoardLayout.Arrange(sizes, boardWidth * expansion, boardHeight * expansion, gap,
                        Settings.AutoLayoutAlign, rounding);
                }
                catch (InvalidOperationException)
                {
                    // 放不下就换更大的虚拟画布重试。
                }
            }

            if (placements is null)
            {
                double stacked = 0;
                placements = sizes.Select(size =>
                {
                    LayoutRect rect = new(0, stacked, size.Width, size.Height);
                    stacked += size.Height + gap;
                    return rect;
                }).ToList();
            }

            // 缩放只看看板视口：网格观感固定，拖动间隔不会让它变大变小。
            double scale = Math.Min(frameWidth / boardWidth, frameHeight / boardHeight);
            double canvasWidth = Math.Max(1, frameWidth / scale), canvasHeight = Math.Max(1, frameHeight / scale);
            double originX = (canvasWidth - boardWidth) / 2, originY = (canvasHeight - boardHeight) / 2;
            for (int index = 0; index < tiles.Count; index++)
            {
                SubjectBoard subject = (SubjectBoard)tiles[index].DataContext!;
                subject.TileWidth = placements[index].Width;
                subject.TileHeight = placements[index].Height;
                tiles[index].ApplyModelLayout();
                Canvas.SetLeft(tiles[index], originX + placements[index].X);
                Canvas.SetTop(tiles[index], originY + placements[index].Y);
            }

            string appearance = $"{GridAppearance.EffectiveStyle(Settings.GridStyle, _isEditing, Settings.ShowGridWhileEditing)}|" +
                $"{gridSize}|{Settings.GridColor}|{Settings.GridLineThickness}|{Settings.GridDotColor}|{Settings.GridDotDiameter}";
            string nextGridKey = $"{appearance}|{canvasWidth:0.##}x{canvasHeight:0.##}|{originX:0.##}|{originY:0.##}";
            if (nextGridKey != gridKey)
            {
                gridKey = nextGridKey;
                board.Width = canvasWidth;
                board.Height = canvasHeight;
                grid.Width = layer.Width = canvasWidth;
                grid.Height = layer.Height = canvasHeight;
                grid.ClipToBounds = true;
                layer.ClipToBounds = true;
                grid.Children.Clear();
                // 网格从看板原点向两侧补齐，磁贴的吸附位置因此和看板完全一致。
                GridRenderer.Draw(
                    grid,
                    GridAppearance.EffectiveStyle(Settings.GridStyle, _isEditing, Settings.ShowGridWhileEditing),
                    gridSize,
                    canvasWidth,
                    canvasHeight,
                    GridAppearance.ParseColor(Settings.GridColor, GridRenderer.DefaultLineColor),
                    Settings.GridLineThickness,
                    GridAppearance.ParseColor(Settings.GridDotColor, GridRenderer.DefaultDotColor),
                    Settings.GridDotDiameter,
                    originX - Math.Floor(originX / gridSize) * gridSize,
                    originY - Math.Floor(originY / gridSize) * gridSize,
                    scale);
            }

            gridLabel.Text = $"网格 {gridSize:0.#} px";
            gapLabel.Text = $"自动排列 · 间隔 {gap:0.#} px";
        }

        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
        // 标签不参与缩放：贴在预览内侧，始终使用真实字号。
        Grid preview = new() { Width = previewWidth, Height = previewHeight };
        preview.Children.Add(new Viewbox
        {
            Margin = new Thickness(previewLeft, previewTop, previewRight, previewBottom),
            Stretch = Stretch.Uniform,
            Child = board
        });
        preview.Children.Add(PreviewLayoutChip(gridLabel, HorizontalAlignment.Left));
        preview.Children.Add(PreviewLayoutChip(gapLabel, HorizontalAlignment.Right));
        scene.Children.Add(preview);
    }

    /// <summary>布局预览的角标：网格大小与自动排列间隔，用真实字号显示。</summary>
    private static Border PreviewLayoutChip(TextBlock label, HorizontalAlignment alignment) => new()
    {
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(12, 12, 12, 0),
        Padding = new Thickness(10, 4),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        BorderBrush = BoardTheme.LineColor.ToBrush(),
        Background = new SolidColorBrush(BoardTheme.IsLight
            ? Color.FromArgb(255, 240, 240, 246)
            : Color.FromArgb(255, 38, 38, 50)),
        Child = label
    };

    /// <summary>
    /// 控制窗预览：按当前停靠、缩放、圆角、颜色与无字模式搭一块迷你控制窗，
    /// 并从窗口坐标里裁出控制窗附近的一块，保持宽高比又不会把它缩得太小。
    /// 说明：背衬毛玻璃取自看板快照，预览里只显示颜色层与边框。
    /// </summary>
    private void BuildToolbarPreview(Grid scene)
    {
        string position = Settings.ToolbarPosition;
        bool vertical = IsVerticalToolbarPosition;
        double scale = ToolbarScale;
        double buttonCross = Settings.ToolbarIconOnly ? 44 : 64;
        double buttonLong = Settings.ToolbarIconOnly ? 44 : 88;
        double toolbarWidth = ((vertical ? buttonCross : buttonLong * 3 + 8) + 14) * scale + 2;
        double toolbarHeight = ((buttonCross * (vertical ? 3 : 1)) + (vertical ? 8 : 0) + 14) * scale + 2;

        double cropHeight = Math.Max(300, Math.Max(toolbarHeight + 64, (toolbarWidth + 64) * 300 / 640));
        double cropWidth = cropHeight * 640 / 300;
        double viewportWidth = Math.Max(cropWidth, Math.Max(Bounds.Width, toolbarWidth + Settings.ToolbarHorizontalInset * 2));
        double viewportHeight = Math.Max(cropHeight, Math.Max(Bounds.Height, toolbarHeight + Settings.ToolbarVerticalInset * 2));

        Canvas viewport = new() { Width = viewportWidth, Height = viewportHeight };
        BackgroundVisual background = new();
        background.Apply(Settings.SharedBackgroundEnabled && Settings.LayoutMode == "Split"
            ? Settings.SharedBackground
            : Settings.BoardBackground);
        viewport.Children.Add(background);

        double left = position.EndsWith("Left", StringComparison.Ordinal) ? Settings.ToolbarHorizontalInset
            : position.EndsWith("Right", StringComparison.Ordinal) ? viewportWidth - Settings.ToolbarHorizontalInset - toolbarWidth
            : (viewportWidth - toolbarWidth) / 2;
        double top = position.StartsWith("Top", StringComparison.Ordinal) ? Settings.ToolbarVerticalInset
            : vertical ? (viewportHeight - toolbarHeight) / 2
            : viewportHeight - Settings.ToolbarVerticalInset - toolbarHeight;

        StackPanel buttons = new()
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = 4 * scale
        };
        foreach ((string symbol, string label) in new[]
                 {
                     (nameof(FluentGlyphs.Edit), "编辑看板"),
                     (nameof(FluentGlyphs.Settings), "设置"),
                     (nameof(FluentGlyphs.FullScreen), "进入全屏")
                 })
        {
            StackPanel content = new()
            {
                Orientation = Orientation.Vertical,
                Spacing = (vertical ? 2 : 4) * scale,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            FluentIcon icon = new()
            {
                Symbol = symbol,
                FontSize = 18 * scale,
                Foreground = BoardTheme.TextColor.ToBrush()
            };
            TextBlock text = new()
            {
                Text = label,
                FontSize = (vertical ? 8 : 10) * scale,
                Foreground = BoardTheme.TextColor.ToBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                IsVisible = !Settings.ToolbarIconOnly
            };
            // 上方停靠时控制窗的说明文字排在图标上面，与看板一致。
            bool labelFirst = !vertical && position.StartsWith("Top", StringComparison.Ordinal);
            if (labelFirst) content.Children.Add(text);
            content.Children.Add(icon);
            if (!labelFirst) content.Children.Add(text);
            buttons.Children.Add(new Button
            {
                Width = (Settings.ToolbarIconOnly ? 44 : vertical ? 64 : 88) * scale,
                Height = buttonCross * scale,
                Padding = new Thickness(6 * scale),
                Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(Math.Clamp(Settings.ToolbarRadius, 0, 40)),
                Content = content
            });
        }

        Border toolbar = new()
        {
            Width = toolbarWidth,
            Height = toolbarHeight,
            Padding = new Thickness(7 * scale),
            CornerRadius = new CornerRadius(Math.Clamp(Settings.ToolbarRadius, 0, 40)),
            Background = ResolveToolbarBackground(),
            BorderBrush = BoardTheme.LineColor.ToBrush(),
            BorderThickness = new Thickness(1),
            Child = buttons
        };
        Canvas.SetLeft(toolbar, left);
        Canvas.SetTop(toolbar, top);
        viewport.Children.Add(toolbar);

        double centerX = left + toolbarWidth / 2, centerY = top + toolbarHeight / 2;
        Canvas crop = new() { Width = cropWidth, Height = cropHeight, ClipToBounds = true };
        Canvas.SetLeft(viewport, -Math.Clamp(centerX - cropWidth / 2, 0, Math.Max(0, viewportWidth - cropWidth)));
        Canvas.SetTop(viewport, -Math.Clamp(centerY - cropHeight / 2, 0, Math.Max(0, viewportHeight - cropHeight)));
        crop.Children.Add(viewport);
        scene.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = crop });
    }

    /// <summary>预览里的背景层：直接复用看板的 BackgroundVisual，颜色层、图片与毛玻璃规则完全一致。</summary>
    private void AddPreviewBackground(Grid target, Func<BackgroundSettings> style, Func<bool>? shared = null)
    {
        BackgroundVisual background = new();
        void Refresh() => background.Apply(shared?.Invoke() == true ? new BackgroundSettings() : style());
        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
        target.Children.Add(background);
    }

    /// <summary>预览用的真实磁贴控件：独立模型副本，只读，不接保存回调。</summary>
    private SubjectTileControl PreviewTile(SubjectBoard subject)
    {
        SubjectTileControl tile = new(
            subject,
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            () => { });
        // 预览可以取回控件内部的模型副本来排布，因此带上数据上下文。
        tile.DataContext = subject;
        tile.IsHitTestVisible = false;
        tile.SetEditing(false);
        return tile;
    }

    /// <summary>磁贴外观页的固定示例：三行正文，主题色与高光色跟随当前色系。</summary>
    private static SubjectBoard CreateTileAppearanceSample()
    {
        SubjectBoard sample = new()
        {
            Name = "这是标题~",
            TileWidth = 560,
            TileHeight = 320,
            AccentHex = ColorPalette.ResolveAccent(TileSampleAccentHex, false, ColorPalette.IsMacaron),
            IsAccentExplicit = true
        };
        BoardColor white = BoardColor.FromRgb(247, 247, 249);
        BoardColor highlight = BoardColor.Parse(ColorPalette.Resolve(TileSampleHighlightHex), BoardColor.FromRgb(244, 114, 182));
        sample.Entries.Add(CreateSampleEntry(TileSampleFirstLine, white, null));
        sample.Entries.Add(CreateSampleEntry("Through hardships to the stars.", white, null));
        sample.Entries.Add(CreateSampleEntry("Per ardua ad astra.", white, highlight));
        return sample;
    }

    /// <summary>示例正文：默认字体、加粗、白色；高光色用格式区间表达，跟随色系解析。</summary>
    private static HomeworkEntry CreateSampleEntry(string text, BoardColor foreground, BoardColor? highlight)
    {
        RichTextFormat format = new(Bold: true, Foreground: foreground);
        List<RichTextSpan> spans = [new(0, text.Length, format)];
        if (highlight is { } color && text.EndsWith("astra.", StringComparison.Ordinal))
        {
            int start = text.Length - "astra.".Length;
            spans.Add(new RichTextSpan(start, "astra".Length, format with { Highlight = color }));
        }

        RichTextDocument document = RichTextDocument.FromSpans(text, spans);
        HomeworkEntry entry = new() { Content = document.Text };
        // 用项目自己的 RTF 写入器保存示例，保证预览走的是和看板完全相同的显示路径。
        entry.RtfContent = document.ToRtf();
        return entry;
    }

    /// <summary>预览里的时钟：文本绑定到看板上的真实控件，时间与日期始终和看板一致。</summary>
    private Control PreviewClock()
    {
        TextBlock LiveText(TextBlock source, double size)
        {
            TextBlock text = new()
            {
                FontSize = size,
                Foreground = BoardTheme.TextColor.ToBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = source.FontWeight,
                FontFamily = source.FontFamily
            };
            text.Bind(TextBlock.TextProperty, new Binding("Text") { Source = source, Mode = BindingMode.OneWay });
            return text;
        }

        StackPanel content = new() { Spacing = 18 };
        // 与看板同款结构：秒数右侧留空多少，左侧就补多少，时间数字因此落在中轴线上。
        Grid time = new() { ColumnSpacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        time.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        TextBlock timeText = LiveText(MainTimeText, 112);
        TextBlock seconds = LiveText(SecondsText, 28);
        seconds.Margin = new Thickness(0, 17, 0, 0);
        Border mirror = new();
        mirror.Bind(WidthProperty, new Binding("Bounds.Width") { Source = seconds, Mode = BindingMode.OneWay });
        Grid.SetColumn(mirror, 0);
        Grid.SetColumn(timeText, 1);
        Grid.SetColumn(seconds, 2);
        time.Children.Add(mirror);
        time.Children.Add(timeText);
        time.Children.Add(seconds);
        content.Children.Add(time);
        content.Children.Add(LiveText(ClockDateText, 20));
        StackPanel components = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        components.Children.Add(LiveText(WeatherText, 14));
        components.Children.Add(LiveText(NoiseText, 14));
        content.Children.Add(components);
        return new Viewbox { Margin = new Thickness(24), Stretch = Stretch.Uniform, Child = content };
    }

    /// <summary>预览里的作业板：按当前项目磁贴的真实位置与内容只读复现一块看板。</summary>
    private Control PreviewBoard()
    {
        double width = Math.Max(430, BoardCanvas.Bounds.Width > 1 ? BoardCanvas.Bounds.Width : 1100);
        double height = Math.Max(300, BoardCanvas.Bounds.Height > 1 ? BoardCanvas.Bounds.Height : 780);
        Canvas canvas = new() { Width = width, Height = height, ClipToBounds = true };
        foreach (SubjectBoard subject in _viewModel.Subjects)
        {
            SubjectTileControl tile = PreviewTile(CloneForPreview(subject));
            Canvas.SetLeft(tile, subject.X);
            Canvas.SetTop(tile, subject.Y);
            canvas.Children.Add(tile);
        }

        if (_viewModel.Subjects.Count == 0)
        {
            SubjectBoard fallback = new() { Name = "语文", TileWidth = 430, TileHeight = 220 };
            fallback.Entries.Add(new HomeworkEntry { Content = "阅读课文，完成课后练习。" });
            canvas.Children.Add(PreviewTile(fallback));
        }

        return new Viewbox { Margin = new Thickness(16), Stretch = Stretch.Uniform, Child = canvas };
    }

    /// <summary>预览用的科目副本：走与项目文件相同的捕获/还原路径，不修改看板数据。</summary>
    private static SubjectBoard CloneForPreview(SubjectBoard subject) =>
        AppDataStore.RestoreSubjects(AppDataStore.CaptureSubjects([subject]))[0];

    /// <summary>预览底图：随包分发的示例图片，按需解码一次。</summary>
    private static Bitmap? LoadPreviewBackground()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri("avares://Pancake.Ui/Assets/Settings/tile-preview-background.png"));
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // 资源缺失不应影响设置页使用，预览退回纯色底。
            return null;
        }
    }
}
