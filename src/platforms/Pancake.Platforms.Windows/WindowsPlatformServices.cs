using System.Diagnostics;
using Avalonia.Controls;
using Microsoft.Win32;
using Pancake.Platforms.Abstraction.Services;
using Pancake.Platforms.Abstraction.Stubs;
using Pancake.Services;

namespace Pancake.Platforms.Windows;

/// <summary>从注册表读取 Steam 与 Wallpaper Engine 的安装位置，供核心层扫描本地壁纸项目。</summary>
public sealed class WindowsWallpaperEnginePathSource : IWallpaperEnginePathSource
{
    public string? GetSteamRoot()
    {
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        steam ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        return string.IsNullOrWhiteSpace(steam) ? null : steam;
    }

    public IReadOnlyList<string> GetInstallLocations()
    {
        List<string> locations = [];
        string? installed = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 431960", "InstallLocation", null) as string;
        installed ??= Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 431960", "InstallLocation", null) as string;
        if (!string.IsNullOrWhiteSpace(installed)) locations.Add(installed);
        return locations;
    }
}

/// <summary>Windows 使用 Mica，并在系统不支持时依次回退到亚克力与模糊。</summary>
public sealed class WindowsBackdropService : IBackdropService
{
    public bool IsSupported => true;

    public void Apply(Window window, bool enabled)
    {
        window.TransparencyLevelHint = enabled
            ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur]
            : [WindowTransparencyLevel.None];
    }
}

/// <summary>Windows 保留解压后覆盖重启的自动更新方式。</summary>
public sealed class WindowsAppUpdateService : IAppUpdateService
{
    public bool SupportsSelfUpdate => true;

    public bool TryLaunchInstaller(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>Windows 能力开关：网页壁纸与壁纸引擎探测仅在 Windows 可用。</summary>
public sealed class WindowsPlatformFeatures : IPlatformFeatures
{
    public bool WebWallpaper => true;

    public bool WallpaperEngine => true;

    public bool SelfUpdate => true;
}
