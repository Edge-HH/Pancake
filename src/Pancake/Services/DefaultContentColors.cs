using System.Text.RegularExpressions;

namespace Pancake.Services;

/// <summary>默认内容色以白色存储，仅显示时适配浅色主题；不修改彩色文字和笔迹。</summary>
public static class DefaultContentColors
{
    public static bool IsDefaultWhite(int red, int green, int blue) =>
        (red, green, blue) is (255, 255, 255) or (247, 247, 249) or (245, 245, 247) or (240, 240, 245);

    public static string AdaptRtf(string rtf, bool light, bool saving = false)
    {
        if (!light) return rtf;
        return Regex.Replace(rtf, @"\{\\colortbl[^{}]*\}", table => Regex.Replace(table.Value,
            @"\\red(\d+)\\green(\d+)\\blue(\d+)", match =>
            {
                int r = int.Parse(match.Groups[1].Value), g = int.Parse(match.Groups[2].Value), b = int.Parse(match.Groups[3].Value);
                if (saving && r == 0 && g == 0 && b == 0) return @"\red247\green247\blue249";
                return !saving && IsDefaultWhite(r, g, b) ? @"\red0\green0\blue0" : match.Value;
            }));
    }
}
