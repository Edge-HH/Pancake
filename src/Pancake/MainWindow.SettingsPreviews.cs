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
    private SubjectTileControl? _tileAppearancePreview;
    private bool _tilePreviewIsLight;
    private readonly Dictionary<string, (string Key, List<Action> Refresh)> _previewScenes = [];
    private List<Action>? _buildingPreviewRefreshers;

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
            if (kind == "Tile" && _tileAppearancePreview is not null && _tilePreviewIsLight == BoardTheme.IsLight)
            {
                _tileAppearancePreview.ApplyAppearance(_settings);
                continue;
            }
            string key = kind == "Toolbar"
                ? $"{BoardTheme.IsLight}|{RootShell.ActualWidth}|{RootShell.ActualHeight}|{_settings.ToolbarPosition}|{_settings.ToolbarScale}|{_settings.ToolbarIconOnly}|{_settings.ToolbarRadius}|{_settings.ToolbarGlass}|{_settings.ToolbarBlur}|{_settings.ToolbarHorizontalInset}|{_settings.ToolbarVerticalInset}"
                : $"{BoardTheme.IsLight}|{_settings.LayoutMode}";
            if (kind != "Grid" && _previewScenes.TryGetValue(kind, out var cached) && cached.Key == key)
            {
                scene.Background = BoardTheme.SurfaceBrush;
                foreach (var refresh in cached.Refresh) refresh();
                continue;
            }
            // 外观值更新只修改已有控件；重建整棵预览树会卸载播放器并丢失播放进度。
            _buildingPreviewRefreshers = [];
            _previewScenes[kind] = (key, _buildingPreviewRefreshers);
            scene.Children.Clear();
            scene.Background = BoardTheme.SurfaceBrush;
            switch (kind)
            {
                case "Tile":
                    scene.Children.Add(new Image
                    {
                        Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Settings/tile-preview-background.png")),
                        Stretch = Stretch.UniformToFill
                    });
                    _tileAppearancePreview = PreviewTile(CreateTileAppearanceSample());
                    _tilePreviewIsLight = BoardTheme.IsLight;
                    scene.Children.Add(new Viewbox { Margin = new Thickness(30), Stretch = Stretch.Uniform,
                        Child = _tileAppearancePreview });
                    break;
                case "Shared":
                    AddPreviewBackground(scene, () => UseSharedBackground ? _settings.SharedBackground : new());
                    Grid split = new();
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star) });
                    _buildingPreviewRefreshers.Add(() =>
                    {
                        split.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star);
                        split.ColumnDefinitions[1].Width = new GridLength(1 - Math.Clamp(_settings.SplitRatio, .1, .9), GridUnitType.Star);
                    });
                    Grid clock = new(), board = new();
                    AddPreviewBackground(clock, () => _settings.ClockBackground, () => UseSharedBackground);
                    AddPreviewBackground(board, () => _settings.BoardBackground, () => UseSharedBackground);
                    clock.Children.Add(PreviewClock()); board.Children.Add(PreviewBoard());
                    split.Children.Add(clock); Grid.SetColumn(board, 1); split.Children.Add(board);
                    scene.Children.Add(split);
                    break;
                case "Clock":
                case "Board":
                    AddPreviewBackground(scene, () => UseSharedBackground ? _settings.SharedBackground : new());
                    AddPreviewBackground(scene, () => _settings.LayoutMode == "Free" ? new() :
                        kind == "Clock" ? _settings.ClockBackground : _settings.BoardBackground, () => UseSharedBackground);
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
            _buildingPreviewRefreshers = null;
        }
    }

    private void AddPreviewBackground(Grid target, Func<BackgroundSettings> settings, Func<bool>? shared = null)
    {
        BackgroundVisual background = new();
        void Refresh() => background.Apply(settings(), BoardTheme.SurfaceBrush, shared?.Invoke() ?? false);
        Refresh();
        _buildingPreviewRefreshers?.Add(Refresh);
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
        _buildingPreviewRefreshers?.Add(() => tile.ApplyAppearance(_settings));
        return tile;
    }

    private static SubjectBoard CreateTileAppearanceSample()
    {
        // 固定示例只属于设置预览，不读取项目作业，也不接入保存回调。
        SubjectBoard sample = new() { Name = "这是标题~", TileWidth = 560, TileHeight = 230 };
        sample.Entries.Add(new HomeworkEntry
        {
            Content = "Through adversity to the stars.",
            RtfContent = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}{\\colortbl ;\\red247\\green247\\blue249;}\\f0\\fs36\\cf1\\i Through adversity to the stars.\\i0\\par}"
        });
        sample.Entries.Add(new HomeworkEntry
        {
            Content = "Per ardua ad astra.",
            RtfContent = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}{\\colortbl ;\\red247\\green247\\blue249;}\\f0\\fs36\\cf1{\\field{\\*\\fldinst HYPERLINK \"https://en.wikipedia.org/wiki/Per_ardua_ad_astra\"}{\\fldrslt Per ardua ad astra}}.\\par}"
        });
        return sample;
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

    private UIElement CreateToolbarPositionPicker()
    {
        Grid screen = new() { Height = 210, Padding = new Thickness(12) };
        for (int index = 0; index < 3; index++)
        {
            screen.RowDefinitions.Add(new RowDefinition());
            screen.ColumnDefinitions.Add(new ColumnDefinition());
        }
        Border frame = new()
        {
            Name = "ToolbarPositionPicker", MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(8), CornerRadius = new CornerRadius(12), Child = screen
        };
        var positions = new[]
        {
            ("TopLeft", "左上", 0, 0), ("TopCenter", "上居中", 0, 1), ("TopRight", "右上", 0, 2),
            ("CenterLeft", "左居中（竖置）", 1, 0), ("CenterRight", "右居中（竖置）", 1, 2),
            ("BottomLeft", "左下", 2, 0), ("BottomCenter", "下居中", 2, 1), ("BottomRight", "右下", 2, 2)
        };
        foreach (var (position, label, row, column) in positions)
        {
            RadioButton button = new()
            {
                Name = "ToolbarPosition" + position, GroupName = "ToolbarPosition", Tag = position,
                MinWidth = 0, MinHeight = 40, Width = 40, Padding = new Thickness(0),
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : column == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = row == 0 ? VerticalAlignment.Top : row == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                IsChecked = _settings.ToolbarPosition == position
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Checked += (_, _) =>
            {
                if (_settings.ToolbarPosition == position) return;
                _settings.ToolbarPosition = position;
                SettingChanged();
            };
            Grid.SetRow(button, row); Grid.SetColumn(button, column); screen.Children.Add(button);
            _refreshSettingAvailability.Add(() => button.IsChecked = _settings.ToolbarPosition == position);
        }
        _refreshSettingAvailability.Add(() =>
        {
            frame.BorderBrush = BoardTheme.TextBrush;
            frame.Background = BoardTheme.SurfaceBrush;
        });
        return frame;
    }

    private UIElement PreviewToolbar()
    {
        string position = _settings.ToolbarPosition;
        bool vertical = position.StartsWith("Center");
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double toolbarWidth = ((vertical ? (_settings.ToolbarIconOnly ? 44 : 64) : (_settings.ToolbarIconOnly ? 44 : 88) * 3 + 8) + 14) * scale + 2;
        double toolbarHeight = (((_settings.ToolbarIconOnly ? 44 : 64) * (vertical ? 3 : 1)) + (vertical ? 8 : 0) + 14) * scale + 2;
        // 在实际窗口坐标中截取控制窗附近，保持预览宽高比，避免整窗缩小和留黑边。
        double cropHeight = Math.Max(300, Math.Max(toolbarHeight + 64, (toolbarWidth + 64) * 300 / 640));
        double cropWidth = cropHeight * 640 / 300;
        Grid viewport = new()
        {
            Width = Math.Max(cropWidth, Math.Max(RootShell.ActualWidth, toolbarWidth + _settings.ToolbarHorizontalInset * 2)),
            Height = Math.Max(cropHeight, Math.Max(RootShell.ActualHeight, toolbarHeight + _settings.ToolbarVerticalInset * 2))
        };
        BackgroundVisual background = new() { MediaStretchOverride = Stretch.UniformToFill };
        background.Apply(UseSharedBackground ? _settings.SharedBackground : _settings.BoardBackground, BoardTheme.SurfaceBrush);
        _buildingPreviewRefreshers?.Add(() => background.Apply(UseSharedBackground ? _settings.SharedBackground : _settings.BoardBackground, BoardTheme.SurfaceBrush));
        viewport.Children.Add(background);
        StackPanel buttons = new() { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4 * scale };
        foreach (var (glyph, label) in new[] { (FluentGlyphs.Edit, "编辑看板"), (FluentGlyphs.Settings, "设置"), (FluentGlyphs.FullScreen, "进入全屏") })
        {
            StackPanel content = new() { Orientation = Orientation.Vertical,
                Spacing = (vertical ? 2 : 4) * scale, HorizontalAlignment = HorizontalAlignment.Center };
            FluentIcon icon = new() { Glyph = glyph, FontSize = 18 * scale, Foreground = BoardTheme.TextBrush };
            TextBlock text = new() { Text = label, FontSize = (vertical ? 8 : 10) * scale, Foreground = BoardTheme.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = _settings.ToolbarIconOnly ? Visibility.Collapsed : Visibility.Visible };
            bool labelFirst = !vertical && position.StartsWith("Top");
            if (labelFirst) content.Children.Add(text);
            content.Children.Add(icon);
            if (!labelFirst) content.Children.Add(text);
            double buttonWidth = (_settings.ToolbarIconOnly ? 44 : vertical ? 64 : 88) * scale;
            double buttonHeight = (_settings.ToolbarIconOnly ? 44 : 64) * scale;
            buttons.Children.Add(new Button { Width = buttonWidth,
                Height = buttonHeight, Padding = new Thickness(6 * scale),
                CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, 7 * scale, buttonWidth, buttonHeight),
                Style = SettingsButton.Style, Content = content, IsTabStop = false });
        }
        viewport.Children.Add(new Border
        {
            Name = "ToolbarPreviewFrame", Child = buttons, Padding = new Thickness(7 * scale), CornerRadius = new CornerRadius(_settings.ToolbarRadius),
            Background = CreateToolbarBackground(),
            BorderBrush = BoardTheme.LineBrush, BorderThickness = new Thickness(1),
            HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            Margin = new Thickness(_settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset, _settings.ToolbarHorizontalInset, _settings.ToolbarVerticalInset)
        });
        double centerX = position.EndsWith("Left") ? _settings.ToolbarHorizontalInset + toolbarWidth / 2
            : position.EndsWith("Right") ? viewport.Width - _settings.ToolbarHorizontalInset - toolbarWidth / 2 : viewport.Width / 2;
        double centerY = position.StartsWith("Top") ? _settings.ToolbarVerticalInset + toolbarHeight / 2
            : vertical ? viewport.Height / 2 : viewport.Height - _settings.ToolbarVerticalInset - toolbarHeight / 2;
        Canvas crop = new() { Width = cropWidth, Height = cropHeight,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, cropWidth, cropHeight) } };
        Canvas.SetLeft(viewport, -Math.Clamp(centerX - cropWidth / 2, 0, viewport.Width - cropWidth));
        Canvas.SetTop(viewport, -Math.Clamp(centerY - cropHeight / 2, 0, viewport.Height - cropHeight));
        crop.Children.Add(viewport);
        return new Viewbox { Stretch = Stretch.Uniform, Child = crop };
    }
}
