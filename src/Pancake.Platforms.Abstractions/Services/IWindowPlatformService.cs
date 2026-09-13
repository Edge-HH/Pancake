using Avalonia.Controls;

namespace Pancake.Platforms.Abstraction.Services;

/// <summary>窗口在全屏、材质保留等行为上的平台差异。</summary>
public interface IWindowPlatformService
{
    /// <summary>切换窗口全屏状态；默认实现直接使用 Avalonia 的 WindowState。</summary>
    void SetFullScreen(Window window, bool fullScreen);

    /// <summary>失活后是否仍保留窗口材质；Windows 的 Mica 需要额外处理。</summary>
    void KeepBackdropOnDeactivate(Window window, bool enabled);
}
