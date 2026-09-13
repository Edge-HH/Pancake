using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 看板布局：分屏、仅作业、仅时钟、自由布局，以及时钟区里时钟与组件栏的摆放。
/// 位置全部保存在项目设置的 Widgets 字典中，并区分“分屏/仅时钟”两套坐标。
/// </summary>
public sealed partial class MainWindow
{
    private const double DockedClockFallbackAspectRatio = 1.8;
    private const double DockedComponentsAspectRatio = 6.5;

    private readonly Dictionary<string, Viewbox> _freeWidgets = [];
    private Thumb? _splitter;
    private bool _layingOut;
    private bool _splitDragging;
    private double _splitDragRatio;
    private bool _boardLayoutPending;












    private bool IsCompact => this.FindControl<Grid>("RootShell")!.Bounds.Width < 900;

    private double DisplayWidth => Math.Max(1, DisplayRoot.Bounds.Width);

    private double DisplayHeight => Math.Max(1, DisplayRoot.Bounds.Height);

    /// <summary>
    /// 网格大小变化时把已有磁贴重新吸附到新网格：位置和尺寸始终落在网格线上。
    /// 设置页打开时看板被折叠，这里只改数据，回到看板时再统一应用。
    /// </summary>
    private void SnapTilesToGrid()
    {
        if (!IsGridSnappingEnabled) return;
        double grid = GridSize;
        double width = Math.Floor(BoardCanvas.Width / grid) * grid;
        double height = Math.Floor(BoardCanvas.Height / grid) * grid;
        double minWidth = Math.Ceiling(280 / grid) * grid;
        if (width < minWidth || height < SubjectTileControl.MinimumTileHeight) return;
        foreach (SubjectBoard subject in _viewModel.Subjects)
        {
            subject.TileWidth = Math.Clamp(SnapToGrid(subject.TileWidth), minWidth, width);
            subject.TileHeight = Math.Clamp(SnapToGrid(subject.TileHeight), SubjectTileControl.MinimumTileHeight, height);
            subject.X = Math.Clamp(SnapToGrid(subject.X), 0, width - subject.TileWidth);
            subject.Y = Math.Clamp(SnapToGrid(subject.Y), 0, height - subject.TileHeight);
        }

        _boardLayoutPending = true;
    }

    /// <summary>设置页里改过网格大小后，回到看板时一次性应用坐标、尺寸与网格重画。</summary>
    private void ApplyPendingBoardLayout()
    {
        if (!_boardLayoutPending) return;
        _boardLayoutPending = false;
        _renderedGridWidth = 0;
        foreach (SubjectBoard subject in _viewModel.Subjects)
        {
            if (FindTile(subject) is not { } tile) continue;
            tile.ApplyModelLayout();
            Canvas.SetLeft(tile, subject.X);
            Canvas.SetTop(tile, subject.Y);
        }

        UpdateBoardBounds();
    }

    private void ApplyDisplayLayout()
    {
        if (!_isLoaded || _layingOut) return;
        _layingOut = true;
        try
        {
            bool free = Settings.LayoutMode == "Free";
            bool split = Settings.LayoutMode == "Split";
            bool compact = IsCompact;
            DisplayGrid.ColumnDefinitions[0].MinWidth = 0;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(0);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(ClockPanel, 0);
            Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(BoardWorkspace, 0);
            Grid.SetRow(BoardWorkspace, 0);
            ClockPanel.IsVisible = !free && Settings.LayoutMode != "Board";
            BoardWorkspace.IsVisible = Settings.LayoutMode != "Clock";
            ClockPanel.BorderThickness = new Thickness(0);
            this.FindControl<TextBlock>("MainTimeText")!.FontSize = compact ? 72 : 112;
            this.FindControl<TextBlock>("BoardModeHint")!.IsVisible = BoardWorkspace.IsVisible;
            if (split)
            {
                double ratio = Math.Clamp(Settings.SplitRatio, .05, .95);
                if (compact)
                {
                    DisplayGrid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                    DisplayGrid.RowDefinitions[1].Height = new GridLength(1 - ratio, GridUnitType.Star);
                    Grid.SetRow(BoardWorkspace, 1);
                }
                else
                {
                    DisplayGrid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                    DisplayGrid.ColumnDefinitions[1].Width = new GridLength(1 - ratio, GridUnitType.Star);
                    Grid.SetColumn(BoardWorkspace, 1);
                }
            }

            ApplyFreeWidgets(free);
            if (!free && Settings.LayoutMode != "Board") ApplyDockedClockLayout();
            UpdateLayoutHandles();
            // 仅时钟模式才显示全屏笔迹层，切换布局时同步显示与可写状态。
            ApplyInkMode();
            UpdateZoomIslandVisibility();
        }
        finally
        {
            _layingOut = false;
        }
    }

