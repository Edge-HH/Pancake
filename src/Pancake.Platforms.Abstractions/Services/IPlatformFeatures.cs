namespace Pancake.Platforms.Abstraction.Services;

/// <summary>平台能力开关，界面据此显示或隐藏入口，而不是静默失败。</summary>
public interface IPlatformFeatures
{
    /// <summary>是否支持网页壁纸（依赖内嵌浏览器）。</summary>
    bool WebWallpaper { get; }

    /// <summary>是否支持探测 Wallpaper Engine 本地项目。</summary>
    bool WallpaperEngine { get; }

    /// <summary>是否支持覆盖式自动更新。</summary>
    bool SelfUpdate { get; }
}
