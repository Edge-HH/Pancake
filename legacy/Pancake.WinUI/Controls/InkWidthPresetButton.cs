using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Services;
using Windows.UI;

namespace Pancake.Controls;

/// <summary>
/// 画笔粗细预设按钮：以从小到大排列的圆点表示细、中、粗三档，
/// 选中状态用系统强调色的描边与填充呈现，取代原先的滑块与“粗细”文字。
/// </summary>
public sealed class InkWidthPresetButton : Button
{
    private const double BaseSize = 40;
    private readonly Ellipse _dot;
    private readonly double _baseDotDiameter;

    /// <summary>点击后写入画笔设置的笔迹粗细值。</summary>
    public double Thickness { get; }
    public bool IsSelected { get; private set; }

    public InkWidthPresetButton(double thickness, double baseDotDiameter, string name)
    {
        Thickness = thickness;
        _baseDotDiameter = baseDotDiameter;
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _dot = new Ellipse();
        Content = _dot;
        AutomationProperties.SetName(this, name);
        ApplyScale(1);
        SetSelected(false);
    }

    /// <summary>跟随控制窗缩放同步按钮外框与圆点直径，缩放后圆点仍居中。</summary>
    public void ApplyScale(double scale)
    {
        Width = Height = BaseSize * scale;
        _dot.Width = _dot.Height = _baseDotDiameter * scale;
        CornerRadius = new CornerRadius(4 * scale);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        BorderThickness = new Thickness(selected ? 2 : 1);
        BorderBrush = selected ? AccentBrush() : new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
        // 主题或色系切换会重建画笔色卡；圆点颜色在此一并刷新，避免保留旧主题的正文色。
        RefreshTheme();
        AutomationProperties.SetItemStatus(this, selected ? "已选中" : "未选中");
    }

    public void RefreshTheme() => _dot.Fill = IsSelected ? AccentBrush() : BoardTheme.TextBrush;

    private static Brush AccentBrush() => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
}
