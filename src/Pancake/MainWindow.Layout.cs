using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private const double DockedClockFallbackAspectRatio = 1.8;
    private const double DockedComponentsAspectRatio = 6.5;
    private readonly Dictionary<string, Viewbox> _freeWidgets = [];
    private Thumb? _splitter;
    private bool _layingOut;
    private bool _splitDragging;
    private double _splitDragRatio;
    private Dictionary<string, RegionPlacement>? _widgetEditSnapshot;
    private HashSet<string>? _dockedCustomizationEditSnapshot;
    private bool _boardLayoutPending;

    /// <summary>
    /// 网格大小变化时把已有磁贴重新吸附到新网格：位置和尺寸始终落在网格线上，不会停在旧网格。
    /// 设置页打开时看板是折叠的，这里只改数据；回到看板时再统一应用，拖动滑块因此不会卡顿。
    /// </summary>
    private void SnapTilesToGrid()
    {
        if (!IsGridSnappingEnabled) return;
        double grid = GridSize;
        double width = Math.Floor(BoardCanvas.Width / grid) * grid;
        double height = Math.Floor(BoardCanvas.Height / grid) * grid;
        double minWidth = Math.Ceiling(280 / grid) * grid;
        // 画板还放不下一个最小磁贴时保持原样，等画板尺寸可用后再吸附。
        if (width < minWidth || height < SubjectTileControl.MinimumTileHeight) return;
        foreach (SubjectBoard subject in ViewModel.Subjects)
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
        foreach (SubjectBoard subject in ViewModel.Subjects)
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
        if (DisplayGrid is null || _layingOut) return;
        _layingOut = true;
        try
        {
            bool free = _settings.LayoutMode == "Free", split = _settings.LayoutMode == "Split";
            bool compact = RootShell.ActualWidth < 900;
            DisplayGrid.ColumnDefinitions[0].MinWidth = 0;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(0);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(ClockPanel, 0); Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(BoardWorkspace, 0); Grid.SetRow(BoardWorkspace, 0);
            ClockPanel.Visibility = free || _settings.LayoutMode == "Board" ? Visibility.Collapsed : Visibility.Visible;
            BoardWorkspace.Visibility = _settings.LayoutMode == "Clock" || CurrentProject is null ? Visibility.Collapsed : Visibility.Visible;
            ClockPanel.BorderThickness = new Thickness(0);
            MainTimeText.FontSize = compact ? 72 : 112;
            if (split)
            {
                double ratio = Math.Clamp(_settings.SplitRatio, .05, .95);
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
            if (!free && _settings.LayoutMode != "Board") ApplyDockedClockLayout();
            UpdateLayoutHandles();
            // 仅时钟模式才显示和书写整屏笔迹，切换布局时同步显示状态。
            RefreshClockInk();
        }
        finally { _layingOut = false; }
    }

    private void ApplyFreeWidgets(bool free)
    {
        // 将现有组件视图迁入统一容器，原服务和数据绑定继续复用。
        if (free && _freeWidgets.Count == 0)
        {
            ClockComponents.Children.Remove(WeatherWidget); ClockComponents.Children.Remove(NoiseWidget);
            ClockLayoutCanvas.Children.Remove(ClockContentView);
            foreach (var (key, element) in new (string, UIElement)[] { ("Clock", ClockContentView), ("Weather", WeatherWidget), ("Noise", NoiseWidget) })
            {
                Viewbox view = new() { Child = element, Stretch = Stretch.Uniform };
                _freeWidgets[key] = view; FreeWidgetsCanvas.Children.Add(view);
            }
        }
        else if (!free && _freeWidgets.Count != 0)
        {
            foreach (var view in _freeWidgets.Values) view.Child = null;
            FreeWidgetsCanvas.Children.Clear(); _freeWidgets.Clear();
            ClockLayoutCanvas.Children.Add(ClockContentView);
            ClockComponents.Children.Add(WeatherWidget); ClockComponents.Children.Add(NoiseWidget);
        }
        foreach (var (key, view) in _freeWidgets)
        {
            if (!_settings.Widgets.TryGetValue(key, out var placement))
            {
                double x = Math.Clamp(ViewModel.Subjects.Select(s => s.X + s.TileWidth + 24).DefaultIfEmpty(20).Max(), 0, Math.Max(0, DisplayRoot.ActualWidth - 420));
                _settings.Widgets[key] = placement = key switch
                {
                    "Clock" => new() { X = x, Y = 20, Width = 400, Height = 210 },
                    "Weather" => new() { X = x, Y = 250, Width = 190, Height = 64 },
                    "Noise" => new() { X = x + 210, Y = 250, Width = 190, Height = 64 },
                    _ => new() { X = x, Y = 340, Width = 240, Height = 100 }
                };
            }
            view.Width = Math.Max(80, placement.Width); view.Height = Math.Max(48, placement.Height);
            Canvas.SetLeft(view, Math.Max(0, placement.X)); Canvas.SetTop(view, Math.Max(0, placement.Y));
        }
    }

    private void ClockPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || _settings.LayoutMode is not ("Split" or "Clock")) return;
        ApplyDockedClockLayout();
        UpdateLayoutHandles();
    }

    private void ClockContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded || _layingOut || _settings.LayoutMode is not ("Split" or "Clock")) return;
        ApplyDockedClockLayout();
        UpdateLayoutHandles();
    }

    private double DockedClockAspectRatio
    {
        get
        {
            double contentWidth = ClockContentView.Child?.DesiredSize.Width ?? 0;
            double contentHeight = ClockContentView.Child?.DesiredSize.Height ?? 0;
            return contentWidth > 1 && contentHeight > 1
                ? Math.Clamp(contentWidth / contentHeight, 1, 4)
                : DockedClockFallbackAspectRatio;
        }
    }

    private void ApplyDockedClockLayout()
    {
        double width = ClockPanel.ActualWidth, height = ClockPanel.ActualHeight;
        if (!_isLoaded || width < 1 || height < 1) return;

        bool splitMode = _settings.LayoutMode == "Split";
        string prefix = splitMode ? "Split" : "ClockMode";
        string clockKey = prefix + "Clock", componentsKey = prefix + "Components";
        double clockAspectRatio = DockedClockAspectRatio;
        if (!_settings.Widgets.TryGetValue(clockKey, out RegionPlacement? clock) || !_settings.CustomizedDockedWidgets.Contains(clockKey))
        {
            double clockWidth = Math.Min(splitMode ? 720 : 760, width * (splitMode ? .84 : .68));
            double clockHeight = clockWidth / clockAspectRatio;
            double componentsHeight = Math.Min(splitMode ? 520 : 560, width * .72) / DockedComponentsAspectRatio;
            double groupHeight = clockHeight + componentsHeight + 18;
            double groupTop = Math.Max(0, (height - groupHeight) / 2 - (splitMode ? height * .08 : 0));
            _settings.Widgets[clockKey] = clock = new RegionPlacement { Y = groupTop, Width = clockWidth, Height = clockHeight };
        }
        else if (Math.Abs(clock.Width / Math.Max(1, clock.Height) - clockAspectRatio) > .01)
        {
            // 旧版本用固定宽高比保存了多余的右侧空白；保留视觉高度，只收紧宽度并重新回到中轴线。
            clock.Width = clock.Height * clockAspectRatio;
        }
        if (!_settings.Widgets.TryGetValue(componentsKey, out RegionPlacement? components) || !_settings.CustomizedDockedWidgets.Contains(componentsKey))
        {
            double componentsWidth = Math.Min(splitMode ? 520 : 560, width * .72);
            double componentsHeight = componentsWidth / DockedComponentsAspectRatio;
            double componentsY = Math.Min(Math.Max(0, height - componentsHeight), clock.Y + clock.Height + 18);
            _settings.Widgets[componentsKey] = components = new RegionPlacement { Y = componentsY, Width = componentsWidth, Height = componentsHeight };
        }
        WidgetLayout.ResizeCentered(clock, 0, 0, clockAspectRatio, 220, width, height);
        WidgetLayout.ResizeCentered(components, 0, 0, DockedComponentsAspectRatio, 240, width, height);
        ApplyCenteredPlacement(ClockContentView, clock);
        ApplyCenteredPlacement(ClockComponentsView, components);
    }

    private static void ApplyCenteredPlacement(FrameworkElement element, RegionPlacement placement)
    {
        element.Width = placement.Width;
        element.Height = placement.Height;
        Canvas.SetLeft(element, placement.X);
        Canvas.SetTop(element, placement.Y);
    }

    private void UpdateLayoutHandles()
    {
        if (FreeLayoutHandles is null || _splitDragging) return;
        FreeLayoutHandles.Children.Clear();
        bool split = _settings.LayoutMode == "Split";
        // 单区模式同样保留分隔滑条：仅时钟贴右侧、仅作业贴左侧，用户随时能拖回分屏。
        bool singleZone = _settings.LayoutMode is "Clock" or "Board";
        bool canEditWidgets = _isEditing && GlobalPenButton.IsChecked != true;
        FreeLayoutHandles.IsHitTestVisible = split || singleZone || (_settings.LayoutMode is "Free" or "Clock" && canEditWidgets);
        if (split || singleZone) AddSplitHandle(split);
        if (_settings.LayoutMode is "Split" or "Clock" && canEditWidgets)
        {
            string prefix = _settings.LayoutMode == "Split" ? "Split" : "ClockMode";
            AddCenteredWidgetInteraction(prefix + "Clock", ClockContentView, "时钟", DockedClockAspectRatio, 220);
            AddCenteredWidgetInteraction(prefix + "Components", ClockComponentsView, "组件栏", DockedComponentsAspectRatio, 240);
        }
        if (_settings.LayoutMode != "Free" || !canEditWidgets) return;
        foreach (var (key, view) in _freeWidgets)
        {
            RegionPlacement placement = _settings.Widgets[key];
            AddWidgetInteractionLayer(view, placement, key == "Clock" ? "时钟" : "组件",
                (dx, dy) => WidgetLayout.Move(placement, dx, dy, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight),
                (dx, dy) => WidgetLayout.Resize(placement, dx, dy, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight),
                ScheduleSave);
        }
    }

    /// <summary>
    /// 分隔滑条：分屏时停在两区交界处，单区时贴在被占满的一侧边缘（仅时钟在右、仅作业在左，
    /// 窄窗口改成下、上）。从单区往回流方向拖动即恢复分屏，拖到另一端则再次切换单区。
    /// </summary>
    private void AddSplitHandle(bool split)
    {
        bool compact = RootShell.ActualWidth < 900;
        bool clockOnly = _settings.LayoutMode == "Clock";
        double width = Math.Max(1, DisplayRoot.ActualWidth), height = Math.Max(1, DisplayRoot.ActualHeight);
        _splitter = new Thumb
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(95, 128, 128, 128)),
            Width = compact ? width : 10,
            Height = compact ? 10 : height
        };
        ToolTipService.SetToolTip(_splitter, split
            ? "拖动调整分屏，拖至边缘切换单区"
            : compact
                ? clockOnly ? "向上拖动回到分屏" : "向下拖动回到分屏"
                : clockOnly ? "向左拖动回到分屏" : "向右拖动回到分屏");
        if (compact)
        {
            Canvas.SetLeft(_splitter, 0);
            Canvas.SetTop(_splitter, split ? height * _settings.SplitRatio - 5 : clockOnly ? height - 10 : 0);
        }
        else
        {
            Canvas.SetLeft(_splitter, split ? width * _settings.SplitRatio - 5 : clockOnly ? width - 10 : 0);
            Canvas.SetTop(_splitter, 0);
        }
        _splitter.DragStarted += (_, _) => BeginSplitDrag();
        _splitter.DragDelta += (_, args) => DragSplitHandle(compact, args.HorizontalChange, args.VerticalChange);
        _splitter.DragCompleted += (_, _) => CompleteSplitDrag();
        Canvas.SetZIndex(_splitter, 100);
        FreeLayoutHandles.Children.Add(_splitter);
    }

    /// <summary>按下分隔条时记录起始比例；单区模式按贴边位置起步（仅时钟 1，仅作业 0）。</summary>
    private void BeginSplitDrag()
    {
        _splitDragging = true;
        _splitDragRatio = _settings.LayoutMode switch { "Clock" => 1, "Board" => 0, _ => _settings.SplitRatio };
    }

    /// <summary>拖动分隔条：按位移换算比例，拖进分屏范围就即时切回分屏预览。</summary>
    private void DragSplitHandle(bool compact, double horizontalChange, double verticalChange)
    {
        double width = Math.Max(1, DisplayRoot.ActualWidth), height = Math.Max(1, DisplayRoot.ActualHeight);
        _splitDragRatio = Math.Clamp(_splitDragRatio + (compact ? verticalChange : horizontalChange) / (compact ? height : width), .01, .99);
        _settings.SplitRatio = _splitDragRatio;
        // 拖回分屏范围时立即切回分屏，松手前就能看到分屏比例，松手时才写入最终模式。
        if (_settings.LayoutMode != "Split" && WidgetLayout.CompleteSplit(_splitDragRatio) == "Split") _settings.LayoutMode = "Split";
        ApplyDisplayLayout();
        if (_splitter is null) return;
        if (compact) Canvas.SetTop(_splitter, height * _settings.SplitRatio - 5);
        else Canvas.SetLeft(_splitter, width * _settings.SplitRatio - 5);
    }

    /// <summary>松开分隔条：落在两端就切换单区并复位比例，否则留在分屏并保留拖出来的比例。</summary>
    private void CompleteSplitDrag()
    {
        _splitDragging = false;
        string completed = WidgetLayout.CompleteSplit(_splitDragRatio);
        if (completed is "Board" or "Clock")
        {
            _settings.LayoutMode = completed;
            _settings.SplitRatio = .4;
        }
        SyncLayoutChoiceSelection();
        SettingChanged();
    }

    /// <summary>拖动分隔条切换模式后，把设置里的布局下拉框同步到同一模式。</summary>
    private void SyncLayoutChoiceSelection()
    {
        if (_layoutChoice is null) return;
        int index = _settings.LayoutMode switch { "Board" => 1, "Clock" => 2, "Free" => 3, _ => 0 };
        if (index < _layoutChoice.Items.Count) _layoutChoice.SelectedIndex = index;
    }

    private void AddCenteredWidgetInteraction(string key, Viewbox view, string label, double aspectRatio, double minimumWidth)
    {
        if (!_settings.Widgets.TryGetValue(key, out RegionPlacement? placement)) return;
        double width = ClockPanel.ActualWidth, height = ClockPanel.ActualHeight;
        AddWidgetInteractionLayer(view, placement, label,
            (_, dy) => WidgetLayout.MoveVerticallyCentered(placement, dy, width, height),
            (dx, dy) => WidgetLayout.ResizeCentered(placement, dx, dy, aspectRatio, minimumWidth, width, height),
            () => { _settings.CustomizedDockedWidgets.Add(key); ScheduleSave(); });
    }

    private void AddWidgetInteractionLayer(Viewbox view, RegionPlacement placement, string label,
        Action<double, double> movePlacement, Action<double, double> resizePlacement, Action complete)
    {
        Grid interactionLayer = new() { Width = placement.Width, Height = placement.Height, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        // 选中框和缩放手柄同步出现：指针进入组件后描一圈表示当前选中的组件，离开组件再一起收起。
        Border selectionBorder = new() { Style = (Style)Application.Current.Resources["WidgetSelectionBorderStyle"] };
        Thumb move = new() { Style = (Style)Application.Current.Resources["InvisibleWidgetMoveThumbStyle"] };
        Thumb resize = new()
        {
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Style = (Style)Application.Current.Resources["WidgetResizeThumbStyle"], Opacity = 0
        };
        // 选中框画在移动层之上、缩放手柄之下，避免被移动层的覆盖范围挡住，同时让手柄始终保持在最上层。
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
        interactionLayer.PointerEntered += (_, _) => { pointerOver = true; SetSelected(true); };
        interactionLayer.PointerExited += (_, _) => { pointerOver = false; if (!dragging) SetSelected(false); };
        void PositionInteraction()
        {
            interactionLayer.Width = placement.Width; interactionLayer.Height = placement.Height;
            Canvas.SetLeft(interactionLayer, placement.X); Canvas.SetTop(interactionLayer, placement.Y);
            ApplyCenteredPlacement(view, placement);
        }
        ToolTipService.SetToolTip(move, $"拖动{label}上下移动（锁定中轴线）");
        ToolTipService.SetToolTip(resize, $"缩放{label}");
        move.DragDelta += (_, args) =>
        {
            movePlacement(args.HorizontalChange, args.VerticalChange);
            PositionInteraction();
        };
        move.DragStarted += (_, _) => { dragging = true; SetSelected(true); };
        resize.DragDelta += (_, args) =>
        {
            resizePlacement(args.HorizontalChange, args.VerticalChange);
            PositionInteraction();
        };
        resize.DragStarted += (_, _) => { dragging = true; SetSelected(true); };
        void CompleteDrag()
        {
            dragging = false;
            // 松手后指针通常仍停在组件上，此时保持选中框可见，等指针离开再收起。
            SetSelected(pointerOver);
            complete();
        }
        move.DragCompleted += (_, _) => CompleteDrag(); resize.DragCompleted += (_, _) => CompleteDrag();
        Canvas.SetZIndex(interactionLayer, 50);
        PositionInteraction(); FreeLayoutHandles.Children.Add(interactionLayer);
    }
}
