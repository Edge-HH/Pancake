using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Controls;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 自动填充设置：学科补全与作业补全。
/// 候选匹配、阈值统计与过期清理都在核心层的 AutofillService 中，这里只做展示与编辑。
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] AutofillLevels = ["Loose", "Normal", "Strict"];

    private static string LevelLabel(string level, bool homework) => (level, homework) switch
    {
        ("Loose", false) => "宽松",
        ("Strict", false) => "严格",
        (_, false) => "正常",
        ("Loose", true) => "宽松（2 次）",
        ("Strict", true) => "严格（5 次）",
        _ => "正常（3 次）"
    };

    private static int LevelIndex(string? level) => AutofillService.NormalizeLevel(level) switch
    {
        "Loose" => 0,
        "Strict" => 2,
        _ => 1
    };

    /// <summary>学科库：作业补全的生效范围与手动添加都从这里取可选学科。</summary>
    private IEnumerable<SubjectSuggestion> SubjectLibrary() =>
        _viewModel.Autofill is { } autofill
            ? autofill.Subject.Subjects.OrderBy(item => item.Name, StringComparer.CurrentCulture)
            : Enumerable.Empty<SubjectSuggestion>();

    /// <summary>自动填充 · 学科补全。</summary>
    private void BuildAutofillSubjectSettings()
    {
        if (_viewModel.Autofill is not { } autofill)
        {
            SettingsContent.Children.Add(CreateCard("学科补全", new TextBlock { Text = "项目数据尚未就绪。" }));
            return;
        }

        SubjectCompletionSettings subject = autofill.Subject;
        ToggleSwitch enabled = CreateToggle(subject.Enabled);
        enabled.IsCheckedChanged += (_, _) => { subject.Enabled = enabled.IsChecked == true; ScheduleSave(); };

        ComboBox level = CreateComboBox(
            [LevelLabel("Loose", false), LevelLabel("Normal", false), LevelLabel("Strict", false)],
            LevelIndex(subject.MatchLevel),
            index => { subject.MatchLevel = AutofillLevels[index]; ScheduleSave(); });

        StackPanel list = new() { Spacing = 6 };
        void RebuildList()
        {
            list.Children.Clear();
            foreach (SubjectSuggestion item in subject.Subjects)
            {
                list.Children.Add(CreateSubjectRow(subject, item, RebuildList));
            }

            if (subject.Subjects.Count == 0)
            {
                list.Children.Add(new TextBlock { Text = "列表为空，可点击“刷新列表”恢复内置学科。", IsEnabled = false });
            }
        }

        TextBox newName = new() { Watermark = "新增学科名称", Width = 200 };
        Button add = CreateActionButton("添加", () =>
        {
            string name = (newName.Text ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > AutofillService.MaxSubjectNameLength) return;
            if (subject.Subjects.Any(candidate => candidate.Name == name)) return;
            subject.Subjects.Add(new SubjectSuggestion { Name = name, Source = AutofillService.ManualSource });
            newName.Text = string.Empty;
            RebuildList();
            ScheduleSave();
        });
        StackPanel addRow = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        addRow.Children.Add(newName);
        addRow.Children.Add(add);

        Button refresh = CreateActionButton("刷新列表", () =>
        {
            autofill.EnsureBuiltIns();
            autofill.Prune(DateTime.Now);
            RebuildList();
            ScheduleSave();
        });

        RebuildList();
        SettingsContent.Children.Add(CreateCard(
            "学科补全",
            CreateRow("启用学科补全", "在磁贴标题里输入中文、全拼或首字母时弹出候选。", enabled),
            CreateRow("匹配程度", "宽松：输入 1 个字符即提示，首字母允许跳字；严格：2 个字符起且只认完整前缀。", level),
            CreateRow("学科列表", "内置学科与手动添加的学科都会参与匹配；采纳后同时套用该学科的颜色。", list),
            CreateRow("添加学科", "只保存名称与颜色，不会自动收录项目里的科目。", addRow),
            refresh));
    }

    /// <summary>单个学科条目：名称、启用开关、颜色与删除。</summary>
    private Control CreateSubjectRow(SubjectCompletionSettings settings, SubjectSuggestion item, Action rebuild)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 10 };
        TextBox name = new() { Text = item.Name, MaxLength = AutofillService.MaxSubjectNameLength, Width = 180 };
        name.TextChanged += (_, _) =>
        {
            string value = (name.Text ?? string.Empty).Trim();
            if (value.Length == 0) return;
            item.Name = value;
            ScheduleSave();
        };
        row.Children.Add(name);

        ToggleSwitch enabled = CreateToggle(item.Enabled);
        enabled.IsCheckedChanged += (_, _) => { item.Enabled = enabled.IsChecked == true; ScheduleSave(); };
        Grid.SetColumn(enabled, 1);
        enabled.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(enabled);

        Button color = CreateSubjectColorButton(item);
        Grid.SetColumn(color, 2);
        color.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(color);

        Button remove = CreateActionButton("删除", () =>
        {
            settings.Subjects.Remove(item);
            rebuild();
            ScheduleSave();
        });
        Grid.SetColumn(remove, 3);
        remove.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(remove);
        return row;
    }

    /// <summary>
    /// 学科颜色：预设色、取色器自定义颜色与「随机」配色。
    /// 随机配色用预设色渐变表示每次采纳都会换成其中一个，手动取色会关闭随机。
    /// </summary>
    private Button CreateSubjectColorButton(SubjectSuggestion item)
    {
        Border swatch = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(4) };
        TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(swatch);
        content.Children.Add(label);
        Button button = new()
        {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Content = content
        };
        void Refresh()
        {
            swatch.Background = item.RandomColor
                ? RandomSubjectColorBrush()
                : new SolidColorBrush(BoardColor.Parse(ColorPalette.Resolve(item.Color), BoardColor.White).ToColor());
            label.Text = item.RandomColor ? "随机颜色" : "默认颜色";
            ToolTip.SetTip(button, item.RandomColor
                ? "随机配色：每次采纳都从预设色里取一个"
                : ColorPalette.Resolve(item.Color));
        }

        StackPanel flyoutContent = new() { Spacing = 10, Margin = new Thickness(6) };
        WrapPanel colors = new() { Orientation = Orientation.Horizontal };
        flyoutContent.Children.Add(colors);

        // 精确取色放在折叠区里，默认只给常用预设色。
        ColorPicker picker = new()
        {
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            Width = 300,
            Color = BoardColor.Parse(ColorPalette.Resolve(item.Color), BoardTheme.SurfaceColor).ToColor()
        };
        StackPanel customArea = new() { Spacing = 8, IsVisible = false };
        customArea.Children.Add(picker);

        // 浮层里的普通按钮：先建控件再挂事件，方便在事件里回头改按钮自身的文字。
        Button customToggle = new() { Content = "自定义颜色", Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left };
        customToggle.Click += (_, _) =>
        {
            customArea.IsVisible = !customArea.IsVisible;
            customToggle.Content = customArea.IsVisible ? "收起自定义" : "自定义颜色";
        };

        Button random = new()
        {
            Content = item.RandomColor ? "取消随机" : "随机配色",
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        random.Click += (_, _) =>
        {
            item.RandomColor = !item.RandomColor;
            random.Content = item.RandomColor ? "取消随机" : "随机配色";
            Refresh();
            ScheduleSave();
        };

        foreach (string preset in ColorPalette.Presets)
        {
            Button presetSwatch = new()
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BoardColor.Parse(preset, BoardColor.White).ToColor())
            };
            presetSwatch.Click += (_, _) =>
            {
                item.Color = preset;
                item.RandomColor = false;
                random.Content = "随机配色";
                Refresh();
                button.Flyout?.Hide();
                ScheduleSave();
            };
            colors.Children.Add(presetSwatch);
        }

        flyoutContent.Children.Add(customToggle);
        flyoutContent.Children.Add(customArea);

        // 随机配色与取色互斥：手动取色即视为放弃随机。
        picker.ColorChanged += (_, _) =>
        {
            item.Color = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
            item.RandomColor = false;
            random.Content = "随机配色";
            Refresh();
            ScheduleSave();
        };
        flyoutContent.Children.Add(random);
        button.Flyout = new Flyout { Content = flyoutContent };
        Refresh();
        return button;
    }

    /// <summary>随机配色的色卡：用当前色系的预设色渐变表示每次补全都会换成其中一个。</summary>
    private static IBrush RandomSubjectColorBrush()
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        IReadOnlyList<string> presets = ColorPalette.Presets;
        for (int index = 0; index < presets.Count; index++)
        {
            brush.GradientStops.Add(new GradientStop(
                BoardColor.Parse(ColorPalette.Resolve(presets[index]), BoardColor.White).ToColor(),
                presets.Count <= 1 ? 0 : index / (double)(presets.Count - 1)));
        }

        return brush;
    }

    /// <summary>自动填充 · 作业补全。</summary>
    private void BuildAutofillHomeworkSettings()
    {
        if (_viewModel.Autofill is not { } autofill)
        {
            SettingsContent.Children.Add(CreateCard("作业补全", new TextBlock { Text = "项目数据尚未就绪。" }));
            return;
        }

        HomeworkCompletionSettings homework = autofill.Homework;
        ToggleSwitch enabled = CreateToggle(homework.Enabled);
        enabled.IsCheckedChanged += (_, _) => { homework.Enabled = enabled.IsChecked == true; ScheduleSave(); };

        ToggleSwitch autoRecord = CreateToggle(homework.AutoRecord);
        autoRecord.IsCheckedChanged += (_, _) => { homework.AutoRecord = autoRecord.IsChecked == true; ScheduleSave(); };

        ComboBox level = CreateComboBox(
            [LevelLabel("Loose", true), LevelLabel("Normal", true), LevelLabel("Strict", true)],
            LevelIndex(homework.RecordLevel),
            index => { homework.RecordLevel = AutofillLevels[index]; ScheduleSave(); });

        ComboBox isolation = CreateComboBox(
            ["全局", "分学科"],
            homework.Isolation == "Subject" ? 1 : 0,
            index => { homework.Isolation = index == 1 ? "Subject" : "Global"; ScheduleSave(); });

        StackPanel promoted = new() { Spacing = 6 };
        StackPanel pending = new() { Spacing = 6 };
        void RebuildLists()
        {
            promoted.Children.Clear();
            pending.Children.Clear();
            foreach (HomeworkSuggestion item in homework.Items.Where(item => item.Promoted)
                         .OrderByDescending(item => item.LastSeenAt)
                         .ThenBy(item => item.Text, StringComparer.CurrentCulture))
            {
                promoted.Children.Add(CreateHomeworkRow(homework, item, rebuild: RebuildLists));
            }

            foreach (HomeworkSuggestion item in homework.Items.Where(item => !item.Promoted)
                         .OrderByDescending(item => item.Count).ThenByDescending(item => item.LastSeenAt))
            {
                pending.Children.Add(CreateHomeworkRow(homework, item, rebuild: RebuildLists));
            }

            if (promoted.Children.Count == 0)
            {
                promoted.Children.Add(new TextBlock { Text = "还没有已收录的作业类型，自动记录满足阈值后会出现在这里。", IsEnabled = false });
            }

            if (pending.Children.Count == 0)
            {
                pending.Children.Add(new TextBlock { Text = "没有等待收录的作业名称。", IsEnabled = false });
            }

            // 已屏蔽的词与待收录放在同一张卡片里，和旧版一致。
            if (homework.Blocked.Count > 0)
            {
                pending.Children.Add(new TextBlock
                {
                    Text = "已屏蔽",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                foreach (string text in homework.Blocked.ToList())
                {
                    pending.Children.Add(CreateBlockedHomeworkRow(homework, text, RebuildLists));
                }
            }
        }

        Button refresh = CreateActionButton("刷新列表", () =>
        {
            autofill.Prune(DateTime.Now);
            RebuildLists();
            ScheduleSave();
        });
        Button clear = CreateActionButton("清空作业词库", () =>
        {
            autofill.ClearHomework();
            RebuildLists();
            ScheduleSave();
        });
        // 手动添加的条目永不过期，用于补上自动学习还没收录的名称。
        Button addType = CreateActionButton("添加作业类型", () => AddHomeworkTypeAsync(homework, RebuildLists));
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(refresh);
        actions.Children.Add(addType);
        actions.Children.Add(clear);

        RebuildLists();
        SettingsContent.Children.Add(CreateCard(
            "作业补全",
            CreateRow("启用作业补全", "在作业正文里输入已收录的名称时弹出候选。", enabled),
            CreateRow("自动记录常输入的作业名称", "关闭后不再学习新词，已记录的内容仍可继续使用与删除。", autoRecord),
            CreateRow("收录阈值", "同一个词出现到该次数后进入候选；过期规则见下方列表。", level),
            CreateRow("记录隔离", "分学科时自动学习写入当前学科桶，全局词始终可用；切换隔离不迁移历史数据。", isolation),
            CreateRuleNote("一条作业编辑结束时统计一次：按空白、标点与数字切分，只保留长度 2–12 且含汉字或两个以上连续字母的词；页码碎片、单字母（如 P）和默认占位文字不会记录。"),
            CreateRuleNote("「双练一测P30」「双练一测第 3 页」这类写法只记录「双练一测」，不会把页码字母或「第」带进名称；只有文本真正改动过的作业才计入次数，反复打开同一条不会重复累计。"),
            CreateRuleNote("全局隔离把所有词记进同一个词库；分学科隔离把自动学习写入当前学科，全局词仍然始终可用。待收录 14 天未再出现即清除，已收录 90 天未使用自动移除，手动添加的条目永不过期。"),
            actions));

        SettingsContent.Children.Add(CreateCard("已收录", promoted));
        SettingsContent.Children.Add(CreateCard("待收录", pending));
    }

    /// <summary>规则说明：设置页里的多段解释文字，样式统一。</summary>
    private static TextBlock CreateRuleNote(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 620,
        Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
    };

    /// <summary>作业候选条目：文本、范围、编辑、屏蔽与删除。</summary>
    private Control CreateHomeworkRow(HomeworkCompletionSettings settings, HomeworkSuggestion item, Action rebuild)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 10 };
        StackPanel text = new() { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = item.Text, FontSize = 14 });
        TextBlock detail = new()
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
        };
        text.Children.Add(detail);
        row.Children.Add(text);

        void RefreshDetail()
        {
            string scope = item.IsGlobal
                ? "全局"
                : item.Subjects.Count == 0 ? "未指定学科" : string.Join("、", item.Subjects);
            detail.Text = item.Promoted
                ? $"{scope} · 已收录 · 剩余 {AutofillService.RemainingDays(item, DateTime.Now)} 天未使用会移除"
                : $"{scope} · 待收录 · 还差 {AutofillService.RemainingCount(item, settings.RecordLevel)} 次";
        }

        RefreshDetail();
        int column = 1;
        if (item.Promoted)
        {
            // 生效范围：全局，或任选若干学科；改动后就地刷新生效范围文字，浮层保持打开。
            Button scope = new() { Content = "生效范围", Padding = new Thickness(12, 8), VerticalAlignment = VerticalAlignment.Center };
            BuildHomeworkScopeFlyout(item, scope, RefreshDetail);
            Grid.SetColumn(scope, column++);
            row.Children.Add(scope);

            Button edit = CreateActionButton("编辑", async () =>
            {
                if (await ShowInputAsync("编辑作业名称", "作业名称", item.Text, "例如 同步练习册",
                        AutofillService.MaxTokenLength, "保存") is not { } value) return;
                if (!(_viewModel.Autofill?.RenameHomework(item, value) ?? false)) return;
                rebuild();
                ScheduleSave();
            });
            Grid.SetColumn(edit, column++);
            edit.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(edit);
        }

        Button block = CreateActionButton(item.Promoted ? "屏蔽" : "不收录", () =>
        {
            _viewModel.Autofill?.BlockHomework(item.Text);
            rebuild();
            ScheduleSave();
        });
        Grid.SetColumn(block, column++);
        block.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(block);

        Button remove = CreateActionButton(item.Promoted ? "删除" : "移除", () =>
        {
            _viewModel.Autofill?.RemoveHomework(item);
            rebuild();
            ScheduleSave();
        });
        Grid.SetColumn(remove, column);
        remove.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(remove);
        return row;
    }

    /// <summary>已屏蔽的词：说明当前状态并提供「恢复收录」。</summary>
    private Control CreateBlockedHomeworkRow(HomeworkCompletionSettings settings, string text, Action rebuild)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Opacity = 0.55 };
        StackPanel label = new() { Spacing = 2 };
        label.Children.Add(new TextBlock { Text = text, FontSize = 14 });
        label.Children.Add(new TextBlock
        {
            Text = "已屏蔽，不再记录和补全",
            FontSize = 11,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
        });
        row.Children.Add(label);

        Button restore = CreateActionButton("恢复收录", () =>
        {
            _viewModel.Autofill?.UnblockHomework(text);
            rebuild();
            ScheduleSave();
        });
        Grid.SetColumn(restore, 1);
        restore.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(restore);
        return row;
    }

    /// <summary>
    /// 生效范围浮层：勾选「全局」会清掉学科勾选，勾选任一学科则取消全局。
    /// 改动即时写回条目，不重建整页，避免浮层被销毁。
    /// </summary>
    private void BuildHomeworkScopeFlyout(
        HomeworkSuggestion item,
        Button button,
        Action refreshDetail)
    {
        StackPanel content = new() { Spacing = 6, Margin = new Thickness(8) };
        CheckBox global = new() { Content = "全局", IsChecked = item.IsGlobal };
        content.Children.Add(global);
        StackPanel subjects = new() { Spacing = 2 };
        List<(CheckBox Box, string Name)> boxes = [];
        foreach (SubjectSuggestion subject in SubjectLibrary())
        {
            CheckBox box = new()
            {
                Content = subject.Name,
                IsChecked = item.Subjects.Contains(subject.Name, StringComparer.OrdinalIgnoreCase)
            };
            boxes.Add((box, subject.Name));
            subjects.Children.Add(box);
        }

        content.Children.Add(new ScrollViewer
        {
            Content = subjects,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        void Apply()
        {
            item.IsGlobal = global.IsChecked == true;
            item.Subjects = boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Name).ToList();
            refreshDetail();
            ScheduleSave();
        }

        global.IsCheckedChanged += (_, _) =>
        {
            if (global.IsChecked == true)
            {
                foreach (var (box, _) in boxes) box.IsChecked = false;
            }

            Apply();
        };
        foreach (var (box, _) in boxes)
        {
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) global.IsChecked = false;
                Apply();
            };
        }

        button.Flyout = new Flyout { Content = content };
    }

    /// <summary>手动添加作业类型：名称 + 生效范围（全局或若干学科）。</summary>
    private async Task AddHomeworkTypeAsync(HomeworkCompletionSettings homework, Action rebuild)
    {
        if (homework.Items.Count(item => item.Source == AutofillService.ManualSource) >= AutofillService.MaxManualItems)
        {
            await ShowMessageAsync("无法添加",
                $"手动添加的作业类型最多 {AutofillService.MaxManualItems} 条，请先删除不再使用的条目。", "知道了");
            return;
        }

        TextBox name = new()
        {
            Watermark = "例如 同步练习册",
            MaxLength = AutofillService.MaxTokenLength,
            Width = 320,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        CheckBox global = new() { Content = "全局", IsChecked = true };
        StackPanel subjectList = new() { Spacing = 2 };
        List<(CheckBox Box, string Name)> boxes = [];
        foreach (SubjectSuggestion subject in SubjectLibrary())
        {
            CheckBox box = new() { Content = subject.Name };
            boxes.Add((box, subject.Name));
            subjectList.Children.Add(box);
        }

        global.IsCheckedChanged += (_, _) =>
        {
            if (global.IsChecked == true)
            {
                foreach (var (box, _) in boxes) box.IsChecked = false;
            }
        };
        foreach (var (box, _) in boxes)
        {
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) global.IsChecked = false;
            };
        }

        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "作业名称", FontSize = 14 });
        content.Children.Add(name);
        content.Children.Add(new TextBlock { Text = "生效范围", FontSize = 14 });
        content.Children.Add(global);
        content.Children.Add(new ScrollViewer
        {
            Content = subjectList,
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        if (await ShowChoiceAsync("添加作业类型", string.Empty, "添加", null, "取消", content) != DialogResult.Primary) return;
        string value = (name.Text ?? string.Empty).Trim();
        if (value.Length == 0) return;
        bool isGlobal = global.IsChecked == true;
        List<string> subjects = boxes.Where(pair => pair.Box.IsChecked == true).Select(pair => pair.Name).ToList();

        HomeworkSuggestion? existing = homework.Items.FirstOrDefault(
            candidate => candidate.Text.Equals(value, StringComparison.OrdinalIgnoreCase) && candidate.IsGlobal == isGlobal);
        if (existing is not null)
        {
            existing.IsGlobal = isGlobal;
            existing.Subjects = subjects;
            existing.Promoted = true;
            existing.LastSeenAt = DateTime.Now;
        }
        else
        {
            homework.Items.Add(new HomeworkSuggestion
            {
                Text = value,
                Source = AutofillService.ManualSource,
                IsGlobal = isGlobal,
                Subjects = subjects,
                Count = AutofillService.RecordThreshold(homework.RecordLevel),
                Promoted = true,
                FirstSeenAt = DateTime.Now,
                LastSeenAt = DateTime.Now
            });
        }

        rebuild();
        ScheduleSave();
    }
}