    /// <summary>自由布局下把时钟、天气、噪音搬进同一个绝对定位容器；退出时再放回时钟区。</summary>
    private void ApplyFreeWidgets(bool free)
    {
        if (free && _freeWidgets.Count == 0)
        {
            ClockComponents.Children.Remove(WeatherWidget);
            ClockComponents.Children.Remove(NoiseWidget);
            ClockLayoutCanvas.Children.Remove(ClockContentView);
            foreach ((string key, Control element) in new (string, Control)[]
                     {
                         ("Clock", ClockContentView), ("Weather", WeatherWidget), ("Noise", NoiseWidget)
                     })
            {
                Viewbox view = new() { Child = element, Stretch = Stretch.Uniform };
                _freeWidgets[key] = view;
                FreeWidgetsCanvas.Children.Add(view);
            }
        }
        else if (!free && _freeWidgets.Count != 0)
        {
            foreach (Viewbox view in _freeWidgets.Values) view.Child = null;
            FreeWidgetsCanvas.Children.Clear();
            _freeWidgets.Clear();
            ClockLayoutCanvas.Children.Add(ClockContentView);
            ClockComponents.Children.Add(WeatherWidget);
            ClockComponents.Children.Add(NoiseWidget);
        }

        foreach ((string key, Viewbox view) in _freeWidgets)
        {
            if (!Settings.Widgets.TryGetValue(key, out RegionPlacement? placement))
            {
                // 首次进入自由布局时把组件排在作业板右侧，避免压在磁贴上。
                double x = Math.Clamp(
                    _viewModel.Subjects.Select(s => s.X + s.TileWidth + 24).DefaultIfEmpty(20).Max(),
                    0,
                    Math.Max(0, DisplayWidth - 420));
                Settings.Widgets[key] = placement = key switch
                {
                    "Clock" => new RegionPlacement { X = x, Y = 20, Width = 400, Height = 210 },
                    "Weather" => new RegionPlacement { X = x, Y = 250, Width = 190, Height = 64 },
                    "Noise" => new RegionPlacement { X = x + 210, Y = 250, Width = 190, Height = 64 },
                    _ => new RegionPlacement { X = x, Y = 340, Width = 240, Height = 100 }
                };
            }

            view.Width = Math.Max(80, placement.Width);
            view.Height = Math.Max(48, placement.Height);
            Canvas.SetLeft(view, Math.Max(0, placement.X));
            Canvas.SetTop(view, Math.Max(0, placement.Y));
        }
    }

    private double DockedClockAspectRatio
    {
        get
        {
            Size size = ClockContentView.Child?.Bounds.Size ?? default;
            // 未完成布局时 Bounds 还是 0，此时用上一帧的期望尺寸，避免中轴线来回跳动。
            if (size.Width <= 1 || size.Height <= 1) size = ClockContentView.Child?.DesiredSize ?? default;
            return size.Width > 1 && size.Height > 1
                ? Math.Clamp(size.Width / size.Height, 1, 4)
                : DockedClockFallbackAspectRatio;
        }
    }

