namespace Pancake.Platforms.Abstraction.Services;

/// <summary>
/// 决定配置与项目数据的存放位置。便携模式优先，其余情况使用各平台约定的用户数据目录。
/// </summary>
public interface IAppPathsService
{
    /// <summary>数据根目录，内部再分 settings 与 projects 等子目录。</summary>
    string DataRoot { get; }

    /// <summary>临时文件与缓存目录，可被系统随时清理。</summary>
    string CacheRoot { get; }

    /// <summary>是否运行在便携模式（数据放在程序目录旁边）。</summary>
    bool IsPortable { get; }
}
