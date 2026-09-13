using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;

namespace Pancake.Views;

/// <summary>
/// 作业板缩放与平移：缩放岛提供缩小、当前比例、连续滑条与放大，
/// 支持 Ctrl+滚轮缩放、鼠标中键平移；无限作业板随磁贴扩展画布并启用滚动条。
/// </summary>
public sealed partial class MainWindow
{
    internal const double MinimumBoardZoom = 0.2;
    internal const double MaximumBoardZoom = 4;

    /// <summary>缩放档位：逐档增减比固定步长更容易停在常用比例上。</summary>
    private static readonly double[] BoardZoomSteps =
        [.2, .25, .33, .5, .67, .75, .8, .9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];

    private TextBlock? _zoomLevelText;
    private Slider? _zoomSlider;
    private bool _syncingZoomSlider;
    private double _boardZoom = 1;
    private bool _panning;
    private IPointer? _panPointer;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;

    /// <summary>当前作业板缩放比例，供设置页与验证读取。</summary>
    internal double BoardZoom => _boardZoom;

    /// <summary>构建缩放岛并接管作业板的滚轮与中键手势。</summary>
    private void InitializeBoardZoom()
    {
        BuildZoomIsland();
        // Ctrl+滚轮缩放：保持指针下的内容位置不动，缩放不会把用户正在看的地方推走。
        BoardScroller.AddHandler(InputElement.PointerWheelChangedEvent, OnBoardPointerWheel, RoutingStrategies.Tunnel);
        // 触摸双指捏合缩放：与 Ctrl+滚轮共用同一套按锚点缩放的逻辑。
        // 注意 PinchEvent 是冒泡事件（不是隧道事件），注册成隧道会收不到任何手势。
        BoardScroller.AddHandler(Gestures.PinchEvent, OnBoardPinch, RoutingStrategies.Bubble);
        BoardScroller.PointerPressed += OnBoardPointerPressed;
        BoardScroller.PointerMoved += OnBoardPointerMoved;
        BoardScroller.PointerReleased += OnBoardPointerReleased;
        BoardScroller.PointerCaptureLost += (_, _) =>
        {
            _panning = false;
            _panPointer = null;
        };
    }

