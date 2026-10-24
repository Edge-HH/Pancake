using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Services;
using Pancake.Shared;

namespace Pancake;

/// <summary>
/// 集控同步与界面的衔接：保存后防抖同步、定时兜底同步、实时通知触发同步，
/// 以及服务器版本替换当前项目、应用班级布局、显示公告与值日。
/// </summary>
public sealed partial class MainWindow
{
    // 本机保存后合并连续改动再上传；实时通知按类型定向同步，短时间内的多条通知合并为一次。
    private readonly DispatcherTimer _cloudSyncDebounce = new() { Interval = TimeSpan.FromSeconds(0.8) };
    private readonly DispatcherTimer _cloudEventDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _cloudProjectDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _cloudLayoutDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _cloudSyncPeriodic = new() { Interval = TimeSpan.FromMinutes(3) };
    private readonly HashSet<Guid> _pendingCloudProjects = [];
    private readonly CloudRealtimeClient _cloudRealtime = new();
    // 班务通知只刷新公告与值日；轮播与定时两个计时器只驱动班务条重绘，不发请求。
    private readonly DispatcherTimer _boardRefreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _boardRotation = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly DispatcherTimer _boardSchedule = new();
    private int _boardRotationIndex;
    // 编辑看板期间收到影响磁贴外观的班级设置：结束编辑后再重建磁贴，避免打断正在进行的编辑。
    private bool _cloudTilesRebuildPending;
    private string _boardBannerDismissedKey = "";

    private CloudSyncService CloudSync => _cloudSyncService ??= new CloudSyncService(_projectStore);
    private CloudSyncService? _cloudSyncService;

