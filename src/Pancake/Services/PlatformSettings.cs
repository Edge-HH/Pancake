using Avalonia;
using Avalonia.Platform;

namespace Pancake.Services;

/// <summary>系统层面的外观信息；目前只用于“跟随系统”主题判断。</summary>
public static class SystemTheme
{
    /// <summary>当前系统偏好浅色主题。</summary>
    public static bool IsSystemLightTheme()
    {
        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // 少数无桌面环境的后端没有颜色值接口，此时按深色处理。
            return false;
        }
    }
}
