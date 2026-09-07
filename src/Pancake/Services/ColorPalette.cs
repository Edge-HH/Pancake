using System.Text.RegularExpressions;

namespace Pancake.Services;

/// <summary>固定的一一对应色板，允许反复切换而不累计颜色误差。</summary>
public static class ColorPalette
{
    private static readonly string[] Vivid = ["#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF", "#F87171", "#65D46E", "#7567FF"];
    private static readonly string[] Macaron = ["#A8D5BA", "#BCBCE3", "#AECBE8", "#E8D5A5", "#E3B6D0", "#A5D5CF", "#E5B3B3", "#B0D5B4", "#BEB5E8"];
    public static bool IsMacaron { get; set; }

    public static string Resolve(string hex) => ConvertHex(hex, IsMacaron);

    public static bool IsPreset(string hex) => Vivid.Any(value => value.Equals(hex, StringComparison.OrdinalIgnoreCase))
        || Macaron.Any(value => value.Equals(hex, StringComparison.OrdinalIgnoreCase));

    public static string ResolveAccent(string hex, bool isExplicit, bool macaron) =>
        isExplicit && !IsPreset(hex) ? hex : ConvertHex(hex, macaron);

    public static string ConvertHex(string hex, bool macaron)
    {
        int index = Array.FindIndex(Vivid, value => value.Equals(hex, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = Array.FindIndex(Macaron, value => value.Equals(hex, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? hex : (macaron ? Macaron : Vivid)[index];
    }

    // 只替换 RTF 颜色表中的 RGB 项，保留正文、选区格式及自定义颜色。
    public static string ConvertRtf(string rtf, bool macaron) => Regex.Replace(rtf,
        @"\{\\colortbl[^{}]*\}", table => ConvertColorTable(table.Value, macaron));

    private static string ConvertColorTable(string table, bool macaron) => Regex.Replace(table,
        @"\\red(\d+)\\green(\d+)\\blue(\d+)", match =>
        {
            string hex = $"#{int.Parse(match.Groups[1].Value):X2}{int.Parse(match.Groups[2].Value):X2}{int.Parse(match.Groups[3].Value):X2}";
            string mapped = ConvertHex(hex, macaron);
            return $"\\red{Convert.ToInt32(mapped.Substring(1, 2), 16)}\\green{Convert.ToInt32(mapped.Substring(3, 2), 16)}\\blue{Convert.ToInt32(mapped.Substring(5, 2), 16)}";
        });
}
