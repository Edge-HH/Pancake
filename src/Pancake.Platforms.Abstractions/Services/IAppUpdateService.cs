namespace Pancake.Platforms.Abstraction.Services;

/// <summary>
/// 平台更新策略。Windows 保留覆盖式自动更新，Linux 与 macOS 只提示新版本，
/// 由用户在发布页自行下载替换。
/// </summary>
public interface IAppUpdateService
{
    /// <summary>本平台是否支持下载后自动覆盖安装。</summary>
    bool SupportsSelfUpdate { get; }

    /// <summary>启动已下载的更新包；不支持或启动失败时返回 false。</summary>
    bool TryLaunchInstaller(string path);
}
