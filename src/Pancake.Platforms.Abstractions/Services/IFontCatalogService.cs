namespace Pancake.Platforms.Abstraction.Services;

/// <summary>系统已安装字体的查询能力，用于判断富文本里保存的字体是否仍然可用。</summary>
public interface IFontCatalogService
{
    /// <summary>系统字体家族名列表，实现内部应缓存结果。</summary>
    IReadOnlyList<string> AvailableFamilies { get; }

    /// <summary>家族名（含本地化别名）是否对应一个已安装字体。</summary>
    bool IsInstalledFamily(string name);
}
