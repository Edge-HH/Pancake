using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;
using Windows.Storage.Pickers;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly List<Action> _refreshSettingAvailability = [];
    private readonly Dictionary<ButtonBase, (UIElement Icon, TextBlock Label)> _toolbarLabels = [];
    private readonly Dictionary<string, SettingsPage> _settingsPages = [];
    private ComboBox? _layoutChoice;

    private sealed record SettingsPage(StackPanel Container, UIElement Content, string Title, string Description);

    private void InitializeExtendedSettings()
    {
        StackPanel layout = SettingsStack();
        _layoutChoice = Choice("布局模式", ["分屏", "仅作业", "仅时钟", "自由布局"],
            ["Split", "Board", "Clock", "Free"], _settings.LayoutMode, value => _settings.LayoutMode = value);
        layout.Children.Add(_layoutChoice);
        layout.Children.Add(Toggle("无限作业板", _settings.InfiniteBoard, value => _settings.InfiniteBoard = value));
        layout.Children.Add(Note("开启后可滚动、触摸平移、Ctrl + 滚轮缩放；编辑磁贴时可用滚动条平移。"));
        layout.Children.Add(Range("网格大小", 16, 160, _settings.GridSize, value =>
        { _settings.GridSize = value; _renderedGridWidth = 0; }));
        layout.Children.Add(Note("分屏模式可拖动分隔条调整比例，拖至两端切换为单区；进入编辑模式后可拖动时钟或组件的任意位置，悬停时使用右下角灰色小框缩放。"));
        LayoutSettingsPanel.Children.Clear();
        layout.Children.Add(Heading("自动布局"));
        layout.Children.Add(Range("自动排版磁贴间隔", 0, 120, _settings.AutoLayoutGap, value => _settings.AutoLayoutGap = value));
        layout.Children.Add(Toggle("自动对齐", _settings.AutoLayoutAlign, value => _settings.AutoLayoutAlign = value));
        layout.Children.Add(Toggle("自动调整磁贴大小", _settings.AutoLayoutResize, value => _settings.AutoLayoutResize = value));
        layout.Children.Add(Note("点击自动排列时按文字、图片和笔迹收紧磁贴；自动对齐会尽量统一相近尺寸并对齐行列。看板与图片导出共用这些设置，优先从左上角排列。"));
        RegisterSettingsPage("Layout", LayoutSettingsPanel, layout, "布局", "调整布局模式、无限作业板与网格。");

        StackPanel tile = SettingsStack();
        tile.Children.Add(CreateAppearancePreview("Tile"));
        tile.Children.Add(Range("标题大小", 16, 72, _settings.TileTitleSize, value => _settings.TileTitleSize = value, TileTitleSizeChanged));
        tile.Children.Add(BackgroundEditor(_settings.TileBackground, () => true, () => true, surface: true));
        StackPanel backgrounds = SettingsStack();
        backgrounds.Children.Add(CreateAppearancePreview("Shared"));
        var shared = Toggle("使用跨区背景", _settings.SharedBackgroundEnabled, value => _settings.SharedBackgroundEnabled = value);
        backgrounds.Children.Add(shared);
        _refreshSettingAvailability.Add(() => shared.IsEnabled = _settings.LayoutMode == "Split");
        backgrounds.Children.Add(BackgroundEditor(_settings.SharedBackground, () => _settings.LayoutMode == "Split" && _settings.SharedBackgroundEnabled, () => false, false));
        backgrounds.Children.Add(CreateAppearancePreview("Clock"));
        backgrounds.Children.Add(Heading("时钟区域背景"));
        backgrounds.Children.Add(BackgroundEditor(_settings.ClockBackground,
            () => _settings.LayoutMode is "Split" or "Clock" && !UseSharedBackground,
            () => _settings.LayoutMode is "Split" or "Clock"));
        backgrounds.Children.Add(CreateAppearancePreview("Board"));
        backgrounds.Children.Add(Heading("作业板区域背景"));
        backgrounds.Children.Add(BackgroundEditor(_settings.BoardBackground,
            () => _settings.LayoutMode is "Split" or "Board" && !UseSharedBackground,
            () => _settings.LayoutMode is "Split" or "Board"));
        backgrounds.Children.Add(Note("跨区背景仅在分屏生效，开启后区域底图由跨区背景统一管理，毛玻璃仍可分别调整。自由布局使用磁贴样式和主题底色。"));
        StackPanel grid = SettingsStack();
        grid.Children.Add(CreateAppearancePreview("Grid"));
        grid.Children.Add(Choice("样式", ["网格", "点阵", "不显示"], ["Grid", "Dots", "None"], _settings.GridStyle, value =>
        { _settings.GridStyle = value; _renderedGridAppearance = string.Empty; }));
        StackPanel gridLineSettings = SettingsStack();
        gridLineSettings.Children.Add(ColorSetting("网格颜色", _settings.GridColor, value => _settings.GridColor = value));
        gridLineSettings.Children.Add(Range("网格粗细", .5, 5, _settings.GridLineThickness, value =>
        { _settings.GridLineThickness = value; _renderedGridAppearance = string.Empty; }));
        StackPanel gridDotSettings = SettingsStack();
        gridDotSettings.Children.Add(ColorSetting("点阵颜色", _settings.GridDotColor, value => _settings.GridDotColor = value));
        gridDotSettings.Children.Add(Range("点的直径", 1, 12, _settings.GridDotDiameter, value =>
        { _settings.GridDotDiameter = value; _renderedGridAppearance = string.Empty; }));
        grid.Children.Add(gridLineSettings);
        grid.Children.Add(gridDotSettings);
        grid.Children.Add(Toggle("编辑时显示常规网格", _settings.ShowGridWhileEditing, value =>
        { _settings.ShowGridWhileEditing = value; _renderedGridAppearance = string.Empty; }));
        grid.Children.Add(Note("此页只改变网格显示样貌；网格吸附仍由编辑工具栏中的吸附按钮独立控制。"));
        _refreshSettingAvailability.Add(() =>
        {
            gridLineSettings.Visibility = _settings.GridStyle == "Grid" ? Visibility.Visible : Visibility.Collapsed;
            gridDotSettings.Visibility = _settings.GridStyle == "Dots" ? Visibility.Visible : Visibility.Collapsed;
        });
        AppearanceSettingsPanel.Children.Clear();
        RegisterSettingsPage("AppearanceTile", AppearanceSettingsPanel, tile, "磁贴", "调整磁贴标题和背景样式。");
        RegisterSettingsPage("AppearanceBackground", AppearanceSettingsPanel, backgrounds, "背景板", "设置跨区、时钟与作业板背景。");
        RegisterSettingsPage("AppearanceTheme", AppearanceSettingsPanel, ThemeSettingsCard, "主题", "调整界面主题和看板色系。");
        RegisterSettingsPage("AppearanceGrid", AppearanceSettingsPanel, grid, "网格", "设置网格的显示样式，不改变吸附逻辑。");
        RegisterSettingsPage("AppearanceToolbar", AppearanceSettingsPanel, ToolbarSettings(), "控制窗", "调整控制窗的显示、位置和外观。");
        ComponentSettingsPanel.Children.Clear();
        ((StackPanel)NoiseSettingsCard.Child).Children.Insert(2,
            Toggle("最小化时暂停监测", _settings.PauseNoiseWhenMinimized, value =>
            { _settings.PauseNoiseWhenMinimized = value; RefreshNoiseSuspension(); }));
        RegisterSettingsPage("ComponentsWeather", ComponentSettingsPanel, WeatherSettingsCard, "天气", "选择天气地区并查看预警。");
        RegisterSettingsPage("ComponentsNoise", ComponentSettingsPanel, NoiseSettingsCard, "噪音检测", "设置麦克风检测、报警与校准。");
        var versionContent = (StackPanel)VersionSettingsCard.Child;
        versionContent.Children.Remove(RepositorySettingsCard);
        RepositorySettingsCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RepositorySettingsCard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RepositorySettingsCard.ColumnDefinitions.Clear();
        RepositorySettingsCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var githubLink = RepositorySettingsCard.Children.OfType<HyperlinkButton>().Single();
        Grid.SetColumn(githubLink, 0); Grid.SetRow(githubLink, 1);
        githubLink.Margin = new Thickness(0, 12, 0, 0);
        StackPanel repositories = SettingsStack();
        repositories.Children.Add(RepositorySettingsCard);
        HyperlinkButton gitee = new()
        {
            NavigateUri = new Uri("https://gitee.com/EdgeHH/pancake/"),
            Background = (Brush)Application.Current.Resources["BoardSurfaceSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8)
        };
        StackPanel link = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        link.Children.Add(new Viewbox { Width = 24, Height = 24, Child = new Microsoft.UI.Xaml.Shapes.Path
        {
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 199, 29, 35)),
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                "M11.984 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.016 0zm6.09 5.333c.328 0 .593.266.592.593v1.482a.594.594 0 0 1-.593.592H9.777c-.982 0-1.778.796-1.778 1.778v5.63c0 .327.266.592.593.592h5.63c.982 0 1.778-.796 1.778-1.778v-.296a.593.593 0 0 0-.592-.593h-4.15a.592.592 0 0 1-.592-.592v-1.482a.593.593 0 0 1 .593-.592h6.815c.327 0 .593.265.593.592v3.408a4 4 0 0 1-4 4H5.926a.593.593 0 0 1-.593-.593V9.778a4.444 4.444 0 0 1 4.445-4.444h8.296Z"),
            Width = 24, Height = 24
        } });
        StackPanel giteeText = new();
        giteeText.Children.Add(new TextBlock { Text = "EdgeHH/pancake", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        giteeText.Children.Add(new TextBlock { Text = "在 Gitee 中打开", FontSize = 12 });
        link.Children.Add(giteeText);
        gitee.Content = link;
        repositories.Children.Add(gitee);
        var updateCard = AboutSettingsPanel.Children.Last();
        AboutSettingsPanel.Children.Clear();
        StackPanel about = SettingsStack();
        about.Children.Add(VersionSettingsCard);
        about.Children.Add(repositories);
        about.Children.Add(updateCard);
        RegisterSettingsPage("About", AboutSettingsPanel, about, "关于", "查看版本、访问仓库与检查更新。");
        UpdateSourceComboBox.SelectedIndex = _settings.UpdateSource == "Gitee" ? 1 : 0;
        UpdateSourceComboBox.SelectionChanged += async (_, _) =>
        {
            _settings.UpdateSource = UpdateSourceComboBox.SelectedIndex == 1 ? "Gitee" : "GitHub";
            ScheduleSave();
            await RefreshUpdateSourceHintAsync();
        };
        UpdateSourceHint.Text = "Gitee 同步可能延迟；选用 Gitee 时会比较 GitHub 最新版本并提醒。";
        foreach (var button in ToolbarItems.Children.OfType<ButtonBase>())
        {
            if (button.Content is not UIElement icon) continue;
            button.Content = null;
            TextBlock label = new() { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            StackPanel content = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(icon); content.Children.Add(label); button.Content = content;
            _toolbarLabels[button] = (icon, label);
        }
        ShowSettingsPage("AppearanceTile");
        ApplyExtendedSettings();
    }

    private static StackPanel SettingsStack() => new() { Spacing = 18, Margin = new Thickness(0, 16, 0, 0) };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 22 };

    private static CornerRadius MatchToolbarButtonRadius(double toolbarRadius, double toolbarPadding, double width, double height)
    {
        // 按钮底色位于悬浮窗内侧，圆角需扣除内边距并受按钮短边限制，避免悬停色块被外框裁切。
        double radius = Math.Clamp(toolbarRadius - toolbarPadding, 0, Math.Min(width, height) / 2);
        return new CornerRadius(radius);
    }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = .75 };

    private Button ColorSetting(string label, string value, Action<string> update)
    {
        ColorPicker picker = new() { IsAlphaEnabled = true, IsHexInputVisible = true,
            Color = GridAppearance.ParseColor(value, Windows.UI.Color.FromArgb(105, 86, 86, 92)) };
        Button button = new() { Content = label, Flyout = new Flyout { Content = picker } };
        picker.ColorChanged += (_, args) =>
        {
            update(GridAppearance.FormatColor(args.NewColor));
            _renderedGridAppearance = string.Empty;
            SettingChanged();
        };
        return button;
    }

    private void RegisterSettingsPage(string tag, StackPanel container, UIElement content, string title, string description)
    {
        content.Visibility = Visibility.Collapsed;
        container.Children.Add(content);
        _settingsPages[tag] = new SettingsPage(container, content, title, description);
    }

    private void ShowSettingsPage(string tag)
    {
        if (!_settingsPages.TryGetValue(tag, out SettingsPage? selected)) return;
        _selectedSettingsPage = tag;
        foreach (StackPanel container in _settingsPages.Values.Select(page => page.Container).Distinct())
            container.Visibility = Visibility.Collapsed;
        foreach (SettingsPage page in _settingsPages.Values)
            page.Content.Visibility = Visibility.Collapsed;

        selected.Container.Visibility = Visibility.Visible;
        selected.Content.Visibility = Visibility.Visible;
        SettingsPageTitle.Text = selected.Title;
        SettingsPageDescription.Text = selected.Description;
        SettingsContentScrollViewer.ChangeView(null, 0, null, true);
        RefreshAppearancePreviews();
    }

    private void SettingChanged()
    {
        ApplyExtendedSettings();
        ScheduleSave();
    }

    private ToggleSwitch Toggle(string label, bool value, Action<bool> update)
    {
        ToggleSwitch toggle = new() { Header = label, IsOn = value };
        toggle.Toggled += (_, _) => { update(toggle.IsOn); SettingChanged(); };
        return toggle;
    }

    private void TileTitleSizeChanged()
    {
        // 连续拖动不走全窗口刷新，保留预览编辑器以及所有背景资源。
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>())
            tile.ApplyTitleSize(_settings.TileTitleSize);
        _tileAppearancePreview?.ApplyTitleSize(_settings.TileTitleSize);
        ScheduleSave();
    }

    private Slider Range(string label, double min, double max, double value, Action<double> update, Action? changed = null)
    {
        Slider slider = new() { Header = label, Minimum = min, Maximum = max, Value = Math.Clamp(double.IsFinite(value) ? value : min, min, max), StepFrequency = .01 };
        slider.ValueChanged += (_, args) => { update(args.NewValue); (changed ?? SettingChanged)(); };
        return slider;
    }

    private ComboBox Choice(string label, string[] labels, string[] values, string value, Action<string> update)
    {
        ComboBox combo = new() { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < labels.Length; i++) combo.Items.Add(new ComboBoxItem { Content = labels[i], Tag = values[i] });
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, value));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem item) { update((string)item.Tag); SettingChanged(); } };
        return combo;
    }

    private StackPanel SurfaceColorEditor(string color, double opacity, Func<bool> cleared, Action<string, bool> updateColor, Action<double> updateOpacity)
    {
        StackPanel panel = SettingsStack();
        ColorPicker picker = new() { IsAlphaEnabled = false, IsHexInputVisible = true,
            Color = GridAppearance.ParseColor(color, BoardTheme.SurfaceBrush.Color) };
        // 显式应用允许清除后重新选择同一种颜色，无需依赖 ColorChanged。
        StackPanel flyoutContent = SettingsStack();
        flyoutContent.Children.Add(picker);
        Button apply = new() { Content = "应用颜色" };
        Flyout flyout = new() { Content = flyoutContent };
        void ApplyColor()
        {
            updateColor($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}", false);
            SettingChanged();
        }
        picker.ColorChanged += (_, _) => ApplyColor();
        apply.Click += (_, _) => { ApplyColor(); flyout.Hide(); };
        flyoutContent.Children.Add(apply);
        panel.Children.Add(new Button { Content = "背景颜色", Flyout = flyout });
        Slider transparency = Range("背景颜色透明度（%）", 0, 100, (1 - opacity) * 100,
            value => updateOpacity(1 - value / 100));
        transparency.StepFrequency = 1;
        panel.Children.Add(transparency);
        Button clear = new() { Content = "清除背景颜色" };
        clear.Click += (_, _) => { updateColor("", true); SettingChanged(); };
        panel.Children.Add(clear);
        Button reset = new() { Content = "恢复主题背景色" };
        reset.Click += (_, _) => { updateColor("", false); SettingChanged(); };
        panel.Children.Add(reset);
        panel.Children.Add(Note("透明度只影响背景颜色：0% 不透明，100% 完全透明。清除颜色会保留已开启的毛玻璃效果和背景媒体。"));
        _refreshSettingAvailability.Add(() => { transparency.IsEnabled = !cleared(); clear.IsEnabled = !cleared(); });
        return panel;
    }

    private StackPanel BackgroundEditor(BackgroundSettings style, Func<bool> allowImage, Func<bool> allowGlass, bool includeGlass = true, bool surface = false)
    {
        StackPanel panel = SettingsStack(), imageControls = SettingsStack();
        ColorPicker color = new() { IsAlphaEnabled = false, IsHexInputVisible = true };
        try { color.Color = ViewModels.MainViewModel.BrushFromHex(string.IsNullOrEmpty(style.Color) ? "#202024" : style.Color).Color; } catch { }
        color.ColorChanged += (_, args) => { style.Color = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; SettingChanged(); };
        if (surface)
            panel.Children.Add(SurfaceColorEditor(style.Color, style.ColorOpacity, () => style.ColorCleared,
                (value, cleared) => { style.Color = value; style.ColorCleared = cleared; }, value => style.ColorOpacity = value));
        else
            imageControls.Children.Add(new Button { Content = "背景颜色", Flyout = new Flyout { Content = color } });
        imageControls.Children.Add(new ContentControl { Content = CreateBackgroundMediaEditor(style), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        imageControls.Children.Add(Choice("图片模式", ["缩放", "拉伸", "适应"], ["Zoom", "Stretch", "Fit"], style.ImageMode, value => style.ImageMode = value));
        Button clear = new() { Content = "清除背景媒体" };
        clear.Click += (_, _) => { style.ImagePath = ""; style.Playlist.Clear(); SettingChanged(); };
        imageControls.Children.Add(clear);
        Button reset = new() { Content = "恢复主题背景色" };
        reset.Click += (_, _) => { style.Color = ""; SettingChanged(); };
        if (!surface) imageControls.Children.Add(reset);
        panel.Children.Add(imageControls);
        _refreshSettingAvailability.Add(() =>
        {
            foreach (Control control in imageControls.Children.OfType<Control>()) control.IsEnabled = allowImage();
            imageControls.Opacity = allowImage() ? 1 : .5;
        });
        if (includeGlass)
        {
            var glass = Toggle("毛玻璃效果", style.Glass, value => style.Glass = value);
            var blur = Range("模糊程度", 0, 100, style.Blur, value => style.Blur = value);
            panel.Children.Add(glass); panel.Children.Add(blur);
            _refreshSettingAvailability.Add(() => { glass.IsEnabled = allowGlass(); blur.IsEnabled = allowGlass() && style.Glass; });
        }
        return panel;
    }

    private StackPanel ToolbarSettings()
    {
        StackPanel panel = SettingsStack();
        panel.Children.Add(CreateToolbarPositionPicker());
        panel.Children.Add(CreateAppearancePreview("Toolbar"));
        panel.Children.Add(SurfaceColorEditor(_settings.ToolbarBackgroundColor, _settings.ToolbarBackgroundOpacity,
            () => _settings.ToolbarBackgroundColorCleared,
            (value, cleared) => { _settings.ToolbarBackgroundColor = value; _settings.ToolbarBackgroundColorCleared = cleared; },
            value => _settings.ToolbarBackgroundOpacity = value));
        panel.Children.Add(Toggle("自动隐藏", _settings.ToolbarAutoHide, value => _settings.ToolbarAutoHide = value));
        NumberBox hideTime = new() { Header = "自动隐藏时间（秒）", Minimum = 1, Maximum = 600,
            Value = ToolbarAutoHidePolicy.NormalizeDelay(_settings.ToolbarAutoHideSeconds),
            SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        hideTime.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue)) return;
            _settings.ToolbarAutoHideSeconds = ToolbarAutoHidePolicy.NormalizeDelay(args.NewValue);
            SettingChanged();
        };
        panel.Children.Add(hideTime);
        var animation = Choice("隐藏动画", ["渐入渐出", "飞出"], ["Fade", "Fly"],
            _settings.ToolbarHideAnimation, value => _settings.ToolbarHideAnimation = value);
        panel.Children.Add(animation);
        panel.Children.Add(Note("仅在查看模式下无操作时隐藏；移动鼠标、触摸或按键后重新显示。飞出动画朝最近的窗口边框移动。"));
        _refreshSettingAvailability.Add(() => hideTime.IsEnabled = animation.IsEnabled = _settings.ToolbarAutoHide);
        panel.Children.Add(Toggle("无字模式", _settings.ToolbarIconOnly, value => _settings.ToolbarIconOnly = value));
        panel.Children.Add(Range("大小", .6, 2, _settings.ToolbarScale, value => _settings.ToolbarScale = value));
        panel.Children.Add(Range("圆角", 0, 60, _settings.ToolbarRadius, value => _settings.ToolbarRadius = value));
        var horizontal = Range("距左右边框", 0, 200, _settings.ToolbarHorizontalInset, value => _settings.ToolbarHorizontalInset = value);
        var vertical = Range("距上下边框", 0, 200, _settings.ToolbarVerticalInset, value => _settings.ToolbarVerticalInset = value);
        panel.Children.Add(horizontal); panel.Children.Add(vertical);
        panel.Children.Add(Toggle("毛玻璃效果", _settings.ToolbarGlass, value => _settings.ToolbarGlass = value));
        var blur = Range("模糊程度", 0, 100, _settings.ToolbarBlur, value => _settings.ToolbarBlur = value); panel.Children.Add(blur);
        _refreshSettingAvailability.Add(() =>
        {
            horizontal.Visibility = _settings.ToolbarPosition.EndsWith("Center") ? Visibility.Collapsed : Visibility.Visible;
            vertical.Visibility = _settings.ToolbarPosition.StartsWith("Center") ? Visibility.Collapsed : Visibility.Visible;
            blur.IsEnabled = _settings.ToolbarGlass;
        });
        return panel;
    }

    private Brush CreateToolbarBackground() => SurfaceBackground.Create(
        _settings.ToolbarBackgroundColor, _settings.ToolbarBackgroundOpacity,
        _settings.ToolbarBackgroundColorCleared, _settings.ToolbarGlass, _settings.ToolbarBlur);

    private bool UseSharedBackground => _settings.LayoutMode == "Split" && _settings.SharedBackgroundEnabled;
    private void ApplyExtendedSettings()
    {
        if (SharedBackgroundVisual is null) return;
        foreach (Action refresh in _refreshSettingAvailability) refresh();
        ApplyDisplayLayout();
        SharedBackgroundVisual.Apply(UseSharedBackground ? _settings.SharedBackground : new(), BoardTheme.SurfaceBrush);
        ClockPanel.Background = BoardWorkspace.Background = null;
        ClockBackgroundVisual.Apply(_settings.LayoutMode == "Free" ? new() : _settings.ClockBackground, BoardTheme.SurfaceBrush, UseSharedBackground);
        BoardBackgroundVisual.Apply(_settings.LayoutMode == "Free" ? new() : _settings.BoardBackground, BoardTheme.SurfaceBrush, UseSharedBackground);
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.ApplyAppearance(_settings);
        BoardScroller.ZoomMode = _settings.InfiniteBoard ? ZoomMode.Enabled : ZoomMode.Disabled;
        BoardScroller.HorizontalScrollMode = BoardScroller.VerticalScrollMode = _settings.InfiniteBoard ? ScrollMode.Enabled : ScrollMode.Disabled;
        BoardScroller.HorizontalScrollBarVisibility = BoardScroller.VerticalScrollBarVisibility = _settings.InfiniteBoard ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        UpdateBoardBounds(); ApplyToolbarSettings();
        RecordToolbarActivity(false);
        RefreshAppearancePreviews();
        if (!_settings.InfiniteBoard)
        {
            // ScrollViewer 在内容尺寸变化的同一轮布局会丢弃 ChangeView；新尺寸提交后再复位。
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!_settings.InfiniteBoard) BoardScroller.ChangeView(0, 0, 1, true);
            });
        }
    }

    private void ApplyToolbarSettings()
    {
        if (FloatingToolbar is null) return;
        string position = _settings.ToolbarPosition;
        bool vertical = position.StartsWith("Center");
        ToolbarItems.Orientation = GlobalInkTools.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        _inkColors.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        MainToolbarScroll.HorizontalScrollBarVisibility = InkToolbarScroll.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        MainToolbarScroll.VerticalScrollBarVisibility = InkToolbarScroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double toolbarPadding = 7 * scale;
        foreach (var (button, pair) in _toolbarLabels)
        {
            bool showFullScreenHint = ReferenceEquals(button, FullScreenButton) && _showFullScreenExitHint;
            // 已显示文字时，滑动提示只变更背景色，不再改变按钮的排版、字号或尺寸。
            bool expandFullScreenHint = showFullScreenHint && _settings.ToolbarIconOnly;
            pair.Label.Text = AutomationProperties.GetName(button);
            pair.Label.Visibility = !_settings.ToolbarIconOnly || showFullScreenHint ? Visibility.Visible : Visibility.Collapsed;
            var content = (StackPanel)button.Content;
            // 无字模式下临时提示沿工具栏轴向扩展；常规文字始终横排在图标下方。
            content.Orientation = expandFullScreenHint
                ? vertical ? Orientation.Vertical : Orientation.Horizontal
                : Orientation.Vertical;
            content.Spacing = (vertical && !expandFullScreenHint ? 2 : 4) * scale;
            bool horizontalFullScreenHint = expandFullScreenHint && !vertical;
            if (pair.Icon is FrameworkElement iconElement)
                iconElement.VerticalAlignment = horizontalFullScreenHint ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            pair.Label.VerticalAlignment = horizontalFullScreenHint ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            // 竖版统一把较小的标签放在图标下方，避免文字从侧面撑宽悬浮窗。
            bool labelFirst = !expandFullScreenHint && !vertical && position.StartsWith("Top");
            content.Children.Clear();
            if (labelFirst) content.Children.Add(pair.Label);
            content.Children.Add(pair.Icon);
            if (!labelFirst) content.Children.Add(pair.Label);
            button.Width = expandFullScreenHint && !vertical
                ? double.NaN
                : (_settings.ToolbarIconOnly ? 44 : vertical ? 64 : 88) * scale;
            button.Height = expandFullScreenHint && vertical ? double.NaN : (_settings.ToolbarIconOnly ? 44 : 64) * scale;
            button.MinWidth = button.MinHeight = expandFullScreenHint ? 44 * scale : 0;
            button.Padding = new Thickness(6 * scale);
            pair.Label.FontSize = (vertical ? 8 : 10) * scale;
            if (pair.Icon is FluentIcon glyph) glyph.FontSize = 18 * scale;
            double buttonWidth = double.IsNaN(button.Width) ? 88 * scale : button.Width;
            double buttonHeight = double.IsNaN(button.Height) ? 64 * scale : button.Height;
            button.CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, toolbarPadding, buttonWidth, buttonHeight);
        }
        foreach (var button in ToolbarItems.Children.OfType<ButtonBase>().Where(button => !_toolbarLabels.ContainsKey(button)))
            button.CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, toolbarPadding, 44, 44);
        ToolbarItems.Spacing = 4 * scale;
        FullScreenIcon.FontSize = 18 * scale;
        foreach (var button in GlobalInkTools.Children.OfType<ButtonBase>())
        {
            button.Width = button.Height = 40 * scale;
            if (button.Content is FluentIcon glyph) glyph.FontSize = 20 * scale;
            if (button.Content is Viewbox { Child: IconSourceElement } vectorIcon)
                vectorIcon.Width = vectorIcon.Height = 20 * scale;
        }
        foreach (var swatch in _inkColors.Children.OfType<ColorSwatchButton>()) swatch.Width = swatch.Height = 32 * scale;
        foreach (var slider in GlobalInkTools.Children.OfType<Slider>())
        {
            slider.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            slider.Width = (vertical ? 40 : 110) * scale;
            slider.Height = (vertical ? 110 : 40) * scale;
        }
        FloatingToolbar.CornerRadius = GlobalInkToolbar.CornerRadius = new CornerRadius(_settings.ToolbarRadius);
        FloatingToolbar.Padding = new Thickness(toolbarPadding);
        GlobalInkToolbar.Padding = new Thickness(12 * scale);
        FloatingToolbar.Background = CreateToolbarBackground();
        GlobalInkToolbar.Background = CreateToolbarBackground();
        FloatingToolbar.HorizontalAlignment = GlobalInkToolbar.HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        FloatingToolbar.VerticalAlignment = GlobalInkToolbar.VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        double x = _settings.ToolbarHorizontalInset, y = _settings.ToolbarVerticalInset;
        FloatingToolbar.Margin = new Thickness(x, y, x, y);
        double offset = ((_settings.ToolbarIconOnly ? 44 : 64) + 14) * scale + 12;
        GlobalInkToolbar.Margin = vertical
            ? new Thickness(x + (position.EndsWith("Left") ? offset : 0), y, x + (position.EndsWith("Right") ? offset : 0), y)
            : new Thickness(x, y + (position.StartsWith("Top") ? offset : 0), x, y + (position.StartsWith("Bottom") ? offset : 0));
        FloatingToolbar.MaxWidth = GlobalInkToolbar.MaxWidth = Math.Max(120, RootShell.ActualWidth - x * 2);
        FloatingToolbar.MaxHeight = Math.Max(80, RootShell.ActualHeight - y * 2);
        GlobalInkToolbar.MaxHeight = Math.Max(80, RootShell.ActualHeight - GlobalInkToolbar.Margin.Top - GlobalInkToolbar.Margin.Bottom);
        GlobalInkToolbar.MaxWidth = Math.Max(80, RootShell.ActualWidth - GlobalInkToolbar.Margin.Left - GlobalInkToolbar.Margin.Right);
    }
}
