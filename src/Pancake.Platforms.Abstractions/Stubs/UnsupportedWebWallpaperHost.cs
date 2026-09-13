using Avalonia.Controls;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>非 Windows 平台不提供网页壁纸，界面据此隐藏入口。</summary>
public sealed class UnsupportedWebWallpaperHost : IWebWallpaperHost
{
    public bool IsSupported => false;

    public Control? TryCreate(string packageDirectory, string entryRelativePath) => null;

    public bool IsReady(Control host) => false;
}