    private void InitializeCloudSync()
    {
        _cloudSyncDebounce.Tick += (_, _) => { _cloudSyncDebounce.Stop(); _ = FlushCloudSyncAsync(); };
        _cloudEventDebounce.Tick += (_, _) => { _cloudEventDebounce.Stop(); _ = FlushCloudSyncAsync(); };
        _cloudProjectDebounce.Tick += (_, _) =>
        {
            _cloudProjectDebounce.Stop();
            List<Guid> ids = [.. _pendingCloudProjects];
            _pendingCloudProjects.Clear();
            _ = RunCloudSyncAsync(() => CloudSync.SyncProjectsOnlyAsync(_library, ids, CloudHooks()));
        };
        _cloudLayoutDebounce.Tick += (_, _) =>
        {
            _cloudLayoutDebounce.Stop();
            _ = RunCloudSyncAsync(() => CloudSync.SyncLayoutOnlyAsync(_library, CloudHooks()));
        };
        _cloudSyncPeriodic.Tick += (_, _) => _ = FlushCloudSyncAsync();
        _boardRefreshDebounce.Tick += async (_, _) =>
        {
            _boardRefreshDebounce.Stop();
            await CloudSync.RefreshBoardAsync(_library);
            RefreshCloudStatusViews();
        };
        _boardRotation.Tick += (_, _) => { _boardRotationIndex++; RenderBoardBanner(); };
        _boardSchedule.Tick += (_, _) => RenderBoardBanner();
        _cloudRealtime.EventReceived += value => DispatcherQueue.TryEnqueue(() => OnCloudEvent(value));
        // 实时连接恢复后立即完整同步一次，补上断线期间漏掉的通知。
        _cloudRealtime.ConnectionChanged += connected => DispatcherQueue.TryEnqueue(() =>
        {
            RefreshCloudStatusViews();
            if (connected) { _cloudEventDebounce.Stop(); _cloudEventDebounce.Start(); }
        });
        CloudSync.BoardChanged += () => DispatcherQueue.TryEnqueue(() => { _boardRotationIndex = 0; RenderBoardBanner(); });
        // 提示条打开或关闭时班务条随之让位，避免被提示条盖住。
        StorageInfoBar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => DispatcherQueue.TryEnqueue(RenderBoardBanner));
        StorageInfoBar.SizeChanged += (_, _) => { if (StorageInfoBar.IsOpen) RenderBoardBanner(); };
        Closed += (_, _) =>
        {
            _cloudSyncDebounce.Stop(); _cloudEventDebounce.Stop(); _cloudProjectDebounce.Stop(); _cloudLayoutDebounce.Stop();
            _cloudSyncPeriodic.Stop(); _boardRefreshDebounce.Stop(); _boardRotation.Stop(); _boardSchedule.Stop();
            _cloudRealtime.Dispose();
        };
        RestartCloudConnection();
    }

    /// <summary>连接设置变化（配对、断开、启动）后重建实时连接与定时同步。</summary>
    private void RestartCloudConnection()
    {
        _cloudRealtime.Stop();
        _cloudSyncPeriodic.Stop();
        CloudSyncSettings cloud = _settings.CloudSync;
        if (cloud.Enabled && _storageReady)
        {
            _cloudRealtime.Start(cloud.ServerUrl, cloud.ClassId!.Value, cloud.DeviceToken);
            _cloudSyncPeriodic.Start();
            _ = FlushCloudSyncAsync();
        }
        RenderBoardBanner();
        UpdateCloudToolbarButton();
    }

    /// <summary>保存后调用：把当前项目放进上传队列，稍等片刻合并连续改动后再同步。</summary>
    private void RequestCloudSync(bool immediate = false)
    {
        if (!_settings.CloudSync.Enabled || !_storageReady) return;
        if (CurrentProject is { } project) CloudSyncService.Queue(_library, project);
        _cloudSyncDebounce.Stop();
        if (immediate) _ = FlushCloudSyncAsync();
        else _cloudSyncDebounce.Start();
    }

    /// <summary>关闭自动保存时项目不写盘也不上传，但设置改动仍要送到集控：只同步班级设置。</summary>
    private void RequestCloudLayoutSync()
    {
        if (!_settings.CloudSync.Enabled || !_storageReady || !_settings.CloudSync.SyncLayout) return;
        _cloudLayoutDebounce.Stop();
        _cloudLayoutDebounce.Interval = TimeSpan.FromSeconds(0.8);
        _cloudLayoutDebounce.Start();
    }

    private Task FlushCloudSyncAsync() => RunCloudSyncAsync(() =>
    {
        CaptureCurrentProject();
        if (CurrentProject is { } project) CloudSyncService.Queue(_library, project);
        return CloudSync.SynchronizeAsync(_library, CloudHooks());
    });

    /// <summary>执行一次同步（完整或定向），之后统一落盘、回收附件目录、展示提示并刷新状态。</summary>
    private async Task RunCloudSyncAsync(Func<Task> sync)
    {
        if (!_settings.CloudSync.Enabled || !_storageReady) return;
        try
        {
            _library.Settings = _settings;
            await sync();
            // 同步可能更新了设置（应用班级布局）或项目清单，统一落盘后再回收附件目录。
            _settings = _library.Settings;
            if (_settings.CloudSync.Enabled) { _projectStore.Save(_library); CloudSync.CleanupRemovedAssets(); }
            UpdateProjectCommands();
            foreach (string notice in CloudSync.TakeNotices()) ShowCloudNotice(notice, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"集控同步失败：{ex.Message}");
        }
        finally { RefreshCloudStatusViews(); }
    }

    private CloudSyncHooks CloudHooks() => new()
    {
        // 正在编辑的当前项目不被替换：编辑结束保存时若有冲突，会按冲突流程保留本机副本。
        IsProjectBusy = id => id == _library.ActiveProjectId && (_isEditing || _projectOperation),
        ProjectReplaced = id => { if (id == _library.ActiveProjectId) ReloadActiveProject(); },
        ProjectRemoved = id =>
        {
            if (_library.ActiveProjectId is not null) return;
            _library.ActiveProjectId = _library.Projects.OrderByDescending(p => p.LastUsedAt).FirstOrDefault()?.Id;
            ReloadActiveProject();
            ShowCloudNotice("当前项目已在集控上被删除，已切换到其他项目。", InfoBarSeverity.Informational);
        },
        LayoutApplied = ApplyCloudLayout,
        BackgroundDirectory = Path.Combine(_dataStore.DataDirectory, "backgrounds"),
        AppVersion = AppVersionText.Text,
        ActiveProjectName = () => CurrentProject?.Name,
        Fonts = () => FontService.AvailableFamilies
    };

    /// <summary>当前项目内容被服务器版本替换后，重新载入磁贴与整屏笔迹。</summary>
    private void ReloadActiveProject()
    {
        _activeInkSubject = null;
        ViewModel.ReplaceSubjects(AppDataStore.RestoreSubjects(CurrentProject?.Subjects ?? []));
        LoadClockInk();
        ApplyPalette();
        BuildTiles();
        UpdateProjectCommands();
        ApplyDisplayLayout();
    }

    /// <summary>应用服务器下发的班级布局：设置对象已整体替换，这里同步控件状态并整体重绘。</summary>
    // 只影响行为、不影响看板外观的设置：应用它们时不必重建磁贴。
    private static readonly HashSet<string> NonVisualCloudKeys =
    [
        "NoiseAlertEnabled", "NoiseThresholdDb", "NoiseIntervalSeconds", "NoiseAlertVolume", "PauseNoiseWhenMinimized", "PastePlainTextOnly", "AutoSaveEnabled",
        "Autofill.Subject.Enabled", "Autofill.Subject.MatchLevel", "Autofill.Homework.Enabled", "Autofill.Homework.AutoRecord",
        "Autofill.Homework.RecordLevel", "Autofill.Homework.Isolation", "BoardBannerLevels", "ShowWeatherAlerts", "WeatherCityName", "WeatherCityCode"
    ];

    /// <summary>
    /// 应用服务器下发的班级设置：设置已原地合并进 _settings，这里按改动的键刷新界面，
    /// 设置页控件、主题、色系、噪音与天气控件同步显示新值。正在编辑看板时不重建磁贴，结束编辑时再统一刷新。
    /// </summary>
    private void ApplyCloudLayout(BoardSettingsState merged)
    {
        IReadOnlyList<string> keys = CloudSync.LastAppliedLayoutKeys;
        _settings = merged;
        _library.Settings = merged;
        _settings.Widgets ??= [];
        _settings.CustomizedDockedWidgets ??= [];
        _settings.GridStyle = GridAppearance.EffectiveStyle(_settings.GridStyle, false, false);
        _settings.GridLineThickness = Math.Clamp(_settings.GridLineThickness, .5, 5);
        _settings.GridDotDiameter = Math.Clamp(_settings.GridDotDiameter, 1, 12);
        _settings.TileBodyFontSize = Math.Round(_settings.TileBodyFontSize, MidpointRounding.AwayFromZero);
        IsGridSnappingEnabled = _settings.GridSnappingEnabled;
        GridSnapToggleButton.IsChecked = IsGridSnappingEnabled;
        // 主题与色系同时写回当前项目，切换项目时不会被项目里保存的旧外观改回去。
        ThemeComboBox.SelectedIndex = _settings.Theme switch { "Light" => 1, "Default" => 2, _ => 0 };
        PaletteComboBox.SelectedIndex = _settings.Palette == "Macaron" ? 1 : 0;
        RememberAppearance();
        RootShell.RequestedTheme = _settings.Theme switch { "Light" => ElementTheme.Light, "Default" => ElementTheme.Default, _ => ElementTheme.Dark };
        WeatherCityTextBox.Text = _settings.WeatherCityName;
        WeatherAlertsToggle.IsOn = _settings.ShowWeatherAlerts;
        NoiseIntervalSlider.Value = Math.Clamp(_settings.NoiseIntervalSeconds, 0.1, 2);
        NoiseThresholdSlider.Value = Math.Clamp(_settings.NoiseThresholdDb, 20, 120);
        NoiseAlertToggle.IsOn = _settings.NoiseAlertEnabled;
        NoiseAlertVolumeSlider.Value = Math.Clamp(_settings.NoiseAlertVolume, 0, 1) * 100;
        RefreshSettingControls();
        _refreshSubjectAutofill?.Invoke();
        _refreshHomeworkAutofill?.Invoke();
        if (keys.Count == 0) return;
        bool visual = keys.Any(k => !NonVisualCloudKeys.Contains(k));
        if (visual)
        {
            _renderedGridWidth = 0;
            _renderedGridAppearance = string.Empty;
            ApplyPalette();
            ApplyExtendedSettings();
            if (_isEditing) _cloudTilesRebuildPending = true;
            else BuildTiles();
        }
        if (keys.Contains("WeatherCityCode")) _ = RefreshWeatherAsync();
        if (keys.Contains("ShowWeatherAlerts")) DisplayWeather();
        if (keys.Contains("BoardBannerLevels")) RenderBoardBanner();
        ShowCloudNotice($"已应用集控下发的班级设置（{keys.Count} 项）。", InfoBarSeverity.Success);
    }

    private void OnCloudEvent(SyncEvent value)
    {
        if (!_settings.CloudSync.Enabled || value.ClassId != _settings.CloudSync.ClassId) return;
        if (value.Type == SyncEventTypes.Command)
        {
            if (value.DeviceId is { } target && _settings.CloudSync.DeviceId is { } mine && target != mine) return;
            if (value.Command == "message" && !string.IsNullOrWhiteSpace(value.Text))
            {
                ShowCloudNotice(value.Text!, InfoBarSeverity.Informational, "来自集控的消息");
                return;
            }
        }
        // 本机自己触发的修改不必再同步一次。
        if (value.SourceDeviceId is { } source && source == _settings.CloudSync.DeviceId) return;
        switch (value.Type)
        {
            case SyncEventTypes.Board:
                _boardRefreshDebounce.Stop();
                _boardRefreshDebounce.Start();
                return;
            case SyncEventTypes.Layout:
                _cloudLayoutDebounce.Stop();
                _cloudLayoutDebounce.Interval = TimeSpan.FromMilliseconds(150);
                _cloudLayoutDebounce.Start();
                return;
            case SyncEventTypes.Updated or SyncEventTypes.Deleted when value.ProjectId != Guid.Empty:
                _pendingCloudProjects.Add(value.ProjectId);
                _cloudProjectDebounce.Stop();
                _cloudProjectDebounce.Start();
                return;
            default:
                // “立即同步”指令和无法识别的通知走完整一轮。
                _cloudEventDebounce.Stop();
                _cloudEventDebounce.Start();
                return;
        }
    }

    private void ShowCloudNotice(string message, InfoBarSeverity severity, string title = "集控")
    {
        // 读取失败等错误提示优先，不被普通同步提示覆盖。
        if (StorageInfoBar.IsOpen && StorageInfoBar.Severity == InfoBarSeverity.Error && severity != InfoBarSeverity.Error) return;
        StorageInfoBar.Title = title;
        StorageInfoBar.Message = message;
        StorageInfoBar.Severity = severity;
        StorageInfoBar.IsOpen = true;
    }

    private void RefreshCloudStatusViews()
    {
        if (CloudControlRoot.Visibility == Visibility.Visible) RenderCloudControl();
        RefreshCloudSyncSettingsPage();
        UpdateCloudToolbarButton();
        RenderBoardBanner();
    }

    private void UpdateCloudToolbarButton()
    {
        if (CloudControlButton is null) return;
        bool show = _settings.CloudSync.Enabled && DisplayRoot.Visibility == Visibility.Visible;
        CloudControlButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>集控状态的一句话描述，设置页与班级页共用。</summary>
    private string CloudStatusLine()
    {
        CloudSyncSettings cloud = _settings.CloudSync;
        if (!cloud.Enabled) return "未连接集控";
        string live = _cloudRealtime.IsConnected ? "实时连接中" : "实时连接断开，按 3 分钟间隔同步";
        string last = CloudSync.LastSyncAt is { } at ? $"上次同步 {at:HH:mm:ss}" : "尚未同步";
        int pending = _library.SyncQueue.Count;
        return $"{live} · {last}{(pending > 0 ? $" · 待上传 {pending} 项" : "")}";
    }

    // ------------------------------------------------------------------ 班务条

    /// <summary>
    /// 看板上的班务条：生效中的公告（多条时每 8 秒轮播一条）和今日值日。点按查看全部内容，可以收起，内容变化后重新出现。
    /// 定时公告到开始或结束时刻自动刷新，不必等下一次同步。
    /// </summary>
    private void RenderBoardBanner()
    {
        if (BoardBanner is null) return;
        BoardBanner.Children.Clear();
        _boardRotation.Stop();
        _boardSchedule.Stop();
        BoardInfo? board = CloudSync.Board;
        CloudSyncSettings cloud = _settings.CloudSync;
        if (!cloud.Enabled || !cloud.ShowBoardBanner || board is null) { BoardBanner.Visibility = Visibility.Collapsed; return; }
        DateTime now = DateTime.UtcNow;
        if (BoardNotices.NextBoundary(board, now) is { } boundary)
        {
            // DispatcherTimer 的间隔不能太长，超过一小时的先睡一小时再重算。
            _boardSchedule.Interval = TimeSpan.FromSeconds(Math.Clamp((boundary - now).TotalSeconds + 1, 1, 3600));
            _boardSchedule.Start();
        }
        List<AnnouncementItem> notices = BoardNotices.Visible(board, now, _settings.BoardBannerLevels);
        string key = BoardNotices.Key(notices, board.Duty);
        if (key == "|" || key == _boardBannerDismissedKey) { BoardBanner.Visibility = Visibility.Collapsed; return; }

        if (notices.Count > 0)
        {
            AnnouncementItem notice = notices[_boardRotationIndex % notices.Count];
            string text = notice.Title + (string.IsNullOrWhiteSpace(notice.Body) ? "" : "　" + notice.Body.ReplaceLineEndings(" "));
            if (notices.Count > 1) text = $"{_boardRotationIndex % notices.Count + 1}/{notices.Count}　{text}";
            BoardBanner.Children.Add(BannerPill("Info", LevelBrush(notice.Level), text));
            if (notices.Count > 1) _boardRotation.Start();
        }
        if (board.Duty is { } duty)
        {
            string text = string.Join("　", duty.Tasks.Select(t => $"{t.Task}：{string.Join("、", t.Students)}"));
            BoardBanner.Children.Add(BannerPill("Clock", "BoardSuccessBrush", $"今日值日 · {text}"));
        }
        bool toolbarTop = _settings.ToolbarPosition.StartsWith("Top", StringComparison.Ordinal);
        BoardBanner.VerticalAlignment = toolbarTop ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        double top = StorageInfoBar.IsOpen ? StorageInfoBar.ActualHeight + 10 : 14;
        // 分屏和仅时钟模式放进时钟区上方，竖向排列，不遮挡作业磁贴；其余模式在顶部居中横排。
        bool inClock = _settings.LayoutMode is "Split" or "Clock" && ClockPanel.Visibility == Visibility.Visible && ClockPanel.ActualWidth > 200 && RootShell.ActualWidth >= 900;
        BoardBanner.Orientation = inClock ? Orientation.Vertical : Orientation.Horizontal;
        BoardBanner.HorizontalAlignment = inClock ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        BoardBanner.MaxWidth = inClock ? Math.Max(200, ClockPanel.ActualWidth - 32) : double.PositiveInfinity;
        foreach (Border pill in BoardBanner.Children.OfType<Border>())
            if (pill.Child is StackPanel { Children.Count: 2 } row && row.Children[1] is TextBlock label)
                label.MaxWidth = inClock ? Math.Max(140, ClockPanel.ActualWidth - 100) : 560;
        BoardBanner.Margin = toolbarTop ? new Thickness(16, 0, 16, 16) : new Thickness(16, top, 16, 0);
        BoardBanner.Tag = key;
        BoardBanner.Visibility = Visibility.Visible;
    }

    private static string LevelBrush(string level) =>
        level == "urgent" ? "BoardDangerBrush" : level == "important" ? "BoardWarningBrush" : "BoardBrandBrush";

    private Border BannerPill(string icon, string accentBrush, string text)
    {
        Brush accent = ResourceBrush(accentBrush);
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FluentIcon { Symbol = icon, Foreground = accent, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 15, MaxWidth = 560, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = BoardTheme.TextBrush });
        Border pill = new()
        {
            Child = row, Padding = new Thickness(14, 8, 16, 8), CornerRadius = new CornerRadius(18),
            Background = ResourceBrush("BoardSurfaceElevatedBrush"),
            BorderBrush = accent, BorderThickness = new Thickness(1)
        };
        ToolTipService.SetToolTip(pill, "点按查看全部");
        return pill;
    }

    /// <summary>点按班务条：浮层列出全部公告的标题、正文、发布人与时间，以及今日值日，可以在这里收起班务条。</summary>
    private void BoardBanner_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (CloudSync.Board is not { } board) return;
        List<AnnouncementItem> notices = BoardNotices.Visible(board, DateTime.UtcNow, _settings.BoardBannerLevels);
        StackPanel list = new() { Spacing = 14, Width = 420 };
        foreach (AnnouncementItem notice in notices)
        {
            StackPanel item = new() { Spacing = 4 };
            StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new FluentIcon { Symbol = "Info", FontSize = 15, Foreground = ResourceBrush(LevelBrush(notice.Level)), VerticalAlignment = VerticalAlignment.Center });
            TextBlock title = Strong(notice.Title, 16); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            head.Children.Add(title);
            item.Children.Add(head);
            if (!string.IsNullOrWhiteSpace(notice.Body)) item.Children.Add(Strong(notice.Body, 14));
            string until = notice.ExpiresAt is { } end ? $" · 显示到 {end.ToLocalTime():M月d日 HH:mm}" : "";
            item.Children.Add(Muted($"{notice.Author} · {(notice.StartsAt ?? notice.CreatedAt).ToLocalTime():M月d日 HH:mm}{until}", 11));
            list.Children.Add(item);
        }
        if (board.Duty is { } duty)
        {
            StackPanel item = new() { Spacing = 4 };
            item.Children.Add(SectionTitle("Clock", "今日值日", duty.Label));
            foreach (DutyTaskAssignment task in duty.Tasks) item.Children.Add(Strong($"{task.Task}：{string.Join("、", task.Students)}", 14));
            list.Children.Add(item);
        }
        Flyout flyout = new();
        Button hide = new() { Content = "收起班务条", HorizontalAlignment = HorizontalAlignment.Right };
        hide.Click += (_, _) =>
        {
            _boardBannerDismissedKey = BoardBanner.Tag as string ?? "";
            flyout.Hide();
            RenderBoardBanner();
        };
        list.Children.Add(hide);
        flyout.Content = new ScrollViewer { Content = list, MaxHeight = 520 };
        flyout.ShowAt(BoardBanner);
    }
}
