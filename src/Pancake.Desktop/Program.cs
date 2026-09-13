using Avalonia;
using Pancake.Platforms.Abstraction;
using Pancake.Platforms.Abstraction.Stubs;
using Pancake.Platforms.Shared;

#if Platforms_Windows
using Pancake.Platforms.Windows;
#endif
#if Platforms_Linux
using Pancake.Platforms.Linux;
#endif
#if Platforms_MacOs
using Pancake.Platforms.MacOs;
#endif

namespace Pancake.Desktop;

/// <summary>
/// 桌面入口。先按当前平台注册平台服务，再启动共用的 Avalonia 应用，
/// 这样界面层永远只看到 PlatformServices 里的能力接口。
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ActivatePlatforms();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia 设计器与预览也需要一个不启动消息循环的 AppBuilder。</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void ActivatePlatforms()
    {
        // 未被任何平台覆盖的能力保持 Stub，界面据此隐藏入口而不是静默失败。
        PlatformServices.AppPaths = new DefaultAppPathsService();
        // 音频是三个桌面平台共用的实现：采集与提示音都走 SoundFlow(MiniAudio)。
        PlatformServices.NoiseCapture = new SoundFlowNoiseCaptureService();
        PlatformServices.NoiseAlert = new SoundFlowNoiseAlertPlayback();

#if Platforms_Windows
        PlatformServices.WebWallpaper = new WindowsWebWallpaperHost();
        PlatformServices.WallpaperEnginePaths = new WindowsWallpaperEnginePathSource();
        PlatformServices.Backdrop = new WindowsBackdropService();
        PlatformServices.AppUpdate = new WindowsAppUpdateService();
        PlatformServices.Features = new WindowsPlatformFeatures();
#endif
#if Platforms_Linux
        PlatformServices.AppPaths = new LinuxAppPathsService();
        PlatformServices.Features = new LinuxPlatformFeatures();
#endif
#if Platforms_MacOs
        PlatformServices.AppPaths = new MacOsAppPathsService();
        PlatformServices.Features = new MacOsPlatformFeatures();
#endif
    }
}
