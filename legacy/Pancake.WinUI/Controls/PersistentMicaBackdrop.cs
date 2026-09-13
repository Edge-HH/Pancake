using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pancake.Controls;

/// <summary>使用系统默认参数，并在窗口失焦时保留材质色彩的 Mica 背景。</summary>
public sealed class PersistentMicaBackdrop : SystemBackdrop
{
    private ISystemBackdropControllerWithTargets? _controller;
    private SystemBackdropConfiguration? _defaultConfiguration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (_controller is not null)
        {
            throw new InvalidOperationException("PersistentMicaBackdrop 不能同时用于多个窗口。");
        }

        _controller = MicaController.IsSupported()
            ? new MicaController { Kind = MicaKind.Base }
            : DesktopAcrylicController.IsSupported()
                ? new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base }
                : null;

        if (_controller is null)
        {
            return;
        }

        // 仅在目标稳定连接时获取一次；WinUI 会持续更新这个默认配置对象。
        _defaultConfiguration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        _controller.AddSystemBackdropTarget(connectedTarget);
        ApplySystemConfiguration();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        // 失焦期间回调参数可能正处于断开状态，不能再用 target 查询默认配置。
        ApplySystemConfiguration();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
        _defaultConfiguration = null;
    }

    private void ApplySystemConfiguration()
    {
        if (_controller is null || _defaultConfiguration is null)
        {
            return;
        }

        _controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration
        {
            Theme = _defaultConfiguration.Theme,
            IsHighContrast = _defaultConfiguration.IsHighContrast,
            // ClassIsland 的原生 Mica 不会在失焦时变成灰色；这里保留同样的视觉状态。
            IsInputActive = true
        });
    }
}
