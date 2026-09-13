namespace Pancake.Services;

public static class GridAppearance
{
    public static string EffectiveStyle(string configuredStyle, bool isEditing, bool showGridWhileEditing) =>
        isEditing && showGridWhileEditing ? "Grid" : configuredStyle is "Grid" or "Dots" or "None" ? configuredStyle : "Grid";

    public static BoardColor ParseColor(string value, BoardColor fallback) => BoardColor.Parse(value, fallback);

    public static string FormatColor(BoardColor color) => color.ToHex();
}
