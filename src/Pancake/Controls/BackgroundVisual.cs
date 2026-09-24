using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Services;
using Pancake.ViewModels;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Pancake.Controls;

/// <summary>背景媒体、模糊与前景分层；重复应用外观不会重新开始当前视频或计时。</summary>
public sealed class BackgroundVisual : Grid
{
    private readonly Grid _media = new();
    private readonly Grid _overlays = new();
    // 效果层常驻在视觉树中；重复应用设置时只替换 Brush，避免清空/重挂载造成闪烁。
    private readonly Border _surfaceOverlay = new() { IsHitTestVisible = false };
    private readonly Border _blurOverlay = new() { IsHitTestVisible = false };
    private readonly Border _glassTintOverlay = new() { IsHitTestVisible = false };
    private readonly BackgroundPlaylist _playlist = new();
    private readonly DispatcherTimer _timer = new();
    // 折叠的祖先不再触发布局/视口事件，媒体启停靠低频兜底定时器重新评估，不依赖事件必然到达。
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private BackgroundSettings _style = new();
    private string _path = "";
    private MediaPlayer? _player;
    private MediaPlayerElement? _video;
    private VideoFrameImage? _videoFrames;
    private Image? _image;
    private WebWallpaperView? _web;
    private bool _shared;
    private bool _failed;
    private bool _inViewport = true;
    private bool _active;
    private bool _overlaySurface;
    private string _overlayKey = "";
    private string _backgroundKey = "";
    private bool _mediaRestoreQueued;

    // 局部外观预览可要求铺满；不改变项目保存的图片显示模式。
    internal Stretch? MediaStretchOverride { get; init; }

    public BackgroundVisual()
    {
        IsHitTestVisible = false;
        Children.Add(_media);
        Children.Add(_overlays);
        _overlays.Children.Add(_blurOverlay);
        _overlays.Children.Add(_glassTintOverlay);
        _overlays.Children.Add(_surfaceOverlay);
        _timer.Tick += (_, _) => Advance();
        _activityTimer.Tick += (_, _) => EvaluateActivity();
        Loaded += (_, _) =>
        {
            // 刚挂载时 EffectiveViewport 可能仍是空矩形；先按可见恢复媒体，错误暂停由后续视口事件纠正。
            _inViewport = true;
            QueueMediaRestore();
            EvaluateActivity();
            _activityTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _activityTimer.Stop();
            _timer.Stop();
            ReleaseMedia();
            _active = false;
            QueueMediaRestore();
        };
        EffectiveViewportChanged += (_, args) =>
        {
            // 滚出可见区域时暂停解码和定时器，避免不可见内容继续消耗资源。
            bool visible = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0;
            if (_inViewport == visible) return;
            _inViewport = visible;
            EvaluateActivity();
        };
    }

    /// <summary>可见性综合判断后的唯一启停入口；隐藏的管线必须停下解码、定时器与帧拷贝。</summary>
    private void EvaluateActivity()
    {
        bool active = IsLoaded && _inViewport && ShownThroughTree(this);
        if (active == _active) return;
        _active = active;
        if (_active) { ShowCurrent(); _player?.Play(); _web?.SetPaused(false); UpdateTimer(); }
        else { _player?.Pause(); _web?.SetPaused(true); _timer.Stop(); }
    }

