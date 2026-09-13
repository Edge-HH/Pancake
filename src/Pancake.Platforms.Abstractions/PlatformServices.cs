using Pancake.Platforms.Abstraction.Services;
using Pancake.Platforms.Abstraction.Stubs;
using Pancake.Services;

namespace Pancake.Platforms.Abstraction;

/// <summary>
/// 各平台服务的统一入口。启动时由桌面端按当前平台替换实现，
/// 未被替换的能力保持 Stub，界面据此隐藏对应入口而不是静默失败。
/// </summary>
public static class PlatformServices
{
    /// <summary>数据与配置的存放位置。</summary>
    public static IAppPathsService AppPaths { get; set; } = new DefaultAppPathsService();

    /// <summary>系统已安装字体查询。</summary>
    public static IFontCatalogService FontCatalog { get; set; } = new EmptyFontCatalogService();

    /// <summary>打开文件、目录、链接与重启应用。</summary>
    public static IExternalLauncher ExternalLauncher { get; set; } = new DefaultExternalLauncher();

    /// <summary>窗口全屏与材质保留行为。</summary>
    public static IWindowPlatformService WindowPlatform { get; set; } = new DefaultWindowPlatformService();

    /// <summary>窗口背景材质。</summary>
    public static IBackdropService Backdrop { get; set; } = new NoopBackdropService();

    /// <summary>麦克风采集。</summary>
    public static INoiseCaptureService NoiseCapture { get; set; } = new UnsupportedNoiseCaptureService();

    /// <summary>噪音提示音播放。</summary>
    public static INoiseAlertPlayback NoiseAlert { get; set; } = new SilentNoiseAlertPlayback();

    /// <summary>网页壁纸宿主。</summary>
    public static IWebWallpaperHost WebWallpaper { get; set; } = new UnsupportedWebWallpaperHost();

    /// <summary>Wallpaper Engine 安装位置来源；非 Windows 平台返回空集合。</summary>
    public static IWallpaperEnginePathSource WallpaperEnginePaths { get; set; } = new UnsupportedWallpaperEnginePathSource();

    /// <summary>平台更新策略。</summary>
    public static IAppUpdateService AppUpdate { get; set; } = new PromptOnlyAppUpdateService();

    /// <summary>平台能力开关。</summary>
    public static IPlatformFeatures Features { get; set; } = new DefaultPlatformFeatures();
}
