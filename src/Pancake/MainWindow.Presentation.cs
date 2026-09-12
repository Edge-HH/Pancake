using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Pancake.Services;
using Windows.Foundation;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _presentationTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly TranslateTransform _toolbarTranslation = new();
    private readonly HashSet<uint> _presentationPointers = [];
    private long _lastToolbarActivity = Environment.TickCount64;
    private bool _toolbarHidden;
    private bool _noiseSuspended;
    private Storyboard? _toolbarAnimation;

    private void InitializePresentationBehavior()
    {
        FloatingToolbar.RenderTransform = _toolbarTranslation;
        // handledEventsToo 保证磁贴、滚动条和按钮已处理的输入也会重置空闲计时。
        RootShell.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        { _presentationPointers.Add(e.Pointer.PointerId); RecordToolbarActivity(); }), true);
        RootShell.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, _) => RecordToolbarActivity()), true);
        RootShell.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => RecordToolbarActivity()), true);
        foreach (RoutedEvent routedEvent in new[] { UIElement.PointerReleasedEvent, UIElement.PointerCanceledEvent, UIElement.PointerCaptureLostEvent })
            RootShell.AddHandler(routedEvent, new PointerEventHandler((_, e) =>
            { _presentationPointers.Remove(e.Pointer.PointerId); RecordToolbarActivity(); }), true);
        RootShell.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => RecordToolbarActivity()), true);
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) _presentationPointers.Clear();
            RecordToolbarActivity();
            RefreshNoiseSuspension();
        };
        _appWindow!.Changed += (_, _) => RefreshNoiseSuspension();
        _presentationTimer.Tick += (_, _) =>
        {
            if (!_isLoaded) return;
            RefreshNoiseSuspension();
            bool interacting = _presentationPointers.Count > 0 ||
                (RootShell.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0);
            if (interacting) _lastToolbarActivity = Environment.TickCount64;
            bool hide = ToolbarAutoHidePolicy.ShouldHide(_settings.ToolbarAutoHide,
                !_isEditing && DisplayRoot.Visibility == Visibility.Visible && SettingsRoot.Visibility != Visibility.Visible,
                interacting, (Environment.TickCount64 - _lastToolbarActivity) / 1000d, _settings.ToolbarAutoHideSeconds);
            SetToolbarHidden(hide, true);
        };
        _presentationTimer.Start();
    }

    private void RecordToolbarActivity(bool animate = true)
    {
        _lastToolbarActivity = Environment.TickCount64;
        SetToolbarHidden(false, animate);
    }

    private void SetToolbarHidden(bool hidden, bool animate)
    {
        if (FloatingToolbar is null || (_toolbarHidden == hidden && animate)) return;
        _toolbarHidden = hidden;
        // 先取动画中的当前值，再停止旧动画，反向唤醒时不会跳回起点。
        double opacity = FloatingToolbar.Opacity, x = _toolbarTranslation.X, y = _toolbarTranslation.Y;
        // 初始化阶段尚未连接可视树；只有实际执行飞出时才查询窗口坐标。
        Point origin = hidden && animate && _settings.ToolbarHideAnimation == "Fly"
            ? FloatingToolbar.TransformToVisual(RootShell).TransformPoint(new Point()) : new Point();
        _toolbarAnimation?.Stop();
        _toolbarAnimation = null;
        FloatingToolbar.IsHitTestVisible = !hidden;
        // 缩放悬浮岛跟随控制窗一起隐藏，否则自动隐藏时只藏了一半。
        ZoomIsland.IsHitTestVisible = !hidden;
        if (!animate)
        {
            FloatingToolbar.Opacity = hidden ? 0 : 1;
            _toolbarTranslation.X = _toolbarTranslation.Y = 0;
            ZoomIsland.Opacity = hidden ? 0 : 1;
            _zoomIslandTranslation.X = _zoomIslandTranslation.Y = 0;
            return;
        }
        bool fly = _settings.ToolbarHideAnimation == "Fly";
        double targetX = 0, targetY = 0;
        if (hidden && fly)
        {
            (targetX, targetY) = ToolbarAutoHidePolicy.ExitOffset(origin.X - x, origin.Y - y,
                FloatingToolbar.ActualWidth, FloatingToolbar.ActualHeight, RootShell.ActualWidth, RootShell.ActualHeight);
        }
        Storyboard storyboard = new();
        void Add(DependencyObject target, string property, double from, double to)
        {
            DoubleAnimation animation = new() { From = from, To = to, Duration = TimeSpan.FromMilliseconds(320),
                EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }
        Add(FloatingToolbar, "Opacity", opacity, hidden && !fly ? 0 : 1);
        Add(_toolbarTranslation, "X", x, targetX);
        Add(_toolbarTranslation, "Y", y, targetY);
        // 缩放岛沿用同一段位移，隐藏与唤醒时都和控制窗保持相对位置。
        Add(ZoomIsland, "Opacity", ZoomIsland.Opacity, hidden && !fly ? 0 : 1);
        Add(_zoomIslandTranslation, "X", _zoomIslandTranslation.X, targetX);
        Add(_zoomIslandTranslation, "Y", _zoomIslandTranslation.Y, targetY);
        _toolbarAnimation = storyboard;
        storyboard.Begin();
    }

    private bool ShouldSuspendNoise => _settings.PauseNoiseWhenMinimized &&
        _appWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void RefreshNoiseSuspension()
    {
        if (!_isLoaded) return;
        bool suspended = ShouldSuspendNoise;
        if (_noiseSuspended == suspended) return;
        _noiseSuspended = suspended;
        if (suspended)
        {
            _noiseMonitor.Stop();
            _noiseAlertPlayer.Dispose();
            _noiseAlertGate.Reset();
            NoiseText.Text = "监测已暂停";
            MicrophoneStatusInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
            MicrophoneStatusInfoBar.Message = "窗口已最小化，恢复窗口后继续监测。";
        }
        else if (_initialView != "verification") StartNoiseMonitoring();
    }
}
