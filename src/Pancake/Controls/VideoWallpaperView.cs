using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 视频背景播放器：静音、循环播放，使用 LibVLC 的原生渲染面。
/// 原生库不可用时保持隐藏并报告失败，让背景降级为图片或纯色。
/// </summary>
public sealed class VideoWallpaperView : Grid
{
    private readonly VideoView _view = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private MediaPlayer? _player;
    private string _loadedPath = string.Empty;

    public VideoWallpaperView()
    {
        IsVisible = false;
        ClipToBounds = true;
        Children.Add(_view);
    }

    /// <summary>视频播放到结尾时触发，供播放队列决定是否切换下一项。</summary>
    public event Action? PlaybackEnded;

    /// <summary>视频无法播放时触发，参数为失败原因。</summary>
    public event Action<string>? PlaybackFailed;

    public bool IsPlaying => _player?.IsPlaying == true;

    /// <summary>当前播放位置（毫秒），用于验证解码确实在推进。</summary>
    public long PositionMilliseconds => _player?.Time ?? 0;

    /// <summary>
    /// 显示并播放指定视频。同一个路径重复调用不会重新起播，
    /// 避免每次刷新外观都从头开始播放。
    /// </summary>
    public void Play(string path, bool loop)
    {
        if (!VideoPlayerHost.IsSupported || VideoPlayerHost.LibVlc is not { } libVlc)
        {
            PlaybackFailed?.Invoke("当前系统缺少 libvlc，无法播放视频背景。");
            return;
        }

        if (!File.Exists(path))
        {
            PlaybackFailed?.Invoke($"视频文件不存在：{path}");
            return;
        }

        if (string.Equals(path, _loadedPath, StringComparison.Ordinal) && _player is not null)
        {
            _player.SetPause(false);
            IsVisible = true;
            return;
        }

        Stop();
        try
        {
            _player = new MediaPlayer(libVlc)
            {
                Mute = true,
                EnableHardwareDecoding = true
            };
            _player.EndReached += (_, _) => PlaybackEnded?.Invoke();
            _player.EncounteredError += (_, _) => PlaybackFailed?.Invoke("视频无法解码或文件损坏。");
            _view.MediaPlayer = _player;
            // 循环播放通过 input-repeat 选项实现，避免自己处理结尾事件时的重启抖动。
            string[] options = loop ? [":input-repeat=65535"] : [];
            using Media media = new(libVlc, path, FromType.FromPath, options);
            _player.Play(media);
            _loadedPath = path;
            IsVisible = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DllNotFoundException)
        {
            Stop();
            PlaybackFailed?.Invoke(ex.Message);
        }
    }

    /// <summary>停止播放并释放播放器；切换媒体或卸载控件时调用。</summary>
    public void Stop()
    {
        try
        {
            _player?.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 原生库在退出过程中可能已经释放，忽略即可。
        }

        _view.MediaPlayer = null;
        _player?.Dispose();
        _player = null;
        _loadedPath = string.Empty;
        IsVisible = false;
    }
}
