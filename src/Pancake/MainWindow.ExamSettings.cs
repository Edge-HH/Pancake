using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Services;
using Pancake.Shared;

namespace Pancake;

/// <summary>
/// 设置页「考试」：不连接集控时也能编辑本机考试安排（多天、一天多场、场间休息）。
/// 所有编辑按索引写回设置对象，布局同步重建列表实例也不会改到旧对象上。
/// </summary>
public sealed partial class MainWindow
{
    private StackPanel? _localExamDays;
    private TextBlock? _localExamProblems;
    private TextBlock? _localExamServerStatus;

    private UIElement BuildExamSettingsPage()
    {
        StackPanel page = SettingsStack();
        page.Children.Add(Note("服务器下发的启用考试计划优先显示；未连接集控或服务器没有启用计划时，使用下面的本机安排。本机安排只保存在这台大屏上。"));
        _localExamServerStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BoardTextMutedBrush"]
        };
        page.Children.Add(_localExamServerStatus);

        page.Children.Add(Toggle("启用本机考试安排", _settings.LocalExam.Enabled,
            value => { _settings.LocalExam.Enabled = value; UpdateExamDisplay(); },
            read: () => _settings.LocalExam.Enabled));

        NumberBox enter = new()
        {
            Header = "开考前提前进入（分钟）",
            Minimum = 0,
            Maximum = 120,
            Value = Math.Clamp(_settings.LocalExam.EnterMinutesBefore, 0, 120),
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        enter.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue)) return;
            _settings.LocalExam.EnterMinutesBefore = (int)Math.Round(args.NewValue);
            SettingChanged();
        };
        page.Children.Add(enter);

        TextBox room = new()
        {
            Header = "考场名（显示在考试画面右上角）",
            MaxLength = 40,
            Text = _settings.LocalExam.RoomName,
            PlaceholderText = "例如 第 12 考场"
        };
        room.TextChanged += (_, _) =>
        {
            if (_refreshingSettingControls) return;
            _settings.LocalExam.RoomName = room.Text;
            SettingChanged();
        };
        page.Children.Add(room);

        page.Children.Add(Toggle("大时钟与倒计时显示到秒", _settings.LocalExam.ShowSeconds,
            value => _settings.LocalExam.ShowSeconds = value,
            read: () => _settings.LocalExam.ShowSeconds));

        page.Children.Add(Heading("考试时间表"));
        _localExamDays = new StackPanel { Spacing = 14 };
        page.Children.Add(_localExamDays);

        Button addDay = new() { Content = "添加考试日", HorizontalAlignment = HorizontalAlignment.Left };
        addDay.Click += (_, _) =>
        {
            LocalExamSettings local = _settings.LocalExam;
            DateOnly date = local.Days.Count == 0 ? DateOnly.FromDateTime(DateTime.Now) : local.Days[^1].Date.AddDays(1);
            local.Days.Add(new LocalExamDay { Date = date });
            SettingChanged();
            RefreshLocalExamEditor();
        };
        page.Children.Add(addDay);

        _localExamProblems = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BoardWarningBrush"]
        };
        page.Children.Add(_localExamProblems);

        RefreshLocalExamEditor();
        return page;
    }

    /// <summary>重建本机考试安排的编辑区；设置页每次打开与结构性修改后调用。</summary>
    private void RefreshLocalExamEditor()
    {
        UpdateLocalExamServerStatus();
        if (_localExamDays is null) return;
        _localExamDays.Children.Clear();
        LocalExamSettings local = _settings.LocalExam;
        local.Days ??= [];
        if (local.Days.Count == 0) _localExamDays.Children.Add(Note("还没有考试日，点击「添加考试日」开始安排。"));
        for (int dayIndex = 0; dayIndex < local.Days.Count; dayIndex++)
            _localExamDays.Children.Add(LocalExamDayCard(dayIndex));
        UpdateLocalExamProblems();
    }

    private void UpdateLocalExamServerStatus()
    {
        if (_localExamServerStatus is null) return;
        _localExamServerStatus.Text = !_settings.CloudSync.Enabled
            ? "未连接集控：使用本机安排。"
            : CloudSync.Exam is { } exam
                ? $"已连接集控：服务器计划“{exam.PlanName}”优先于本机安排。"
                : "已连接集控，但服务器当前没有启用的考试计划。";
    }

    private void UpdateLocalExamProblems()
    {
        if (_localExamProblems is null) return;
        List<string> problems = ExamSchedule.ValidateLocal(_settings.LocalExam);
        _localExamProblems.Text = problems.Count == 0
            ? ""
            : "请检查：" + string.Join("；", problems.Take(4)) + (problems.Count > 4 ? $" 等 {problems.Count} 项" : "");
        _localExamProblems.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement LocalExamDayCard(int dayIndex)
    {
        LocalExamDay day = _settings.LocalExam.Days[dayIndex];
        StackPanel card = new() { Spacing = 10, Padding = new Thickness(14) };
        Border border = new()
        {
            Child = card,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["BoardSurfaceSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BoardLineBrush"]
        };

        Grid head = new() { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        DatePicker date = new() { Date = day.Date.ToDateTime(TimeOnly.MinValue), Header = "考试日期" };
        date.DateChanged += (_, args) =>
        {
            if (_settings.LocalExam.Days.Count <= dayIndex) return;
            _settings.LocalExam.Days[dayIndex].Date = DateOnly.FromDateTime(args.NewDate.Date);
            SettingChanged();
            UpdateLocalExamProblems();
        };
        head.Children.Add(date);
        Button removeDay = new() { Content = "删除当天", VerticalAlignment = VerticalAlignment.Bottom };
        removeDay.Click += (_, _) =>
        {
            if (_settings.LocalExam.Days.Count <= dayIndex) return;
            _settings.LocalExam.Days.RemoveAt(dayIndex);
            SettingChanged();
            RefreshLocalExamEditor();
        };
        Grid.SetColumn(removeDay, 1);
        head.Children.Add(removeDay);
        card.Children.Add(head);

        StackPanel rows = new() { Spacing = 8 };
        if (day.Segments.Count == 0) rows.Children.Add(Note("还没有时间段，先添加一场考试。"));
        for (int segmentIndex = 0; segmentIndex < day.Segments.Count; segmentIndex++)
            rows.Children.Add(LocalExamSegmentRow(dayIndex, segmentIndex));
        card.Children.Add(rows);

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button addExam = new() { Content = "添加场次" };
        addExam.Click += (_, _) => AddLocalExamSegment(dayIndex, ExamSegmentKinds.Exam);
        Button addBreak = new() { Content = "添加休息" };
        addBreak.Click += (_, _) => AddLocalExamSegment(dayIndex, ExamSegmentKinds.Break);
        Button fillBreaks = new() { Content = "自动填充休息" };
        fillBreaks.Click += (_, _) => FillLocalExamBreaks(dayIndex);
        actions.Children.Add(addExam);
        actions.Children.Add(addBreak);
        actions.Children.Add(fillBreaks);
        card.Children.Add(actions);
        return border;
    }

    private UIElement LocalExamSegmentRow(int dayIndex, int segmentIndex)
    {
        LocalExamSegment segment = _settings.LocalExam.Days[dayIndex].Segments[segmentIndex];
        Grid row = new() { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        ComboBox kind = new() { MinWidth = 96, VerticalAlignment = VerticalAlignment.Bottom };
        kind.Items.Add(new ComboBoxItem { Content = "考试", Tag = ExamSegmentKinds.Exam });
        kind.Items.Add(new ComboBoxItem { Content = "休息", Tag = ExamSegmentKinds.Break });
        kind.SelectedIndex = segment.Kind == ExamSegmentKinds.Break ? 1 : 0;
        row.Children.Add(kind);

        TextBox text = new()
        {
            Text = segment.Kind == ExamSegmentKinds.Exam ? segment.Subject : segment.Label,
            PlaceholderText = segment.Kind == ExamSegmentKinds.Exam ? "科目，如 语文" : "休息文案，如 课间休息",
            MaxLength = 30,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        TimePicker start = new()
        {
            ClockIdentifier = "24HourClock",
            SelectedTime = TimeSpan.FromMinutes(Math.Clamp(segment.StartMinute, 0, 1439)),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(start, 2);
        row.Children.Add(start);

        TimePicker end = new()
        {
            ClockIdentifier = "24HourClock",
            // TimePicker 最大显示 23:59；当天结束（1440）按 23:59 显示，用户不改动时保存值不变。
            SelectedTime = TimeSpan.FromMinutes(Math.Clamp(segment.EndMinute, 1, 1440) >= 1440 ? 1439 : Math.Clamp(segment.EndMinute, 1, 1440)),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetColumn(end, 3);
        row.Children.Add(end);

        Button remove = new() { Content = "删除", VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        void Update(Action<LocalExamSegment> change)
        {
            if (_settings.LocalExam.Days.Count <= dayIndex) return;
            List<LocalExamSegment> segments = _settings.LocalExam.Days[dayIndex].Segments;
            if (segments.Count <= segmentIndex) return;
            change(segments[segmentIndex]);
        }

        kind.SelectionChanged += (_, _) =>
        {
            if (_refreshingSettingControls || kind.SelectedItem is not ComboBoxItem item) return;
            Update(value => value.Kind = (string)item.Tag);
            SettingChanged();
            RefreshLocalExamEditor();
        };
        text.TextChanged += (_, _) =>
        {
            if (_refreshingSettingControls) return;
            Update(value => { if (value.Kind == ExamSegmentKinds.Exam) value.Subject = text.Text; else value.Label = text.Text; });
            SettingChanged();
            UpdateLocalExamProblems();
        };
        start.SelectedTimeChanged += (_, args) =>
        {
            if (_refreshingSettingControls || args.NewTime is not { } time) return;
            Update(value => value.StartMinute = (int)Math.Clamp(time.TotalMinutes, 0, 1439));
            SettingChanged();
            UpdateLocalExamProblems();
        };
        end.SelectedTimeChanged += (_, args) =>
        {
            if (_refreshingSettingControls || args.NewTime is not { } time) return;
            Update(value => value.EndMinute = (int)Math.Clamp(Math.Round(time.TotalMinutes), 1, 1440));
            SettingChanged();
            UpdateLocalExamProblems();
        };
        remove.Click += (_, _) =>
        {
            if (_settings.LocalExam.Days.Count <= dayIndex || _settings.LocalExam.Days[dayIndex].Segments.Count <= segmentIndex) return;
            _settings.LocalExam.Days[dayIndex].Segments.RemoveAt(segmentIndex);
            SettingChanged();
            RefreshLocalExamEditor();
        };
        return row;
    }

    private void AddLocalExamSegment(int dayIndex, string kind)
    {
        if (_settings.LocalExam.Days.Count <= dayIndex) return;
        LocalExamDay day = _settings.LocalExam.Days[dayIndex];
        int lastEnd = day.Segments.Count == 0 ? 0 : day.Segments.Max(x => x.EndMinute);
        int start = lastEnd is > 0 and < 1440 ? lastEnd : 9 * 60;
        int length = kind == ExamSegmentKinds.Break ? 15 : 90;
        int end = Math.Min(1440, start + length);
        if (end - start < 5) { start = Math.Max(0, 1440 - length); end = 1440; }
        day.Segments.Add(new LocalExamSegment { Kind = kind, StartMinute = start, EndMinute = end });
        day.Segments.Sort((left, right) => left.StartMinute.CompareTo(right.StartMinute));
        SettingChanged();
        RefreshLocalExamEditor();
    }

    private void FillLocalExamBreaks(int dayIndex)
    {
        if (_settings.LocalExam.Days.Count <= dayIndex) return;
        LocalExamDay day = _settings.LocalExam.Days[dayIndex];
        List<LocalExamSegment> exams = day.Segments.Where(x => x.Kind == ExamSegmentKinds.Exam).OrderBy(x => x.StartMinute).ToList();
        List<LocalExamSegment> breaks = [];
        for (int index = 1; index < exams.Count; index++)
        {
            if (exams[index - 1].EndMinute < exams[index].StartMinute)
            {
                breaks.Add(new LocalExamSegment
                {
                    Kind = ExamSegmentKinds.Break,
                    Label = ExamSchedule.DefaultBreakLabel,
                    StartMinute = exams[index - 1].EndMinute,
                    EndMinute = exams[index].StartMinute
                });
            }
        }
        day.Segments.RemoveAll(x => x.Kind == ExamSegmentKinds.Break);
        day.Segments.AddRange(breaks);
        day.Segments.Sort((left, right) => left.StartMinute.CompareTo(right.StartMinute));
        SettingChanged();
        RefreshLocalExamEditor();
    }
}
