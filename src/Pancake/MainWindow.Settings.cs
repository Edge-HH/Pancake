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
        layout.Children.Add(Note("分屏模式可拖动分隔条调整比例，拖至两端切换为单区；自由布局中在编辑模式拖动组件上边缘、右下角调整位置与大小。"));
        LayoutSettingsPanel.Children.Clear();
        RegisterSettingsPage("Layout", LayoutSettingsPanel, layout, "布局", "调整布局模式、无限作业板与网格。");

        StackPanel tile = SettingsStack();
        tile.Children.Add(Range("标题大小", 16, 72, _settings.TileTitleSize, value => _settings.TileTitleSize = value));
        tile.Children.Add(BackgroundEditor(_settings.TileBackground, () => true, () => true));
        StackPanel backgrounds = SettingsStack();
        var shared = Toggle("使用跨区背景", _settings.SharedBackgroundEnabled, value => _settings.SharedBackgroundEnabled = value);
        backgrounds.Children.Add(shared);
        _refreshSettingAvailability.Add(() => shared.IsEnabled = _settings.LayoutMode == "Split");
        backgrounds.Children.Add(BackgroundEditor(_settings.SharedBackground, () => _settings.LayoutMode == "Split" && _settings.SharedBackgroundEnabled, () => false, false));
        backgrounds.Children.Add(Heading("时钟区域背景"));
        backgrounds.Children.Add(BackgroundEditor(_settings.ClockBackground,
            () => _settings.LayoutMode is "Split" or "Clock" && !UseSharedBackground,
            () => _settings.LayoutMode is "Split" or "Clock"));
        backgrounds.Children.Add(Heading("作业板区域背景"));
        backgrounds.Children.Add(BackgroundEditor(_settings.BoardBackground,
            () => _settings.LayoutMode is "Split" or "Board" && !UseSharedBackground,
            () => _settings.LayoutMode is "Split" or "Board"));
        backgrounds.Children.Add(Note("跨区背景仅在分屏生效，开启后区域底图由跨区背景统一管理，毛玻璃仍可分别调整。自由布局使用磁贴样式和主题底色。"));
        AppearanceSettingsPanel.Children.Clear();
        RegisterSettingsPage("AppearanceTile", AppearanceSettingsPanel, tile, "磁贴", "调整磁贴标题和背景样式。");
        RegisterSettingsPage("AppearanceBackground", AppearanceSettingsPanel, backgrounds, "背景板", "设置跨区、时钟与作业板背景。");
        RegisterSettingsPage("AppearanceTheme", AppearanceSettingsPanel, ThemeSettingsCard, "主题", "调整界面主题和看板色系。");
        RegisterSettingsPage("AppearanceToolbar", AppearanceSettingsPanel, ToolbarSettings(), "控制窗", "调整控制窗的显示、位置和外观。");
        ComponentSettingsPanel.Children.Clear();
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
        HyperlinkButton gitee = new() { NavigateUri = new Uri("https://gitee.com/EdgeHH/pancake/"), Padding = new Thickness(14, 8, 14, 8) };
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
        RegisterSettingsPage("AboutRepositories", AboutSettingsPanel, repositories, "仓库", "访问 Pancake 的代码仓库。");
        RegisterSettingsPage("AboutVersion", AboutSettingsPanel, VersionSettingsCard, "版本", "查看当前 Pancake 版本信息。");
        RegisterSettingsPage("AboutUpdate", AboutSettingsPanel, updateCard, "更新", "检查更新并选择更新来源。");
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
        ShowSettingsPage((SettingsRoot.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "AppearanceTile");
        ApplyExtendedSettings();
    }

    private static StackPanel SettingsStack() => new() { Spacing = 18, Margin = new Thickness(0, 16, 0, 0) };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 22 };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = .75 };

    private void RegisterSettingsPage(string tag, StackPanel container, UIElement content, string title, string description)
    {
        content.Visibility = Visibility.Collapsed;
        container.Children.Add(content);
        _settingsPages[tag] = new SettingsPage(container, content, title, description);
    }

    private void ShowSettingsPage(string tag)
    {
        if (!_settingsPages.TryGetValue(tag, out SettingsPage? selected)) return;
        foreach (StackPanel container in _settingsPages.Values.Select(page => page.Container).Distinct())
            container.Visibility = Visibility.Collapsed;
        foreach (SettingsPage page in _settingsPages.Values)
            page.Content.Visibility = Visibility.Collapsed;

        selected.Container.Visibility = Visibility.Visible;
        selected.Content.Visibility = Visibility.Visible;
        SettingsPageTitle.Text = selected.Title;
        SettingsPageDescription.Text = selected.Description;
        SettingsContentScrollViewer.ChangeView(null, 0, null, true);
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

    private Slider Range(string label, double min, double max, double value, Action<double> update)
    {
        Slider slider = new() { Header = label, Minimum = min, Maximum = max, Value = Math.Clamp(double.IsFinite(value) ? value : min, min, max), StepFrequency = .01 };
        slider.ValueChanged += (_, args) => { update(args.NewValue); SettingChanged(); };
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

    private StackPanel BackgroundEditor(BackgroundSettings style, Func<bool> allowImage, Func<bool> allowGlass, bool includeGlass = true)
    {
        StackPanel panel = SettingsStack(), imageControls = SettingsStack();
        ColorPicker color = new() { IsAlphaEnabled = false, IsHexInputVisible = true };
        try { color.Color = ViewModels.MainViewModel.BrushFromHex(string.IsNullOrEmpty(style.Color) ? "#202024" : style.Color).Color; } catch { }
        color.ColorChanged += (_, args) => { style.Color = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; SettingChanged(); };
        imageControls.Children.Add(new Button { Content = "背景颜色", Flyout = new Flyout { Content = color } });
        Button pick = new() { Content = "选择背景图片" };
        pick.Click += async (_, _) =>
        {
            try
            {
                FileOpenPicker picker = new();
                foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }) picker.FileTypeFilter.Add(ext);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                string directory = Path.Combine(_dataStore.DataDirectory, "backgrounds"); Directory.CreateDirectory(directory);
                string owned = Path.Combine(directory, Guid.NewGuid().ToString("N") + file.FileType);
                File.Copy(file.Path, owned); style.ImagePath = owned; SettingChanged();
            }
            catch (Exception ex) { await ShowMessageAsync("无法设置背景", ex.Message, "知道了"); }
        };
        imageControls.Children.Add(pick);
        imageControls.Children.Add(Choice("图片模式", ["缩放", "拉伸", "适应"], ["Zoom", "Stretch", "Fit"], style.ImageMode, value => style.ImageMode = value));
        Button clear = new() { Content = "清除图片" };
        clear.Click += (_, _) => { style.ImagePath = ""; SettingChanged(); };
        imageControls.Children.Add(clear);
        Button reset = new() { Content = "恢复主题背景色" };
        reset.Click += (_, _) => { style.Color = ""; SettingChanged(); };
        imageControls.Children.Add(reset);
        panel.Children.Add(imageControls);
        _refreshSettingAvailability.Add(() => { foreach (Control control in imageControls.Children.OfType<Control>()) control.IsEnabled = allowImage(); });
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
        panel.Children.Add(Toggle("无字模式", _settings.ToolbarIconOnly, value => _settings.ToolbarIconOnly = value));
        panel.Children.Add(Choice("位置", ["左下", "居中下", "右下", "上居中", "左上", "右上", "左居中（竖置）", "右居中（竖置）"],
            ["BottomLeft", "BottomCenter", "BottomRight", "TopCenter", "TopLeft", "TopRight", "CenterLeft", "CenterRight"],
            _settings.ToolbarPosition, value => _settings.ToolbarPosition = value));
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
        foreach (var (button, pair) in _toolbarLabels)
        {
            pair.Label.Text = AutomationProperties.GetName(button);
            pair.Label.Visibility = _settings.ToolbarIconOnly ? Visibility.Collapsed : Visibility.Visible;
            var content = (StackPanel)button.Content;
            content.Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical;
            bool labelFirst = position.StartsWith("Top") || position == "CenterLeft";
            content.Children.Clear();
            if (labelFirst) content.Children.Add(pair.Label);
            content.Children.Add(pair.Icon);
            if (!labelFirst) content.Children.Add(pair.Label);
            button.Width = (_settings.ToolbarIconOnly ? 44 : 88) * scale;
            button.Height = (_settings.ToolbarIconOnly ? 44 : 64) * scale;
            button.Padding = new Thickness(6 * scale);
            pair.Label.FontSize = 10 * scale;
            if (pair.Icon is FluentIcon glyph) glyph.FontSize = 18 * scale;
        }
        ToolbarItems.Spacing = 4 * scale;
        FullScreenIcon.FontSize = 18 * scale;
        foreach (var button in GlobalInkTools.Children.OfType<ButtonBase>())
        {
            button.Width = button.Height = 40 * scale;
            if (button.Content is FluentIcon glyph) glyph.FontSize = 20 * scale;
        }
        foreach (var swatch in _inkColors.Children.OfType<ColorSwatchButton>()) swatch.Width = swatch.Height = 32 * scale;
        foreach (var slider in GlobalInkTools.Children.OfType<Slider>()) slider.Width = 110 * scale;
        FloatingToolbar.CornerRadius = GlobalInkToolbar.CornerRadius = new CornerRadius(_settings.ToolbarRadius);
        FloatingToolbar.Padding = new Thickness(7 * scale);
        GlobalInkToolbar.Padding = new Thickness(12 * scale);
        FloatingToolbar.Background = _settings.ToolbarGlass ? new BlurBackdropBrush(_settings.ToolbarBlur) : BoardTheme.SurfaceBrush;
        GlobalInkToolbar.Background = _settings.ToolbarGlass ? new BlurBackdropBrush(_settings.ToolbarBlur) : BoardTheme.SurfaceBrush;
        FloatingToolbar.HorizontalAlignment = GlobalInkToolbar.HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        FloatingToolbar.VerticalAlignment = GlobalInkToolbar.VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        double x = _settings.ToolbarHorizontalInset, y = _settings.ToolbarVerticalInset;
        FloatingToolbar.Margin = new Thickness(x, y, x, y);
        double offset = ((_settings.ToolbarIconOnly ? 44 : vertical ? 88 : 64) + 14) * scale + 12;
        GlobalInkToolbar.Margin = vertical
            ? new Thickness(x + (position.EndsWith("Left") ? offset : 0), y, x + (position.EndsWith("Right") ? offset : 0), y)
            : new Thickness(x, y + (position.StartsWith("Top") ? offset : 0), x, y + (position.StartsWith("Bottom") ? offset : 0));
        FloatingToolbar.MaxWidth = GlobalInkToolbar.MaxWidth = Math.Max(120, RootShell.ActualWidth - x * 2);
        FloatingToolbar.MaxHeight = Math.Max(80, RootShell.ActualHeight - y * 2);
        GlobalInkToolbar.MaxHeight = Math.Max(80, RootShell.ActualHeight - GlobalInkToolbar.Margin.Top - GlobalInkToolbar.Margin.Bottom);
        GlobalInkToolbar.MaxWidth = Math.Max(80, RootShell.ActualWidth - GlobalInkToolbar.Margin.Left - GlobalInkToolbar.Margin.Right);
    }
}