    /// <summary>WinUI 3 没有有效可见性事件；沿父链检查 Visibility，祖先折叠时子树不参与布局。</summary>
    internal static bool ShownThroughTree(UIElement element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private void QueueMediaRestore()
    {
        // Viewbox 间重挂载可能在同一轮布局中交错触发 Loaded/Unloaded。
        // 等事件结束后以当前挂载状态为准恢复，避免最后一次 Unloaded 清空已加载的图片。
        if (_mediaRestoreQueued) return;
        _mediaRestoreQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _mediaRestoreQueued = false;
            if (!IsLoaded) return;
            ShowCurrent();
            UpdateTimer();
        });
    }

    public void Apply(BackgroundSettings style, Brush fallback, bool shared = false, bool surface = false, double blurScale = 1)
    {
        bool wasShared = _shared;
        _style = style;
        _shared = shared;
        blurScale = double.IsFinite(blurScale) ? Math.Max(0, blurScale) : 1;
        double effectiveBlur = style.Blur * blurScale;
        // SurfaceBrush 每次都是新实例，不能用哈希参与缓存键，否则每次 Apply 都会重置底色。
        string backgroundKey = $"{shared}|{surface}|{style.Glass}|{style.Color}|{style.ColorOpacity:0.####}|{style.ColorCleared}|{style.Blur:0.####}|{BoardTheme.IsLight}";
        if (_backgroundKey != backgroundKey)
        {
            _backgroundKey = backgroundKey;
            Background = shared || (style.Glass && string.IsNullOrWhiteSpace(style.Color)) ? null : string.IsNullOrWhiteSpace(style.Color) ? fallback : SafeColor(style.Color, fallback);
            if (surface) Background = style.Glass ? null : SurfaceBackground.Create(
                style.Color, style.ColorOpacity, style.ColorCleared, false, style.Blur);
        }
        bool changed = _playlist.Configure(style);
        if (changed) { _failed = false; _timer.Stop(); }
        // 共享区域本身不承载媒体；只有从独立媒体切换到共享层时才需要释放旧播放器。
        if (shared && !wasShared) ReleaseMedia();
        else if (IsLoaded && !_failed) ShowCurrent();
        string overlayKey = $"{surface}|{style.Glass}|{style.Color}|{style.ColorOpacity:0.####}|{style.ColorCleared}|{effectiveBlur:0.####}|{BoardTheme.IsLight}";
        if (_overlayKey != overlayKey)
        {
            _overlayKey = overlayKey;
            _overlaySurface = surface;
            _surfaceOverlay.Background = surface && style.Glass
                ? SurfaceBackground.Create(style.Color, style.ColorOpacity, style.ColorCleared, true, style.Blur)
                : null;
            _blurOverlay.Background = !surface && style.Glass ? new BlurBackdropBrush(effectiveBlur) : null;
            _glassTintOverlay.Background = !surface && style.Glass
                ? new SolidColorBrush(BoardTheme.IsLight
                    ? Windows.UI.Color.FromArgb(36, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(36, 0, 0, 0))
                : null;
        }
        _surfaceOverlay.Visibility = _overlaySurface && style.Glass ? Visibility.Visible : Visibility.Collapsed;
        _blurOverlay.Visibility = !_overlaySurface && style.Glass ? Visibility.Visible : Visibility.Collapsed;
        _glassTintOverlay.Visibility = !_overlaySurface && style.Glass ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimer();
    }

    private void ShowCurrent()
    {
        if (_shared || !IsLoaded || !_active || _failed) return;
        string path = _playlist.Current;
        if (_path != path)
        {
            ReleaseMedia();
            if (string.IsNullOrEmpty(path)) _path = path;
            else if (File.Exists(path))
            {
                _path = path;
                try
                {
                    if (MediaLibrary.IsWeb(path))
                    {
                        WebWallpaperView web = new(path);
                        _web = web;
                        web.Failed += () => { if (ReferenceEquals(_web, web)) Failed(); };
                        _media.Children.Add(web);
                    }
                    else if (MediaLibrary.IsVideo(path))
                    {
                        MediaPlayer player = new() { IsMuted = true, AutoPlay = true };
                        _player = player;
                        player.CommandManager.IsEnabled = false;
                        _video = new MediaPlayerElement { AreTransportControlsEnabled = false, IsHitTestVisible = false };
                        _video.SetMediaPlayer(player);
                        _media.Children.Add(_video);
                        // MediaPlayerElement 的视频合成层不能被背景模糊采样；帧服务模式由 XAML 图像呈现。
                        _videoFrames = new VideoFrameImage(player);
                        _videoFrames.Failed += () => { if (ReferenceEquals(_player, player)) Failed(); };
                        _media.Children.Add(_videoFrames);
                        // 回调不在 UI 线程，并可能晚于换曲或卸载，必须核对播放器身份。
                        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
                        {
                            if (ReferenceEquals(_player, player) && _style.PlaylistEnabled && _style.SwitchOnMediaEnded) Advance();
                        });
                        player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
                        {
                            if (ReferenceEquals(_player, player)) Failed();
                        });
                        player.Source = MediaSource.CreateFromUri(new Uri(path));
                    }
                    else
                    {
                        _image = new Image();
                        Image current = _image;
                        current.ImageFailed += (_, _) => { if (ReferenceEquals(_image, current)) Failed(); };
                        current.Source = new BitmapImage(new Uri(path));
                        _media.Children.Add(current);
                    }
                }
                catch (Exception) { Failed(); }
            }
        }
        Stretch stretch = MediaStretchOverride ?? (_style.ImageMode switch { "Stretch" => Stretch.Fill, "Fit" => Stretch.Uniform, _ => Stretch.UniformToFill });
        if (_image is not null) _image.Stretch = stretch;
        if (_video is not null) _video.Stretch = stretch;
        if (_videoFrames is not null) _videoFrames.Stretch = stretch;
        if (_player is not null) _player.IsLoopingEnabled = !_style.PlaylistEnabled || !_style.SwitchOnMediaEnded;
    }

    private void Advance()
    {
        _timer.Stop();
        if (_playlist.Advance(_style.Shuffle)) { _failed = false; ShowCurrent(); }
        else if (!_failed && _player is not null) { _player.PlaybackSession.Position = TimeSpan.Zero; _player.Play(); }
        UpdateTimer();
    }

    private void Failed()
    {
        _timer.Stop();
        if (_playlist.Advance(_style.Shuffle, failed: true)) { ShowCurrent(); UpdateTimer(); }
        else
        {
            _failed = true;
            ReleaseMedia();
            _media.Children.Add(new TextBlock { Text = "背景媒体无法播放，请重新选择文件或检查视频编码。",
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        }
    }

    private void UpdateTimer()
    {
        if (!IsLoaded || !_active || _shared || _failed || !_style.PlaylistEnabled || !_style.SwitchOnTimer || string.IsNullOrEmpty(_playlist.Current)) { _timer.Stop(); return; }
        TimeSpan interval = TimeSpan.FromSeconds(BackgroundPlaylist.NormalizeInterval(_style.SwitchIntervalSeconds));
        if (_timer.Interval != interval) { _timer.Stop(); _timer.Interval = interval; }
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void ReleaseMedia()
    {
        MediaPlayer? player = _player;
        _player = null;
        _videoFrames?.Dispose();
        _videoFrames = null;
        _video?.SetMediaPlayer(null);
        player?.Dispose();
        _video = null;
        _web?.Dispose();
        _web = null;
        _image = null;
        _path = "";
        _media.Children.Clear();
    }

    private static Brush SafeColor(string value, Brush fallback)
    {
        try { return MainViewModel.BrushFromHex(value); }
        catch { return fallback; }
    }
}
