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
    private readonly BackgroundPlaylist _playlist = new();
    private readonly DispatcherTimer _timer = new();
    private BackgroundSettings _style = new();
    private string _path = "";
    private MediaPlayer? _player;
    private MediaPlayerElement? _video;
    private Image? _image;
    private WebWallpaperView? _web;
    private bool _shared;
    private bool _failed;
    private bool _inViewport = true;

    // 局部外观预览可要求铺满；不改变项目保存的图片显示模式。
    internal Stretch? MediaStretchOverride { get; init; }

    public BackgroundVisual()
    {
        IsHitTestVisible = false;
        Children.Add(_media);
        Children.Add(_overlays);
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => { ShowCurrent(); UpdateTimer(); };
        Unloaded += (_, _) => { _timer.Stop(); ReleaseMedia(); };
        EffectiveViewportChanged += (_, args) =>
        {
            // 折叠设置页或滚出可见区域时暂停解码和定时器，避免隐藏预览继续消耗资源。
            bool visible = args.EffectiveViewport.Width > 0 && args.EffectiveViewport.Height > 0;
            if (_inViewport == visible) return;
            _inViewport = visible;
            if (visible) { ShowCurrent(); _player?.Play(); _web?.SetPaused(false); UpdateTimer(); }
            else { _player?.Pause(); _web?.SetPaused(true); _timer.Stop(); }
        };
    }

    public void Apply(BackgroundSettings style, Brush fallback, bool shared = false, bool surface = false)
    {
        _style = style;
        _shared = shared;
        Background = shared || (style.Glass && string.IsNullOrWhiteSpace(style.Color)) ? null : string.IsNullOrWhiteSpace(style.Color) ? fallback : SafeColor(style.Color, fallback);
        if (surface) Background = style.Glass ? null : SurfaceBackground.Create(
            style.Color, style.ColorOpacity, style.ColorCleared, false, style.Blur);
        bool changed = _playlist.Configure(style);
        if (changed) { _failed = false; _timer.Stop(); }
        if (shared) ReleaseMedia();
        else if (IsLoaded && !_failed) ShowCurrent();
        _overlays.Children.Clear();
        if (surface)
        {
            // 在媒体之上统一合成颜色与模糊，清除颜色不能移除模糊层。
            if (style.Glass)
                _overlays.Children.Add(new Border { Background = SurfaceBackground.Create(
                    style.Color, style.ColorOpacity, style.ColorCleared, true, style.Blur) });
        }
        else if (style.Glass)
        {
            _overlays.Children.Add(new Border { Background = new BlurBackdropBrush(style.Blur) });
            _overlays.Children.Add(new Border { Background = new SolidColorBrush(BoardTheme.IsLight
                ? Windows.UI.Color.FromArgb(36, 255, 255, 255) : Windows.UI.Color.FromArgb(36, 0, 0, 0)) });
        }
        UpdateTimer();
    }

    private void ShowCurrent()
    {
        if (_shared || !IsLoaded || !_inViewport || _failed) return;
        string path = _playlist.Current;
        if (_path != path)
        {
            ReleaseMedia();
            _path = path;
            if (File.Exists(path))
            {
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
        if (!IsLoaded || !_inViewport || _shared || _failed || !_style.PlaylistEnabled || !_style.SwitchOnTimer || string.IsNullOrEmpty(_playlist.Current)) { _timer.Stop(); return; }
        TimeSpan interval = TimeSpan.FromSeconds(BackgroundPlaylist.NormalizeInterval(_style.SwitchIntervalSeconds));
        if (_timer.Interval != interval) { _timer.Stop(); _timer.Interval = interval; }
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void ReleaseMedia()
    {
        MediaPlayer? player = _player;
        _player = null;
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
