using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 设置页：左侧分类导航、右侧卡片式内容。分类与旧版一致，
/// 每个控件改动后立即生效并安排保存。
/// </summary>
public sealed partial class MainWindow
{
    private sealed record SettingsSection(string Tag, string Title, string Description);

    private static readonly SettingsSection[] SettingsSections =
    [
        new("Layout", "布局", "选择看板布局、网格大小与自动排列方式。"),
        new("AppearanceTheme", "外观 · 主题", "深色、浅色或跟随系统，以及磁贴与笔迹的色系。"),
        new("AppearanceTile", "外观 · 磁贴", "磁贴标题大小与磁贴背景。"),
        new("AppearanceGrid", "外观 · 网格", "网格与点阵的显示样式、颜色和粗细。"),
        new("AppearanceToolbar", "外观 · 控制窗", "控制窗的位置、大小、背景与自动隐藏。"),
        new("AppearanceBackground", "外观 · 背景板", "跨区背景与时钟区、作业板区域的底图。"),
        new("ComponentsWeather", "组件 · 天气", "选择地区、刷新天气与显示极端天气预警。"),
        new("ComponentsNoise", "组件 · 噪音检测", "麦克风输入、吵闹阈值、提示音与目标音量校准。"),
        new("AutofillSubject", "自动填充 · 学科补全", "按学科名称、全拼或首字母补全磁贴标题。"),
        new("AutofillHomework", "自动填充 · 作业补全", "学习常输入的作业名称并在正文里补全。"),
        new("About", "关于", "版本信息、项目仓库与更新设置。")
    ];

    private ComboBox? _layoutChoice;
    private string _settingsSection = "Layout";

    /// <summary>设置控件在主题或外部变化后需要同步的刷新动作。</summary>
    private readonly List<Action> _refreshSettingAvailability = [];

    private void ShowSettings()
    {
        // 设置页是唯一透出窗口材质的地方，进入前按当前材质状态确认一次底色。
        UpdateBackdropSurfaces();
        DisplayRoot.IsVisible = false;
        FloatingToolbar.IsVisible = false;
        SettingsRoot.IsVisible = true;
        BuildSettingsNavigation();
        ShowSettingsSection(_settingsSection);
        // 设置页占满内容区，顶栏的项目入口先收起，返回看板时再恢复。
        UpdateProjectCommands();
    }

    // WinUI 的 NavigationView 默认展开“外观”，其余分组按用户操作保持展开状态。
    // 重建导航时不丢失这个状态，切换设置项不会意外折叠菜单。
    private readonly Dictionary<string, bool> _expandedSettingsGroups = new()
    {
        ["Appearance"] = true,
        ["Components"] = false,
        ["Autofill"] = false
    };

    private void BuildSettingsNavigation()
    {
        SettingsNavigation.Children.Clear();
        SettingsNavigation.Children.Add(CreateSettingsNavItem("Layout", "布局", FluentGlyphs.Layout));
        AddSettingsNavGroup("Appearance", "外观", FluentGlyphs.Color,
        [
            ("AppearanceTile", "磁贴", FluentGlyphs.Board),
            ("AppearanceBackground", "背景板", FluentGlyphs.Image),
            ("AppearanceTheme", "主题", FluentGlyphs.Color),
            ("AppearanceGrid", "网格", FluentGlyphs.Grid),
            ("AppearanceToolbar", "控制窗", FluentGlyphs.Settings)
        ]);
        AddSettingsNavGroup("Components", "组件", FluentGlyphs.Components,
        [
            ("ComponentsWeather", "天气", FluentGlyphs.Weather),
            ("ComponentsNoise", "噪音检测", FluentGlyphs.Microphone)
        ]);
        AddSettingsNavGroup("Autofill", "自动填充", FluentGlyphs.TextGrammarWand,
        [
            ("AutofillSubject", "学科补全", FluentGlyphs.Board),
            ("AutofillHomework", "作业补全", FluentGlyphs.Document)
        ]);
        SettingsNavigation.Children.Add(CreateSettingsNavItem("About", "关于", FluentGlyphs.Info));
    }

