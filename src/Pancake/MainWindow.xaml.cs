using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;
using Pancake.ViewModels;
using Windows.Graphics;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Pancake;

public sealed partial class MainWindow : Window
{
    // 网格大小只取整数：旧配置里的小数值（例如拖动过旧版滑块留下的 48.37）在这里统一收敛。
    private double GridSize => Math.Clamp(Math.Round(_settings.GridSize), 16, 160);
    private static readonly Brush FullScreenHintBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 38, 38));
    private static readonly Brush ToolbarButtonBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0));
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fullScreenLabelTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly NoiseMonitorService _noiseMonitor = new();
    private readonly XiaomiWeatherService _weatherService = new();
    private readonly WeatherCityCatalog _weatherCityCatalog = new();
    private readonly ReleaseUpdateService _updateService = new();
    private readonly AppDataStore _dataStore = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly AutofillService _autofill;
    private readonly AutofillPopup _autofillPopup = new();
    private BoardSettingsState _settings = new();
    private readonly bool _startFullScreen;
    private readonly string _initialView;
    private AppWindow? _appWindow;
    private bool _isEditing;
    private bool _isFullScreen;
    private bool _isLoaded;
    private bool _refreshingDevices;
    private bool _checkingForUpdates;
    private bool _showFullScreenExitHint;
    private readonly NoiseAlertGate _noiseAlertGate = new();
    private readonly NoiseAlertPlayer _noiseAlertPlayer = new();
    private readonly System.Diagnostics.Stopwatch _noiseAlertClock = System.Diagnostics.Stopwatch.StartNew();
    private WeatherSnapshot? _lastWeather;
    private int _activeTileInteractions;
    private double _renderedGridWidth;
    private double _renderedGridHeight;
    private string _renderedGridAppearance = string.Empty;
    private bool IsGridSnappingEnabled = true;
    private bool _dialogOpen;
    private double _autofillScrollX;
    private double _autofillScrollY;
    private uint? _globalGesturePointerId;
    private Point _globalGestureStart;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow(bool startFullScreen = true, string initialView = "display")
    {
        _startFullScreen = startFullScreen;
        _initialView = initialView;
        InitializeComponent();
        // 补全设置跟随当前设置对象，切换项目或回滚时无需重新创建服务。
        _autofill = new AutofillService(() => _settings.Autofill);
        _autofillPopup.AttachTo(RootShell);
        // 滚动位置变化时让候选浮层跟随输入位置：缩放和内容尺寸变化也会触发 ViewChanged，
        // 这些无关更新不应该关掉正在使用的浮层，否则输入过程中候选会莫名消失。
        BoardScroller.ViewChanged += (_, _) =>
        {
            double x = BoardScroller.HorizontalOffset;
            double y = BoardScroller.VerticalOffset;
            if (Math.Abs(x - _autofillScrollX) < 0.5 && Math.Abs(y - _autofillScrollY) < 0.5) return;
            _autofillScrollX = x;
            _autofillScrollY = y;
            _autofillPopup.Reposition();
        };
        // 候选浮层的键盘操作放在窗口根面板：Tab 与上下键在输入框内部会被焦点导航或光标移动抢先处理。
        // 只挂 PreviewKeyDown：同时挂 KeyDown 会让一次按键被处理两遍，
        // 上下键会先加一又立刻减一，表现成完全切不动的样子。
        RootShell.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(RootShell_PreviewKeyDown), true);
        // 仅时钟模式的整屏笔迹写进当前项目，改动后按常规节奏安排保存。
        ClockInkLayer.StrokesChanged += ScheduleSave;
        LoadPersistentState();
        InitializeProjectCommands();
        InitializeFloatingIslands();
        InitializeExtendedSettings();
        InitializeBoardNavigation();

        RootShell.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootShell_GlobalPointerPressed), true);
        RootShell.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(RootShell_GlobalPointerMoved), true);
        RootShell.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(RootShell_GlobalPointerReleased), true);
        RootShell.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(RootShell_GlobalPointerReleased), true);
        RootShell.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(RootShell_GlobalPointerReleased), true);

        RootShell.RequestedTheme = _settings.Theme switch { "Light" => ElementTheme.Light, "Default" => ElementTheme.Default, _ => ElementTheme.Dark };
        ApplyPalette();
        RootShell.ActualThemeChanged += (_, _) => ApplyActualTheme();
        BoardTheme.IsLight = RootShell.ActualTheme == ElementTheme.Light;
        AppVersionText.Text = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "未知版本";
        RefreshMicrophoneDevices();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        SystemBackdrop = new PersistentMicaBackdrop();
        InitializeAppWindow();
        InitializeTimersAndServices();
        InitializePresentationBehavior();

        RootShell.Loaded += (_, _) =>
        {
            _isLoaded = true;
            ApplyActualTheme();
            ApplyExtendedSettings();
            ShowInitialView();
            if (_initialView != "verification")
            {
                StartNoiseMonitoring();
                ScheduleSave();
                _ = RefreshWeatherAsync();
                if (_settings.AutoUpdateEnabled) _ = CheckForUpdatesAsync(false);
            }
            RootShell.Focus(FocusState.Programmatic);
            if (_startFullScreen)
            {
                DispatcherQueue.TryEnqueue(() => SetFullScreen(true));
            }
        };
        Closed += (_, _) =>
        {
            _isLoaded = false;
            _clockTimer.Stop();
            _weatherTimer.Stop();
            _presentationTimer.Stop();
            _toolbarAnimation?.Stop();
            SaveStateNow();
            _noiseMonitor.Dispose();
            _noiseAlertPlayer.Dispose();
        };
    }

    private void InitializeTimersAndServices()
    {
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        _fullScreenLabelTimer.Tick += (_, _) =>
        {
            _fullScreenLabelTimer.Stop();
            HideFullScreenExitHint();
        };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveStateNow(); };

        _noiseMonitor.FastLevelAvailable += (_, level) => DispatcherQueue.TryEnqueue(() => ProcessNoiseAlert(level));
        _noiseMonitor.LevelAvailable += (_, level) => DispatcherQueue.TryEnqueue(() => UpdateNoiseDisplay(level));
        _noiseMonitor.CaptureFailed += (_, message) => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded || _noiseSuspended || ShouldSuspendNoise) return;
            NoiseText.Text = "麦克风不可用";
            MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Error;
            MicrophoneStatusInfoBar.Message = $"麦克风启动失败：{message}";
        });
    }

    private void InitializeAppWindow()
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow?.Resize(new SizeInt32(1440, 900));
    }

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        string dateText = $"{now:yyyy年M月d日} 星期{GetChineseWeekday(now.DayOfWeek)}";
        MainTimeText.Text = now.ToString("HH:mm");
        SecondsText.Text = now.ToString("ss");
        ClockDateText.Text = dateText;


    }

    private static string GetChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一", DayOfWeek.Tuesday => "二", DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四", DayOfWeek.Friday => "五", DayOfWeek.Saturday => "六", _ => "日"
    };

    private void ShowInitialView()
    {
        if (_initialView == "settings")
        {
            ShowSettings();
            return;
        }
        ShowBoard();
        if (_initialView is "editor" or "ink") EnterEditing();
        if (_initialView == "ink") GlobalPenButton.IsChecked = true;
    }

    private void ShowBoard()
    {
        RecordToolbarActivity(false);
        UpdateEditButtonPosition();
        DisplayRoot.Visibility = Visibility.Visible;
        SettingsRoot.Visibility = Visibility.Collapsed;
        // 设置页里调整过网格大小或吸附后，回到看板时一次性应用磁贴位置与网格。
        ApplyPendingBoardLayout();
        UpdateProjectCommands();
        BackToBoardButton.Visibility = Visibility.Collapsed;
        EditBoardButton.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        // 回到作业板时缩放岛重新出现，并重新贴合控制窗。
        RefreshIslands();
    }

    private void ShowSettings()
    {
        _previewScenes.Remove("Shared");
        _previewScenes.Remove("Board");
        HideAutofillPopups();
        RecordToolbarActivity(false);
        DisplayRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Visible;
        if (SettingsRoot.SelectedItem is null)
        {
            // 等导航模板加载并显示后再设置默认项，确保背景和选中指示条使用同一状态。
            AppearanceNavigationGroup.IsExpanded = true;
            SettingsRoot.SelectedItem = AppearanceNavItem;
        }
        ProjectCommands.Visibility = Visibility.Collapsed;
        EmptyProjectPanel.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Visible;
        EditBoardButton.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        UpdateEditingToolbarVisibility();
        // 设置页里没有作业板，缩放岛随作业板一起收起。
        RefreshIslands();
    }

    private void EditBoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing) FinishEditing(); else EnterEditing();
    }

    private void EnterEditing()
    {
        if (_isEditing || CurrentProject is null) return;
        ViewModel.BeginEditing();
        _widgetEditSnapshot = WidgetLayout.Copy(_settings.Widgets);
        _dockedCustomizationEditSnapshot = [.. _settings.CustomizedDockedWidgets];
        _isEditing = true;
        UpdateEditButtonPosition();
        UpdateRichTextToolbar();
        SnapshotClockInkForEditing();
        EditBoardIcon.Glyph = FluentGlyphs.Checkmark;
        AutomationProperties.SetName(EditBoardButton, "保存修改");
        UpdateEditingToolbarVisibility();
        UpdateGridSnapHint();
        SetTilesEditing(true);
        ApplyExtendedSettings();
    }

    private void FinishEditing()
    {
        ViewModel.PublishEditing();
        _widgetEditSnapshot = null;
        _dockedCustomizationEditSnapshot = null;
        _clockInkEditSnapshot = null;
        _isEditing = false;
        UpdateEditButtonPosition();
        GlobalPenButton.IsChecked = false;
        UpdateRichTextToolbar();
        _activeTileInteractions = 0;
        EditBoardIcon.Glyph = FluentGlyphs.Edit;
        AutomationProperties.SetName(EditBoardButton, "编辑看板");
        UpdateEditingToolbarVisibility();
        SetTilesEditing(false);
        ApplyExtendedSettings();
        ScheduleSave();
    }

    private async void DiscardEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (await ShowConfirmAsync("放弃更改", "是否放弃本次更改？") != ContentDialogResult.Primary) return;
        ViewModel.DiscardEditing();
        if (_widgetEditSnapshot is not null) _settings.Widgets = _widgetEditSnapshot;
        if (_dockedCustomizationEditSnapshot is not null) _settings.CustomizedDockedWidgets = _dockedCustomizationEditSnapshot;
        _widgetEditSnapshot = null;
        _dockedCustomizationEditSnapshot = null;
        RestoreClockInkSnapshot();
        _isEditing = false;
        UpdateEditButtonPosition();
        GlobalPenButton.IsChecked = false;
        UpdateRichTextToolbar();
        _activeTileInteractions = 0;
        BuildTiles();
        EditBoardIcon.Glyph = FluentGlyphs.Edit;
        ApplyExtendedSettings();
        UpdateEditingToolbarVisibility();
        ScheduleSave();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing) FinishEditing();
        ShowSettings();
    }

    private void BackToBoardButton_Click(object sender, RoutedEventArgs e) => ShowBoard();

    private void ManageSubjectsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowBoard();
        EnterEditing();
    }

    private void BuildTiles()
    {
        _previewScenes.Remove("Shared");
        _previewScenes.Remove("Board");
        AlignmentCanvas.Children.Clear();
        UpdateRichTextToolbar();
        BoardCanvas.Children.Clear();
        foreach (SubjectBoard subject in ViewModel.Subjects) AddTile(subject);
        ApplyGlobalInkMode();
        UpdateBoardBounds();
        UpdateSubjectCount();
    }

    private void AddTile(SubjectBoard subject)
    {
        SubjectTileControl tile = new(
            subject,
            DeleteSubject,
            LayoutChanged,
            LayoutCommitted,
            SetTileInteractionActive,
            AddAttachmentAsync,
            ScheduleSave,
            _autofill,
            _autofillPopup)
        {
            DataContext = subject
        };
        tile.FormattingToolbarChanged += (toolbar, active) =>
        {
            // 工具条由窗口统一承载在控制窗左侧的悬浮岛上，只在当前作业获得焦点时出现。
            if (active && _isEditing) ShowRichTextToolbar(toolbar);
            else HideRichTextToolbar(toolbar);
        };
        tile.InkActivated += subject => { _activeInkSubject = subject; UpdateInkSubjectLabel(); };
        tile.ApplyAppearance(_settings);
        tile.SetEditing(_isEditing);
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        BoardCanvas.Children.Add(tile);
    }

    private void SetTilesEditing(bool editing)
    {
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.SetEditing(editing);
    }

    /// <summary>看板滚动或切换布局时收起补全浮层，避免候选窗停在旧位置。</summary>
    private void HideAutofillPopups()
    {
        _autofillPopup.Hide();
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.HideAutofill();
    }

    /// <summary>候选浮层打开时接管上下键、Tab、回车与 Esc，其余按键照常交给输入框。</summary>
    private void RootShell_PreviewKeyDown(object sender, KeyRoutedEventArgs e) => _autofillPopup.HandleKey(e);

    private void UpdateRichTextToolbar()
    {
        // 结束编辑或重建磁贴时释放旧编辑器，避免按钮继续修改已删除的作业。
        RichTextToolbarHost.Content = null;
        RefreshIslands();
        UpdateProjectCommands();
    }

    private void SetTileInteractionActive(bool active)
    {
        _activeTileInteractions = Math.Max(0, _activeTileInteractions + (active ? 1 : -1));
    }

    private void AddSubjectButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectBoard subject = ViewModel.AddSubject("新科目");
        LayoutCommitted(subject);
        AddTile(subject);
        UpdateBoardBounds();
        UpdateSubjectCount();
    }

    private void UpdateEditButtonPosition()
    {
        ToolbarItems.Children.Remove(EditBoardButton);
        ToolbarItems.Children.Insert(_isEditing ? ToolbarItems.Children.Count : 0, EditBoardButton);
    }

    private async void AutoArrangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditing || ViewModel.Subjects.Count == 0) return;
        try
        {
            ArrangeTiles(BoardScroller.ActualWidth > 1 ? BoardScroller.ActualWidth : 1100,
                BoardScroller.ActualHeight > 1 ? BoardScroller.ActualHeight : 780);
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("无法自动排列", exception.Message, "知道了");
        }
    }

    /// <summary>按给定作业板尺寸自动排列磁贴；空间不足时抛出异常，由调用方决定如何提示。</summary>
    private void ArrangeTiles(double width, double height)
    {
        bool aligned = _settings.AutoLayoutResize && _settings.AutoLayoutAlign;
        double grid = IsGridSnappingEnabled && _settings.AutoLayoutAlign ? GridSize : 0;
        double gap = _settings.AutoLayoutGap;
        List<SubjectTileControl> tiles = ViewModel.Subjects.Select(subject => FindTile(subject)!).ToList();
        List<LayoutRect>? TryArrange(IReadOnlyList<(double Width, double Height)> values, double rowWidth, double rowHeight)
        {
            try { return BoardLayout.Arrange(values, rowWidth, rowHeight, gap, _settings.AutoLayoutAlign, grid); }
            catch (InvalidOperationException) { return null; }
        }
        List<(double Width, double Height)> AlignAndSnap(List<(double Width, double Height)> sizes)
        {
            var fitted = aligned ? BoardLayout.AlignSizes(sizes) : sizes;
            return grid > 0
                ? fitted.Select(s => (Math.Ceiling(s.Item1 / grid) * grid, Math.Ceiling(s.Item2 / grid) * grid)).ToList()
                : fitted;
        }
        List<LayoutRect>? placements;
        if (_settings.AutoLayoutResize)
        {
            // 先横排满一行再换行：从最多列开始试，必要时收窄磁贴让文字换行；放不下才减少列数。
            int maxColumns = Math.Min(tiles.Count,
                Math.Max(1, (int)Math.Floor((width + gap) / (SubjectTileControl.MinimumTileWidth + gap))));
            placements = null;
            List<(double Width, double Height)> content = [];
            for (int columns = maxColumns; columns >= 1 && placements is null; columns--)
            {
                double cap = columns <= 1 ? width : (width - gap * (columns - 1)) / columns;
                content = tiles.Select(tile => tile.MeasureContentSize(cap)).ToList();
                var sizes = AlignAndSnap(content);
                double rowWidth = _settings.InfiniteBoard ? Math.Max(width, sizes.Max(size => size.Item1)) : width;
                double rowHeight = _settings.InfiniteBoard ? Math.Max(height, sizes.Sum(size => size.Item2 + gap + grid)) : height;
                placements = TryArrange(sizes, rowWidth, rowHeight);
            }
            if (placements is null)
            {
                // 内容尺寸仍放不下时等比缩小，保留旧版“总能排好”的行为；磁贴内文字可滚动，不会因此丢失。
                for (double scale = .9; scale >= .4 && placements is null; scale -= .1)
                {
                    var scaled = content.Select(size => (Math.Max(SubjectTileControl.MinimumTileWidth, size.Width * scale),
                        Math.Max(SubjectTileControl.MinimumTileHeight, size.Height * scale))).ToList();
                    placements = TryArrange(scaled, width, height);
                }
            }
        }
        else placements = TryArrange(ViewModel.Subjects.Select(subject => (subject.TileWidth, subject.TileHeight)).ToList(), width, height);
        if (placements is null)
            throw new InvalidOperationException("当前作业板空间不足，无法在不低于最小磁贴尺寸的情况下自动排列。");
        for (int index = 0; index < placements.Count; index++)
        {
            var subject = ViewModel.Subjects[index];
            var placement = placements[index];
            subject.X = placement.X; subject.Y = placement.Y;
            subject.TileWidth = placement.Width; subject.TileHeight = placement.Height;
        }
        BuildTiles();
        ScheduleSave();
    }

    private async void DeleteSubject(SubjectBoard subject)
    {
        if (await ShowConfirmAsync("删除这个科目", $"确定删除“{subject.Name}”以及其中的文字、图片和笔迹吗？") != ContentDialogResult.Primary) return;
        ViewModel.Subjects.Remove(subject);
        BuildTiles();
        ScheduleSave();
    }

    private void LayoutChanged(SubjectBoard subject)
    {
        ClampSubjectToViewport(subject);
        SubjectTileControl? tile = FindTile(subject);
        if (tile is null) return;
        if (!IsGridSnappingEnabled && tile.IsMoving)
        {
            var snap = BoardLayout.Snap(new(subject.X, subject.Y, subject.TileWidth, subject.TileHeight),
                ViewModel.Subjects.Where(s => !ReferenceEquals(s, subject)).Select(s => new LayoutRect(s.X, s.Y, s.TileWidth, s.TileHeight)),
                BoardCanvas.Width, BoardCanvas.Height);
            subject.X = snap.X; subject.Y = snap.Y;
            AlignmentGuides.Draw(AlignmentCanvas, snap, BoardCanvas.Width, BoardCanvas.Height);
        }
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        tile.ApplyModelLayout();
    }

    private void LayoutCommitted(SubjectBoard subject)
    {
        if (IsGridSnappingEnabled)
        {
            double width = Math.Floor(BoardCanvas.Width / GridSize) * GridSize;
            double height = Math.Floor(BoardCanvas.Height / GridSize) * GridSize;
            double minWidth = Math.Ceiling(280 / GridSize) * GridSize;
            if (width >= minWidth && height >= SubjectTileControl.MinimumTileHeight)
            {
                subject.TileWidth = Math.Clamp(SnapToGrid(subject.TileWidth), minWidth, width);
                subject.TileHeight = Math.Clamp(SnapToGrid(subject.TileHeight), SubjectTileControl.MinimumTileHeight, height);
                subject.X = Math.Clamp(SnapToGrid(subject.X), 0, width - subject.TileWidth);
                subject.Y = Math.Clamp(SnapToGrid(subject.Y), 0, height - subject.TileHeight);
            }
        }
        LayoutChanged(subject);
        AlignmentCanvas.Children.Clear();
        ScheduleSave();
    }

    private double SnapToGrid(double value) => Math.Round(value / GridSize) * GridSize;

    private SubjectTileControl? FindTile(SubjectBoard subject) => BoardCanvas.Children
        .OfType<SubjectTileControl>()
        .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, subject));

    private async Task AddAttachmentAsync(HomeworkEntry homework)
    {
        string owned;
        string name;
        var project = CurrentProject;
        if (project is null) return;
        try
        {
            string? selected = await SelectAttachmentImageAsync();
            if (selected is null || CurrentProject != project) return;
            string recent = await MediaLibraryStore.ImportAsync(selected);
            if (CurrentProject != project) return;
            owned = _projectStore.CopyAttachment(project.Id, recent);
            name = MediaLibrary.DisplayName(selected);
        }
        catch (Exception ex) { await ShowMessageAsync("添加图片失败", ex.Message, "知道了"); return; }
        homework.Attachments.Add(new AttachmentItem { Name = name, Kind = "图片", Path = owned });
        homework.NotifyAttachmentsChanged();
        BuildTiles();
        SetTilesEditing(_isEditing);
        ScheduleSave();
    }

    private void UpdateBoardBounds()
    {
        if (_updatingBoardBounds) return;
        _updatingBoardBounds = true;
        try
        {
        // XAML 在首次布局前会报告 0；使用设计基准尺寸可避免启动时把磁贴压缩到最小值。
        double width = BoardScroller.ActualWidth > 1 ? BoardScroller.ActualWidth : 1100;
        double height = BoardScroller.ActualHeight > 1 ? BoardScroller.ActualHeight : 780;
        if (_settings.InfiniteBoard)
        {
            width = Math.Max(width / BoardScroller.ZoomFactor, ViewModel.Subjects.Select(s => s.X + s.TileWidth + 600).DefaultIfEmpty(2000).Max());
            height = Math.Max(height / BoardScroller.ZoomFactor, ViewModel.Subjects.Select(s => s.Y + s.TileHeight + 600).DefaultIfEmpty(1600).Max());
            _infiniteWidth = Math.Max(_infiniteWidth, Math.Max(width, (BoardScroller.HorizontalOffset + BoardScroller.ActualWidth) / BoardScroller.ZoomFactor + 600));
            _infiniteHeight = Math.Max(_infiniteHeight, Math.Max(height, (BoardScroller.VerticalOffset + BoardScroller.ActualHeight) / BoardScroller.ZoomFactor + 600));
            width = _infiniteWidth; height = _infiniteHeight;
        }
        else { _infiniteWidth = _infiniteHeight = 0; }
        BoardSurface.Width = width;
        BoardSurface.Height = height;
        BoardCanvas.Width = width;
        BoardCanvas.Height = height;
        GridCanvas.Width = width;
        GridCanvas.Height = height;
        string gridAppearance = $"{GridAppearance.EffectiveStyle(_settings.GridStyle, _isEditing, _settings.ShowGridWhileEditing)}|{GridSize}|{_settings.GridColor}|{_settings.GridLineThickness}|{_settings.GridDotColor}|{_settings.GridDotDiameter}";
        if (Math.Abs(width - _renderedGridWidth) > 0.5 || Math.Abs(height - _renderedGridHeight) > 0.5 || _renderedGridAppearance != gridAppearance)
        {
            RenderGrid(width, height);
            _renderedGridWidth = width;
            _renderedGridHeight = height;
            _renderedGridAppearance = gridAppearance;
        }
        }
        finally { _updatingBoardBounds = false; }
    }

    private void BoardViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBoardBounds();
        // 窗口启动和全屏切换会经过短暂的小视口，不能把这个临时尺寸裁剪回持久布局。
        // 只有用户实际拖动或缩放磁贴时，LayoutChanged 才能修改模型坐标。
    }

    private void ClampSubjectToViewport(SubjectBoard subject)
    {
        if (_settings.InfiniteBoard)
        {
            subject.X = Math.Max(0, subject.X); subject.Y = Math.Max(0, subject.Y);
            UpdateBoardBounds(); return;
        }
        double viewportWidth = BoardScroller.ActualWidth > 1 ? BoardScroller.ActualWidth : 1100;
        double viewportHeight = BoardScroller.ActualHeight > 1 ? BoardScroller.ActualHeight : 780;
        subject.TileWidth = Math.Clamp(subject.TileWidth, Math.Min(280, viewportWidth), viewportWidth);
        subject.TileHeight = Math.Clamp(subject.TileHeight, Math.Min(SubjectTileControl.MinimumTileHeight, viewportHeight), viewportHeight);
        subject.X = Math.Clamp(subject.X, 0, Math.Max(0, viewportWidth - subject.TileWidth));
        subject.Y = Math.Clamp(subject.Y, 0, Math.Max(0, viewportHeight - subject.TileHeight));
    }

    private void RenderGrid(double width, double height)
    {
        GridCanvas.Children.Clear();
        string style = GridAppearance.EffectiveStyle(_settings.GridStyle, _isEditing, _settings.ShowGridWhileEditing);
        if (style == "None") return;
        // 无限画板只绘制视口附近的网格图形，平移很远也不会创建成千上万的 XAML 对象。
        double left = _settings.InfiniteBoard ? Math.Max(0, Math.Floor(BoardScroller.HorizontalOffset / BoardScroller.ZoomFactor / GridSize) * GridSize) : 0;
        double top = _settings.InfiniteBoard ? Math.Max(0, Math.Floor(BoardScroller.VerticalOffset / BoardScroller.ZoomFactor / GridSize) * GridSize) : 0;
        double right = _settings.InfiniteBoard ? Math.Min(width, left + BoardScroller.ActualWidth / BoardScroller.ZoomFactor + GridSize * 2) : width;
        double bottom = _settings.InfiniteBoard ? Math.Min(height, top + BoardScroller.ActualHeight / BoardScroller.ZoomFactor + GridSize * 2) : height;
        DrawGrid(GridCanvas, style, left, top, right, bottom);
    }

    /// <param name="viewScale">预览等缩小显示的倍率：线宽与点直径按倍率反向放大，缩放后仍与看板观感一致。</param>
    private void DrawGrid(Canvas canvas, string style, double left, double top, double right, double bottom, double viewScale = 1)
    {
        canvas.Children.Clear();
        if (style == "None") return;
        double inverseScale = viewScale > 0 ? 1 / viewScale : 1;
        if (style == "Dots")
        {
            double diameter = Math.Clamp(_settings.GridDotDiameter, 1, 12) * inverseScale;
            SolidColorBrush fill = new(GridAppearance.ParseColor(_settings.GridDotColor, Windows.UI.Color.FromArgb(143, 86, 86, 92)));
            for (double y = top; y <= bottom; y += GridSize)
            for (double x = left; x <= right; x += GridSize)
            {
                Ellipse dot = new() { Width = diameter, Height = diameter, Fill = fill };
                Canvas.SetLeft(dot, x - diameter / 2); Canvas.SetTop(dot, y - diameter / 2);
                canvas.Children.Add(dot);
            }
            return;
        }
        SolidColorBrush stroke = new(GridAppearance.ParseColor(_settings.GridColor, Windows.UI.Color.FromArgb(105, 86, 86, 92)));
        double thickness = Math.Clamp(_settings.GridLineThickness, .5, 5) * inverseScale;
        for (double x = left; x <= right; x += GridSize)
        {
            canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = top, Y2 = bottom,
                Stroke = stroke,
                StrokeThickness = thickness
            });
        }
        for (double y = top; y <= bottom; y += GridSize)
        {
            canvas.Children.Add(new Line
            {
                X1 = left, X2 = right, Y1 = y, Y2 = y,
                Stroke = stroke,
                StrokeThickness = thickness
            });
        }
    }

    private void GridSnapToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        IsGridSnappingEnabled = true;
        // 设置页的“吸附到网格”是同一个开关，这里同步它的显示状态。
        if (_gridSnapToggle is not null) _gridSnapToggle.IsOn = true;
        UpdateGridSnapHint();
        UpdateAutoLayoutGapStep();
        ScheduleSave();
    }

    private void GridSnapToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        IsGridSnappingEnabled = false;
        if (_gridSnapToggle is not null) _gridSnapToggle.IsOn = false;
        UpdateGridSnapHint();
        UpdateAutoLayoutGapStep();
        ScheduleSave();
    }

    private void UpdateGridSnapHint()
    {
        ToolTipService.SetToolTip(GridSnapToggleButton, IsGridSnappingEnabled
            ? $"网格吸附已开启：位置和大小吸附到 {GridSize:0.#}px 网格"
            : "网格吸附已关闭：磁贴仍限制在可视区域内");
    }

    private void UpdateSubjectCount() => SubjectCountText.Text = $"{ViewModel.Subjects.Count} 个科目";

    private void SettingsNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag?.ToString() is { } page)
            ShowSettingsPage(page);
    }

    private void SettingsRoot_Loaded(object sender, RoutedEventArgs e)
    {
        // NavigationView 会给内部 SplitView 单独套右侧圆角，需在模板生成后精准移除。
        SettingsRoot.ApplyTemplate();
        if (FindNamedDescendant<SplitView>(SettingsRoot, "RootSplitView") is { } splitView)
        {
            splitView.CornerRadius = new CornerRadius(0);
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
            {
                return element;
            }

            if (FindNamedDescendant<T>(child, name) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ApplyActualTheme()
    {
        BoardTheme.IsLight = RootShell.ActualTheme == ElementTheme.Light;
        if (!_isLoaded) return;
        ApplyExtendedSettings();
        RefreshInkPalette();
        // 已有笔迹保留原始颜色；富文本在加载和保存时进行默认内容色转换。
        BuildTiles();
        HideFullScreenExitHint();
        if (_appWindow is not null)
        {
            _appWindow.TitleBar.ButtonForegroundColor = BoardTheme.TextColor;
            _appWindow.TitleBar.ButtonInactiveForegroundColor = BoardTheme.TextColor;
            _appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
    }

    private void PaletteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.Palette = PaletteComboBox.SelectedIndex == 1 ? "Macaron" : "Vivid";
        RememberAppearance();
        ApplyPalette();
        BuildTiles();
        ScheduleSave();
    }

    private void ApplyPalette()
    {
        ColorPalette.IsMacaron = _settings.Palette == "Macaron";
        RefreshInkPalette();
        // 学科色卡按当前色系解析，切换色系后重建列表即可保持显示的颜色与套用结果一致。
        _refreshSubjectAutofill?.Invoke();
        foreach (var subject in ViewModel.Subjects)
        {
            subject.AccentBrush = MainViewModel.BrushFromHex(ColorPalette.ResolveAccent(subject.AccentHex, subject.IsAccentExplicit, ColorPalette.IsMacaron));
        }
    }

    private void StartNoiseMonitoring()
    {
        if (ShouldSuspendNoise) { RefreshNoiseSuspension(); return; }
        _noiseMonitor.IntervalSeconds = _settings.NoiseIntervalSeconds;
        _noiseMonitor.CalibrationOffsetDb = _settings.MicrophoneCalibrationDb;
        MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Informational;
        MicrophoneStatusInfoBar.Message = "正在启动输入设备…";
        _noiseAlertGate.Reset();
        _noiseAlertPlayer.Volume = (float)Math.Clamp(_settings.NoiseAlertVolume, 0, 1);
        _noiseMonitor.Start(_settings.MicrophoneDeviceId);
        if (_settings.NoiseAlertEnabled) PrepareNoiseAlert();
    }

    private void RefreshMicrophoneDevices_Click(object sender, RoutedEventArgs e) => RefreshMicrophoneDevices();

    private void RefreshMicrophoneDevices()
    {
        _refreshingDevices = true;
        try
        {
            var devices = NoiseMonitorService.GetDevices();
            // 保留失联设备的选择，避免悄悄切换到另一支未经校准的麦克风。
            if (!devices.Any(device => device.Id == _settings.MicrophoneDeviceId))
                devices.Add(new(_settings.MicrophoneDeviceId, "已选择的设备不可用（请选择其他输入设备）"));
            MicrophoneDeviceComboBox.DisplayMemberPath = "Name";
            MicrophoneDeviceComboBox.ItemsSource = devices;
            MicrophoneDeviceComboBox.SelectedItem = devices.First(device => device.Id == _settings.MicrophoneDeviceId);
        }
        catch (Exception exception) { MicrophoneStatusInfoBar.Message = exception.Message; }
        finally { _refreshingDevices = false; }
        if (_isLoaded) StartNoiseMonitoring();
    }

    private void MicrophoneDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _refreshingDevices || MicrophoneDeviceComboBox.SelectedItem is not MicrophoneDevice device) return;
        _settings.MicrophoneDeviceId = device.Id;
        _settings.MicrophoneCalibrationDb = 0;
        MicrophoneCalibrationLabel.Text = "校准偏移 · 0 dB（请重新校准）";
        StartNoiseMonitoring();
        ScheduleSave();
    }

    private void NoiseIntervalSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (NoiseIntervalLabel is not null) NoiseIntervalLabel.Text = $"检测间隔 · {e.NewValue:0.0} 秒";
        if (!_isLoaded) return;
        _settings.NoiseIntervalSeconds = Math.Round(e.NewValue, 1);
        _noiseMonitor.IntervalSeconds = _settings.NoiseIntervalSeconds;
        ScheduleSave();
    }

    private void NoiseThresholdSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (NoiseThresholdLabel is not null) NoiseThresholdLabel.Text = $"吵闹阈值 · {e.NewValue:0} dB";
        if (!_isLoaded) return;
        _settings.NoiseThresholdDb = e.NewValue;
        ScheduleSave();
    }

    private void NoiseAlertToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.NoiseAlertEnabled = NoiseAlertToggle.IsOn;
        _noiseAlertGate.Reset();
        if (_settings.NoiseAlertEnabled) PrepareNoiseAlert();
        else _noiseAlertPlayer.Dispose();
        ScheduleSave();
    }

    private void NoiseAlertVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (NoiseAlertVolumeLabel is not null) NoiseAlertVolumeLabel.Text = $"提示音音量 · {e.NewValue:0}%";
        if (!_isLoaded) return;
        _settings.NoiseAlertVolume = Math.Clamp(e.NewValue / 100, 0, 1);
        _noiseAlertPlayer.Volume = (float)_settings.NoiseAlertVolume;
        ScheduleSave();
    }

    private void CalibrateMicrophone_Click(object sender, RoutedEventArgs e)
    {
        if (!_noiseMonitor.TryCalibrate(CalibrationTargetBox.Value))
        {
            MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Warning;
            MicrophoneStatusInfoBar.Message = "请填写 20–120 dB 的环境音量，并等待麦克风产生有效读数后再校准。";
            return;
        }
        _settings.CalibrationTargetDb = CalibrationTargetBox.Value;
        _settings.MicrophoneCalibrationDb = _noiseMonitor.CalibrationOffsetDb;
        MicrophoneCalibrationLabel.Text = $"校准偏移 · {_settings.MicrophoneCalibrationDb:+0.0;-0.0;0} dB";
        ScheduleSave();
    }

    private void UpdateNoiseDisplay(double level)
    {
        if (!_isLoaded || _noiseSuspended || ShouldSuspendNoise) return;
        bool noisy = level >= _settings.NoiseThresholdDb;
        string state = noisy ? "吵闹" : level < Math.Min(45, _settings.NoiseThresholdDb) ? "安静" : "适中";
        NoiseText.Text = $"{level:0} dB · {state}";
        MicrophoneStatusInfoBar.Severity = noisy ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        MicrophoneStatusInfoBar.Message = $"麦克风工作正常 · {level:0.0} dB";
    }

    private void PrepareNoiseAlert()
    {
        try { _noiseAlertPlayer.Prepare(); }
        catch (Exception exception) { MicrophoneStatusInfoBar.Message = $"提示音设备不可用：{exception.Message}"; }
    }

    private void ProcessNoiseAlert(double level)
    {
        if (!_isLoaded || _noiseSuspended || ShouldSuspendNoise) return;
        if (_noiseAlertGate.ShouldPlay(level, _settings.NoiseThresholdDb, _settings.NoiseAlertEnabled, _noiseAlertClock.Elapsed.TotalSeconds))
            PlayNoiseAlert();
    }

    private void PreviewNoiseAlert_Click(object sender, RoutedEventArgs e)
    {
        _noiseAlertGate.Reset();
        _noiseAlertGate.ShouldPlay(_settings.NoiseThresholdDb, _settings.NoiseThresholdDb, true, _noiseAlertClock.Elapsed.TotalSeconds);
        PlayNoiseAlert();
    }

    private void PlayNoiseAlert()
    {
        try { _noiseAlertPlayer.Play(); }
        catch (Exception exception)
        {
            MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Warning;
            MicrophoneStatusInfoBar.Message = $"提示音播放失败：{exception.Message}";
        }
    }

    private void WeatherAlertsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _settings.ShowWeatherAlerts = WeatherAlertsToggle.IsOn;
        DisplayWeather();
        ScheduleSave();
    }

    private void DisplayWeather()
    {
        if (_lastWeather is not WeatherSnapshot snapshot) return;
        string alerts = _settings.ShowWeatherAlerts ? string.Join("；", snapshot.Alerts.Select(alert => alert.Title)) : "";
        WeatherText.Text = $"{snapshot.Condition}  {snapshot.TemperatureCelsius:0.#}°C" + (alerts.Length > 0 ? $" · {alerts}" : "");
        ToolTipService.SetToolTip(WeatherText, string.Join("\n\n", snapshot.Alerts.Select(alert => $"{alert.Title}\n{alert.Detail}")));
        WeatherAlertsText.Text = _settings.ShowWeatherAlerts
            ? (snapshot.Alerts.Count == 0 ? "当前地区暂无预警" : string.Join("\n\n", snapshot.Alerts.Select(alert => $"{alert.Title}\n{alert.Detail}")))
            : "已关闭预警显示";
    }

    private async void RefreshWeatherButton_Click(object sender, RoutedEventArgs e) => await RefreshWeatherAsync();

    private async Task RefreshWeatherAsync()
    {
        WeatherStatusText.Text = "正在刷新…";
        try
        {
            WeatherSnapshot snapshot = await _weatherService.GetCurrentAsync(_settings.WeatherCityCode);
            _lastWeather = snapshot;
            DisplayWeather();
            WeatherStatusText.Text = $"更新于 {DateTime.Now:HH:mm}";
            if (!_weatherTimer.IsEnabled) _weatherTimer.Start();
        }
        catch (Exception exception)
        {
            _lastWeather = null;
            WeatherAlertsText.Text = "预警更新失败，请稍后重试。";
            ToolTipService.SetToolTip(WeatherText, null);
            WeatherText.Text = "天气不可用";
            WeatherStatusText.Text = exception.Message;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme) return;
        RootShell.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _settings.Theme = theme;
        RememberAppearance();
        ScheduleSave();
    }

    private async void ChooseWeatherCityButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = new() { PlaceholderText = "搜索地区名称" };
        ListView results = new() { Height = 420, SelectionMode = ListViewSelectionMode.Single };
        async Task RefreshResultsAsync() => results.ItemsSource = await _weatherCityCatalog.SearchAsync(searchBox.Text);
        searchBox.TextChanged += async (_, _) => await RefreshResultsAsync();
        await RefreshResultsAsync();
        StackPanel content = new() { Width = 560, Spacing = 10 };
        content.Children.Add(searchBox);
        content.Children.Add(results);
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot, Title = "选择天气地区", Content = content,
            PrimaryButtonText = "选择", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || results.SelectedItem is not WeatherCity city) return;
        _settings.WeatherCityName = city.Name;
        _settings.WeatherCityCode = city.Code;
        WeatherCityTextBox.Text = city.Name;
        ScheduleSave();
        await RefreshWeatherAsync();
    }

    private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.AutoUpdateEnabled = AutoUpdateToggle.IsOn;
        ScheduleSave();
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            UpdateStatusText.Text = $"正在检查 {_settings.UpdateSource} Release…";
            await RefreshUpdateSourceHintAsync();
            var latest = await _updateService.GetLatestAsync(_settings.UpdateSource);
            GitHubReleaseUpdate? update = latest is not null && ReleaseUpdateService.IsNewer(latest.Version, typeof(MainWindow).Assembly.GetName().Version!) ? latest.Update : null;
            if (update is null)
            {
                UpdateStatusText.Text = "当前已是最新版，或最新 Release 没有可安装资产。";
                return;
            }
            UpdateStatusText.Text = $"发现 {update.Tag}，正在下载 {update.AssetName}…";
            string package = await _updateService.DownloadAsync(update, _dataStore.DataDirectory);
            bool portable = System.IO.Path.GetExtension(package).Equals(".zip", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetExtension(package).Equals(".7z", StringComparison.OrdinalIgnoreCase);
            if (portable) package = await SplitUpdatePackage.NormalizeAsync(package);
            UpdateStatusText.Text = portable ? $"{update.Tag} 便携版已下载，可以自动更新。" : $"{update.Tag} 已下载，等待安装。";
            string prompt = portable
                ? $"已下载 {update.Tag} 便携版。现在保存项目并重启更新吗？程序将自动解压、覆盖旧版本并重新启动，项目和设置会保留。"
                : $"已将 {update.Tag} 下载到软件目录。现在启动安装程序吗？";
            if (await ShowConfirmAsync("发现新版本", prompt) == ContentDialogResult.Primary)
            {
                if (portable)
                {
                    UpdateStatusText.Text = "正在校验并解压更新…";
                    string configuration = await Task.Run(() => PortableUpdateService.Prepare(package, AppContext.BaseDirectory,
                        System.IO.Path.Combine(_dataStore.DataDirectory, "updates"), Environment.ProcessId));
                    PersistProjects();
                    PortableUpdateService.Launch(configuration);
                    Close();
                }
                else GitHubUpdateService.LaunchInstaller(package);
            }
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"更新检查失败：{exception.Message}";
            if (interactive) await ShowMessageAsync("无法检查更新", exception.Message, "知道了");
        }
        finally { _checkingForUpdates = false; }
    }

    private void LoadPersistentState()
    {
        try
        {
            _library = _projectStore.Load();
            _settings = _library.Settings;
            _settings.Widgets ??= [];
            _settings.CustomizedDockedWidgets ??= [];
            if (_settings.DockedWidgetLayoutVersion < 2)
            {
                foreach (string key in new[] { "SplitClock", "SplitComponents", "ClockModeClock", "ClockModeComponents" })
                    _settings.Widgets.Remove(key);
                _settings.CustomizedDockedWidgets.Clear();
                _settings.DockedWidgetLayoutVersion = 2;
            }
            _settings.GridStyle = GridAppearance.EffectiveStyle(_settings.GridStyle, false, false);
            // 旧配置缺少自动填充字段或整体为 null 时补齐默认值，保持向后兼容。
            _settings.Autofill ??= new AutofillSettings();
            _settings.Autofill.Subject ??= new SubjectCompletionSettings();
            _settings.Autofill.Homework ??= new HomeworkCompletionSettings();
            _settings.Autofill.Subject.Subjects ??= [];
            _settings.Autofill.Homework.Items ??= [];
            _settings.Autofill.Homework.Blocked ??= [];
            _autofill.EnsureBuiltIns();
            _autofill.Prune(DateTime.Now);
            _settings.GridLineThickness = Math.Clamp(_settings.GridLineThickness, .5, 5);
            _settings.GridDotDiameter = Math.Clamp(_settings.GridDotDiameter, 1, 12);
            _library.ActiveProjectId = CurrentProject?.Id ?? _library.Projects.OrderByDescending(p => p.LastUsedAt).FirstOrDefault()?.Id;
            ViewModel.ReplaceSubjects(AppDataStore.RestoreSubjects(CurrentProject?.Subjects ?? []));
            LoadClockInk();
            ThemeComboBox.SelectedIndex = _settings.Theme switch { "Light" => 1, "Default" => 2, _ => 0 };
            PaletteComboBox.SelectedIndex = _settings.Palette == "Macaron" ? 1 : 0;
            // 设置页在构造时就会按色系解析预设色（学科色卡等），因此先确定色系再构建设置页。
            ColorPalette.IsMacaron = _settings.Palette == "Macaron";
            NoiseIntervalSlider.Value = Math.Clamp(_settings.NoiseIntervalSeconds, 0.1, 2);
            _settings.NoiseIntervalSeconds = NoiseIntervalSlider.Value;
            NoiseThresholdSlider.Value = Math.Clamp(_settings.NoiseThresholdDb, 20, 120);
            _settings.NoiseThresholdDb = NoiseThresholdSlider.Value;
            NoiseAlertToggle.IsOn = _settings.NoiseAlertEnabled;
            NoiseAlertVolumeSlider.Value = Math.Clamp(_settings.NoiseAlertVolume, 0, 1) * 100;
            _settings.NoiseAlertVolume = NoiseAlertVolumeSlider.Value / 100;
            CalibrationTargetBox.Value = _settings.CalibrationTargetDb;
            MicrophoneCalibrationLabel.Text = $"校准偏移 · {_settings.MicrophoneCalibrationDb:+0.0;-0.0;0} dB";
            WeatherAlertsToggle.IsOn = _settings.ShowWeatherAlerts;
            WeatherCityTextBox.Text = _settings.WeatherCityName;
            IsGridSnappingEnabled = _settings.GridSnappingEnabled;
            GridSnapToggleButton.IsChecked = IsGridSnappingEnabled;
            AutoUpdateToggle.IsOn = _settings.AutoUpdateEnabled;
        }
        catch (Exception exception)
        {
            _storageReady = false;
            ViewModel.ReplaceSubjects([]);
            ReportStorageError("读取项目失败，已停止写入以保护原数据", exception);
        }
    }

    private void ScheduleSave()
    {
        if (!_isLoaded) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveStateNow()
    {
        if (!_storageReady) return;
        try
        {
            _settings.GridSnappingEnabled = IsGridSnappingEnabled;
            _settings.AutoUpdateEnabled = AutoUpdateToggle.IsOn;
            CaptureCurrentProject();
            _library.Settings = _settings;
            _projectStore.Save(_library);
        }
        catch (Exception exception)
        {
            ReportStorageError("自动保存失败", exception);
        }
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_isFullScreen);

    private void RootShell_GlobalPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isEditing || !_isFullScreen) return;
        // 子控件可能接管指针且不再把释放事件路由到根容器；新的按下必须能覆盖陈旧状态。
        _globalGesturePointerId = e.Pointer.PointerId;
        _globalGestureStart = e.GetCurrentPoint(RootShell).Position;
    }

    private void RootShell_GlobalPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_globalGesturePointerId != e.Pointer.PointerId) return;
        Point current = e.GetCurrentPoint(RootShell).Position;
        double distance = Math.Sqrt(Math.Pow(current.X - _globalGestureStart.X, 2) + Math.Pow(current.Y - _globalGestureStart.Y, 2));
        if (distance < 8) return;
        ShowFullScreenExitHint();
        _globalGesturePointerId = null;
    }

    private void RootShell_GlobalPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_globalGesturePointerId == e.Pointer.PointerId) _globalGesturePointerId = null;
    }

    private void ShowFullScreenExitHint()
    {
        if (_isEditing || !_isFullScreen) return;
        _showFullScreenExitHint = true;
        FullScreenButton.Background = FullScreenHintBrush;
        FullScreenButton.Foreground = BoardTheme.TextBrush;
        ApplyToolbarSettings();
        _fullScreenLabelTimer.Stop();
        _fullScreenLabelTimer.Start();
    }

    private void HideFullScreenExitHint()
    {
        _fullScreenLabelTimer.Stop();
        _showFullScreenExitHint = false;
        FullScreenButton.Background = ToolbarButtonBrush;
        FullScreenButton.Foreground = BoardTheme.TextBrush;
        ApplyToolbarSettings();
    }

    private void SetFullScreen(bool isFullScreen)
    {
        if (_appWindow is null || _isFullScreen == isFullScreen) return;
        _appWindow.SetPresenter(isFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        _isFullScreen = isFullScreen;
        FullScreenIcon.Glyph = isFullScreen ? FluentGlyphs.ExitFullScreen : FluentGlyphs.FullScreen;
        AutomationProperties.SetName(FullScreenButton, isFullScreen ? "退出全屏" : "进入全屏");
        HideFullScreenExitHint();
    }

    private void RootShell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (_isEditing) FinishEditing(); else if (_isFullScreen) SetFullScreen(false);
        e.Handled = true;
    }

    private void RootShell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDisplayLayout();
        ApplyToolbarSettings();
        RecordToolbarActivity(false);
        RefreshAppearancePreviews();
    }

    private async Task<ContentDialogResult> ShowConfirmAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot, Title = title, Content = message,
            PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        // WinUI 同一时间只能显示一个 ContentDialog；重复触发按“取消”处理，避免异步点击直接崩溃。
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }

    private async Task ShowMessageAsync(string title, string message, string closeText)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = title, Content = message, CloseButtonText = closeText };
        try { await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
    }
}
