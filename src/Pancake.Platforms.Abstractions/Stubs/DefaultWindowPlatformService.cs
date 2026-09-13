using Avalonia.Controls;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>Avalonia 本身支持全屏，因此默认实现直接使用 WindowState。</summary>
public class DefaultWindowPlatformService : IWindowPlatformService
{
    public virtual void SetFullScreen(Window window, bool fullScreen) =>
        window.WindowState = fullScreen ? WindowState.FullScreen : WindowState.Normal;

    public virtual void KeepBackdropOnDeactivate(Window window, bool enabled)
    {
        // 其它平台没有 Mica 失活变灰的问题，无需额外处理。
    }
}
