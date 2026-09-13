using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 区域背景层：先铺颜色层，再放图片（或后续接入的视频、网页壁纸），最后按需模糊。
/// 磁贴、时钟区、作业区和跨区背景共用这一个控件，样式全部来自 BackgroundSettings。
/// </summary>
public sealed class BackgroundVisual : Grid
{
    private readonly Border _colorLayer = new();
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly Panel _mediaHost = new();
    private readonly VideoWallpaperView _video = new();
    private Control? _web;
    private string _webPath = string.Empty;
    private readonly BackgroundPlaylist _playlist = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private BackgroundSettings _style = new();
    private string _loadedPath = string.Empty;
    private bool _failed;

    public BackgroundVisual()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _mediaHost.Children.Add(_image);
        _mediaHost.Children.Add(_video);
        Children.Add(_mediaHost);
        Children.Add(_colorLayer);
        _timer.Tick += (_, _) => Advance();
        // 视频播完时按设置切换下一项；循环播放时不会触发该事件。
        _video.PlaybackEnded += () =>
        {
            if (_style.PlaylistEnabled && _style.SwitchOnMediaEnded) Advance();
        };
        _video.PlaybackFailed += _ => { };
        Unloaded += (_, _) =>
        {
            // 离开可视树时释放视频与网页资源，避免隐藏的预览继续解码或执行脚本。
            _timer.Stop();
            _video.Stop();
            HideWebWallpaper();
        };
    }

    /// <summary>区域自身的默认颜色（未设置颜色层时使用，一般为当前主题的界面背景）。</summary>
    public BoardColor FallbackColor { get; set; } = BoardColor.FromRgb(21, 21, 21);

#if PANCAKE_UI_TESTS
    /// <summary>验证构建专用：当前实际加载的媒体路径。</summary>
    internal string CurrentMediaPathForVerification => _loadedPath;

    internal bool IsTimerRunningForVerification => _timer.IsEnabled;

    internal bool IsVideoPlayingForVerification => _video.IsPlaying;

    internal long VideoPositionForVerification => _video.PositionMilliseconds;
#endif

    /// <summary>是否使用主题的界面背景色作为默认色，而不是固定色值。</summary>
    public bool UseThemeFallback { get; init; }

    public void Apply(BackgroundSettings style)
    {
        _style = style;
        ApplyColorLayer(style);
        // 播放队列内容变化时立刻切到当前项，否则只刷新显示参数。
        if (_playlist.Configure(style)) ShowCurrent();
        else ApplyMedia(style);
        UpdateTimer();
    }

    /// <summary>按设置启动或停止定时切换；视频结束时切换需要播放器事件，当前仅图片生效。</summary>
    private void UpdateTimer()
    {
        _timer.Stop();
        if (!_style.PlaylistEnabled || !_style.SwitchOnTimer || _style.Playlist.Count <= 1) return;
        _timer.Interval = TimeSpan.FromSeconds(BackgroundPlaylist.NormalizeInterval(_style.SwitchIntervalSeconds));
        _timer.Start();
    }

    /// <summary>切换到下一项；全部不可用时停止轮播，避免反复重试坏文件。</summary>
    private void Advance()
    {
        if (!_playlist.Advance(_style.Shuffle)) _timer.Stop();
        ShowCurrent();
    }

    private void ShowCurrent() => ApplyMedia(_style);

    /// <summary>
    /// 显示网页壁纸：由平台服务创建承载控件，同一个项目重复应用不会重建页面。
    /// 项目损坏或平台不支持时保持隐藏，背景自然退回颜色层。
    /// </summary>
    private void ShowWebWallpaper(string entry)
    {
        if (string.Equals(entry, _webPath, StringComparison.Ordinal) && _web is not null) return;
        HideWebWallpaper();
        try
        {
            WebWallpaperPackage package = WebWallpaperPackage.Load(entry);
            Control? view = PlatformServices.WebWallpaper.TryCreate(package.Directory, package.EntryPath);
            if (view is null) return;
            _web = view;
            _webPath = entry;
            _mediaHost.Children.Add(view);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // 损坏的网页项目直接跳过，避免整个看板加载失败。
            HideWebWallpaper();
        }
    }

    private void HideWebWallpaper()
    {
        if (_web is null) return;
        _mediaHost.Children.Remove(_web);
        (_web as IDisposable)?.Dispose();
        _web = null;
        _webPath = string.Empty;
    }

    private void ApplyColorLayer(BackgroundSettings style)
    {
        BoardColor baseColor = UseThemeFallback ? BoardTheme.SurfaceColor : FallbackColor;
        if (style.ColorCleared)
        {
            _colorLayer.Background = null;
            return;
        }

        BoardColor color = string.IsNullOrWhiteSpace(style.Color)
            ? baseColor
            : BoardColor.Parse(style.Color, baseColor);
        _colorLayer.Background = new SolidColorBrush(color.ToColor());
        _colorLayer.Opacity = Math.Clamp(style.ColorOpacity, 0, 1);
    }

    private void ApplyMedia(BackgroundSettings style)
    {
        string path = ResolveMediaPath(style);
        if (MediaLibrary.IsWeb(path) && PlatformServices.WebWallpaper.IsSupported)
        {
            ShowWebWallpaper(path);
            _image.Source = null;
            _image.IsVisible = false;
            _video.Stop();
            _mediaHost.Effect = null;
            return;
        }

        HideWebWallpaper();
        bool isVideo = MediaLibrary.IsVideo(path) && VideoPlayerHost.IsSupported;
        if (isVideo)
        {
            // 视频交给原生渲染面（无法被 Avalonia 合成器模糊），图片层随即清空。
            _image.Source = null;
            _image.IsVisible = false;
            _loadedPath = string.Empty;
            _mediaHost.Effect = null;
            if (style.SwitchOnMediaEnded)
            {
                // 结束时切换才需要监听结尾事件；否则直接循环播放。
                _video.Play(path, loop: false);
            }
            else
            {
                _video.Play(path, loop: true);
            }

            return;
        }

        _video.Stop();
        if (!string.Equals(path, _loadedPath, StringComparison.Ordinal))
        {
            _loadedPath = path;
            _failed = false;
            _image.Source = null;
            if (!string.IsNullOrWhiteSpace(path)) _ = LoadImageAsync(path);
        }

        _image.Stretch = style.ImageMode switch
        {
            "Stretch" => Stretch.Fill,
            "Fit" => Stretch.Uniform,
            _ => Stretch.UniformToFill
        };
        _image.IsVisible = _image.Source is not null && !_failed;

        // 毛玻璃只作用于媒体层，颜色层保持清晰，和 WinUI 版本的分层一致。
        _mediaHost.Effect = style.Glass && style.Blur > 0
            ? new BlurEffect { Radius = Math.Clamp(style.Blur, 0, 80) }
            : null;
    }

    /// <summary>播放队列启用时取当前项，否则用单媒体路径。</summary>
    private string ResolveMediaPath(BackgroundSettings style) =>
        style.PlaylistEnabled && style.Playlist.Count > 0
            ? _playlist.Current
            : style.ImagePath;

    private async Task LoadImageAsync(string path)
    {
        try
        {
            Bitmap bitmap = await Task.Run(() => new Bitmap(path));
            // 异步加载期间可能已经切换到别的媒体，这里丢弃过期结果。
            if (!string.Equals(path, _loadedPath, StringComparison.Ordinal))
            {
                bitmap.Dispose();
                return;
            }

            _image.Source = bitmap;
            _image.IsVisible = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _failed = true;
            _image.IsVisible = false;
        }
    }
}
