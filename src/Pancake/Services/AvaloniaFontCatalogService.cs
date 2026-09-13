using Avalonia.Media;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Services;

/// <summary>
/// 通过 Avalonia 的字体管理器枚举系统字体，替代旧版依赖 GDI 的 `EnumFontFamiliesEx`。
/// 结果缓存一次，避免每次富文本加载都重新枚举。
/// </summary>
public sealed class AvaloniaFontCatalogService : IFontCatalogService
{
    private readonly Lazy<IReadOnlyList<string>> _families = new(EnumerateFamilies);

    public IReadOnlyList<string> AvailableFamilies => _families.Value;

    public bool IsInstalledFamily(string name) =>
        !string.IsNullOrWhiteSpace(name) && AvailableFamilies.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> EnumerateFamilies()
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(family => family.Name.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // 字体后端尚未就绪时退化为空列表，界面仍可正常显示内置字体。
            return [];
        }
    }
}
