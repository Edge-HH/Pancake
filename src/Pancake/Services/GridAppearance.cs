using Windows.UI;

namespace Pancake.Services;

public static class GridAppearance
{
    public static string EffectiveStyle(string configuredStyle, bool isEditing, bool showGridWhileEditing) =>
        isEditing && showGridWhileEditing ? "Grid" : configuredStyle is "Grid" or "Dots" or "None" ? configuredStyle : "Grid";

    public static Color ParseColor(string value, Color fallback)
    {
        try
        {
            string hex = value.Trim().TrimStart('#');
            return hex.Length switch
            {
                6 => Color.FromArgb(255, Byte(hex, 0), Byte(hex, 2), Byte(hex, 4)),
                8 => Color.FromArgb(Byte(hex, 0), Byte(hex, 2), Byte(hex, 4), Byte(hex, 6)),
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    public static string FormatColor(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static byte Byte(string value, int start) => Convert.ToByte(value.Substring(start, 2), 16);
}
