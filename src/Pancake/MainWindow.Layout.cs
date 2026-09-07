using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Viewbox> _freeWidgets = [];
    private Thumb? _splitter;
    private bool _layingOut;
    private bool _splitDragging;
    private double _splitDragRatio;
    private Dictionary<string, RegionPlacement>? _widgetEditSnapshot;

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
            var clockGrid = (Grid)ClockPanel.Child;
            clockGrid.Children.Remove(ClockContentView);
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
            ((Grid)ClockPanel.Child).Children.Add(ClockContentView);
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

    private void UpdateLayoutHandles()
    {
        if (FreeLayoutHandles is null || _splitDragging) return;
        FreeLayoutHandles.Children.Clear();
        bool split = _settings.LayoutMode == "Split";
        FreeLayoutHandles.IsHitTestVisible = split || (_settings.LayoutMode == "Free" && _isEditing && GlobalPenButton.IsChecked != true);
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
            FreeLayoutHandles.Children.Add(_splitter);
        }
        if (_settings.LayoutMode != "Free" || !_isEditing || GlobalPenButton.IsChecked == true) return;
        foreach (var (key, view) in _freeWidgets)
        {
            RegionPlacement placement = _settings.Widgets[key];
            void MoveHandles(Thumb move, Thumb resize)
            {
                Canvas.SetLeft(move, placement.X); Canvas.SetTop(move, placement.Y);
                move.Width = placement.Width;
                Canvas.SetLeft(resize, placement.X + placement.Width - 20); Canvas.SetTop(resize, placement.Y + placement.Height - 20);
                Canvas.SetLeft(view, placement.X); Canvas.SetTop(view, placement.Y); view.Width = placement.Width; view.Height = placement.Height;
            }
            Thumb move = new() { Height = 18, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 96, 165, 250)) };
            Thumb resize = new() { Width = 20, Height = 20, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 96, 165, 250)) };
            ToolTipService.SetToolTip(move, "移动组件"); ToolTipService.SetToolTip(resize, "缩放组件");
            move.DragDelta += (_, args) =>
            {
                WidgetLayout.Move(placement, args.HorizontalChange, args.VerticalChange, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight);
                MoveHandles(move, resize);
            };
            resize.DragDelta += (_, args) =>
            {
                WidgetLayout.Resize(placement, args.HorizontalChange, args.VerticalChange, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight);
                MoveHandles(move, resize);
            };
            move.DragCompleted += (_, _) => ScheduleSave(); resize.DragCompleted += (_, _) => ScheduleSave();
            MoveHandles(move, resize); FreeLayoutHandles.Children.Add(move); FreeLayoutHandles.Children.Add(resize);
        }
    }
}
