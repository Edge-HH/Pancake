using Pancake.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>非 Windows 平台没有注册表，也就无法探测 Wallpaper Engine 安装位置。</summary>
public sealed class UnsupportedWallpaperEnginePathSource : IWallpaperEnginePathSource
{
    public string? GetSteamRoot() => null;

    public IReadOnlyList<string> GetInstallLocations() => [];
}
