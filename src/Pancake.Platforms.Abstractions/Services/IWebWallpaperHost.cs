using Avalonia.Controls;

namespace Pancake.Platforms.Abstraction.Services;

/// <summary>网页壁纸宿主；目前仅 Windows 实现，其它平台返回不支持。</summary>
public interface IWebWallpaperHost
{
    bool IsSupported { get; }

    /// <summary>创建承载本地网页壁纸的控件并加载入口文件；不支持时返回 null。</summary>
    Control? TryCreate(string packageDirectory, string entryRelativePath);

    /// <summary>
    /// 承载控件是否已经成功加载页面。宿主可以据此在加载失败时降级回图片背景；
    /// 尚未加载完成与加载失败都返回 false。
    /// </summary>
    bool IsReady(Control host);
}
