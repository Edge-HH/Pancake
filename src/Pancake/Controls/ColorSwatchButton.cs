using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Pancake.Controls;

/// <summary>共用色卡：保持悬停颜色，并以系统强调色边框和右上角勾选呈现当前颜色。</summary>
public sealed class ColorSwatchButton : Button
{
    private readonly Border _check;
    public Color Color { get; }
    public bool IsSelected { get; private set; }

    public ColorSwatchButton(Color color, double size)
    {
        Color = color;
        Width = Height = size;
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(color);
        Resources["ButtonBackgroundPointerOver"] = Background;
        Resources["ButtonBackgroundPressed"] = Background;
        CornerRadius = new CornerRadius(4);
        _check = new Border
        {
            Width = size * .45, Height = size * .45,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Child = new FluentIcon { Symbol = "Checkmark", FontSize = size * .34, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) }
        };
        Content = new Grid { IsHitTestVisible = false, Children = { _check } };
        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        _check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        BorderThickness = new Thickness(selected ? 2 : 1);
        BorderBrush = selected
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
        Resources["ButtonBorderBrushPointerOver"] = BorderBrush;
        Resources["ButtonBorderBrushPressed"] = BorderBrush;
        AutomationProperties.SetItemStatus(this, selected ? "已选中" : "未选中");
    }
}