    /// <summary>
    /// 时钟区内的中轴布局：时钟在上、组件栏在下，两者都水平居中并按各自宽高比缩放。
    /// 用户手动调过尺寸的位置会保留，不再被默认值覆盖。
    /// </summary>
    private void ApplyDockedClockLayout()
    {
        double width = ClockPanel.Bounds.Width;
        double height = ClockPanel.Bounds.Height;
        if (!_isLoaded || width < 1 || height < 1) return;

        bool splitMode = Settings.LayoutMode == "Split";
        string prefix = splitMode ? "Split" : "ClockMode";
        string clockKey = prefix + "Clock";
        string componentsKey = prefix + "Components";
        double clockAspectRatio = DockedClockAspectRatio;
        if (!Settings.Widgets.TryGetValue(clockKey, out RegionPlacement? clock) ||
            !Settings.CustomizedDockedWidgets.Contains(clockKey))
        {
            double clockWidth = Math.Min(splitMode ? 720 : 760, width * (splitMode ? .84 : .68));
            double clockHeight = clockWidth / clockAspectRatio;
            double componentsHeight = Math.Min(splitMode ? 520 : 560, width * .72) / DockedComponentsAspectRatio;
            double groupHeight = clockHeight + componentsHeight + 18;
            double groupTop = Math.Max(0, (height - groupHeight) / 2 - (splitMode ? height * .08 : 0));
            Settings.Widgets[clockKey] = clock = new RegionPlacement { Y = groupTop, Width = clockWidth, Height = clockHeight };
        }
        else if (Math.Abs(clock.Width / Math.Max(1, clock.Height) - clockAspectRatio) > .01)
        {
            // 旧版本保存过多余的右侧空白：保留视觉高度，只收紧宽度并回到中轴线。
            clock.Width = clock.Height * clockAspectRatio;
        }

        if (!Settings.Widgets.TryGetValue(componentsKey, out RegionPlacement? components) ||
            !Settings.CustomizedDockedWidgets.Contains(componentsKey))
        {
            double componentsWidth = Math.Min(splitMode ? 520 : 560, width * .72);
            double componentsHeight = componentsWidth / DockedComponentsAspectRatio;
            double componentsY = Math.Min(Math.Max(0, height - componentsHeight), clock.Y + clock.Height + 18);
            Settings.Widgets[componentsKey] = components =
                new RegionPlacement { Y = componentsY, Width = componentsWidth, Height = componentsHeight };
        }

        WidgetLayout.ResizeCentered(clock, 0, 0, clockAspectRatio, 220, width, height);
        WidgetLayout.ResizeCentered(components, 0, 0, DockedComponentsAspectRatio, 240, width, height);
        ApplyCenteredPlacement(ClockContentView, clock);
        ApplyCenteredPlacement(ClockComponentsView, components);
    }

    private static void ApplyCenteredPlacement(Control element, RegionPlacement placement)
    {
        element.Width = placement.Width;
        element.Height = placement.Height;
        Canvas.SetLeft(element, placement.X);
        Canvas.SetTop(element, placement.Y);
    }

    /// <summary>重建分隔条与组件移动、缩放入口；查看模式下只保留分隔条。</summary>
    private void UpdateLayoutHandles()
    {
        if (!_isLoaded || _splitDragging) return;
        FreeLayoutHandles.Children.Clear();
        bool split = Settings.LayoutMode == "Split";
        // 单区模式同样保留分隔滑条：仅时钟贴右侧、仅作业贴左侧，随时能拖回分屏。
        bool singleZone = Settings.LayoutMode is "Clock" or "Board";
        bool canEditWidgets = _isEditing && this.FindControl<ToggleButton>("GlobalPenButton")?.IsChecked != true;
        FreeLayoutHandles.IsHitTestVisible = split || singleZone || (Settings.LayoutMode == "Free" && canEditWidgets);
        if (split || singleZone) AddSplitHandle(split);
        if (Settings.LayoutMode is "Split" or "Clock" && canEditWidgets)
        {
            string prefix = Settings.LayoutMode == "Split" ? "Split" : "ClockMode";
            AddCenteredWidgetInteraction(prefix + "Clock", ClockContentView, "时钟", DockedClockAspectRatio, 220);
            AddCenteredWidgetInteraction(prefix + "Components", ClockComponentsView, "组件栏", DockedComponentsAspectRatio, 240);
        }

        if (Settings.LayoutMode != "Free" || !canEditWidgets) return;
        foreach ((string key, Viewbox view) in _freeWidgets)
        {
            RegionPlacement placement = Settings.Widgets[key];
            AddWidgetInteractionLayer(
                view,
                placement,
                key == "Clock" ? "时钟" : "组件",
                (dx, dy) => WidgetLayout.Move(placement, dx, dy, DisplayWidth, DisplayHeight),
                (dx, dy) => WidgetLayout.Resize(placement, dx, dy, DisplayWidth, DisplayHeight),
                ScheduleSave);
        }
    }

