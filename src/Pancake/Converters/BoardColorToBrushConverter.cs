using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Pancake.Converters;

/// <summary>把核心层的 BoardColor 转成 Avalonia 画刷，供磁贴主题色绑定使用。</summary>
public sealed class BoardColorToBrushConverter : IValueConverter
{
    public static BoardColorToBrushConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BoardColor color
            ? new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B))
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