    /// <summary>缩小、当前比例（兼恢复 100%）、连续滑条、放大。</summary>
    private void BuildZoomIsland()
    {
        ZoomIslandItems.Children.Clear();
        ZoomIslandItems.Children.Add(CreateZoomButton(nameof(FluentGlyphs.ZoomOut), "缩小作业板", () => StepBoardZoom(-1)));
        _zoomLevelText = new TextBlock
        {
            Text = "100%",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 56,
            TextAlignment = TextAlignment.Center
        };
        ZoomIslandItems.Children.Add(CreateZoomButton(_zoomLevelText, "恢复 100%", () => SetBoardZoom(1)));
        _zoomSlider = new Slider
        {
            Minimum = MinimumBoardZoom * 100,
            Maximum = MaximumBoardZoom * 100,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 10,
            Width = 160,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(_zoomSlider, "拖动调整作业板比例");
        AutomationProperties.SetName(_zoomSlider, "作业板缩放比例");
        _zoomSlider.ValueChanged += (_, _) =>
        {
            if (_syncingZoomSlider) return;
            SetBoardZoom(_zoomSlider.Value / 100);
        };
        ZoomIslandItems.Children.Add(_zoomSlider);
        ZoomIslandItems.Children.Add(CreateZoomButton(nameof(FluentGlyphs.ZoomIn), "放大作业板", () => StepBoardZoom(1)));
        UpdateZoomLevelText();
    }

    private Button CreateZoomButton(object content, string name, Action click)
    {
        Button button = new()
        {
            MinWidth = 40,
            MinHeight = 40,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Content = content,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (content is string symbol) button.Content = new FluentIcon { Symbol = symbol, FontSize = 16 };
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private void StepBoardZoom(int direction)
    {
        // 找到当前比例在档位序列中的位置，再移动到相邻档位。
        int index = Array.FindIndex(BoardZoomSteps, step => step >= _boardZoom - 0.001);
        if (index < 0) index = BoardZoomSteps.Length - 1;
        else if (Math.Abs(BoardZoomSteps[index] - _boardZoom) > 0.001)
        {
            // 当前比例不在档位上：向目标方向就近跳到下一个档位。
            index = direction > 0 ? index : Math.Max(0, index - 1);
        }

        index = Math.Clamp(index + direction, 0, BoardZoomSteps.Length - 1);
        SetBoardZoom(BoardZoomSteps[index]);
    }

    /// <summary>应用作业板缩放；查看模式下同样保留缩放结果。</summary>
    private void SetBoardZoom(double zoom, Point? anchor = null)
    {
        double target = Math.Clamp(zoom, MinimumBoardZoom, MaximumBoardZoom);
        if (Math.Abs(target - _boardZoom) < 0.0001) return;

        // 记录缩放前指针下的内容坐标，缩放后把同一内容点移回指针下。
        double offsetX = BoardScroller.Offset.X;
        double offsetY = BoardScroller.Offset.Y;
        Point anchorPoint = anchor ?? new Point(BoardScroller.Bounds.Width / 2, BoardScroller.Bounds.Height / 2);
        double contentX = (offsetX + anchorPoint.X) / _boardZoom;
        double contentY = (offsetY + anchorPoint.Y) / _boardZoom;

        _boardZoom = target;
        ApplyBoardZoom();

        BoardScroller.Offset = new Vector(
            Math.Max(0, contentX * target - anchorPoint.X),
            Math.Max(0, contentY * target - anchorPoint.Y));
        UpdateZoomLevelText();
        ScheduleSave();
    }

    private void ApplyBoardZoom()
    {
        BoardSurface.RenderTransform = new ScaleTransform(_boardZoom, _boardZoom);
        BoardSurface.RenderTransformOrigin = RelativePoint.TopLeft;
        UpdateBoardBounds();
    }

    private void UpdateZoomLevelText()
    {
        if (_zoomLevelText is not null) _zoomLevelText.Text = $"{_boardZoom * 100:0}%";
        if (_zoomSlider is null) return;
        _syncingZoomSlider = true;
        try
        {
            _zoomSlider.Value = _boardZoom * 100;
        }
        finally
        {
            _syncingZoomSlider = false;
        }
    }

    /// <summary>缩放岛只在编辑模式、且当前有作业板时出现。</summary>
    private void UpdateZoomIslandVisibility()
    {
        bool boardVisible = BoardWorkspace.IsVisible;
        ZoomIsland.IsVisible = boardVisible && _isEditing;
        // 不显示时也要同步一次布局：查看模式的滚动/缩放能力由这行统一设置。
        bool scrollable = boardVisible && (_boardZoom > 1.0001 || Settings.InfiniteBoard);
        BoardScroller.HorizontalScrollBarVisibility = scrollable
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        BoardScroller.VerticalScrollBarVisibility = scrollable
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    /// <summary>Ctrl+滚轮缩放作业板；未按 Ctrl 时保持普通滚动。</summary>
    private void OnBoardPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            if (_boardZoom <= 1.0001 && !Settings.InfiniteBoard) return;
            return;
        }

        Point position = e.GetPosition(BoardScroller);
        SetBoardZoom(_boardZoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), position);
        e.Handled = true;
    }

    /// <summary>双指捏合：按手势给出的比例与锚点缩放，触摸平移由滚动条本身负责。</summary>
    private void OnBoardPinch(object? sender, PinchEventArgs e)
    {
        ApplyPinch(e.Scale, e.ScaleOrigin);
        e.Handled = true;
    }

    /// <summary>把一次捏合手势换算成缩放；独立出来便于自动化验证同一套计算。</summary>
    internal void ApplyPinch(double scale, Point origin)
    {
        if (!double.IsFinite(scale) || scale <= 0) return;
        bool scrollable = BoardWorkspace.IsVisible && (_boardZoom > 1.0001 || Settings.InfiniteBoard || scale > 1);
        if (!scrollable) return;
        SetBoardZoom(_boardZoom * scale, origin);
    }

    /// <summary>鼠标中键拖动平移；触摸与触控笔由滚动条与磁贴手势负责。</summary>
    private void OnBoardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(BoardScroller).Properties.IsMiddleButtonPressed) return;
        if (!Settings.InfiniteBoard && _boardZoom <= 1.0001) return;
        _panning = true;
        _panPointer = e.Pointer;
        _panStart = e.GetPosition(BoardScroller);
        _panOffsetX = BoardScroller.Offset.X;
        _panOffsetY = BoardScroller.Offset.Y;
        e.Pointer.Capture(BoardScroller);
        e.Handled = true;
    }

    private void OnBoardPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning || _panPointer is null || !ReferenceEquals(e.Pointer, _panPointer)) return;
        Point current = e.GetPosition(BoardScroller);
        BoardScroller.Offset = new Vector(
            Math.Max(0, _panOffsetX - (current.X - _panStart.X)),
            Math.Max(0, _panOffsetY - (current.Y - _panStart.Y)));
        e.Handled = true;
    }

    private void OnBoardPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        e.Pointer.Capture(null);
        _panning = false;
        _panPointer = null;
        e.Handled = true;
    }
}