    /// <summary>
    /// 分隔滑条：分屏时停在两区交界处，单区时贴在被占满的一侧边缘。
    /// 从单区往回流方向拖动即恢复分屏，拖到另一端则再次切换单区。
    /// </summary>
    private void AddSplitHandle(bool split)
    {
        bool compact = IsCompact;
        bool clockOnly = Settings.LayoutMode == "Clock";
        double width = DisplayWidth;
        double height = DisplayHeight;
        _splitter = ThumbVisuals.Apply(new Thumb
        {
            Background = new SolidColorBrush(BoardColor.FromArgb(95, 128, 128, 128).ToColor()),
            Width = compact ? width : 10,
            Height = compact ? 10 : height
        });
        ToolTip.SetTip(_splitter, split
            ? "拖动调整分屏，拖至边缘切换单区"
            : compact
                ? clockOnly ? "向上拖动回到分屏" : "向下拖动回到分屏"
                : clockOnly ? "向左拖动回到分屏" : "向右拖动回到分屏");
        if (compact)
        {
            Canvas.SetLeft(_splitter, 0);
            Canvas.SetTop(_splitter, split ? height * Settings.SplitRatio - 5 : clockOnly ? height - 10 : 0);
        }
        else
        {
            Canvas.SetLeft(_splitter, split ? width * Settings.SplitRatio - 5 : clockOnly ? width - 10 : 0);
            Canvas.SetTop(_splitter, 0);
        }

        _splitter.DragStarted += (_, _) => BeginSplitDrag();
        _splitter.DragDelta += (_, args) => DragSplitHandle(compact, args.Vector.X, args.Vector.Y);
        _splitter.DragCompleted += (_, _) => CompleteSplitDrag();
        _splitter.ZIndex = 100;
        FreeLayoutHandles.Children.Add(_splitter);
    }

    /// <summary>按下分隔条时记录起始比例；单区模式按贴边位置起步（仅时钟 1，仅作业 0）。</summary>
    private void BeginSplitDrag()
    {
        _splitDragging = true;
        _splitDragRatio = Settings.LayoutMode switch
        {
            "Clock" => 1,
            "Board" => 0,
            _ => Settings.SplitRatio
        };
    }

    /// <summary>拖动分隔条：按位移换算比例，拖进分屏范围就即时切回分屏预览。</summary>
    private void DragSplitHandle(bool compact, double horizontalChange, double verticalChange)
    {
        double width = DisplayWidth;
        double height = DisplayHeight;
        _splitDragRatio = Math.Clamp(
            _splitDragRatio + (compact ? verticalChange : horizontalChange) / (compact ? height : width),
            .01,
            .99);
        Settings.SplitRatio = _splitDragRatio;
        if (Settings.LayoutMode != "Split" && WidgetLayout.CompleteSplit(_splitDragRatio) == "Split")
        {
            Settings.LayoutMode = "Split";
        }

        ApplyDisplayLayout();
        if (_splitter is null) return;
        if (compact) Canvas.SetTop(_splitter, height * Settings.SplitRatio - 5);
        else Canvas.SetLeft(_splitter, width * Settings.SplitRatio - 5);
    }

    /// <summary>松开分隔条：落在两端就切换单区并复位比例，否则留在分屏并保留拖出来的比例。</summary>
    private void CompleteSplitDrag()
    {
        _splitDragging = false;
        string completed = WidgetLayout.CompleteSplit(_splitDragRatio);
        if (completed is "Board" or "Clock")
        {
            Settings.LayoutMode = completed;
            Settings.SplitRatio = .4;
        }

        SyncLayoutChoiceSelection();
        SettingChanged();
    }

