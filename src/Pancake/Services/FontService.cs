using Avalonia.Media;
using Pancake.Platforms.Abstraction;

namespace Pancake.Services;

/// <summary>
/// 字体显示名与随包字体资源集中解析。三端都使用内置的 HarmonyOS Sans 与 Fluent System Icons，
/// 避免不同系统默认字体导致中文换行和图标位置出现差异。
/// </summary>
public static class FontService
{
    public const string FamilyName = "HarmonyOS Sans SC";

    public const string IconFamilyName = "FluentSystemIcons-Resizable";

    public static FontFamily DefaultFamily { get; } =
        new($"avares://Pancake.Ui/Assets/Fonts/HarmonyOS_Sans_SC_Regular.ttf#{FamilyName}");

    public static FontFamily MediumFamily { get; } =
        new($"avares://Pancake.Ui/Assets/Fonts/HarmonyOS_Sans_SC_Medium.ttf#{FamilyName}");

    public static FontFamily BoldFamily { get; } =
        new($"avares://Pancake.Ui/Assets/Fonts/HarmonyOS_Sans_SC_Bold.ttf#{FamilyName}");

    public static FontFamily IconFamily { get; } =
        new($"avares://Pancake.Ui/Assets/Fonts/FluentSystemIcons-Resizable.ttf#{IconFamilyName}");

    /// <summary>触发一次访问，确保内置字体在首帧前完成注册。</summary>
    public static void RegisterBundledFonts()
    {
        _ = DefaultFamily;
        _ = IconFamily;
        PlatformServices.FontCatalog = new AvaloniaFontCatalogService();
    }

    /// <summary>判断富文本里保存的字体家族在当前系统是否可用；随包字体始终视为可用。</summary>
    public static bool IsInstalledFamily(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals(FamilyName, StringComparison.OrdinalIgnoreCase) || PlatformServices.FontCatalog.IsInstalledFamily(name));

    /// <summary>
    /// 把富文本里保存的字体家族解析成实际用来渲染的家族。
    /// 空值表示跟随默认（随包字体）；随包字体与系统已安装的字体按名字使用；
    /// 本机没有安装的字体退回随包字体，避免出现缺字方框——原始家族名仍保留在富文本里，
    /// 换到装有该字体的机器上会按原名显示（旧版用 FontFallbacks 记录原始名，Avalonia 版本直接存在格式里）。
    /// </summary>
    public static FontFamily? ResolveRichTextFamily(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;
        if (family.Equals(FamilyName, StringComparison.OrdinalIgnoreCase)) return DefaultFamily;
        return PlatformServices.FontCatalog.IsInstalledFamily(family) ? new FontFamily(family) : DefaultFamily;
    }

    /// <summary>可选字体家族：随包字体排在最前，其余按当前区域排序。</summary>
    public static IReadOnlyList<string> SelectableFamilies()
    {
        List<string> families = [FamilyName];
        families.AddRange(PlatformServices.FontCatalog.AvailableFamilies
            .Where(name => !string.Equals(name, FamilyName, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(name => name, StringComparer.CurrentCulture));
        return families;
    }
}
