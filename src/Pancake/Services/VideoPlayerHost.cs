using LibVLCSharp.Shared;

namespace Pancake.Services;

/// <summary>
/// 视频播放的公共宿主：LibVLC 实例只初始化一次，底层原生库缺失时整体降级为不支持，
/// 这样没有安装 libvlc 的 Linux 发行版仍然可以正常使用图片背景。
/// </summary>
public static class VideoPlayerHost
{
    private static readonly Lazy<LibVLC?> Shared = new(Create, isThreadSafe: true);

    /// <summary>当前平台是否可用视频播放。</summary>
    public static bool IsSupported => Shared.Value is not null;

    /// <summary>共享的 LibVLC 实例；不可用时返回 null。</summary>
    public static LibVLC? LibVlc => Shared.Value;

    private static LibVLC? Create()
    {
        try
        {
            // Windows 与 macOS 使用随包原生库；Linux 使用发行版提供的 libvlc。
            Core.Initialize();
            return new LibVLC("--no-video-title-show", "--quiet");
        }
        catch (Exception)
        {
            // 任何加载失败都当作“这个平台没有视频能力”：视频背景是可降级能力，
            // Linux 依赖发行版的 libvlc，缺失时不能让设置页或看板构建失败。
            // LibVLCSharp 在这种情况下抛的是 VLCException（不是 DllNotFoundException），
            // 早期版本只捕获了后者，设置页因此在没有 libvlc 的 Linux 上会直接崩。
            return null;
        }
    }
}
