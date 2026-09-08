using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Grid> _appearancePreviews = [];
    private string _selectedSettingsPage = "";

    private UIElement CreateAppearancePreview(string kind)
    {
        Grid scene = new() { Width = 640, Height = 300, IsHitTestVisible = false };
        _appearancePreviews.Add(kind, scene);
        // 固定设计坐标经 Viewbox 等比缩放，窄窗口也不会撑宽设置页。
        Border frame = new()
        {
            Name = kind + "AppearancePreview", MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = scene }
        };
        // 边框随设置刷新使用当前主题；预览内容完全只读，不连接保存回调。
        frame.BorderBrush = BoardTheme.LineBrush;
        return frame;
    }

    private void RefreshAppearancePreviews()
    {
        string[] kinds = _selectedSettingsPage switch
        {
            "AppearanceTile" => ["Tile"], "AppearanceBackground" => ["Shared", "Clock", "Board"],
            "AppearanceGrid" => ["Grid"], "AppearanceToolbar" => ["Toolbar"], _ => []
        };
        foreach (string kind in kinds)
        {
            if (!_appearancePreviews.TryGetValue(kind, out Grid? scene)) continue;
            if (scene.Parent is Viewbox { Parent: Border frame }) frame.BorderBrush = BoardTheme.LineBrush;
            scene.Children.Clear();
            scene.Background = BoardTheme.SurfaceBrush;
            switch (kind)
            {
                case "Tile":
                    scene.Children.Add(new Viewbox { Margin = new Thickness(30), Stretch = Stretch.Uniform,
                        Child = PreviewTile(ViewModel.Subjects.FirstOrDefault()) });
                    break;
                case "Shared":
                    AddPreviewBackground(scene, UseSharedBackground ? _settings.SharedBackground : new());
                    Grid split = new();
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    Grid clock = new(), board = new();
                    AddPreviewBackground(clock, _settings.ClockBackground, UseSharedBackground);
                    AddPreviewBackground(board, _settings.BoardBackground, UseSharedBackground);
                    clock.Children.Add(PreviewClock()); board.Children.Add(PreviewBoard());
                    split.Children.Add(clock); Grid.SetColumn(board, 1); split.Children.Add(board);
                    scene.Children.Add(split);
                    break;
                case "Clock":
                case "Board":
                    if (UseSharedBackground) AddPreviewBackground(scene, _settings.SharedBackground);
                    AddPreviewBackground(scene, _settings.LayoutMode == "Free" ? new() :
                        kind == "Clock" ? _settings.ClockBackground : _settings.BoardBackground, UseSharedBackground);
                    scene.Children.Add(kind == "Clock" ? PreviewClock() : PreviewBoard());
                    break;
                case "Grid":
                    Grid comparison = new();
                    comparison.ColumnDefinitions.Add(new ColumnDefinition());
                    comparison.ColumnDefinitions.Add(new ColumnDefinition());
                    for (int column = 0; column < 2; column++)
                    {
                        Grid sample = new();
                        Canvas lines = new() { Width = 320, Height = 300,
                            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 320, 300) } };
                        DrawGrid(lines, GridAppearance.EffectiveStyle(_settings.GridStyle, column == 1, _settings.ShowGridWhileEditing), 0, 0, 320, 300);
                        sample.Children.Add(lines);
                        sample.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 4, 10, 4), Background = BoardTheme.SurfaceBrush,
                            Child = new TextBlock { Text = column == 0 ? "查看模式" : "编辑模式", Foreground = BoardTheme.TextBrush } });
                        Grid.SetColumn(sample, column); comparison.Children.Add(sample);
                    }
                    scene.Children.Add(comparison);
                    break;
                case "Toolbar":
                    scene.Children.Add(PreviewToolbar());
                    break;
            }
        }
    }

    private static void AddPreviewBackground(Grid target, BackgroundSettings settings, bool shared = false)
    {
        BackgroundVisual background = new();
        background.Apply(settings, BoardTheme.SurfaceBrush, shared);
        target.Children.Add(background);
    }

    private SubjectTileControl PreviewTile(SubjectBoard? source)
    {
        // 使用真实磁贴控件和独立模型副本，富文本、附件、笔迹与标题样式和看板一致。
        SubjectBoard subject = source?.Clone() ?? new SubjectBoard { Name = "语文", TileWidth = 430, TileHeight = 220 };
        if (source is null) subject.Entries.Add(new HomeworkEntry { Content = "阅读课文，完成课后练习。" });
        SubjectTileControl tile = new(subject, _ => { }, _ => { }, _ => { }, _ => { }, _ => Task.CompletedTask, () => { });
        tile.IsHitTestVisible = false;
        tile.Loaded += (_, _) => DisablePreviewTabStops(tile);
        tile.ApplyAppearance(_settings);
        return tile;
    }

    private static void DisablePreviewTabStops(DependencyObject element)
    {
        // 预览中的只读编辑器和附件按钮不能占用设置页的键盘导航顺序。
        if (element is Control control) control.IsTabStop = false;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            DisablePreviewTabStops(VisualTreeHelper.GetChild(element, index));
    }

    private UIElement PreviewBoard()
    {
        double width = Math.Max(430, BoardCanvas.Width), height = Math.Max(300, BoardCanvas.Height);
        Canvas canvas = new() { Width = width, Height = height,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) } };
        foreach (SubjectBoard subject in ViewModel.Subjects)
        {
            SubjectTileControl tile = PreviewTile(subject);
            Canvas.SetLeft(tile, subject.X); Canvas.SetTop(tile, subject.Y); canvas.Children.Add(tile);
        }
        if (ViewModel.Subjects.Count == 0) canvas.Children.Add(PreviewTile(null));
        return new Viewbox { Margin = new Thickness(16), Stretch = Stretch.Uniform, Child = canvas };
    }

    private UIElement PreviewClock()
    {
        TextBlock LiveText(TextBlock source, double size)
        {
            TextBlock text = new() { FontSize = size, Foreground = BoardTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, FontWeight = source.FontWeight, FontFamily = source.FontFamily };
            text.SetBinding(TextBlock.TextProperty, new Binding { Source = source, Path = new PropertyPath("Text"), Mode = BindingMode.OneWay });
            return text;
        }
        StackPanel content = new() { Spacing = 18 };
        StackPanel time = new() { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        time.Children.Add(LiveText(MainTimeText, 112));
        TextBlock seconds = LiveText(SecondsText, 28); seconds.Margin = new Thickness(0, 17, 0, 0); time.Children.Add(seconds);
        content.Children.Add(time); content.Children.Add(LiveText(ClockDateText, 20));
        StackPanel components = new() { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center };
        components.Children.Add(LiveText(WeatherText, 14)); components.Children.Add(LiveText(NoiseText, 14));
        content.Children.Add(components);
        return new Viewbox { Margin = new Thickness(24), Stretch = Stretch.Uniform, Child = content };
    }

    private UIElement PreviewToolbar()
    {
        // 采用窗口尺寸作为预览坐标，让位置、边距和大小保持实际的相对比例。
        Grid viewport = new() { Width = Math.Max(640, RootShell.ActualWidth), Height = Math.Max(400, RootShell.ActualHeight) };
        AddPreviewBackground(viewport, UseSharedBackground ? _settings.SharedBackground : _settings.BoardBackground);
        string position = _settings.ToolbarPosition;
        bool vertical = position.StartsWith("Center");
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        StackPanel buttons = new() { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4 * scale };
        foreach (var (glyph, label) in new[] { (FluentGlyphs.Edit, "编辑看板"), (FluentGlyphs.Settings, "设置"), (FluentGlyphs.FullScreen, "进入全屏") })
        {
            StackPanel content = new() { Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical,
                Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            FluentIcon icon = new() { Glyph = glyph, FontSize = 18 * scale, Foreground = BoardTheme.TextBrush };
            TextBlock text = new() { Text = label, FontSize = 10 * scale, Foreground = BoardTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = _settings.ToolbarIconOnly ? Visibility.Collapsed : Visibility.Visible };
            bool labelFirst = position.StartsWith("Top") || position == "CenterLeft";
            if (labelFirst) content.Children.Add(text);
            content.Children.Add(icon);
            if (!labelFirst) content.Children.Add(text);
            buttons.Children.Add(new Button { Width = (_settings.ToolbarIconOnly ? 44 : 88) * scale,
                Height = (_settings.ToolbarIconOnly ? 44 : 64) * scale, Padding = new Thickness(6 * scale),
                Style = SettingsButton.Style, Content = content, IsTabStop = false });
        }
        viewport.Children.Add(new Border
        {
            Name = "ToolbarPreviewFrame", Child = buttons, Padding = new Thickness(7 * scale), CornerRadius = new CornerRadius(_settings.ToolbarRadius),
            Background = _settings.ToolbarGlass ? new BlurBackdropBrush(_settings.ToolbarBlur) : BoardTheme.SurfaceBrush,
            BorderBrush = BoardTheme.LineBrush, BorderThickness = new Thickness(1),
            HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            Margin = new Thickness(_settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset, _settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset)
        });
        return new Viewbox { Stretch = Stretch.Uniform, Child = viewport };
    }
}
