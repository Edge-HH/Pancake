using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 控制窗（底部工具栏）的外观：位置、缩放、圆角、边距、背景颜色与无字模式。
/// 这些设置同时决定编辑模式下浮岛的外观，因此统一在这里计算。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>控制窗背衬的刷新间隔：背景变化不需要逐帧跟随，模糊本身也看不出来。</summary>
    private static readonly TimeSpan ToolbarBackdropInterval = TimeSpan.FromMilliseconds(800);
    private readonly DispatcherTimer _toolbarBackdropTimer = new() { Interval = ToolbarBackdropInterval };
    private RenderTargetBitmap? _toolbarBackdropBitmap;
    private bool _toolbarBackdropTimerStarted;
    private DateTime _toolbarBackdropRenderedAt = DateTime.MinValue;

    /// <summary>控制窗与浮岛共用的一组图层：外框、颜色层、毛玻璃背衬。</summary>
    private readonly record struct ToolbarSurface(Border Container, Border Tint, BackdropBlurLayer Backdrop);

    /// <summary>顶栏控制窗停下时按钮竖排（左右居中停靠），其余位置横排。</summary>
    private bool IsVerticalToolbarPosition => Settings.ToolbarPosition is "CenterLeft" or "CenterRight";

    /// <summary>控制窗与浮岛的缩放比例，非法取值退回 1。</summary>
    private double ToolbarScale => Math.Clamp(Settings.ToolbarScale <= 0 ? 1 : Settings.ToolbarScale, 0.5, 2.5);

    /// <summary>
    /// 控制窗缩放后的实际占位。缩放原点由停靠位置决定，浮岛必须贴着放大后的边，
    /// 因此布局坐标要按缩放换算，不能直接使用未缩放的 <see cref="Visual.Bounds"/>。
    /// </summary>
    private Rect ToolbarRenderedBounds()
    {
        Rect bounds = FloatingToolbar.Bounds;
        double scale = ToolbarScale;
        if (Math.Abs(scale - 1) < 0.001) return bounds;
        RelativePoint origin = FloatingToolbar.RenderTransformOrigin;
        double left = bounds.X + bounds.Width * origin.Point.X * (1 - scale);
        double top = bounds.Y + bounds.Height * origin.Point.Y * (1 - scale);
        return new Rect(left, top, bounds.Width * scale, bounds.Height * scale);
    }

    /// <summary>控制窗与三块浮岛的外观一致，因此统一遍历。</summary>
    private IEnumerable<ToolbarSurface> ToolbarSurfaces()
    {
        yield return new(FloatingToolbar, ToolbarTint, ToolbarBackdrop);
        yield return new(PenToolbar, PenToolbarTint, PenToolbarBackdrop);
        yield return new(ZoomIsland, ZoomIslandTint, ZoomIslandBackdrop);
        yield return new(RichTextIsland, RichTextIslandTint, RichTextIslandBackdrop);
    }

    /// <summary>按设置刷新控制窗与浮岛的位置、尺寸、背景与按钮文字。</summary>
    private void ApplyToolbarAppearance()
    {
        if (!_isLoaded) return;
        CornerRadius corner = new(Math.Clamp(Settings.ToolbarRadius, 0, 40));
        double scale = ToolbarScale;
        IBrush background = ResolveToolbarBackground();
        // 颜色层与毛玻璃背衬分别成层：模糊只作用于背衬，颜色层与前景保持清晰。
        foreach (ToolbarSurface surface in ToolbarSurfaces())
        {
            surface.Container.CornerRadius = corner;
            surface.Container.Background = null;
            surface.Container.RenderTransform = new ScaleTransform(scale, scale);
            surface.Tint.Background = background;
            surface.Backdrop.CornerRadius = corner;
        }

        double horizontal = Math.Max(0, Settings.ToolbarHorizontalInset);
        double vertical = Math.Max(0, Settings.ToolbarVerticalInset);
        FloatingToolbar.HorizontalAlignment = ExpectedToolbarHorizontal();
        FloatingToolbar.VerticalAlignment = ExpectedToolbarVertical();
        FloatingToolbar.Margin = new Thickness(horizontal, vertical, horizontal, vertical);
        // 缩放原点跟着停靠方向走，工具栏放大后仍然贴住对应的边。
        (double originX, double originY) = Settings.ToolbarPosition switch
        {
            "BottomLeft" or "TopLeft" or "CenterLeft" => (0, 0),
            "BottomRight" or "TopRight" or "CenterRight" => (1, 0),
            "TopCenter" => (0.5, 0),
            _ => (0.5, 1)
        };
        FloatingToolbar.RenderTransformOrigin = new RelativePoint(originX, originY, RelativeUnit.Relative);
        // 浮岛由画布按左上角定位，缩放原点固定在左上角，占位计算才与视觉一致。
        foreach (ToolbarSurface surface in ToolbarSurfaces())
        {
            if (!ReferenceEquals(surface.Container, FloatingToolbar))
            {
                surface.Container.RenderTransformOrigin = RelativePoint.TopLeft;
            }
        }

        ApplyToolbarOrientation();
        ApplyToolbarLabels();
        ApplyIslandLabels();
        ApplyToolbarButtonVisibility();
        UpdateGridSnapHint();
        ApplyIslandCrossSize();
        ApplyToolbarBackdrop(corner);
        ApplyIslandPlacement();
    }

    /// <summary>
    /// 浮岛与控制窗的截面尺寸对齐：横置控制窗时两块浮岛与控制窗等高，竖版控制窗时等宽，
    /// 看上去是同一套控制面板（与旧版一致）。尺寸按缩放后的实际占位换算回布局尺寸。
    /// 画笔栏内容更高（调色板会换行），因此与旧版一样只对齐富文本岛与缩放岛。
    /// </summary>
    private void ApplyIslandCrossSize()
    {
        if (!_isLoaded) return;
        Rect toolbar = ToolbarRenderedBounds();
        if (toolbar.Width < 1 || toolbar.Height < 1) return;
        bool vertical = IsVerticalToolbarPosition;
        double cross = (vertical ? toolbar.Width : toolbar.Height) / ToolbarScale;
        foreach (Border island in new[] { RichTextIsland, ZoomIsland })
        {
            // 只在数值真的变化时赋值：布局回调会重复调用这里，反复赋相同的值没有必要。
            double width = vertical ? cross : double.NaN;
            double height = vertical ? double.NaN : cross;
            if (!SameSize(island.Width, width))
            {
                island.Width = width;
            }

            if (!SameSize(island.Height, height))
            {
                island.Height = height;
            }
        }
    }

    /// <summary>尺寸比较：NaN（自动）与具体数值按同一套规则判断，避免无谓的重新布局。</summary>
    private static bool SameSize(double left, double right) =>
        double.IsNaN(left) && double.IsNaN(right) || Math.Abs(left - right) < 0.1;

    /// <summary>停靠位置对应的横向对齐；设置页的位置选择器与验证都按这份映射判断。</summary>
    private HorizontalAlignment ExpectedToolbarHorizontal() => Settings.ToolbarPosition switch
    {
        "BottomLeft" or "TopLeft" or "CenterLeft" => HorizontalAlignment.Left,
        "BottomRight" or "TopRight" or "CenterRight" => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    /// <summary>停靠位置对应的纵向对齐。</summary>
    private VerticalAlignment ExpectedToolbarVertical() => Settings.ToolbarPosition switch
    {
        "TopCenter" or "TopLeft" or "TopRight" => VerticalAlignment.Top,
        "CenterLeft" or "CenterRight" => VerticalAlignment.Center,
        _ => VerticalAlignment.Bottom
    };

    /// <summary>
    /// 竖版控制窗（左右居中停靠）里按钮与控制窗内容竖排，浮岛也跟着换成竖排并翻转滚动方向，
    /// 缩放滑条改成竖向以保留足够的拖动行程。
    /// </summary>
    private void ApplyToolbarOrientation()
    {
        bool vertical = IsVerticalToolbarPosition;
        Orientation orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        MainToolbarScroll.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        MainToolbarScroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ToolbarItems.Orientation = orientation;

        foreach (ScrollViewer scroll in new[] { PenToolbarScroll, ZoomIslandScroll, RichTextIslandScroll })
        {
            scroll.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            scroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        }

        PenToolbarItems.Orientation = orientation;
        ZoomIslandItems.Orientation = orientation;
        RichTextIslandItems.Orientation = orientation;

        if (_zoomSlider is null) return;
        _zoomSlider.Orientation = orientation;
        // 竖向滑条没有天然长度，显式给一段高度；横向恢复固定宽度。
        _zoomSlider.Width = vertical ? double.NaN : 160;
        _zoomSlider.Height = vertical ? 140 : double.NaN;
    }

    /// <summary>
    /// 浮岛按钮跟随控制窗的无字模式：关闭后在图标下方显示名称。
    /// 名称取按钮的说明文字，浮岛重建后再次调用即可（已包装过的按钮不会被重复包装）。
    /// </summary>
    private void ApplyIslandLabels()
    {
        bool iconOnly = Settings.ToolbarIconOnly;
        foreach (Control child in PenToolbarItems.Children
                     .Concat(ZoomIslandItems.Children)
                     .Concat(RichTextIslandItems.Children))
        {
            if (child is not Button button) continue;
            if (button.Content is FluentIcon icon)
            {
                // 缩放岛的百分比文字是动态更新的 TextBlock，这里只包装纯图标按钮。
                button.Content = null;
                TextBlock caption = new()
                {
                    Text = ToolTip.GetTip(button) as string ?? string.Empty,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsVisible = !iconOnly
                };
                StackPanel stack = new() { Orientation = Orientation.Vertical, Spacing = 2 };
                stack.Children.Add(icon);
                stack.Children.Add(caption);
                button.Content = stack;
            }
            else if (button.Content is StackPanel { Children.Count: 2 } existing &&
                     existing.Children[1] is TextBlock text)
            {
                text.IsVisible = !iconOnly;
            }
        }
    }

    /// <summary>
    /// 控制窗与浮岛的毛玻璃：把作业板区域拍成快照后模糊，作为它们的背衬。
    /// 关掉毛玻璃或模糊半径为 0 时直接撤掉背衬，只留颜色层。
    /// </summary>
    private void ApplyToolbarBackdrop(CornerRadius radius)
    {
        bool enabled = Settings.ToolbarGlass && Settings.ToolbarBlur > 0 && DisplayRoot.IsVisible;
        foreach (ToolbarSurface surface in ToolbarSurfaces())
        {
            surface.Backdrop.CornerRadius = radius;
            surface.Backdrop.Effect = enabled
                // 模糊半径按设置等比换算，上限 50 像素兼顾观感与开销。
                ? new BlurEffect { Radius = Math.Clamp(Settings.ToolbarBlur * 0.5, 1, 50) }
                : null;
            if (!enabled) surface.Backdrop.Snapshot = null;
        }

        if (!enabled)
        {
            StopToolbarBackdropTimer();
            return;
        }

        StartToolbarBackdropTimer();
        RefreshToolbarBackdrop();
    }

    /// <summary>
    /// 重新拍摄背衬并贴回控制窗。拖滑块这类连续改动会高频调用，
    /// 因此这里按刷新间隔节流，剩下的交给定时器补拍。
    /// </summary>
    private void RefreshToolbarBackdrop(bool force = false)
    {
        if (!Settings.ToolbarGlass || Settings.ToolbarBlur <= 0) return;
        // 窗口最小化、看板被设置页盖住、控制窗已隐藏时都不需要背衬。
        if (!_isLoaded || WindowState == WindowState.Minimized) return;
        if (!DisplayRoot.IsVisible || !FloatingToolbar.IsVisible || FloatingToolbar.Opacity <= 0.01) return;
        Size size = DisplayRoot.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return;
        DateTime now = DateTime.UtcNow;
        if (!force && now - _toolbarBackdropRenderedAt < ToolbarBackdropInterval) return;
        _toolbarBackdropRenderedAt = now;
        // 快照按 1:1 的界面像素拍摄：反正是模糊背景，不需要按屏幕缩放放大。
        _toolbarBackdropBitmap?.Dispose();
        RenderTargetBitmap bitmap = new(
            new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height))),
            new Vector(96, 96));
        bitmap.Render(DisplayRoot);
        _toolbarBackdropBitmap = bitmap;
        // 四块面板共用同一张快照，只按各自在作业板里的位置取用。
        foreach (ToolbarSurface surface in ToolbarSurfaces())
        {
            surface.Backdrop.Snapshot = bitmap;
            surface.Backdrop.SourceOffset = surface.Backdrop.TranslatePoint(default, DisplayRoot) ?? default;
        }
    }

    private void StartToolbarBackdropTimer()
    {
        if (_toolbarBackdropTimerStarted) return;
        _toolbarBackdropTimerStarted = true;
        _toolbarBackdropTimer.Tick += ToolbarBackdropTimer_Tick;
        _toolbarBackdropTimer.Start();
    }

    private void StopToolbarBackdropTimer()
    {
        if (!_toolbarBackdropTimerStarted) return;
        _toolbarBackdropTimerStarted = false;
        _toolbarBackdropTimer.Tick -= ToolbarBackdropTimer_Tick;
        _toolbarBackdropTimer.Stop();
    }

    private void ToolbarBackdropTimer_Tick(object? sender, EventArgs e) => RefreshToolbarBackdrop();

    /// <summary>
    /// 网格吸附按钮的说明：状态与当前网格步长都会写进气泡，和旧版一致。
    /// 网格大小变化不走工具栏外观刷新，因此设置页改动后也要调用这里。
    /// </summary>
    private void UpdateGridSnapHint()
    {
        if (this.FindControl<ToggleButton>("GridSnapToggleButton") is not { } snap) return;
        ToolTip.SetTip(snap, Settings.GridSnappingEnabled
            ? $"网格吸附已开启：位置和大小吸附到 {GridSize:0.#}px 网格"
            : "网格吸附已关闭：磁贴仍限制在可视区域内");
    }

    /// <summary>
    /// 颜色层与毛玻璃分开处理：清除颜色后仍保留毛玻璃，透明度只影响颜色层。
    /// </summary>
    private IBrush ResolveToolbarBackground()
    {
        if (Settings.ToolbarBackgroundColorCleared)
        {
            // 颜色层被显式清除时退回半透明的主题表面色，按钮仍可读。
            BoardColor surface = BoardTheme.SurfaceColor;
            return new SolidColorBrush(surface.WithOpacity(0.85).ToColor());
        }

        BoardColor baseColor = string.IsNullOrWhiteSpace(Settings.ToolbarBackgroundColor)
            ? BoardTheme.SurfaceColor
            : BoardColor.Parse(Settings.ToolbarBackgroundColor, BoardTheme.SurfaceColor);
        double opacity = Math.Clamp(Settings.ToolbarBackgroundOpacity, 0, 1);
        return new SolidColorBrush(baseColor.WithOpacity(opacity).ToColor());
    }

    /// <summary>
    /// 无字模式下按钮只显示图标；关闭后在图标下方显示名称。
    /// 首次调用时把按钮内容包装成“图标 + 文字”的竖排结构，后续只切换文字与文字可见性。
    /// 全屏退出提示会临时把「退出全屏」显示出来，即使处于无字模式。
    /// </summary>
    private void ApplyToolbarLabels()
    {
        bool iconOnly = Settings.ToolbarIconOnly;
        foreach ((Button button, string label) in ToolbarButtons())
        {
            if (button.Content is FluentIcon icon)
            {
                // 先解除按钮与图标的关联，再把图标放进新的竖排容器：
                // 否则图标会同时挂在按钮的内容宿主与新容器上，逻辑父级冲突并抛异常。
                button.Content = null;
                TextBlock caption = new()
                {
                    Text = label,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsVisible = !iconOnly || IsFullScreenHintVisible
                };
                StackPanel stack = new() { Orientation = Orientation.Vertical, Spacing = 2 };
                stack.Children.Add(icon);
                stack.Children.Add(caption);
                button.Content = stack;
            }
            else if (button.Content is StackPanel { Children.Count: 2 } existing &&
                     existing.Children[1] is TextBlock text)
            {
                // 按钮文字随状态变化（全屏按钮在进入/退出全屏之间切换），因此每次都重新赋值。
                text.Text = label;
                text.IsVisible = !iconOnly || (IsFullScreenHintVisible && ReferenceEquals(button, FullScreenButton));
            }
        }

        FullScreenButton.Background = IsFullScreenHintVisible ? FullScreenHintBrush : Brushes.Transparent;
    }

    /// <summary>全屏提示是否应当显示：仅全屏查看模式下、手势触发后的 2.2 秒内。</summary>
    private bool IsFullScreenHintVisible => _showFullScreenExitHint && _isFullScreen && !_isEditing;

    /// <summary>查看模式只保留编辑、设置与全屏；编辑模式再展开磁贴相关按钮。</summary>
    private void ApplyToolbarButtonVisibility()
    {
        bool editing = _isEditing;
        SetVisible("GlobalPenButton", editing);
        SetVisible("AddSubjectButton", editing && Settings.LayoutMode != "Clock");
        SetVisible("AutoArrangeButton", editing && Settings.LayoutMode != "Clock");
        SetVisible("GridSnapToggleButton", editing && Settings.LayoutMode != "Clock");
        SetVisible("BackToBoardButton", false);
        SetVisible("DiscardEditButton", editing);
        SetVisible("SettingsButton", !editing);
        SetVisible("FullScreenButton", !editing);
        SetVisible("EditBoardButton", true);
        this.FindControl<FluentIcon>("EditBoardIcon")!.Symbol =
            editing ? nameof(FluentGlyphs.Checkmark) : nameof(FluentGlyphs.Edit);
        if (this.FindControl<ToggleButton>("GridSnapToggleButton") is { } snap)
        {
            snap.IsChecked = Settings.GridSnappingEnabled;
        }
    }

    private void SetVisible(string name, bool visible)
    {
        if (this.FindControl<Control>(name) is { } control) control.IsVisible = visible;
    }

    /// <summary>工具栏按钮与显示名称的对应关系，顺序即界面顺序。</summary>
    private IEnumerable<(Button Button, string Label)> ToolbarButtons()
    {
        yield return (this.FindControl<Button>("GlobalPenButton")!, "画笔");
        yield return (this.FindControl<Button>("BackToBoardButton")!, "返回看板");
        yield return (this.FindControl<Button>("AddSubjectButton")!, "添加科目");
        yield return (this.FindControl<Button>("AutoArrangeButton")!, "自动排列");
        yield return (this.FindControl<Button>("GridSnapToggleButton")!, "网格");
        yield return (this.FindControl<Button>("SettingsButton")!, "设置");
        // 全屏按钮的文字跟随状态：进入全屏后按钮名字变成「退出全屏」。
        yield return (this.FindControl<Button>("FullScreenButton")!, _isFullScreen ? "退出全屏" : "进入全屏");
        yield return (this.FindControl<Button>("DiscardEditButton")!, "放弃");
        yield return (this.FindControl<Button>("EditBoardButton")!, _isEditing ? "保存" : "编辑");
    }
}
