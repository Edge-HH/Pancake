using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 手写相关界面：底部画笔栏、画笔与橡皮擦切换、颜色与粗细预设、清空与撤销，
/// 以及仅时钟模式的全屏笔迹层。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>细、中、粗三档笔迹粗细；圆点直径用于按钮上的可视提示。</summary>
    private static readonly (double Thickness, double DotDiameter, string Name)[] InkWidthPresets =
        [(2d, 8d, "细"), (5d, 13d, "中"), (12d, 19d, "粗")];

    private readonly InkToolSettings _inkSettings = new();
    private readonly StackPanel _inkColors = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel _inkWidths = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private ToggleButton? _inkPen;
    private ToggleButton? _inkEraser;
    private readonly List<Button> _inkWidthButtons = [];
    private readonly List<Button> _inkColorButtons = [];




    /// <summary>构建画笔栏；只在窗口打开时执行一次。</summary>
    private void BuildPenToolbar()
    {
        _inkPen = CreateToolToggle(nameof(FluentGlyphs.Pen), "画笔");
        _inkEraser = CreateToolToggle(nameof(FluentGlyphs.Eraser), "橡皮擦");
        _inkPen.IsChecked = true;
        _inkPen.Click += (_, _) => SelectInkTool(eraser: false);
        _inkEraser.Click += (_, _) => SelectInkTool(eraser: true);
        PenToolbarItems.Children.Add(_inkPen);
        PenToolbarItems.Children.Add(_inkEraser);

        PenToolbarItems.Children.Add(_inkColors);
        BuildInkPalette();
        PenToolbarItems.Children.Add(_inkWidths);
        foreach ((double thickness, double dotDiameter, string name) in InkWidthPresets)
        {
            Button preset = CreateInkWidthButton(thickness, dotDiameter, name);
            _inkWidthButtons.Add(preset);
            _inkWidths.Children.Add(preset);
        }

        Button clear = CreateToolButton(nameof(FluentGlyphs.Delete), "清空笔迹", danger: true, onClick: ClearInk_Click);
        PenToolbarItems.Children.Add(clear);
        PenToolbarItems.Children.Add(CreateToolButton(nameof(FluentGlyphs.Undo), "撤销最后一笔", danger: false, onClick: UndoInk_Click));
        RefreshInkSelection();

        // 全屏笔迹层绑定当前项目的时钟笔迹，改动后安排保存。
        ClockInkLayer.Attach(_viewModel.ClockInk);
        ClockInkLayer.StrokesChanged += ScheduleSave;
    }

    private static ToggleButton CreateToolToggle(string symbol, string tooltip)
    {
        ToggleButton button = new()
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Content = new FluentIcon { Symbol = symbol, FontSize = 16 }
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private static Button CreateToolButton(string symbol, string tooltip, bool danger, EventHandler<RoutedEventArgs> onClick)
    {
        Button button = new()
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Content = new FluentIcon
            {
                Symbol = symbol,
                FontSize = 15,
                Foreground = new SolidColorBrush(
                    danger ? BoardColor.FromRgb(248, 113, 113).ToColor() : BoardTheme.TextColor.ToColor())
            }
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += onClick;
        return button;
    }

    private void SelectInkTool(bool eraser)
    {
        _inkSettings.Eraser = eraser;
        if (_inkPen is not null) _inkPen.IsChecked = !eraser;
        if (_inkEraser is not null) _inkEraser.IsChecked = eraser;
    }

    /// <summary>笔迹颜色沿用色系解析，浅色主题下默认白色会显示为黑色。</summary>
    private void BuildInkPalette()
    {
        _inkColors.Children.Clear();
        _inkColorButtons.Clear();
        BoardColor current = _inkSettings.Color;
        _inkSettings.Color = BoardColor.Parse(ColorPalette.Resolve(current.ToHex()), current);
        foreach (string hex in new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA" }.Select(ColorPalette.Resolve))
        {
            BoardColor color = BoardColor.Parse(hex, BoardColor.White);
            Button swatch = new()
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BoardTheme.DisplayContentColor(color).ToColor()),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(BoardColor.FromArgb(64, 255, 255, 255).ToColor())
            };
            ToolTip.SetTip(swatch, "笔迹颜色");
            swatch.Click += (_, _) =>
            {
                _inkSettings.Color = color;
                SelectInkTool(eraser: false);
                RefreshInkSelection();
            };
            _inkColorButtons.Add(swatch);
            _inkColors.Children.Add(swatch);
        }
    }

    /// <summary>粗细预设按钮：按钮上的圆点直径与线宽成比例，便于辨认当前档位。</summary>
    private Button CreateInkWidthButton(double thickness, double dotDiameter, string name)
    {
        Border dot = new()
        {
            Width = dotDiameter,
            Height = dotDiameter,
            CornerRadius = new CornerRadius(dotDiameter / 2),
            Background = new SolidColorBrush(BoardTheme.TextColor.ToColor()),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button button = new()
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Content = dot
        };
        ToolTip.SetTip(button, $"笔迹粗细：{name}");
        button.Click += (_, _) =>
        {
            _inkSettings.Thickness = thickness;
            SelectInkTool(eraser: false);
            RefreshInkSelection();
        };
        return button;
    }

    /// <summary>同步画笔/橡皮擦与颜色、粗细按钮的选中状态。</summary>
    private void RefreshInkSelection()
    {
        for (int index = 0; index < _inkWidthButtons.Count; index++)
        {
            bool selected = Math.Abs(InkWidthPresets[index].Thickness - _inkSettings.Thickness) < 0.01;
            _inkWidthButtons[index].BorderThickness = new Thickness(selected ? 2 : 0);
            _inkWidthButtons[index].BorderBrush = new SolidColorBrush(BoardBrandColor.ToColor());
        }

        int colorIndex = 0;
        foreach (string hex in new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA" }.Select(ColorPalette.Resolve))
        {
            if (colorIndex >= _inkColorButtons.Count) break;
            bool selected = BoardColor.Parse(hex, BoardColor.White).Equals(_inkSettings.Color);
            _inkColorButtons[colorIndex].BorderThickness = new Thickness(selected ? 3 : 1);
            _inkColorButtons[colorIndex].BorderBrush = new SolidColorBrush(
                selected ? BoardBrandColor.ToColor() : BoardColor.FromArgb(64, 255, 255, 255).ToColor());
            colorIndex++;
        }

        if (_inkPen is not null) _inkPen.IsChecked = !_inkSettings.Eraser;
        if (_inkEraser is not null) _inkEraser.IsChecked = _inkSettings.Eraser;
    }

    private static BoardColor BoardBrandColor => BoardColor.FromRgb(109, 112, 246);

    /// <summary>
    /// 应用手写状态：开启画笔后只有笔迹层接收指针，画笔栏显示、磁贴与组件编辑暂停；
    /// 仅时钟模式下全屏笔迹层同样可写。
    /// </summary>
    private void ApplyInkMode()
    {
        bool drawing = _isEditing && this.FindControl<ToggleButton>("GlobalPenButton")?.IsChecked == true;
        foreach (SubjectTileControl tile in _tiles.Values) tile.SetInkMode(drawing, _inkSettings);
        bool clockOnly = Settings.LayoutMode == "Clock";
        ClockInkLayer.IsVisible = clockOnly;
        ClockInkLayer.SetInkMode(clockOnly && drawing, _inkSettings);
        PenToolbar.IsVisible = drawing;
        // 画笔与格式工具互斥：开启画笔时收起富文本悬浮岛，避免两块岛抢同一个位置。
        if (drawing) HideRichTextIsland();
        UpdateLayoutHandles();
    }

    private void ClearInk_Click(object? sender, RoutedEventArgs e)
    {
        bool clockOnly = Settings.LayoutMode == "Clock";
        // 仅时钟模式先问清范围：只清时钟区域，还是连同全部磁贴一起清空。
        if (clockOnly && ClockInkLayer.StrokeCount > 0)
        {
            ClearClockInkWithChoice();
            return;
        }

        ClearAllTileInk();
    }

    private async void ClearClockInkWithChoice()
    {
        bool confirmed = await ShowConfirmAsync(
            "清空笔迹",
            "只清空时钟区域，还是连同全部磁贴的笔迹一起清空？",
            "只清时钟区域",
            "全部清空");
        if (!confirmed)
        {
            ClearAllTileInk();
            return;
        }

        ClockInkLayer.ClearStrokes();
        ScheduleSave();
    }

    private void ClearAllTileInk()
    {
        foreach (SubjectTileControl tile in _tiles.Values) tile.ClearInk();
        ScheduleSave();
    }

    private void UndoInk_Click(object? sender, RoutedEventArgs e)
    {
        // 仅时钟模式优先撤销屏幕笔迹，其余情况撤销最后动过的磁贴。
        if (Settings.LayoutMode == "Clock" && ClockInkLayer.UndoLastStroke()) return;
        foreach (SubjectTileControl tile in _tiles.Values.Reverse())
        {
            if (tile.UndoInk()) return;
        }
    }
}