    private void AddCenteredWidgetInteraction(
        string key,
        Control view,
        string label,
        double aspectRatio,
        double minimumWidth)
    {
        if (!Settings.Widgets.TryGetValue(key, out RegionPlacement? placement)) return;
        double width = ClockPanel.Bounds.Width;
        double height = ClockPanel.Bounds.Height;
        AddWidgetInteractionLayer(
            view,
            placement,
            label,
            (_, dy) => WidgetLayout.MoveVerticallyCentered(placement, dy, width, height),
            (dx, dy) => WidgetLayout.ResizeCentered(placement, dx, dy, aspectRatio, minimumWidth, width, height),
            () =>
            {
                Settings.CustomizedDockedWidgets.Add(key);
                ScheduleSave();
            });
    }

    /// <summary>
    /// 组件的移动与缩放层：指针进入时显示选中框与右下角缩放手柄，
    /// 拖动期间持续更新位置，松手后写入设置。
    /// </summary>
    private void AddWidgetInteractionLayer(
        Control view,
        RegionPlacement placement,
        string label,
        Action<double, double> movePlacement,
        Action<double, double> resizePlacement,
        Action complete)
    {
        Grid interactionLayer = new()
        {
            Width = placement.Width,
            Height = placement.Height,
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor())
        };
        Border selectionBorder = new()
        {
            BorderBrush = new SolidColorBrush(BoardColor.FromRgb(96, 165, 250).ToColor()),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Opacity = 0,
            IsHitTestVisible = false
        };
        Thumb move = ThumbVisuals.Apply(new Thumb { Background = new SolidColorBrush(BoardColor.Transparent.ToColor()) });
        Thumb resize = ThumbVisuals.Apply(new Thumb
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(BoardColor.FromRgb(96, 165, 250).ToColor()),
            Opacity = 0
        });
        // 选中框画在移动层之上、缩放手柄之下，手柄始终位于最上层。
        interactionLayer.Children.Add(move);
        interactionLayer.Children.Add(selectionBorder);
        interactionLayer.Children.Add(resize);
        bool dragging = false;
        bool pointerOver = false;
        void SetSelected(bool selected)
        {
            selectionBorder.Opacity = selected ? 1 : 0;
            resize.Opacity = selected ? 1 : 0;
        }

        interactionLayer.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            SetSelected(true);
        };
        interactionLayer.PointerExited += (_, _) =>
        {
            pointerOver = false;
            if (!dragging) SetSelected(false);
        };
        void PositionInteraction()
        {
            interactionLayer.Width = placement.Width;
            interactionLayer.Height = placement.Height;
            Canvas.SetLeft(interactionLayer, placement.X);
            Canvas.SetTop(interactionLayer, placement.Y);
            ApplyCenteredPlacement(view, placement);
        }

        ToolTip.SetTip(move, $"拖动{label}上下移动（锁定中轴线）");
        ToolTip.SetTip(resize, $"缩放{label}");
        move.DragDelta += (_, args) =>
        {
            movePlacement(args.Vector.X, args.Vector.Y);
            PositionInteraction();
        };
        move.DragStarted += (_, _) =>
        {
            dragging = true;
            SetSelected(true);
        };
        resize.DragDelta += (_, args) =>
        {
            resizePlacement(args.Vector.X, args.Vector.Y);
            PositionInteraction();
        };
        resize.DragStarted += (_, _) =>
        {
            dragging = true;
            SetSelected(true);
        };
        void CompleteDrag()
        {
            dragging = false;
            // 松手后指针通常仍停在组件上，此时保持选中框可见，等指针离开再收起。
            SetSelected(pointerOver);
            complete();
        }

        move.DragCompleted += (_, _) => CompleteDrag();
        resize.DragCompleted += (_, _) => CompleteDrag();
        interactionLayer.ZIndex = 50;
        PositionInteraction();
        FreeLayoutHandles.Children.Add(interactionLayer);
    }

    private void UpdateSubjectCount()
    {
        this.FindControl<TextBlock>("SubjectCountText")!.Text = _viewModel.SubjectCountText;
    }

    private double SnapToGrid(double value) => Math.Round(value / GridSize) * GridSize;
}
