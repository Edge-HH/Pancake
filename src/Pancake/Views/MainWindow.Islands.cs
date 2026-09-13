using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Threading;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 控制窗周边的浮岛与控制窗自动隐藏：
/// 画笔栏与作业板缩放岛按控制窗的实际位置贴在它的两侧（竖版控制窗时改为上下），
/// 查看模式下无操作达到设定时长后一起淡出或飞出，任何操作都会把它们带回来。
/// </summary>
public sealed partial class MainWindow
{
    private const double IslandGap = 10;

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    // 按下的指针：按住拖动（磁贴以外的分隔条、滚动条等）期间控制窗不应自动隐藏。
    private readonly HashSet<IPointer> _pressedPointers = [];
    private double _idleSeconds;
    private bool _toolbarHidden;
    private bool _pointerOverIsland;

    /// <summary>订阅空闲计时与活动事件；只在窗口打开时执行一次。</summary>
    private void InitializeIslands()
    {
        _idleTimer.Tick += (_, _) => CheckAutoHide();
        _idleTimer.Start();
        // 控制窗尺寸或位置变化后浮岛要重新贴合。
        FloatingToolbar.SizeChanged += (_, _) => ApplyIslandPlacement();
        // 控制窗改变对齐方式后会重新布局：先按新的实际占位对齐浮岛截面尺寸，再贴合位置。
        FloatingToolbar.LayoutUpdated += (_, _) =>
        {
            ApplyIslandCrossSize();
            ApplyIslandPlacement();
        };
        AddHandler(InputElement.PointerMovedEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            _pressedPointers.Add(e.Pointer);
            RegisterActivity();
        }, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            _pressedPointers.Remove(e.Pointer);
            RegisterActivity();
        }, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerCaptureLostEvent, (_, e) => _pressedPointers.Remove(e.Pointer),
            RoutingStrategies.Tunnel, handledEventsToo: true);
        // 窗口失去激活时指针可能在其他窗口释放，释放事件收不到，这里直接清空。
        Deactivated += (_, _) => _pressedPointers.Clear();
        PenToolbar.PointerEntered += (_, _) => _pointerOverIsland = true;
        PenToolbar.PointerExited += (_, _) => _pointerOverIsland = false;
        ZoomIsland.PointerEntered += (_, _) => _pointerOverIsland = true;
        ZoomIsland.PointerExited += (_, _) => _pointerOverIsland = false;
    }

    /// <summary>
    /// 按控制窗实际占位摆放两块浮岛：横置控制窗时画笔栏在左、缩放岛在右；
    /// 竖置控制窗（左右居中）时改为画笔栏在上、缩放岛在下。
    /// </summary>
    private void ApplyIslandPlacement()
    {
        if (!_isLoaded) return;
        double canvasWidth = IslandCanvas.Bounds.Width;
        double canvasHeight = IslandCanvas.Bounds.Height;
        if (canvasWidth < 1 || canvasHeight < 1) return;

        // 控制窗可能被整体缩放，浮岛要贴着缩放后的实际占位。
        Rect toolbar = ToolbarRenderedBounds();
        if (toolbar.Width < 1 || toolbar.Height < 1) return;
        double scale = ToolbarScale;
        bool vertical = IsVerticalToolbarPosition;

        if (vertical)
        {
            // 每块岛按自身尺寸贴合控制窗，宽度或高度不同也不会互相压住。
            Place(PenToolbar, toolbar.Left, toolbar.Top - IslandGap - IslandHeight(PenToolbar) * scale);
            Place(RichTextIsland, toolbar.Left, toolbar.Top - IslandGap - IslandHeight(RichTextIsland) * scale);
            Place(ZoomIsland, toolbar.Left, toolbar.Bottom + IslandGap);
        }
        else
        {
            Place(PenToolbar, toolbar.Left - IslandGap - IslandWidth(PenToolbar) * scale, toolbar.Top + (toolbar.Height - IslandHeight(PenToolbar) * scale) / 2);
            Place(RichTextIsland, toolbar.Left - IslandGap - IslandWidth(RichTextIsland) * scale, toolbar.Top + (toolbar.Height - IslandHeight(RichTextIsland) * scale) / 2);
            Place(ZoomIsland, toolbar.Right + IslandGap, toolbar.Top + (toolbar.Height - IslandHeight(ZoomIsland) * scale) / 2);
        }

        void Place(Border island, double left, double top)
        {
            // 浮岛自身也带缩放，边界要按缩放后的占位判断。
            Canvas.SetLeft(island, Math.Clamp(left, 0, Math.Max(0, canvasWidth - IslandWidth(island) * scale)));
            Canvas.SetTop(island, Math.Clamp(top, 0, Math.Max(0, canvasHeight - IslandHeight(island) * scale)));
        }
    }

    private static double IslandWidth(Border island) => island.Bounds.Width > 1 ? island.Bounds.Width : island.DesiredSize.Width;

    private static double IslandHeight(Border island) => island.Bounds.Height > 1 ? island.Bounds.Height : island.DesiredSize.Height;

    /// <summary>任何指针或键盘操作都重置空闲计时，并把隐藏的控制窗带回来。</summary>
    private void RegisterActivity()
    {
        _idleSeconds = 0;
        if (!_toolbarHidden) return;
        _toolbarHidden = false;
        ApplyToolbarHiddenState();
    }

    /// <summary>空闲计时到点后按设置判断是否隐藏控制窗与浮岛。</summary>
    private void CheckAutoHide()
    {
        if (!_isLoaded) return;
        _idleSeconds += _idleTimer.Interval.TotalSeconds;
        bool interacting = _isTileInteracting || _pointerOverIsland || _pressedPointers.Count > 0 ||
                           this.FindControl<ToggleButton>("GlobalPenButton")?.IsChecked == true ||
                           DialogOverlay.IsVisible ||
                           // 打开菜单或取色浮层时不要藏控制窗：用户还在操作它。
                           HasOpenPopup();
        bool viewing = !_isEditing && !SettingsRoot.IsVisible;
        bool shouldHide = ToolbarAutoHidePolicy.ShouldHide(
            Settings.ToolbarAutoHide,
            viewing,
            interacting,
            _idleSeconds,
            Settings.ToolbarAutoHideSeconds);
        if (shouldHide == _toolbarHidden) return;
        _toolbarHidden = shouldHide;
        ApplyToolbarHiddenState();
    }

    /// <summary>
    /// 是否有打开的弹出层（菜单、取色器、候选浮层等）。
    /// 弹出层由框架托管在独立的弹出宿主里，不在本窗口的可视树中，
    /// 因此这里判断“轻量消失层”是否被框架显示出来，另外单独跟踪自己的候选浮层。
    /// </summary>
    private bool HasOpenPopup() =>
        _autofillPopup.IsOpen ||
        this.GetVisualDescendants().OfType<LightDismissOverlayLayer>().Any(layer => layer.IsVisible);

    /// <summary>隐藏时按设置淡出或飞出；飞出方向取距离最近的窗口边框。</summary>
    private void ApplyToolbarHiddenState()
    {
        foreach (Border island in new[] { FloatingToolbar, PenToolbar, ZoomIsland, RichTextIsland })
        {
            if (!_toolbarHidden)
            {
                island.Opacity = 1;
                // 控制窗与浮岛都带缩放，恢复时要把缩放装回去，不能只清掉飞出位移。
                SetSurfaceTransform(island, null);
                island.IsHitTestVisible = true;
                continue;
            }

            island.Opacity = 0;
            island.IsHitTestVisible = false;
            if (Settings.ToolbarHideAnimation != "Fly") continue;
            Rect bounds = island.Bounds;
            (double x, double y) = ToolbarAutoHidePolicy.ExitOffset(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                IslandCanvas.Bounds.Width,
                IslandCanvas.Bounds.Height);
            SetSurfaceTransform(island, new TranslateTransform(x, y));
        }
    }

    /// <summary>
    /// 组合缩放与飞出位移：飞行方向按窗口坐标计算，因此缩放先于位移生效。
    /// </summary>
    private void SetSurfaceTransform(Border island, TranslateTransform? fly)
    {
        ScaleTransform scale = new(ToolbarScale, ToolbarScale);
        if (fly is null)
        {
            island.RenderTransform = scale;
            return;
        }

        TransformGroup group = new();
        group.Children.Add(scale);
        group.Children.Add(fly);
        island.RenderTransform = group;
    }
}
