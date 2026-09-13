using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;
using Pancake.ViewModels;
using Windows.Storage.Pickers;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly List<Action> _refreshSettingAvailability = [];
    private readonly Dictionary<ButtonBase, (UIElement Icon, TextBlock Label)> _toolbarLabels = [];
    private readonly Dictionary<string, SettingsPage> _settingsPages = [];
    private ComboBox? _layoutChoice;
    private Slider? _gridSizeSlider;
    private Slider? _autoLayoutGapSlider;
    private ToggleSwitch? _gridSnapToggle;
    private Action? _refreshHomeworkAutofill;
    private Action? _refreshSubjectAutofill;

    private sealed record SettingsPage(StackPanel Container, UIElement Content, string Title, string Description);

    private void InitializeExtendedSettings()
    {
        StackPanel layout = SettingsStack();
        _layoutChoice = Choice("布局模式", ["分屏", "仅作业", "仅时钟", "自由布局"],
            ["Split", "Board", "Clock", "Free"], _settings.LayoutMode, value => _settings.LayoutMode = value);
        layout.Children.Add(_layoutChoice);
        layout.Children.Add(Toggle("无限作业板", _settings.InfiniteBoard, value => _settings.InfiniteBoard = value));
        layout.Children.Add(Note("开启后可滚动、触摸平移、Ctrl + 滚轮缩放；编辑磁贴时可用滚动条平移。"));
        // 网格大小与自动布局共用同一张预览，改任一项都能立刻看到结果。
        layout.Children.Add(CreateAppearancePreview("Layout"));
        _gridSizeSlider = Range("网格大小", 16, 160, Math.Round(_settings.GridSize), value =>
        { _settings.GridSize = Math.Round(value); _renderedGridWidth = 0; }, GridSizeChanged);
        // 网格大小按整数调节，避免出现 48.37 px 这类没有意义的取值。
        _gridSizeSlider.StepFrequency = 1;
        _gridSizeSlider.SmallChange = 1;
        _gridSizeSlider.LargeChange = 8;
        layout.Children.Add(_gridSizeSlider);
        layout.Children.Add(Note("分屏模式可拖动分隔条调整比例，拖至两端切换为单区；仅作业与仅时钟会把分隔条留在屏幕边缘，往回拖动即可恢复分屏；进入编辑模式后可拖动时钟或组件的任意位置，悬停时使用右下角灰色小框缩放；仅时钟模式没有作业板，编辑工具栏只保留画笔，画笔可以写在屏幕任意位置。"));
        LayoutSettingsPanel.Children.Clear();
        layout.Children.Add(Heading("自动布局"));
        // 与编辑工具栏的吸附按钮是同一个设置，两边状态实时同步。
        _gridSnapToggle = Toggle("吸附到网格", _settings.GridSnappingEnabled, SetGridSnapping);
        layout.Children.Add(_gridSnapToggle);
        _autoLayoutGapSlider = Range("自动排版磁贴间隔", 0, 120, _settings.AutoLayoutGap,
            value => _settings.AutoLayoutGap = value, AutoLayoutGapChanged);
        layout.Children.Add(_autoLayoutGapSlider);
        layout.Children.Add(Toggle("自动对齐", _settings.AutoLayoutAlign, value => _settings.AutoLayoutAlign = value));
        layout.Children.Add(Toggle("自动调整磁贴大小", _settings.AutoLayoutResize, value => _settings.AutoLayoutResize = value));
        layout.Children.Add(Note("点击自动排列时按文字、图片和笔迹收紧磁贴；自动对齐会尽量统一相近尺寸并对齐行列。看板与图片导出共用这些设置，优先从左上角排列。"));
        _refreshSettingAvailability.Add(UpdateAutoLayoutGapStep);
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
        AutofillSettingsPanel.Children.Clear();
        RegisterSettingsPage("AutofillSubject", AutofillSettingsPanel, SubjectAutofillSettings(), "学科补全", "设置可补全的学科、默认颜色与匹配严格度。");
        RegisterSettingsPage("AutofillHomework", AutofillSettingsPanel, HomeworkAutofillSettings(), "作业补全", "记录常输入的作业名称，并按学科范围提示补全。");
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

    /// <summary>自动填充 / 学科补全：总开关、匹配程度，以及逐项开关与默认颜色的学科列表。</summary>
    private StackPanel SubjectAutofillSettings()
    {
        StackPanel page = SettingsStack();
        page.Children.Add(Toggle("学科补全", _settings.Autofill.Subject.Enabled, value => _settings.Autofill.Subject.Enabled = value));
        page.Children.Add(Choice("匹配程度", ["宽松", "正常", "严格"], ["Loose", "Normal", "Strict"],
            _settings.Autofill.Subject.MatchLevel, value => _settings.Autofill.Subject.MatchLevel = value));
        page.Children.Add(Note("开启后，在磁贴标题里输入中文、全拼或首字母会弹出候选，上下键切换、Tab 或回车采纳，也可以直接点击；采纳候选会把标题与磁贴主题色切换为该学科的默认颜色。"));
        page.Children.Add(Note("宽松：输入 1 个字符即提示，首字母允许跳字；正常：汉字 1 个或字母 2 个起按前缀匹配；严格：2 个字符起，只认完整前缀。"));
        page.Children.Add(Note("行内的色卡始终显示当前色系下的学科颜色。颜色浮层默认只给常用预设色，需要精确取色时展开「自定义颜色」；点「随机」后，每次采纳该学科都从预设色里随机取一个（不连续重复），再点一次恢复固定颜色，手动取色也会关闭随机。"));

        StackPanel list = SettingsStack();

        void Refresh()
        {
            list.Children.Clear();
            List<SubjectSuggestion> subjects = _settings.Autofill.Subject.Subjects
                .OrderBy(item => item.Source == AutofillService.BuiltInSource ? 0 : 1)
                .ThenBy(item => item.Name, StringComparer.CurrentCulture)
                .ToList();
            if (subjects.Count == 0)
            {
                list.Children.Add(Note("还没有可补全的学科，点击下方按钮添加。"));
                return;
            }

            foreach (SubjectSuggestion item in subjects) list.Children.Add(SubjectAutofillRow(item, Refresh));
        }

        // 列表上方提供手动刷新，便于在修改色系或外部数据后重新渲染。
        Button refresh = new() { Content = "刷新列表", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => Refresh();
        page.Children.Add(refresh);
        page.Children.Add(Heading("学科列表"));
        page.Children.Add(list);

        Button add = new() { Content = "添加学科", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += async (_, _) =>
        {
            if (_settings.Autofill.Subject.Subjects.Count >= AutofillService.MaxSubjectEntries)
            {
                await ShowMessageAsync("无法添加", $"学科库最多 {AutofillService.MaxSubjectEntries} 条，请先删除不再使用的学科。", "知道了");
                return;
            }

            if (await AskNameWithColorAsync("添加学科", "学科名称", "语文", "#818CF8") is not { } result) return;
            SubjectSuggestion? existing = _settings.Autofill.Subject.Subjects
                .FirstOrDefault(item => item.Name.Equals(result.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Enabled = true;
                existing.Color = result.Color;
            }
            else
            {
                _settings.Autofill.Subject.Subjects.Add(new SubjectSuggestion
                {
                    Name = result.Name,
                    Enabled = true,
                    Color = result.Color,
                    Source = AutofillService.ManualSource
                });
            }

            Refresh();
            SettingChanged();
        };
        page.Children.Add(add);
        page.Children.Add(Note("内置学科在首次启动时生成；学科库只包含内置学科和这里手动添加的学科，程序不会自动收录项目或磁贴里的科目。"));
        Refresh();
        // 色系切换后色卡要按新色系重新解析，行内颜色与颜色浮层都跟着刷新。
        _refreshSubjectAutofill = Refresh;
        return page;
    }

    private Grid SubjectAutofillRow(SubjectSuggestion item, Action refresh)
    {
        Grid row = new() { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel label = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = item.Name, FontSize = 15, Foreground = BoardTheme.TextBrush });
        label.Children.Add(new TextBlock
        {
            Text = SubjectSourceLabel(item.Source), FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BoardTextMutedBrush"]
        });
        row.Children.Add(label);

        ToggleSwitch toggle = new()
        {
            OffContent = string.Empty, OnContent = string.Empty, IsOn = item.Enabled,
            MinWidth = 0, VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (_, _) =>
        {
            item.Enabled = toggle.IsOn;
            SettingChanged();
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        Button color = new() { VerticalAlignment = VerticalAlignment.Center };
        Border swatch = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(4) };
        TextBlock colorLabel = new() { VerticalAlignment = VerticalAlignment.Center };
        StackPanel colorContent = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        colorContent.Children.Add(swatch);
        colorContent.Children.Add(colorLabel);
        color.Content = colorContent;
        // 色卡始终显示当前色系下的学科颜色：预设色跟随鲜明/马卡龙切换，自定义色原样显示，
        // 随机配色用预设色的渐变表示每次补全都会换成其中一个。
        void RefreshColor()
        {
            swatch.Background = item.RandomColor
                ? RandomSubjectColorBrush()
                : MainViewModel.BrushFromHex(SafeColorHex(ColorPalette.Resolve(SafeColorHex(item.Color))));
            colorLabel.Text = item.RandomColor ? "随机颜色" : "默认颜色";
        }

        RefreshColor();
        // 默认只给常用预设色，需要精确取色时再展开完整颜色控件。
        StackPanel flyoutContent = new() { Spacing = 10, Padding = new Thickness(4) };
        StackPanel presets = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        flyoutContent.Children.Add(presets);
        StackPanel customArea = new() { Spacing = 8, Visibility = Visibility.Collapsed };
        ColorPicker picker = new()
        {
            IsAlphaEnabled = false, IsHexInputVisible = true,
            // 初始颜色跟随当前色系，打开取色器时看到的就是磁贴实际显示的颜色。
            Color = SafeColor(ColorPalette.Resolve(SafeColorHex(item.Color)), BoardTheme.SurfaceBrush.Color)
        };
        Button customToggle = new() { Content = "自定义颜色" };
        ToggleButton randomToggle = new() { Content = "随机", IsChecked = item.RandomColor, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(randomToggle, "每次补全都从预设色里随机取一个颜色");
        picker.ColorChanged += (_, args) =>
        {
            item.Color = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}";
            // 手动取色即视为放弃随机配色。
            item.RandomColor = false;
            randomToggle.IsChecked = false;
            RefreshColor();
            SettingChanged();
        };
        customArea.Children.Add(picker);
        customToggle.Click += (_, _) =>
        {
            customArea.Visibility = customArea.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            customToggle.Content = customArea.Visibility == Visibility.Visible ? "收起自定义" : "自定义颜色";
        };
        randomToggle.Checked += (_, _) =>
        {
            item.RandomColor = true;
            RefreshColor();
            SettingChanged();
        };
        randomToggle.Unchecked += (_, _) =>
        {
            item.RandomColor = false;
            RefreshColor();
            SettingChanged();
        };
        foreach (string preset in ColorPalette.Presets)
        {
            Border presetSwatch = new()
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
                Background = MainViewModel.BrushFromHex(ColorPalette.Resolve(preset))
            };
            Button presetButton = new() { Padding = new Thickness(3), MinWidth = 0, Content = presetSwatch };
            ToolTipService.SetToolTip(presetButton, preset);
            presetButton.Click += (_, _) =>
            {
                // 保存预设值本身，预设色继续跟随鲜明/马卡龙色系切换。
                item.Color = preset;
                item.RandomColor = false;
                randomToggle.IsChecked = false;
                RefreshColor();
                SettingChanged();
            };
            presets.Children.Add(presetButton);
        }

        StackPanel colorButtons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        colorButtons.Children.Add(customToggle);
        colorButtons.Children.Add(randomToggle);
        flyoutContent.Children.Add(colorButtons);
        flyoutContent.Children.Add(customArea);
        color.Flyout = new Flyout { Content = flyoutContent };
        Grid.SetColumn(color, 2);
        row.Children.Add(color);

        Button delete = new() { Content = "删除", VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            _settings.Autofill.Subject.Subjects.Remove(item);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>随机配色的色卡：用当前色系的预设色渐变表示每次补全都会换成其中一个。</summary>
    private static Brush RandomSubjectColorBrush()
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        IReadOnlyList<string> presets = ColorPalette.Presets;
        for (int index = 0; index < presets.Count; index++)
        {
            brush.GradientStops.Add(new GradientStop
            {
                Color = MainViewModel.BrushFromHex(ColorPalette.Resolve(presets[index])).Color,
                Offset = presets.Count <= 1 ? 0 : index / (double)(presets.Count - 1)
            });
        }

        return brush;
    }

    /// <summary>自动填充 / 作业补全：总开关、记录阈值、隔离模式，以及已收录与待收录两个列表。</summary>
    private StackPanel HomeworkAutofillSettings()
    {
        StackPanel page = SettingsStack();
        page.Children.Add(Toggle("作业补全", _settings.Autofill.Homework.Enabled, value => _settings.Autofill.Homework.Enabled = value));
        page.Children.Add(Toggle("自动记录常输入的作业名称", _settings.Autofill.Homework.AutoRecord, value => _settings.Autofill.Homework.AutoRecord = value));
        page.Children.Add(Choice("自动记录阈值", ["宽松（2 次）", "正常（3 次）", "严格（5 次）"], ["Loose", "Normal", "Strict"],
            _settings.Autofill.Homework.RecordLevel, value => _settings.Autofill.Homework.RecordLevel = value));
        page.Children.Add(Choice("记录隔离", ["全局", "分学科"], ["Global", "Subject"],
            _settings.Autofill.Homework.Isolation, value => _settings.Autofill.Homework.Isolation = value));
        page.Children.Add(Note("一条作业编辑结束时统计一次：按空白、标点与数字切分，只保留长度 2–12 且含汉字或两个以上连续字母的词；页码碎片、单字母（如 P）和默认占位文字不会记录。"));
        page.Children.Add(Note("「双练一测P30」「双练一测第 3 页」这类写法只记录「双练一测」，不会把页码字母或「第」带进名称；只有文本真正改动过的作业才计入次数，反复打开同一条不会重复累计。"));
        page.Children.Add(Note("全局隔离把所有词记进同一个词库；分学科隔离把自动学习写入当前学科，全局词仍然始终可用。待收录 14 天未再出现即清除，已收录 90 天未使用自动移除，手动添加的条目永不过期。"));

        Button refresh = new() { Content = "刷新列表", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) =>
        {
            // 刷新会先清理过期条目，列表里的剩余天数与状态随之更新。
            if (_autofill.Prune(DateTime.Now)) ScheduleSave();
            _refreshHomeworkAutofill?.Invoke();
        };
        page.Children.Add(refresh);
        page.Children.Add(Heading("已收录"));
        StackPanel promoted = SettingsStack();
        page.Children.Add(promoted);
        Button addType = new() { Content = "添加作业类型", HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(addType);

        page.Children.Add(Heading("待收录"));
        StackPanel pending = SettingsStack();
        page.Children.Add(pending);

        Button clear = new() { Content = "清空作业词库", HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            _autofill.ClearHomework();
            Refresh();
            SettingChanged();
        };
        page.Children.Add(clear);

        addType.Click += async (_, _) =>
        {
            if (_settings.Autofill.Homework.Items.Count(item => item.Source == AutofillService.ManualSource) >= AutofillService.MaxManualItems)
            {
                await ShowMessageAsync("无法添加", $"手动添加的作业类型最多 {AutofillService.MaxManualItems} 条，请先删除不再使用的条目。", "知道了");
                return;
            }

            if (await AskHomeworkTypeAsync() is not { } result) return;
            HomeworkSuggestion? existing = _settings.Autofill.Homework.Items
                .FirstOrDefault(item => item.Text.Equals(result.Text, StringComparison.OrdinalIgnoreCase) && item.IsGlobal == result.IsGlobal);
            if (existing is not null)
            {
                existing.IsGlobal = result.IsGlobal;
                existing.Subjects = result.Subjects;
                existing.Promoted = true;
                existing.LastSeenAt = DateTime.Now;
            }
            else
            {
                _settings.Autofill.Homework.Items.Add(new HomeworkSuggestion
                {
                    Text = result.Text,
                    Source = AutofillService.ManualSource,
                    IsGlobal = result.IsGlobal,
                    Subjects = result.Subjects,
                    Count = AutofillService.RecordThreshold(_settings.Autofill.Homework.RecordLevel),
                    Promoted = true,
                    FirstSeenAt = DateTime.Now,
                    LastSeenAt = DateTime.Now
                });
            }

            Refresh();
            SettingChanged();
        };

        void Refresh()
        {
            DateTime now = DateTime.Now;
            promoted.Children.Clear();
            pending.Children.Clear();

            List<HomeworkSuggestion> recorded = _settings.Autofill.Homework.Items
                .Where(item => item.Promoted)
                .OrderByDescending(item => item.LastSeenAt)
                .ThenBy(item => item.Text, StringComparer.CurrentCulture)
                .ToList();
            if (recorded.Count == 0) promoted.Children.Add(Note("还没有已收录的作业类型，自动记录满足阈值后会出现在这里。"));
            foreach (HomeworkSuggestion item in recorded) promoted.Children.Add(HomeworkRecordedRow(item, Refresh));

            List<HomeworkSuggestion> waiting = _settings.Autofill.Homework.Items
                .Where(item => !item.Promoted)
                .OrderByDescending(item => item.Count)
                .ThenByDescending(item => item.LastSeenAt)
                .ToList();
            if (waiting.Count == 0) pending.Children.Add(Note("没有等待收录的作业名称。"));
            foreach (HomeworkSuggestion item in waiting) pending.Children.Add(HomeworkPendingRow(item, Refresh, now));

            List<string> blocked = _settings.Autofill.Homework.Blocked.ToList();
            if (blocked.Count > 0) pending.Children.Add(Heading("已屏蔽"));
            foreach (string text in blocked) pending.Children.Add(HomeworkBlockedRow(text, Refresh));
        }

        _refreshHomeworkAutofill = Refresh;
        Refresh();
        return page;
    }

    private Grid HomeworkRecordedRow(HomeworkSuggestion item, Action refresh)
    {
        Grid row = SettingsRow(out StackPanel label);

        label.Children.Add(new TextBlock { Text = item.Text, FontSize = 15, Foreground = BoardTheme.TextBrush, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock
        {
            Text = $"{HomeworkSourceLabel(item.Source)} · {HomeworkScopeLabel(item)} · 已输入 {item.Count} 次",
            FontSize = 12, Foreground = (Brush)Application.Current.Resources["BoardTextMutedBrush"]
        });

        // 每行右侧依次是生效范围、编辑、屏蔽与删除。
        for (int index = 0; index < 4; index++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button scope = new() { Content = "生效范围", VerticalAlignment = VerticalAlignment.Center, Flyout = HomeworkScopeFlyout(item, refresh) };
        Grid.SetColumn(scope, 1);
        row.Children.Add(scope);

        Button edit = new() { Content = "编辑", VerticalAlignment = VerticalAlignment.Center };
        edit.Click += async (_, _) =>
        {
            if (await AskHomeworkRenameAsync(item) is not { } value) return;
            if (!_autofill.RenameHomework(item, value)) return;
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(edit, 2);
        row.Children.Add(edit);

        Button block = new() { Content = "屏蔽", VerticalAlignment = VerticalAlignment.Center };
        block.Click += (_, _) =>
        {
            _autofill.BlockHomework(item.Text);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(block, 3);
        row.Children.Add(block);

        Button delete = new() { Content = "删除", VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            _autofill.RemoveHomework(item);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(delete, 4);
        row.Children.Add(delete);
        return row;
    }

    private Grid HomeworkPendingRow(HomeworkSuggestion item, Action refresh, DateTime now)
    {
        Grid row = SettingsRow(out StackPanel label);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int remaining = AutofillService.RemainingCount(item, _settings.Autofill.Homework.RecordLevel);
        label.Children.Add(new TextBlock { Text = item.Text, FontSize = 15, Foreground = BoardTheme.TextBrush, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock
        {
            Text = $"还需 {remaining} 次 · 剩余 {AutofillService.RemainingDays(item, now)} 天过期 · 已输入 {item.Count} 次",
            FontSize = 12, Foreground = (Brush)Application.Current.Resources["BoardTextMutedBrush"]
        });

        Button block = new() { Content = "不再收录", VerticalAlignment = VerticalAlignment.Center };
        block.Click += (_, _) =>
        {
            _autofill.BlockHomework(item.Text);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(block, 1);
        row.Children.Add(block);

        Button delete = new() { Content = "删除", VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            _autofill.RemoveHomework(item);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(delete, 2);
        row.Children.Add(delete);
        return row;
    }

    private Grid HomeworkBlockedRow(string text, Action refresh)
    {
        Grid row = SettingsRow(out StackPanel label);
        row.Opacity = .55;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.Children.Add(new TextBlock { Text = text, FontSize = 15, Foreground = BoardTheme.TextBrush, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock
        {
            Text = "已屏蔽，不再记录和补全", FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BoardTextMutedBrush"]
        });
        Button restore = new() { Content = "恢复收录", VerticalAlignment = VerticalAlignment.Center };
        restore.Click += (_, _) =>
        {
            _autofill.UnblockHomework(text);
            refresh();
            SettingChanged();
        };
        Grid.SetColumn(restore, 1);
        row.Children.Add(restore);
        return row;
    }

    /// <summary>两列行骨架：左侧名称与说明，右侧由调用方继续追加按钮。</summary>
    private static Grid SettingsRow(out StackPanel label)
    {
        Grid row = new() { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        label = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        return row;
    }

    /// <summary>作业补全的生效范围：全局，或任选若干学科。</summary>
    private Flyout HomeworkScopeFlyout(HomeworkSuggestion item, Action refresh)
    {
        StackPanel content = new() { Spacing = 6, Padding = new Thickness(4) };
        CheckBox global = new() { Content = "全局", IsChecked = item.IsGlobal };
        content.Children.Add(global);
        StackPanel subjects = new() { Spacing = 2 };
        List<(CheckBox Box, string Name)> boxes = [];
        foreach (SubjectSuggestion subject in _settings.Autofill.Subject.Subjects.OrderBy(value => value.Name, StringComparer.CurrentCulture))
        {
            CheckBox box = new() { Content = subject.Name, IsChecked = item.Subjects.Contains(subject.Name, StringComparer.OrdinalIgnoreCase) };
            boxes.Add((box, subject.Name));
            subjects.Children.Add(box);
        }

        content.Children.Add(new ScrollViewer { Content = subjects, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Flyout flyout = new() { Content = content };
        void Apply()
        {
            item.IsGlobal = global.IsChecked == true;
            item.Subjects = boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Name).ToList();
            refresh();
            SettingChanged();
        }

        global.Checked += (_, _) =>
        {
            foreach ((CheckBox box, _) in boxes) box.IsChecked = false;
            Apply();
        };
        global.Unchecked += (_, _) => Apply();
        foreach ((CheckBox box, _) in boxes)
        {
            box.Checked += (_, _) =>
            {
                global.IsChecked = false;
                Apply();
            };
            box.Unchecked += (_, _) => Apply();
        }

        return flyout;
    }

    /// <summary>改写已收录的作业名称；取消或空名称返回 null。</summary>
    private async Task<string?> AskHomeworkRenameAsync(HomeworkSuggestion item)
    {
        if (_dialogOpen) return null;
        TextBox name = new() { Header = "作业名称", Text = item.Text, MaxLength = AutofillService.MaxTokenLength };
        ContentDialog dialog = new()
        {
            Title = "编辑作业名称", Content = name, PrimaryButtonText = "保存", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return null;
        string value = name.Text.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>名称 + 颜色的通用输入对话框；取消或空名称返回 null。</summary>
    private async Task<(string Name, string Color)?> AskNameWithColorAsync(string title, string header, string placeholder, string hex)
    {
        if (_dialogOpen) return null;
        TextBox name = new() { Header = header, PlaceholderText = placeholder, MaxLength = AutofillService.MaxSubjectNameLength };
        Windows.UI.Color brand = Application.Current.Resources.TryGetValue("BoardBrandBrush", out object? resource) && resource is SolidColorBrush brandBrush
            ? brandBrush.Color
            : BoardTheme.SurfaceBrush.Color;
        ColorPicker picker = new()
        {
            IsAlphaEnabled = false, IsHexInputVisible = true,
            Color = SafeColor(hex, brand)
        };
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(name);
        content.Children.Add(picker);
        ContentDialog dialog = new()
        {
            Title = title, Content = content, PrimaryButtonText = "添加", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return null;
        string value = name.Text.Trim();
        if (value.Length == 0) return null;
        return (value, $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
    }

    /// <summary>手动添加作业类型：名称 + 生效范围（全局或若干学科）。</summary>
    private async Task<(string Text, bool IsGlobal, List<string> Subjects)?> AskHomeworkTypeAsync()
    {
        if (_dialogOpen) return null;
        TextBox name = new() { Header = "作业名称", PlaceholderText = "例如 同步练习册", MaxLength = AutofillService.MaxTokenLength };
        CheckBox global = new() { Content = "全局", IsChecked = true };
        StackPanel subjectList = new() { Spacing = 2 };
        List<(CheckBox Box, string Name)> boxes = [];
        foreach (SubjectSuggestion subject in _settings.Autofill.Subject.Subjects.OrderBy(value => value.Name, StringComparer.CurrentCulture))
        {
            CheckBox box = new() { Content = subject.Name };
            boxes.Add((box, subject.Name));
            subjectList.Children.Add(box);
        }

        global.Checked += (_, _) =>
        {
            foreach ((CheckBox box, _) in boxes) box.IsChecked = false;
        };
        foreach ((CheckBox box, _) in boxes)
            box.Checked += (_, _) => global.IsChecked = false;

        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = "生效范围", FontSize = 14 });
        content.Children.Add(global);
        content.Children.Add(new ScrollViewer { Content = subjectList, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        ContentDialog dialog = new()
        {
            Title = "添加作业类型", Content = content, PrimaryButtonText = "添加", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return null;
        string value = name.Text.Trim();
        if (value.Length == 0) return null;
        List<string> subjects = boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Name).ToList();
        return (value, global.IsChecked == true || subjects.Count == 0, subjects);
    }

    private static string SubjectSourceLabel(string source) => source switch
    {
        AutofillService.BuiltInSource => "内置学科",
        // 旧版本自动登记过的条目保留来源说明，便于用户辨认与清理。
        AutofillService.LearnedSource => "旧版自动登记",
        _ => "手动添加"
    };

    private static string HomeworkSourceLabel(string source) =>
        source == AutofillService.ManualSource ? "手动添加" : "自动记录";

    private static string HomeworkScopeLabel(HomeworkSuggestion item) =>
        item.IsGlobal || item.Subjects.Count == 0 ? "全局" : string.Join("、", item.Subjects);

    private static string SafeColorHex(string value) =>
        value.Length is 7 or 9 && value[0] == '#' ? value : "#818CF8";

    private static Windows.UI.Color SafeColor(string value, Windows.UI.Color fallback)
    {
        try
        {
            return MainViewModel.BrushFromHex(SafeColorHex(value)).Color;
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
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
        if (tag == "AutofillHomework")
        {
            // 打开作业补全页时清理过期条目，列表里的剩余天数才准确。
            if (_autofill.Prune(DateTime.Now)) ScheduleSave();
            _refreshHomeworkAutofill?.Invoke();
        }

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

    /// <summary>
    /// 拖动网格大小只处理数据与预览：磁贴重新吸附到新网格，看板网格等回到看板时再重画。
    /// 设置页打开时看板是折叠的，这里避开整窗重刷和不可见的看板重绘，拖动才不会卡。
    /// </summary>
    private void GridSizeChanged()
    {
        SnapTilesToGrid();
        UpdateAutoLayoutGapStep();
        RefreshAppearancePreviews();
        ScheduleSave();
    }

    /// <summary>自动排版间隔只影响自动排列与预览，拖动时同样不重刷整个界面。</summary>
    private void AutoLayoutGapChanged()
    {
        RefreshAppearancePreviews();
        ScheduleSave();
    }

    /// <summary>间隔滑块跟随网格吸附：关闭吸附按 10px 步进，开启吸附按半格步进。</summary>
    private void UpdateAutoLayoutGapStep()
    {
        if (_autoLayoutGapSlider is null) return;
        double step = IsGridSnappingEnabled ? Math.Max(1, GridSize) / 2 : 10;
        _autoLayoutGapSlider.StepFrequency = step;
        _autoLayoutGapSlider.SmallChange = step;
        _autoLayoutGapSlider.LargeChange = step * 2;
    }

    /// <summary>自动布局里的“吸附到网格”和编辑工具栏的吸附按钮共用同一份设置。</summary>
    private void SetGridSnapping(bool enabled)
    {
        IsGridSnappingEnabled = enabled;
        _settings.GridSnappingEnabled = enabled;
        if (GridSnapToggleButton.IsChecked != enabled) GridSnapToggleButton.IsChecked = enabled;
        UpdateGridSnapHint();
        UpdateAutoLayoutGapStep();
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
        // 布局模式切换会改变编辑工具栏可用项和整屏笔迹的显示状态。
        UpdateEditingToolbarVisibility();
        RefreshClockInk();
        SharedBackgroundVisual.Apply(UseSharedBackground ? _settings.SharedBackground : new(), BoardTheme.SurfaceBrush);
        ClockPanel.Background = BoardWorkspace.Background = null;
        ClockBackgroundVisual.Apply(_settings.LayoutMode == "Free" ? new() : _settings.ClockBackground, BoardTheme.SurfaceBrush, UseSharedBackground);
        BoardBackgroundVisual.Apply(_settings.LayoutMode == "Free" ? new() : _settings.BoardBackground, BoardTheme.SurfaceBrush, UseSharedBackground);
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.ApplyAppearance(_settings);
        // 从无限作业板切回固定作业板时缩放比例回到 100%：原先放大后的位置已经没有意义。
        if (!_settings.InfiniteBoard && _wasInfiniteBoard)
        {
            _wasInfiniteBoard = false;
            _boardZoom = 1;
            UpdateZoomLevelText();
        }
        else if (_settings.InfiniteBoard)
        {
            _wasInfiniteBoard = true;
        }
        UpdateBoardBounds(); ApplyToolbarSettings();
        RecordToolbarActivity(false);
        RefreshAppearancePreviews();
        if (!_settings.InfiniteBoard)
        {
            // ScrollViewer 在内容尺寸变化的同一轮布局会丢弃 ChangeView；新尺寸提交后再复位。
            // 放大后保留用户看到的位置，只有整块作业板正好铺满视口时才把滚动量归零。
            double targetZoom = _boardZoom;
            bool fillViewport = targetZoom <= 1.001;
            // 复位期间视口会短暂报出旧比例，这段状态不算用户改动。
            _pendingBoardViewReset = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _pendingBoardViewReset = false;
                if (_settings.InfiniteBoard) return;
                BoardScroller.ChangeView(fillViewport ? 0 : null, fillViewport ? 0 : null, (float)targetZoom, true);
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
        _inkWidths.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        MainToolbarScroll.HorizontalScrollBarVisibility = InkToolbarScroll.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        MainToolbarScroll.VerticalScrollBarVisibility = InkToolbarScroll.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        double scale = Math.Clamp(_settings.ToolbarScale, .6, 2);
        double toolbarPadding = 7 * scale;
        // 画笔栏按钮比主控制窗小一号，用内边距补足差值；两块窗口的高（竖版为宽）因此完全一致。
        double inkToolbarPadding = Math.Max(0,
            (ToolbarCrossSize(ToolbarContentSize(scale), toolbarPadding) - 40 * scale) / 2 - IslandBorderThickness);
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
            double iconButtonSize = 40 * scale;
            button.Width = button.Height = iconButtonSize;
            // 系统按钮默认内边距（11,5,11,6）比 20 DIP 图标的内容区还宽，会把图标挤出内容区并整体偏右；
            // 图标按钮改为零内边距加居中，保证图标四周留白一致且完整可见。
            button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            // 与主控制窗按钮同一规则：圆角随控制窗圆角并扣除画笔栏内边距，保持与外框同心。
            button.CornerRadius = MatchToolbarButtonRadius(_settings.ToolbarRadius, inkToolbarPadding, iconButtonSize, iconButtonSize);
            if (button.Content is FluentIcon glyph) glyph.FontSize = 20 * scale;
            if (button.Content is Viewbox { Child: IconSourceElement } vectorIcon)
                vectorIcon.Width = vectorIcon.Height = 20 * scale;
        }
        foreach (var swatch in _inkColors.Children.OfType<ColorSwatchButton>()) swatch.Width = swatch.Height = 32 * scale;
        foreach (var preset in _inkWidths.Children.OfType<InkWidthPresetButton>()) preset.ApplyScale(scale);
        FloatingToolbar.CornerRadius = GlobalInkToolbar.CornerRadius = new CornerRadius(_settings.ToolbarRadius);
        FloatingToolbar.Padding = new Thickness(toolbarPadding);
        GlobalInkToolbar.Padding = new Thickness(inkToolbarPadding);
        FloatingToolbar.Background = CreateToolbarBackground();
        GlobalInkToolbar.Background = CreateToolbarBackground();
        FloatingToolbar.HorizontalAlignment = position.EndsWith("Left") ? HorizontalAlignment.Left : position.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        FloatingToolbar.VerticalAlignment = position.StartsWith("Top") ? VerticalAlignment.Top : vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        double x = _settings.ToolbarHorizontalInset, y = _settings.ToolbarVerticalInset;
        FloatingToolbar.Margin = new Thickness(x, y, x, y);
        FloatingToolbar.MaxWidth = Math.Max(120, RootShell.ActualWidth - x * 2);
        FloatingToolbar.MaxHeight = Math.Max(80, RootShell.ActualHeight - y * 2);
        // 画笔栏、富文本岛与缩放岛的位置由悬浮岛排版统一计算，保证三块窗口互不遮挡。
        RefreshIslands();
    }
}
