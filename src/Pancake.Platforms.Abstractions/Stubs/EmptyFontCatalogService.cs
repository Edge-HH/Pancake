using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>没有字体枚举能力时的占位实现：只保证界面能正常构建。</summary>
public sealed class EmptyFontCatalogService : IFontCatalogService
{
    public IReadOnlyList<string> AvailableFamilies => [];

    public bool IsInstalledFamily(string name) => false;
}
