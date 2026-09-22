using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Playback;

namespace Pancake.Controls;

/// <summary>把视频帧绘制进 XAML 图像层，供前方控件的 CompositionBackdropBrush 采样。</summary>
internal sealed class VideoFrameImage : Grid, IDisposable
{
    private readonly Image _image = new();
    private readonly MediaPlayer _player;
    private CanvasRenderTarget? _frame;
    private CanvasImageSource? _source;
    private int _pending;
    private bool _disposed;
    internal event Action? Failed;
    internal int PresentedFrames { get; private set; }
    internal Microsoft.UI.Xaml.Media.Stretch Stretch { get => _image.Stretch; set => _image.Stretch = value; }

    internal VideoFrameImage(MediaPlayer player)
    {
        _player = player;
        IsHitTestVisible = false;
        Children.Add(_image);
        player.IsVideoFrameServerEnabled = true;
        player.VideoFrameAvailable += FrameAvailable;
    }

    private void FrameAvailable(MediaPlayer sender, object args)
    {
        // 解码回调来自后台线程；最多保留一个 UI 更新，避免繁忙时积压过期帧。
        if (Interlocked.Exchange(ref _pending, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_disposed) return;
                int width = (int)sender.PlaybackSession.NaturalVideoWidth;
                int height = (int)sender.PlaybackSession.NaturalVideoHeight;
                if (width <= 0 || height <= 0) return;
                if (_frame is null || _frame.SizeInPixels.Width != width || _frame.SizeInPixels.Height != height)
                {
                    ReleaseSurfaces();
                    var device = CanvasDevice.GetSharedDevice();
                    _frame = new CanvasRenderTarget(device, width, height, 96);
                    _source = new CanvasImageSource(device, width, height, 96);
                    _image.Source = _source;
                }
                sender.CopyFrameToVideoSurface(_frame);
                using var drawing = _source!.CreateDrawingSession(Microsoft.UI.Colors.Transparent);
                drawing.DrawImage(_frame);
                PresentedFrames++;
            }
            catch (Exception)
            {
                // 与普通解码失败走同一跳过/报错路径，不让异步绘制异常终止应用。
                if (!_disposed) Failed?.Invoke();
            }
            finally { Interlocked.Exchange(ref _pending, 0); }
        })) Interlocked.Exchange(ref _pending, 0);
    }

    private void ReleaseSurfaces()
    {
        _image.Source = null;
        _source = null;
        _frame?.Dispose();
        _frame = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.VideoFrameAvailable -= FrameAvailable;
        ReleaseSurfaces();
    }
}
