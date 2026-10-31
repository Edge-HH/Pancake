using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;
using Pancake.Shared;

namespace Pancake;

/// <summary>“班级”页：集控连接状态、今日值日、班级公告，以及全部项目的同步情况与作业内容。</summary>
public sealed partial class MainWindow
{
    private ProjectDocument? _cloudSelectedProject;

    private void CloudControlButton_Click(object sender, RoutedEventArgs e) => ShowCloudControl();

    private void ShowCloudControl()
    {
        if (_isEditing) FinishEditing();
        RecordToolbarActivity(false);
        HideAutofillPopups();
        DisplayRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Collapsed;
        CloudControlRoot.Visibility = Visibility.Visible;
        // 考试画面只在看板上显示，打开班级页后立即收起。
        UpdateExamDisplay();
        ProjectCommands.Visibility = Visibility.Collapsed;
        EmptyProjectPanel.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Visible;
        EditBoardButton.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Visible;
        CloudControlButton.Visibility = Visibility.Collapsed;
        RenderCloudControl();
        RefreshIslands();
        _ = FlushCloudSyncAsync();
    }

    /// <summary>
    /// 按窗口实际深浅主题取画笔。Application.Current.Resources 直接索引只会拿到默认（深色）主题字典，
    /// 代码生成的控件在浅色主题下会出现深色底配黑字。
    /// </summary>
    private static Brush ResourceBrush(string key)
    {
        string theme = BoardTheme.IsLight ? "Light" : "Dark";
        foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out object? themed) && themed is ResourceDictionary resources && resources.TryGetValue(key, out object? value) && value is Brush brush)
                return brush;
        return (Brush)Application.Current.Resources[key];
    }

    private static Border Card(UIElement child, double padding = 14) => new()
    {
        Child = child, Background = ResourceBrush("BoardSurfaceSecondaryBrush"), BorderBrush = ResourceBrush("BoardLineBrush"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(padding)
    };

    private static TextBlock Muted(string text, double size = 12) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("BoardTextMutedBrush") };
    private static TextBlock Strong(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("BoardTextBrush") };

    private void RenderCloudControl()
    {
        CloudClassesPanel.Children.Clear();
        CloudSyncSettings settings = _settings.CloudSync;

        if (!settings.Enabled)
        {
            CloudClassesPanel.Children.Add(Muted("尚未连接集控。连接后可以在这里查看公告、今日值日和全部项目的同步情况。", 13));
            Button openSettings = new() { Content = "去设置连接班级", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
            openSettings.Click += (_, _) => { ShowSettings(); ShowSettingsPage("CloudSync"); };
            CloudClassesPanel.Children.Add(openSettings);
            CloudProjectsPanel.Children.Clear();
            CloudHomeworkPanel.Children.Clear();
            CloudProjectHint.Text = "连接后查看项目";
            CloudSyncProgressText.Text = "未连接";
            CloudHomeworkTitle.Text = "还没有连接班级";
            CloudHomeworkSubtitle.Text = "打开设置 → 集控，输入配对码即可连接";
            return;
        }

        // 班级与连接状态
        StackPanel status = new() { Spacing = 6 };
        StackPanel title = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Microsoft.UI.Xaml.Shapes.Ellipse dot = new() { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Fill = ResourceBrush(_cloudRealtime.IsConnected ? "BoardSuccessBrush" : CloudSync.LastSyncError is null ? "BoardWarningBrush" : "BoardDangerBrush") };
        title.Children.Add(dot);
        title.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(settings.ClassName) ? "已连接班级" : settings.ClassName, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ResourceBrush("BoardTextBrush") });
        status.Children.Add(title);
        status.Children.Add(Muted(CloudStatusLine()));
        if (CloudSync.LastSyncError is { } error) status.Children.Add(new TextBlock { Text = error, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("BoardDangerBrush") });
        string policy = settings.LayoutPolicy switch { LayoutPolicies.Managed => "网页统一下发", LayoutPolicies.Off => "不同步", _ => "双向同步" };
        status.Children.Add(Muted($"班级布局：{(settings.SyncLayout ? policy : "本机已关闭")}"));
        Button syncNow = new() { Content = CloudSync.IsRunning ? "正在同步…" : "立即同步", IsEnabled = !CloudSync.IsRunning, Margin = new Thickness(0, 4, 0, 0) };
        syncNow.Click += async (_, _) => { syncNow.IsEnabled = false; await FlushCloudSyncAsync(); };
        status.Children.Add(syncNow);
        CloudClassesPanel.Children.Add(Card(status));

        BoardInfo? board = CloudSync.Board;
        // 今日值日
        StackPanel duty = new() { Spacing = 6 };
        duty.Children.Add(SectionTitle("Clock", "今日值日", board?.Duty?.Label));
        if (board?.Duty is { } today)
            foreach (DutyTaskAssignment task in today.Tasks)
            {
                StackPanel row = new() { Spacing = 2 };
                row.Children.Add(new TextBlock { Text = task.Task, FontSize = 12, Foreground = ResourceBrush("BoardTextMutedBrush") });
                row.Children.Add(Strong(string.Join("、", task.Students), 15));
                duty.Children.Add(row);
            }
        else duty.Children.Add(Muted(board is null ? "同步后显示" : "今天没有值日安排"));
        CloudClassesPanel.Children.Add(Card(duty));

        // 公告
        StackPanel notices = new() { Spacing = 10 };
        DateTime now = DateTime.UtcNow;
        List<AnnouncementItem> active = board?.Announcements.Where(a => (a.StartsAt is null || a.StartsAt <= now) && (a.ExpiresAt is null || a.ExpiresAt > now)).ToList() ?? [];
        notices.Children.Add(SectionTitle("Info", "班级公告", active.Count > 0 ? $"{active.Count} 条" : null));
        foreach (AnnouncementItem item in active)
        {
            StackPanel row = new() { Spacing = 3 };
            StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (item.Level != "info")
                head.Children.Add(new Border
                {
                    Background = ResourceBrush(item.Level == "urgent" ? "BoardDangerBrush" : "BoardWarningBrush"), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                    Child = new TextBlock { Text = item.Level == "urgent" ? "紧急" : "重要", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) }
                });
            TextBlock titleText = Strong(item.Title, 15); titleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            head.Children.Add(titleText);
            row.Children.Add(head);
            if (!string.IsNullOrWhiteSpace(item.Body)) row.Children.Add(Strong(item.Body, 13));
            row.Children.Add(Muted($"{item.Author} · {item.CreatedAt.ToLocalTime():M月d日 HH:mm}", 11));
            notices.Children.Add(row);
        }
        if (active.Count == 0) notices.Children.Add(Muted(board is null ? "同步后显示" : "暂无公告"));
        CloudClassesPanel.Children.Add(Card(notices));

        RenderCloudProjects();
    }

    private static StackPanel SectionTitle(string icon, string text, string? trailing)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(new FluentIcon { Symbol = icon, FontSize = 15, Foreground = ResourceBrush("BoardBrandBrush"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = ResourceBrush("BoardTextBrush") });
        if (trailing is not null) row.Children.Add(new TextBlock { Text = trailing, FontSize = 12, Foreground = ResourceBrush("BoardTextMutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private void RenderCloudProjects()
    {
        CloudProjectsPanel.Children.Clear();
        List<ProjectDocument> projects = _library.Projects.OrderByDescending(x => x.AssignmentDate).ThenByDescending(x => x.LastUsedAt).ToList();
        CloudProjectHint.Text = projects.Count == 0 ? "暂无项目" : $"{projects.Count} 个项目";
        int synced = 0;
        foreach (ProjectDocument project in projects)
        {
            bool isSynced = CloudSyncService.IsSynced(_library, project);
            if (isSynced) synced++;
            Grid content = new() { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel text = new() { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ResourceBrush("BoardTextBrush"), FontWeight = project.Id == _library.ActiveProjectId ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal });
            text.Children.Add(Muted((project.AssignmentDate is { } d ? $"{d:M月d日}" : "无日期") + (project.Id == _library.ActiveProjectId ? " · 当前" : ""), 11));
            content.Children.Add(text);
            FluentIcon icon = new() { Symbol = isSynced ? "CloudCheckmark" : "ArrowSync", Foreground = ResourceBrush(isSynced ? "BoardSuccessBrush" : "BoardTextMutedBrush"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(icon, isSynced ? $"已同步 · 修订 {project.SyncRevision}" : "等待上传");
            Grid.SetColumn(icon, 1);
            content.Children.Add(icon);
            Button item = new() { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 10, 8), Tag = project };
            if (ReferenceEquals(project, _cloudSelectedProject)) item.Background = ResourceBrush("BoardBrandSoftBrush");
            item.Click += (_, _) => { _cloudSelectedProject = project; RenderCloudProjects(); };
            CloudProjectsPanel.Children.Add(item);
        }
        CloudSyncProgressText.Text = projects.Count == 0 ? "没有本地项目" : $"已同步 {synced}/{projects.Count} 个项目" + (_library.SyncQueue.Count > 0 ? $" · 待上传 {_library.SyncQueue.Count} 项" : "");
        if (_cloudSelectedProject is null || !projects.Contains(_cloudSelectedProject)) _cloudSelectedProject = CurrentProject ?? projects.FirstOrDefault();
        if (_cloudSelectedProject is not null) RenderCloudHomework(_cloudSelectedProject);
        else
        {
            CloudHomeworkTitle.Text = "选择项目";
            CloudHomeworkSubtitle.Text = "项目同步后会显示作业内容";
            CloudHomeworkPanel.Children.Clear();
        }
    }

    private void RenderCloudHomework(ProjectDocument project)
    {
        CloudHomeworkPanel.Children.Clear();
        CloudHomeworkTitle.Text = project.Name;
        string revision = project.SyncRevision > 0 ? $"修订 {project.SyncRevision}" : "尚未上传";
        CloudHomeworkSubtitle.Text = project.AssignmentDate is { } date ? $"{date:yyyy年M月d日} · {revision}" : revision;
        if (project.Id != _library.ActiveProjectId)
        {
            Button open = new() { Content = "在看板上打开", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += async (_, _) => await RunProjectAction(async () => await SwitchProjectAsync(project));
            CloudHomeworkPanel.Children.Add(open);
        }
        foreach (SubjectState subject in project.Subjects)
        {
            Border subjectCard = new()
            {
                Background = ResourceBrush("BoardSurfaceBrush"), BorderBrush = ResourceBrush("BoardLineBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 14, 18, 14)
            };
            StackPanel body = new() { Spacing = 10 };
            StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(new Border { Width = 4, Height = 20, CornerRadius = new CornerRadius(2), Background = ViewModels.MainViewModel.BrushFromHex(ColorPalette.ResolveAccent(subject.AccentHex, subject.IsAccentExplicit, ColorPalette.IsMacaron)) });
            TextBlock subjectName = Strong(subject.Name, 19); subjectName.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            head.Children.Add(subjectName);
            body.Children.Add(head);
            List<HomeworkState> entries = subject.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Content) || e.Attachments.Count > 0 || e.HasHandwriting).ToList();
            if (entries.Count == 0) body.Children.Add(Muted("暂无作业", 14));
            foreach (HomeworkState entry in entries)
            {
                StackPanel row = new() { Spacing = 4 };
                row.Children.Add(Strong(string.IsNullOrWhiteSpace(entry.Content) ? "（手写或图片作业）" : entry.Content.Trim(), 16));
                if (entry.Attachments.Count > 0 || entry.HasHandwriting)
                    row.Children.Add(Muted(string.Join(" · ", new[] { entry.Attachments.Count > 0 ? $"{entry.Attachments.Count} 个附件" : "", entry.HasHandwriting ? "含手写" : "" }.Where(x => x.Length > 0))));
                body.Children.Add(row);
            }
            subjectCard.Child = body;
            CloudHomeworkPanel.Children.Add(subjectCard);
        }
        if (project.Subjects.Count == 0) CloudHomeworkPanel.Children.Add(Muted("这个项目还没有科目和作业。", 14));
    }
}
