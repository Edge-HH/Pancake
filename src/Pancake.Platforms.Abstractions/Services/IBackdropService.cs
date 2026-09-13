using Avalonia.Controls;

namespace Pancake.Platforms.Abstraction.Services;

/// <summary>窗口背景材质；不支持的系统由实现自行回退为纯色或模糊。</summary>
public interface IBackdropService
{
    /// <summary>当前平台是否具备原生材质。</summary>
    bool IsSupported { get; }

    /// <summary>按平台能力设置窗口材质。</summary>
    void Apply(Window window, bool enabled);
}