    private void AddSettingsNavGroup(string key, string title, string glyph,
        (string Tag, string Title, string Glyph)[] children)
    {
        bool expanded = _expandedSettingsGroups[key];
        Button header = CreateSettingsNavButton(title, glyph, selected: false, indent: 0);
        header.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                CreateSettingsNavLabel(title, glyph),
                new TextBlock
                {
                    Text = expanded ? "\uE450" : "\uE448",
                    FontFamily = FontService.IconFamily,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Foreground = new SolidColorBrush(Color.Parse(BoardTheme.IsLight ? "#555568" : "#A4A4B4"))
                }
            }
        };
        Grid.SetColumn((Control)((Grid)header.Content).Children[1], 1);
        header.Click += (_, _) =>
        {
            _expandedSettingsGroups[key] = !_expandedSettingsGroups[key];
            BuildSettingsNavigation();
        };
        SettingsNavigation.Children.Add(header);

        if (!expanded) return;
        foreach ((string tag, string childTitle, string childGlyph) in children)
            SettingsNavigation.Children.Add(CreateSettingsNavItem(tag, childTitle, childGlyph, indent: 24));
    }

    private Button CreateSettingsNavItem(string tag, string title, string glyph, int indent = 0)
    {
        bool selected = tag == _settingsSection;
        Button button = CreateSettingsNavButton(title, glyph, selected, indent);
        Grid item = new() { ColumnDefinitions = new ColumnDefinitions("4,*"), ColumnSpacing = 10 };
        item.Children.Add(new Border
        {
            Width = 3, Height = 18, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.Parse(BoardTheme.IsLight ? "#5457DB" : "#42E6D5")),
            Opacity = selected ? 1 : 0,
            VerticalAlignment = VerticalAlignment.Center
        });
        StackPanel label = CreateSettingsNavLabel(title, glyph);
        Grid.SetColumn(label, 1);
        item.Children.Add(label);
        button.Content = item;
        button.Click += (_, _) => ShowSettingsSection(tag);
        return button;
    }

    private static Button CreateSettingsNavButton(string title, string glyph, bool selected, int indent)
    {
        Color background = selected
            ? Color.Parse(BoardTheme.IsLight ? "#E6E6ED" : "#2A2A35")
            : Colors.Transparent;
        return new Button
        {
            Content = CreateSettingsNavLabel(title, glyph),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40,
            Margin = new Thickness(indent, 0, 0, 0),
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(background),
            Foreground = new SolidColorBrush(Color.Parse(BoardTheme.IsLight ? "#000000" : "#F0F0F5"))
        };
    }

    private static StackPanel CreateSettingsNavLabel(string title, string glyph)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = glyph, FontFamily = FontService.IconFamily, FontSize = 20,
                    VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }
            }
        };
    }
    private void ShowSettingsSection(string tag)
    {
        _settingsSection = tag;
        SettingsSection section = SettingsSections.First(item => item.Tag == tag);
        this.FindControl<TextBlock>("SettingsPageTitle")!.Text = section.Title.Split(" · ")[^1];
        this.FindControl<TextBlock>("SettingsPageDescription")!.Text = section.Description;
        SettingsContent.Children.Clear();
        _refreshSettingAvailability.Clear();
        switch (tag)
        {
            case "Layout":
                BuildLayoutSettings();
                break;
            case "AppearanceTheme":
                BuildThemeSettings();
                break;
            case "AppearanceTile":
                BuildTileAppearanceSettings();
                break;
            case "AppearanceGrid":
                BuildGridSettings();
                break;
            case "AppearanceToolbar":
                BuildToolbarSettings();
                break;
            case "AppearanceBackground":
                BuildBackgroundSettings();
                break;
            case "ComponentsWeather":
                BuildWeatherSettings();
                break;
            case "ComponentsNoise":
                BuildNoiseSettings();
                break;
            case "AutofillSubject":
                BuildAutofillSubjectSettings();
                break;
            case "AutofillHomework":
                BuildAutofillHomeworkSettings();
                break;
            case "About":
                BuildAboutSettings();
                break;
        }

        BuildSettingsNavigation();
        RefreshSettingAvailability();
        // 切到某个分类时先按当前设置画出它自己的预览。
        RefreshAppearancePreviews();
    }

    private void RefreshSettingAvailability()
    {
        foreach (Action refresh in _refreshSettingAvailability) refresh();
    }

    /// <summary>布局设置：布局模式、分屏比例、网格与自动排列。</summary>
    private void BuildLayoutSettings()
    {
        ComboBox layoutChoice = CreateComboBox(
            ["分屏", "仅作业", "仅时钟", "自由布局"],
            Settings.LayoutMode switch { "Board" => 1, "Clock" => 2, "Free" => 3, _ => 0 },
            index =>
            {
                Settings.LayoutMode = index switch { 1 => "Board", 2 => "Clock", 3 => "Free", _ => "Split" };
                SettingChanged();
            });
        _layoutChoice = layoutChoice;

        Slider splitRatio = CreateSlider(0.05, 0.95, 0.05, Settings.SplitRatio);
        splitRatio.ValueChanged += (_, _) =>
        {
            Settings.SplitRatio = splitRatio.Value;
            ApplyDisplayLayout();
            ScheduleSave();
        };

        Slider gridSize = CreateSlider(16, 160, 1, GridSize, snapToTick: true);
        gridSize.ValueChanged += (_, _) =>
        {
            Settings.GridSize = Math.Round(gridSize.Value);
            SnapTilesToGrid();
            _renderedGridWidth = 0;
            UpdateBoardBounds();
            // 吸附说明里写着当前网格步长，改大小后要跟着更新。
            UpdateGridSnapHint();
            ScheduleSave();
        };

        ToggleSwitch snapToggle = CreateToggle(Settings.GridSnappingEnabled);
        snapToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.GridSnappingEnabled = snapToggle.IsChecked == true;
            SettingChanged();
        };

        Slider gap = CreateSlider(0, 200, 1, Settings.AutoLayoutGap);
        gap.ValueChanged += (_, _) =>
        {
            Settings.AutoLayoutGap = Math.Round(gap.Value);
            ScheduleSave();
        };

        ToggleSwitch alignToggle = CreateToggle(Settings.AutoLayoutAlign);
        alignToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.AutoLayoutAlign = alignToggle.IsChecked == true;
            ScheduleSave();
        };

        ToggleSwitch resizeToggle = CreateToggle(Settings.AutoLayoutResize);
        resizeToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.AutoLayoutResize = resizeToggle.IsChecked == true;
            ScheduleSave();
        };

        ToggleSwitch infinite = CreateToggle(Settings.InfiniteBoard);
        infinite.IsCheckedChanged += (_, _) =>
        {
            Settings.InfiniteBoard = infinite.IsChecked == true;
            ApplyBoardZoom();
            UpdateZoomIslandVisibility();
            ScheduleSave();
        };

        SettingsContent.Children.Add(CreateCard(
            "布局模式",
            CreateRow("布局模式", "分屏可拖动中间分隔条调整两区大小；仅作业与仅时钟保留边缘分隔条，随时能拖回分屏。", layoutChoice),
            CreateRow("分屏比例", "分屏模式下时钟区占整块看板的比例。", splitRatio)));

        SettingsContent.Children.Add(CreateCard(
            "网格",
            // 网格大小与自动排列共用同一张预览：改任一项都能立刻看到结果。
            CreateAppearancePreview("Layout"),
            CreateRow("网格大小", "磁贴吸附与自动排列使用的网格步长（16–160 像素）。", gridSize),
            CreateRow("吸附到网格", "编辑工具栏的吸附按钮与这里保持同步。", snapToggle),
            CreateRow("无限作业板", "画布随磁贴向外扩展，可用滚动条、鼠标中键拖动或触屏平移；Ctrl + 滚轮缩放。", infinite)));

        SettingsContent.Children.Add(CreateCard(
            "自动排列",
            CreateRow("磁贴最小间隔", "自动排列时相邻磁贴之间保留的最小间距。", gap),
            CreateRow("自动对齐", "把位置尽量落在同一网格线上，使边缘对齐。", alignToggle),
            CreateRow("自动调整磁贴大小", "按内容宽高收紧磁贴，过宽时换行而不是溢出。", resizeToggle)));

        Button arrange = CreateActionButton("立即自动排列", () =>
        {
            ArrangeTiles();
            ScheduleSave();
        });
        SettingsContent.Children.Add(CreateCard("执行", arrange));
    }

    /// <summary>主题设置：主题与色系。</summary>
    private void BuildThemeSettings()
    {
        ComboBox theme = CreateComboBox(
            ["深色", "浅色", "跟随系统"],
            Settings.Theme switch { "Light" => 1, "Default" => 2, _ => 0 },
            index =>
            {
                Settings.Theme = index switch { 1 => "Light", 2 => "Default", _ => "Dark" };
                SettingChanged();
            });

        ComboBox palette = CreateComboBox(
            ["鲜明", "马卡龙"],
            Settings.Palette == "Macaron" ? 1 : 0,
            index => { Settings.Palette = index == 1 ? "Macaron" : "Vivid"; SettingChanged(); });

        SettingsContent.Children.Add(CreateCard(
            "主题",
            CreateRow("主题", "深色更接近教室黑板，强光环境可切换浅色；跟随系统会随系统外观变化。", theme),
            CreateRow("色系", "切换后只改变内置预设色，已有彩色文字、高光、笔迹与自定义磁贴颜色保持原值。", palette)));
    }

    /// <summary>网格设置：样式、颜色、粗细与点直径。</summary>
    private void BuildGridSettings()
    {
        ComboBox style = CreateComboBox(
            ["网格", "点阵", "不显示"],
            Settings.GridStyle switch { "Dots" => 1, "None" => 2, _ => 0 },
            index =>
            {
                Settings.GridStyle = index switch { 1 => "Dots", 2 => "None", _ => "Grid" };
                _renderedGridWidth = 0;
                SettingChanged();
            });

        Slider thickness = CreateSlider(0.5, 5, 0.1, Settings.GridLineThickness);
        thickness.ValueChanged += (_, _) =>
        {
            Settings.GridLineThickness = thickness.Value;
            _renderedGridWidth = 0;
            ScheduleSave();
        };

        Slider dotDiameter = CreateSlider(1, 12, 1, Settings.GridDotDiameter, snapToTick: true);
        dotDiameter.ValueChanged += (_, _) =>
        {
            Settings.GridDotDiameter = Math.Round(dotDiameter.Value);
            _renderedGridWidth = 0;
            ScheduleSave();
        };

        ToggleSwitch showWhileEditing = CreateToggle(Settings.ShowGridWhileEditing);
        showWhileEditing.IsCheckedChanged += (_, _) =>
        {
            Settings.ShowGridWhileEditing = showWhileEditing.IsChecked == true;
            _renderedGridWidth = 0;
            SettingChanged();
        };

        SettingsContent.Children.Add(CreateCard(
            "网格",
            CreateAppearancePreview("Grid"),
            CreateRow("网格样式", "点阵与网格只改变显示，吸附逻辑不变。", style),
            CreateRow("网格颜色", "网格线的颜色，拖动透明度可叠加到底图之上。", CreateColorRow(
                () => Settings.GridColor,
                value => { Settings.GridColor = value; _renderedGridWidth = 0; ScheduleSave(); },
                includeOpacity: false)),
            CreateRow("网格粗细", "网格线的宽度（0.5–5 像素）。", thickness)));

        SettingsContent.Children.Add(CreateCard(
            "点阵",
            CreateRow("点颜色", "点阵模式下的点颜色。", CreateColorRow(
                () => Settings.GridDotColor,
                value => { Settings.GridDotColor = value; _renderedGridWidth = 0; ScheduleSave(); },
                includeOpacity: false)),
            CreateRow("点直径", "点阵模式下单个点的直径（1–12 像素）。", dotDiameter)));

        SettingsContent.Children.Add(CreateCard(
            "编辑提示",
            CreateRow("编辑时显示常规网格", "进入编辑时临时显示网格线，退出后恢复用户选择的样式。", showWhileEditing)));
    }

    /// <summary>控制窗设置：位置、大小、圆角、背景与自动隐藏。</summary>
    private void BuildToolbarSettings()
    {
        ToggleSwitch iconOnly = CreateToggle(Settings.ToolbarIconOnly);
        iconOnly.IsCheckedChanged += (_, _) =>
        {
            Settings.ToolbarIconOnly = iconOnly.IsChecked == true;
            SettingChanged();
        };

        Slider scale = CreateSlider(0.6, 2, 0.05, Settings.ToolbarScale);
        scale.ValueChanged += (_, _) => { Settings.ToolbarScale = scale.Value; ApplyToolbarAppearance(); ScheduleSave(); };

        Slider radius = CreateSlider(0, 60, 1, Settings.ToolbarRadius, snapToTick: true);
        radius.ValueChanged += (_, _) => { Settings.ToolbarRadius = radius.Value; ApplyToolbarAppearance(); ScheduleSave(); };

        Slider horizontal = CreateSlider(0, 200, 1, Settings.ToolbarHorizontalInset, snapToTick: true);
        horizontal.ValueChanged += (_, _) => { Settings.ToolbarHorizontalInset = horizontal.Value; ApplyToolbarAppearance(); ScheduleSave(); };

        Slider vertical = CreateSlider(0, 200, 1, Settings.ToolbarVerticalInset, snapToTick: true);
        vertical.ValueChanged += (_, _) => { Settings.ToolbarVerticalInset = vertical.Value; ApplyToolbarAppearance(); ScheduleSave(); };

        // 居中停靠的那一侧不需要对应的边距设置，按位置动态显隐。
        Control horizontalRow = CreateRow("距左右边框", "仅在靠左或靠右停靠时生效。", horizontal);
        Control verticalRow = CreateRow("距上下边框", "仅在上方或下方停靠时生效。", vertical);
        _refreshSettingAvailability.Add(() =>
        {
            horizontalRow.IsVisible = Settings.ToolbarPosition is not ("TopCenter" or "BottomCenter");
            verticalRow.IsVisible = Settings.ToolbarPosition is not ("CenterLeft" or "CenterRight");
        });

        ToggleSwitch autoHide = CreateToggle(Settings.ToolbarAutoHide);
        Slider hideSeconds = CreateSlider(1, 600, 1, ToolbarAutoHidePolicy.NormalizeDelay(Settings.ToolbarAutoHideSeconds), snapToTick: true);
        hideSeconds.ValueChanged += (_, _) =>
        {
            Settings.ToolbarAutoHideSeconds = ToolbarAutoHidePolicy.NormalizeDelay(hideSeconds.Value);
            ScheduleSave();
        };
        ComboBox hideAnimation = CreateComboBox(
            ["渐入渐出", "飞出"],
            Settings.ToolbarHideAnimation == "Fly" ? 1 : 0,
            index => { Settings.ToolbarHideAnimation = index == 1 ? "Fly" : "Fade"; ScheduleSave(); });
        autoHide.IsCheckedChanged += (_, _) =>
        {
            Settings.ToolbarAutoHide = autoHide.IsChecked == true;
            RefreshSettingAvailability();
            ScheduleSave();
        };
        Control hideSecondsRow = CreateRow("自动隐藏时间", "查看模式下连续无操作达到该时长后隐藏控制窗（1–600 秒）。", hideSeconds);
        Control hideAnimationRow = CreateRow("隐藏动画", "渐入渐出改变透明度；飞出按最近边框移动。", hideAnimation);
        _refreshSettingAvailability.Add(() =>
        {
            hideSecondsRow.IsEnabled = Settings.ToolbarAutoHide;
            hideAnimationRow.IsEnabled = Settings.ToolbarAutoHide;
        });

        // 毛玻璃与模糊程度：只有开启毛玻璃时模糊滑条可用，和旧版的可用性规则一致。
        ToggleSwitch toolbarGlass = CreateToggle(Settings.ToolbarGlass);
        Slider toolbarBlur = CreateSlider(0, 100, 1, Settings.ToolbarBlur, snapToTick: true);
        toolbarBlur.ValueChanged += (_, _) =>
        {
            Settings.ToolbarBlur = Math.Round(toolbarBlur.Value);
            ApplyToolbarAppearance();
            ScheduleSave();
        };
        toolbarGlass.IsCheckedChanged += (_, _) =>
        {
            Settings.ToolbarGlass = toolbarGlass.IsChecked == true;
            RefreshSettingAvailability();
            ApplyToolbarAppearance();
            ScheduleSave();
        };
        Control toolbarBlurRow = CreateRow("模糊程度", "毛玻璃的模糊半径（0–100）。", toolbarBlur);
        _refreshSettingAvailability.Add(() => toolbarBlurRow.IsEnabled = Settings.ToolbarGlass);

        SettingsContent.Children.Add(CreateCard(
            "控制窗",
            CreatePositionPicker(),
            CreateAppearancePreview("Toolbar"),
            CreateRow("大小", "控制窗与浮岛的统一缩放比例（0.6–2）。", scale),
            CreateRow("圆角", "控制窗、按钮与浮岛圆角（0–60 像素）。", radius),
            horizontalRow,
            verticalRow));

        SettingsContent.Children.Add(CreateCard(
            "外观",
            CreateRow("无字模式", "关闭后在图标下方显示按钮名称。", iconOnly),
            CreateRow("背景颜色", "颜色层与透明度只影响底色，不影响文字、按钮与模糊。", CreateSurfaceColorEditor(
                () => Settings.ToolbarBackgroundColor,
                value => { Settings.ToolbarBackgroundColor = value; Settings.ToolbarBackgroundColorCleared = false; ApplyToolbarAppearance(); ScheduleSave(); },
                () => Settings.ToolbarBackgroundOpacity,
                value => { Settings.ToolbarBackgroundOpacity = value; ApplyToolbarAppearance(); ScheduleSave(); },
                () => Settings.ToolbarBackgroundColorCleared,
                cleared => { Settings.ToolbarBackgroundColorCleared = cleared; ApplyToolbarAppearance(); ScheduleSave(); })),
            CreateRow("毛玻璃效果", "把控制窗后面的看板拍成快照后模糊，颜色层叠加在它上面。", toolbarGlass),
            toolbarBlurRow));

        SettingsContent.Children.Add(CreateCard(
            "自动隐藏",
            CreateRow("自动隐藏", "编辑、设置、弹出菜单与持续触摸期间保持可用。", autoHide),
            hideSecondsRow,
            hideAnimationRow));
    }

    /// <summary>关于：版本、仓库、更新源与检查更新。</summary>
    private void BuildAboutSettings()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
        StackPanel info = new() { Spacing = 4 };
        info.Children.Add(new TextBlock { Text = $"Pancake {version}", FontSize = 18, FontWeight = FontWeight.SemiBold });
        info.Children.Add(new TextBlock
        {
            Text = "班级作业看板 · Windows / Linux / macOS",
            FontSize = 12,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        });
        info.Children.Add(new TextBlock
        {
            Text = "本软件使用 HarmonyOS Sans 字体与 Fluent System Icons。字体版权与完整许可见程序 Assets/Fonts 目录。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        });

        Button repository = CreateActionButton("打开项目仓库（Edge-HH/Pancake）", () =>
            PlatformServices.ExternalLauncher.OpenUri("https://github.com/Edge-HH/Pancake"));

        ComboBox source = CreateComboBox(
            ["GitHub", "Gitee"],
            Settings.UpdateSource == "Gitee" ? 1 : 0,
            index =>
            {
                Settings.UpdateSource = index == 1 ? "Gitee" : "GitHub";
                _ = RefreshUpdateSourceHintAsync();
                ScheduleSave();
            });

        ToggleSwitch autoUpdate = CreateToggle(Settings.AutoUpdateEnabled);
        autoUpdate.IsCheckedChanged += (_, _) =>
        {
            Settings.AutoUpdateEnabled = autoUpdate.IsChecked == true;
            ScheduleSave();
        };

        TextBlock status = new()
        {
            Text = "尚未检查",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        };
        Button check = CreateActionButton("检查更新", async () => await CheckForUpdatesAsync(status, interactive: true));
        Grid checkRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        checkRow.Children.Add(status);
        Grid.SetColumn(check, 1);
        checkRow.Children.Add(check);

        SettingsContent.Children.Add(CreateCard("应用信息", info, repository));
        SettingsContent.Children.Add(CreateCard(
            "更新",
            CreateRow("更新源", "GitHub 为默认来源；选择 Gitee 时会与 GitHub 比较版本并提示同步延迟。", source),
            CreateRow("启动时自动检查更新", "关闭后仍可手动检查。", autoUpdate),
            checkRow));

        _ = RefreshUpdateSourceHintAsync();
    }

    /// <summary>更新源为 Gitee 时比较两边版本，提示是否应切换回 GitHub。</summary>
    private int _updateSourceRequest;

    private async Task RefreshUpdateSourceHintAsync()
    {
        _ = _viewModel;
        int request = ++_updateSourceRequest;
        if (Settings.UpdateSource != "Gitee") return;
        try
        {
            ReleaseUpdateService service = new();
            Task<ReleaseSnapshot?> gitee = service.GetLatestAsync("Gitee");
            Task<ReleaseSnapshot?> github = service.GetLatestAsync("GitHub");
            await Task.WhenAll(gitee, github);
            if (request != _updateSourceRequest || Settings.UpdateSource != "Gitee") return;
            if (ReleaseUpdateService.RecommendGitHub(gitee.Result, github.Result))
            {
                ShowStorageInfo("更新源提醒", $"Gitee 尚未同步 GitHub 的 {github.Result!.Tag}，建议将更新源切换到 GitHub。", isError: true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 网络不可用时保持静默，用户仍可手动检查所选源。
        }
    }

    /// <summary>控制窗方位选择器：3×3 布局里的 8 个方位。</summary>
    private Control CreatePositionPicker()
    {
        (string Position, string Label, int Row, int Column)[] positions =
        [
            ("TopLeft", "左上", 0, 0), ("TopCenter", "上居中", 0, 1), ("TopRight", "右上", 0, 2),
            ("CenterLeft", "左居中（竖置）", 1, 0), ("CenterRight", "右居中（竖置）", 1, 2),
            ("BottomLeft", "左下", 2, 0), ("BottomCenter", "下居中", 2, 1), ("BottomRight", "右下", 2, 2)
        ];
        Grid screen = new()
        {
            Height = 190,
            RowDefinitions = new RowDefinitions("*,*,*"),
            ColumnDefinitions = new ColumnDefinitions("*,*,*")
        };
        List<(ToggleButton Button, string Position)> buttons = [];
        foreach ((string position, string label, int row, int column) in positions)
        {
            ToggleButton button = new()
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                IsChecked = Settings.ToolbarPosition == position,
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left
                    : column == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalAlignment = row == 0 ? VerticalAlignment.Top
                    : row == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center
            };
            ToolTip.SetTip(button, label);
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                Settings.ToolbarPosition = position;
                foreach ((ToggleButton other, string otherPosition) in buttons)
                {
                    other.IsChecked = otherPosition == position;
                }

                ApplyToolbarAppearance();
                RefreshSettingAvailability();
                ScheduleSave();
            };
            buttons.Add((button, position));
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            screen.Children.Add(button);
        }

        _refreshSettingAvailability.Add(() =>
        {
            foreach ((ToggleButton button, string position) in buttons) button.IsChecked = Settings.ToolbarPosition == position;
        });
        Border frame = new()
        {
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(BoardTheme.TextColor.ToColor()),
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor()),
            Child = screen
        };
        return frame;
    }

    /// <summary>把设置页里改动布局后，把下拉框同步成实际模式。</summary>
    private void SyncLayoutChoiceSelection()
    {
        if (_layoutChoice is null) return;
        _layoutChoice.SelectedIndex = Settings.LayoutMode switch
        {
            "Board" => 1,
            "Clock" => 2,
            "Free" => 3,
            _ => 0
        };
    }

    private static ComboBox CreateComboBox(string[] items, int selectedIndex, Action<int> changed)
    {
        ComboBox combo = new() { ItemsSource = items, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex);
        };
        return combo;
    }

    private static Slider CreateSlider(double minimum, double maximum, double step, double value, bool snapToTick = false) =>
        new()
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = step,
            SmallChange = step,
            IsSnapToTickEnabled = snapToTick,
            Value = Math.Clamp(value, minimum, maximum),
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left
        };

    private static ToggleSwitch CreateToggle(bool value) => new()
    {
        IsChecked = value,
        OnContent = "开启",
        OffContent = "关闭"
    };

    private static Button CreateActionButton(string text, Action onClick)
    {
        Button button = new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 10)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>带异步操作的按钮；点击后按钮自身不参与后续布局改动。</summary>
    private static Button CreateActionButton(string text, Func<Task> onClick)
    {
        Button button = new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 10)
        };
        button.Click += async (_, _) => await onClick();
        return button;
    }

    private static Control CreateRow(string title, string description, Control control)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
        StackPanel text = new() { Spacing = 3 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        });
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(control);
        return row;
    }

    /// <summary>设置卡片：统一的标题样式与内边距。</summary>
    /// <summary>
    /// WinUI 原版设置页使用连续的 NavigationView 内容流，而不是卡片套卡片。
    /// 保留这个工厂方法是为了让各设置页共用一致的纵向间距，同时避免改动已有交互逻辑。
    /// </summary>
    private static Control CreateCard(string title, params Control[] content)
    {
        StackPanel stack = new() { Spacing = 18 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor())
        });
        foreach (Control item in content)
        {
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            stack.Children.Add(item);
        }
        return stack;
    }
    /// <summary>颜色值转 #RRGGBB；取色器与预设色共用同一种保存格式。</summary>
    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// 颜色选择面板：常用预设色 + 取色器。
    /// 取色器选中即生效，另有「应用颜色」按钮，用于清除颜色后重新应用同一种颜色。
    /// </summary>
    private static Control CreateColorRow(Func<string> getColor, Action<string> setColor, bool includeOpacity)
    {
        StackPanel panel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        string[] presets = ["#F7F7F9", "#B4B4C4", "#69696E", "#FBBF24", "#F87171", "#60A5FA", "#4ADE80", "#2DD4BF"];
        foreach (string preset in presets)
        {
            Button swatch = new()
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BoardColor.Parse(preset, BoardColor.White).ToColor()),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(BoardColor.FromArgb(64, 255, 255, 255).ToColor())
            };
            ToolTip.SetTip(swatch, preset);
            string captured = preset;
            swatch.Click += (_, _) => setColor(captured);
            panel.Children.Add(swatch);
        }

        ColorPicker picker = new()
        {
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            Color = BoardColor.Parse(getColor(), BoardTheme.SurfaceColor).ToColor(),
            Width = 320
        };
        Button apply = CreateActionButton("应用颜色", () => setColor(ToHex(picker.Color)));
        StackPanel flyoutContent = new() { Spacing = 10 };
        flyoutContent.Children.Add(picker);
        flyoutContent.Children.Add(apply);
        Button custom = new() { Content = "自定义颜色", Padding = new Thickness(12, 8) };
        Flyout flyout = new() { Content = flyoutContent };
        custom.Flyout = flyout;
        // 拖动取色器就实时应用，和旧版的 ColorChanged 行为一致。
        picker.ColorChanged += (_, _) => setColor(ToHex(picker.Color));
        apply.Click += (_, _) => flyout.Hide();
        panel.Children.Add(custom);

        _ = includeOpacity;
        return panel;
    }

    /// <summary>
    /// 表面底色编辑器（控制窗、跨区底图、时钟区、作业板、磁贴）：预设色与取色器、
    /// 背景颜色透明度、清除背景颜色与恢复主题背景色。透明度只作用于颜色层。
    /// </summary>
    private Control CreateSurfaceColorEditor(
        Func<string> getColor,
        Action<string> setColor,
        Func<double> getOpacity,
        Action<double> setOpacity,
        Func<bool> getCleared,
        Action<bool> setCleared)
    {
        StackPanel panel = new() { Spacing = 10, MinWidth = 260 };
        panel.Children.Add(CreateColorRow(getColor, value =>
        {
            setCleared(false);
            setColor(value);
        }, includeOpacity: false));

        // 界面上按透明度显示（0% 不透明、100% 完全透明），设置里保存的是不透明度。
        Slider transparency = CreateSlider(0, 100, 1, (1 - Math.Clamp(getOpacity(), 0, 1)) * 100, snapToTick: true);
        transparency.ValueChanged += (_, _) => setOpacity(1 - transparency.Value / 100);
        StackPanel transparencyRow = new() { Spacing = 4 };
        transparencyRow.Children.Add(new TextBlock { Text = "背景颜色透明度（%）", FontSize = 12 });
        transparencyRow.Children.Add(transparency);
        panel.Children.Add(transparencyRow);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button clear = CreateActionButton("清除背景颜色", () => setCleared(true));
        Button reset = CreateActionButton("恢复主题背景色", () =>
        {
            // 恢复主题色只清掉自定义颜色，透明度与毛玻璃保持不变。
            setCleared(false);
            setColor(string.Empty);
        });
        buttons.Children.Add(clear);
        buttons.Children.Add(reset);
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock
        {
            Text = "透明度只影响背景颜色：0% 不透明，100% 完全透明。清除颜色会保留已开启的毛玻璃效果和背景媒体。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        });

        _refreshSettingAvailability.Add(() =>
        {
            bool cleared = getCleared();
            transparency.Value = (1 - Math.Clamp(getOpacity(), 0, 1)) * 100;
            transparency.IsEnabled = !cleared;
            clear.IsEnabled = !cleared;
            clear.Content = cleared ? "已清除（点色块可恢复）" : "清除背景颜色";
        });
        return panel;
    }
}
