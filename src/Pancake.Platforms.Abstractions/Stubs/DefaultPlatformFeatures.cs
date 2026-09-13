using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>保守的能力默认值：全部关闭，由各平台工程按实际实现开启。</summary>
public sealed class DefaultPlatformFeatures : IPlatformFeatures
{
    public bool WebWallpaper => false;

    public bool WallpaperEngine => false;

    public bool SelfUpdate => false;
}
