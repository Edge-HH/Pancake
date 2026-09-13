using Avalonia.Controls;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>没有系统材质时的占位实现，窗口使用纯色或模糊背景自行绘制。</summary>
public sealed class NoopBackdropService : IBackdropService
{
    public bool IsSupported => false;

    public void Apply(Window window, bool enabled)
    {
    }
}
