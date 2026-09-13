using System.Globalization;

namespace Pancake;

/// <summary>
/// 与界面框架无关的 32 位 ARGB 颜色，用于在核心层保存颜色而不依赖 WinUI 或 Avalonia 类型。
/// 界面层负责把它转换成各自的画刷。
/// </summary>
public readonly record struct BoardColor(byte A, byte R, byte G, byte B)
{
    public static BoardColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static BoardColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static BoardColor White { get; } = new(255, 255, 255, 255);

    public static BoardColor Black { get; } = new(255, 0, 0, 0);

    public static BoardColor Transparent { get; } = new(0, 0, 0, 0);

    /// <summary>按 0~1 的透明度重新计算 Alpha 通道，用于把颜色层和透明度设置组合成实际画刷颜色。</summary>
    public BoardColor WithOpacity(double opacity)
    {
        double clamped = double.IsNaN(opacity) ? 0 : Math.Clamp(opacity, 0, 1);
        return new BoardColor((byte)Math.Round(clamped * 255), R, G, B);
    }

    /// <summary>判断 RGB 是否等于给定的分量，忽略 Alpha。</summary>
    public bool HasRgb(byte r, byte g, byte b) => R == r && G == g && B == b;

    /// <summary>按 <c>#RRGGBB</c> 或 <c>#AARRGGBB</c> 解析；无法解析时返回备用颜色。</summary>
    public static BoardColor Parse(string? value, BoardColor fallback)
    {
        string hex = (value ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length is not (6 or 8)) return fallback;
        foreach (char c in hex)
        {
            if (!Uri.IsHexDigit(c)) return fallback;
        }

        static byte Channel(string text, int start) =>
            byte.Parse(text.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return hex.Length == 6
            ? FromRgb(Channel(hex, 0), Channel(hex, 2), Channel(hex, 4))
            : FromArgb(Channel(hex, 0), Channel(hex, 2), Channel(hex, 4), Channel(hex, 6));
    }

    /// <summary>按 <c>#AARRGGBB</c> 输出，与项目文件里既有的颜色写法保持一致。</summary>
    public string ToHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}
