using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private const double DockedClockAspectRatio = 2.4;
    private const double DockedComponentsAspectRatio = 6.5;
    private readonly Dictionary<string, Viewbox> _freeWidgets = [];
    private Thumb? _splitter;
    private bool _layingOut;
    private bool _splitDragging;
    private double _splitDragRatio;
    private Dictionary<string, RegionPlacement>? _widgetEditSnapshot;
    private HashSet<string>? _dockedCustomizationEditSnapshot;

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

    private void ApplyDockedClockLayout()
    {
        double width = ClockPanel.ActualWidth, height = ClockPanel.ActualHeight;
        if (!_isLoaded || width < 1 || height < 1) return;

        bool splitMode = _settings.LayoutMode == "Split";
        string prefix = splitMode ? "Split" : "ClockMode";
        string clockKey = prefix + "Clock", componentsKey = prefix + "Components";
        if (!_settings.Widgets.TryGetValue(clockKey, out RegionPlacement? clock) || !_settings.CustomizedDockedWidgets.Contains(clockKey))
        {
            double clockWidth = Math.Min(splitMode ? 720 : 760, width * (splitMode ? .84 : .68));
            double clockHeight = clockWidth / DockedClockAspectRatio;
            double componentsHeight = Math.Min(splitMode ? 520 : 560, width * .72) / DockedComponentsAspectRatio;
            double groupHeight = clockHeight + componentsHeight + 18;
            double groupTop = Math.Max(0, (height - groupHeight) / 2 - (splitMode ? height * .08 : 0));
            _settings.Widgets[clockKey] = clock = new RegionPlacement { Y = groupTop, Width = clockWidth, Height = clockHeight };
        }
        if (!_settings.Widgets.TryGetValue(componentsKey, out RegionPlacement? components) || !_settings.CustomizedDockedWidgets.Contains(componentsKey))
        {
            double componentsWidth = Math.Min(splitMode ? 520 : 560, width * .72);
            double componentsHeight = componentsWidth / DockedComponentsAspectRatio;
            double componentsY = Math.Min(Math.Max(0, height - componentsHeight), clock.Y + clock.Height + 18);
            _settings.Widgets[componentsKey] = components = new RegionPlacement { Y = componentsY, Width = componentsWidth, Height = componentsHeight };
        }
        WidgetLayout.ResizeCentered(clock, 0, 0, DockedClockAspectRatio, 220, width, height);
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
        bool canEditWidgets = _isEditing && GlobalPenButton.IsChecked != true;
        FreeLayoutHandles.IsHitTestVisible = split || (_settings.LayoutMode is "Free" or "Clock" && canEditWidgets);
        if (split)
        {
            bool compact = RootShell.ActualWidth < 900;
            _splitter = new Thumb { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(95, 128, 128, 128)),
                Width = compact ? Math.Max(1, DisplayRoot.ActualWidth) : 10,
                Height = compact ? 10 : Math.Max(1, DisplayRoot.ActualHeight) };
            ToolTipService.SetToolTip(_splitter, "拖动调整分屏，拖至边缘切换单区");
            Canvas.SetLeft(_splitter, compact ? 0 : DisplayRoot.ActualWidth * _settings.SplitRatio - 5);
            Canvas.SetTop(_splitter, compact ? DisplayRoot.ActualHeight * _settings.SplitRatio - 5 : 0);
            _splitter.DragStarted += (_, _) => { _splitDragging = true; _splitDragRatio = _settings.SplitRatio; };
            _splitter.DragDelta += (_, args) =>
            {
                _splitDragRatio += (compact ? args.VerticalChange : args.HorizontalChange) / Math.Max(1, compact ? DisplayRoot.ActualHeight : DisplayRoot.ActualWidth);
                _settings.SplitRatio = Math.Clamp(_splitDragRatio, .01, .99);
                ApplyDisplayLayout();
                if (compact) Canvas.SetTop(_splitter, DisplayRoot.ActualHeight * _settings.SplitRatio - 5);
                else Canvas.SetLeft(_splitter, DisplayRoot.ActualWidth * _settings.SplitRatio - 5);
            };
            _splitter.DragCompleted += (_, _) =>
            {
                _splitDragging = false;
                if (WidgetLayout.CompleteSplit(_splitDragRatio) is "Board" or "Clock")
                {
                    _settings.LayoutMode = WidgetLayout.CompleteSplit(_splitDragRatio);
                    _settings.SplitRatio = .4;
                    if (_layoutChoice is not null) _layoutChoice.SelectedIndex = _settings.LayoutMode == "Board" ? 1 : 2;
                }
                SettingChanged();
            };
            Canvas.SetZIndex(_splitter, 100);
            FreeLayoutHandles.Children.Add(_splitter);
        }
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
        Thumb move = new() { Style = (Style)Application.Current.Resources["InvisibleWidgetMoveThumbStyle"] };
        Thumb resize = new()
        {
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Style = (Style)Application.Current.Resources["WidgetResizeThumbStyle"], Opacity = 0
        };
        interactionLayer.Children.Add(move);
        interactionLayer.Children.Add(resize);
        bool dragging = false;
        interactionLayer.PointerEntered += (_, _) => resize.Opacity = 1;
        interactionLayer.PointerExited += (_, _) => { if (!dragging) resize.Opacity = 0; };
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
        move.DragStarted += (_, _) => dragging = true;
        resize.DragDelta += (_, args) =>
        {
            resizePlacement(args.HorizontalChange, args.VerticalChange);
            PositionInteraction();
        };
        resize.DragStarted += (_, _) => { dragging = true; resize.Opacity = 1; };
        void CompleteDrag()
        {
            dragging = false;
            resize.Opacity = 0;
            complete();
        }
        move.DragCompleted += (_, _) => CompleteDrag(); resize.DragCompleted += (_, _) => CompleteDrag();
        Canvas.SetZIndex(interactionLayer, 50);
        PositionInteraction(); FreeLayoutHandles.Children.Add(interactionLayer);
    }
}
