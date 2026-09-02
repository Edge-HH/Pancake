using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PancakeBoard.Controls;
using PancakeBoard.Models;
using PancakeBoard.Services;
using PancakeBoard.ViewModels;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace PancakeBoard;

public sealed partial class MainWindow : Window
{
    private const double GridSize = 16;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _fullScreenLabelTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly NoiseMonitorService _noiseMonitor = new();
    private readonly XiaomiWeatherService _weatherService = new();
    private readonly bool _startFullScreen;
    private readonly string _initialView;
    private AppWindow? _appWindow;
    private bool _isEditing;
    private bool _isFullScreen;
    private bool _isLoaded;
    private int _activeTileInteractions;
    private double _renderedGridWidth;
    private double _renderedGridHeight;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow(bool startFullScreen = true, string initialView = "display")
    {
        _startFullScreen = startFullScreen;
        _initialView = initialView;
        InitializeComponent();

        RootShell.RequestedTheme = ElementTheme.Dark;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        SystemBackdrop = new MicaBackdrop();
        InitializeAppWindow();
        InitializeTimersAndServices();

        RootShell.Loaded += (_, _) =>
        {
            _isLoaded = true;
            BuildTiles();
            ShowInitialView();
            StartNoiseMonitoring();
            RootShell.Focus(FocusState.Programmatic);
            if (_startFullScreen)
            {
                DispatcherQueue.TryEnqueue(() => SetFullScreen(true));
            }
        };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _weatherTimer.Stop();
            _noiseMonitor.Dispose();
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
            FullScreenLabel.Visibility = Visibility.Collapsed;
        };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();

        _noiseMonitor.LevelAvailable += (_, level) => DispatcherQueue.TryEnqueue(() => UpdateNoiseDisplay(level));
        _noiseMonitor.CaptureFailed += (_, message) => DispatcherQueue.TryEnqueue(() =>
        {
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
        TopDateText.Text = dateText;
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
    }

    private void ShowBoard()
    {
        DisplayRoot.Visibility = Visibility.Visible;
        SettingsRoot.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Collapsed;
        EditBoardButton.Visibility = Visibility.Visible;
        SettingsButton.Visibility = Visibility.Visible;
        ApplyViewportInteractionMode();
    }

    private void ShowSettings()
    {
        DisplayRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Visible;
        BackToBoardButton.Visibility = Visibility.Visible;
        EditBoardButton.Visibility = Visibility.Collapsed;
        SettingsButton.Visibility = Visibility.Collapsed;
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
    }

    private void EditBoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing) FinishEditing(); else EnterEditing();
    }

    private void EnterEditing()
    {
        if (_isEditing) return;
        ViewModel.BeginEditing();
        _isEditing = true;
        EditBoardIcon.Glyph = "\uE73E";
        AutomationProperties.SetName(EditBoardButton, "完成编辑");
        AddSubjectButton.Visibility = Visibility.Visible;
        DiscardEditButton.Visibility = Visibility.Visible;
        BoardModeHint.Text = "拖动磁贴或任意边框，位置和大小会吸附到网格";
        SetTilesEditing(true);
        ApplyViewportInteractionMode();
    }

    private void FinishEditing()
    {
        ViewModel.PublishEditing();
        _isEditing = false;
        _activeTileInteractions = 0;
        EditBoardIcon.Glyph = "\uE70F";
        AutomationProperties.SetName(EditBoardButton, "编辑看板");
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
        BoardModeHint.Text = "所有文字和笔迹都完整显示在磁贴上";
        SetTilesEditing(false);
        ApplyViewportInteractionMode();
    }

    private void DiscardEditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DiscardEditing();
        _isEditing = false;
        _activeTileInteractions = 0;
        BuildTiles();
        EditBoardIcon.Glyph = "\uE70F";
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
        BoardModeHint.Text = "所有文字和笔迹都完整显示在磁贴上";
        ApplyViewportInteractionMode();
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
        BoardCanvas.Children.Clear();
        foreach (SubjectBoard subject in ViewModel.Subjects) AddTile(subject);
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
            AddAttachmentAsync)
        {
            DataContext = subject
        };
        tile.SetEditing(_isEditing);
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        BoardCanvas.Children.Add(tile);
    }

    private void SetTilesEditing(bool editing)
    {
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.SetEditing(editing);
    }

    private void SetTileInteractionActive(bool active)
    {
        _activeTileInteractions = Math.Max(0, _activeTileInteractions + (active ? 1 : -1));
        ApplyViewportInteractionMode();
    }

    private void ApplyViewportInteractionMode()
    {
        bool isTileInteractionActive = _isEditing || _activeTileInteractions > 0;
        BoardScrollViewer.HorizontalScrollMode = isTileInteractionActive ? ScrollMode.Disabled : ScrollMode.Enabled;
        BoardScrollViewer.VerticalScrollMode = isTileInteractionActive ? ScrollMode.Disabled : ScrollMode.Enabled;
        BoardScrollViewer.HorizontalScrollBarVisibility = isTileInteractionActive ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        BoardScrollViewer.VerticalScrollBarVisibility = isTileInteractionActive ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
    }

    private void AddSubjectButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectBoard subject = ViewModel.AddSubject("新科目");
        LayoutCommitted(subject);
        AddTile(subject);
        UpdateBoardBounds();
        UpdateSubjectCount();
    }

    private async void DeleteSubject(SubjectBoard subject)
    {
        if (await ShowConfirmAsync("删除这个科目", $"确定删除“{subject.Name}”以及其中的文字、附件和笔迹吗？") != ContentDialogResult.Primary) return;
        ViewModel.Subjects.Remove(subject);
        BuildTiles();
    }

    private void LayoutChanged(SubjectBoard subject)
    {
        SubjectTileControl? tile = FindTile(subject);
        if (tile is null) return;
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        tile.ApplyModelLayout();
        UpdateBoardBounds();
    }

    private void LayoutCommitted(SubjectBoard subject)
    {
        subject.X = Math.Max(0, SnapToGrid(subject.X));
        subject.Y = Math.Max(0, SnapToGrid(subject.Y));
        subject.TileWidth = Math.Max(280, SnapToGrid(subject.TileWidth));
        subject.TileHeight = Math.Max(190, SnapToGrid(subject.TileHeight));
        LayoutChanged(subject);
    }

    private static double SnapToGrid(double value) => Math.Round(value / GridSize) * GridSize;

    private SubjectTileControl? FindTile(SubjectBoard subject) => BoardCanvas.Children
        .OfType<SubjectTileControl>()
        .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, subject));

    private async Task AddAttachmentAsync(HomeworkEntry homework)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add("*");
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;
        string extension = file.FileType.ToLowerInvariant();
        string kind = extension == ".pdf" ? "PDF" : IsImageExtension(extension) ? "图片" : "文件";
        homework.Attachments.Add(new AttachmentItem { Name = file.Name, Kind = kind, Path = file.Path });
        homework.NotifyAttachmentsChanged();
        BuildTiles();
        SetTilesEditing(_isEditing);
    }

    private static bool IsImageExtension(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";

    private void UpdateBoardBounds()
    {
        double width = Math.Max(1100, ViewModel.Subjects.Count == 0 ? 1100 : ViewModel.Subjects.Max(subject => subject.X + subject.TileWidth + 48));
        double height = Math.Max(780, ViewModel.Subjects.Count == 0 ? 780 : ViewModel.Subjects.Max(subject => subject.Y + subject.TileHeight + 48));
        BoardSurface.Width = width;
        BoardSurface.Height = height;
        BoardCanvas.Width = width;
        BoardCanvas.Height = height;
        GridCanvas.Width = width;
        GridCanvas.Height = height;
        if (Math.Abs(width - _renderedGridWidth) > 0.5 || Math.Abs(height - _renderedGridHeight) > 0.5)
        {
            RenderGrid(width, height);
            _renderedGridWidth = width;
            _renderedGridHeight = height;
        }
    }

    private void RenderGrid(double width, double height)
    {
        GridCanvas.Children.Clear();
        for (double x = 0; x <= width; x += GridSize)
        {
            bool major = ((int)(x / GridSize)) % 4 == 0;
            GridCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = height,
                Stroke = new SolidColorBrush(major ? Windows.UI.Color.FromArgb(72, 80, 80, 84) : Windows.UI.Color.FromArgb(35, 70, 70, 74)),
                StrokeThickness = 1
            });
        }
        for (double y = 0; y <= height; y += GridSize)
        {
            bool major = ((int)(y / GridSize)) % 4 == 0;
            GridCanvas.Children.Add(new Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(major ? Windows.UI.Color.FromArgb(72, 80, 80, 84) : Windows.UI.Color.FromArgb(35, 70, 70, 74)),
                StrokeThickness = 1
            });
        }
    }

    private void BoardScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_isEditing || !_isFullScreen || !e.IsIntermediate) return;
        FullScreenLabel.Visibility = Visibility.Visible;
        _fullScreenLabelTimer.Stop();
        _fullScreenLabelTimer.Start();
    }

    private void UpdateSubjectCount() => SubjectCountText.Text = $"{ViewModel.Subjects.Count} 个科目";

    private void StartNoiseMonitoring()
    {
        int sampleRate = GetSelectedSampleRate();
        MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Informational;
        MicrophoneStatusInfoBar.Message = $"正在以 {sampleRate} Hz 采样，只计算音量";
        _noiseMonitor.Start(sampleRate);
    }

    private int GetSelectedSampleRate()
    {
        if (MicrophoneSampleRateComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int sampleRate)) return sampleRate;
        return 16000;
    }

    private void MicrophoneSampleRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoaded) StartNoiseMonitoring();
    }

    private void MicrophoneCalibrationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _noiseMonitor.CalibrationOffsetDb = e.NewValue;
        if (MicrophoneCalibrationLabel is not null) MicrophoneCalibrationLabel.Text = $"校准偏移 · {e.NewValue:+0;-0;0} dB";
    }

    private void UpdateNoiseDisplay(double level)
    {
        string state = level < 45 ? "安静" : level < 60 ? "适中" : "偏吵";
        NoiseText.Text = $"{level:0} dB · {state}";
        MicrophoneStatusInfoBar.Severity = InfoBarSeverity.Success;
        MicrophoneStatusInfoBar.Message = $"麦克风工作正常 · {level:0.0} dB";
    }

    private async void RefreshWeatherButton_Click(object sender, RoutedEventArgs e) => await RefreshWeatherAsync();

    private async Task RefreshWeatherAsync()
    {
        WeatherStatusText.Text = "正在刷新…";
        try
        {
            WeatherSnapshot snapshot = await _weatherService.GetCurrentAsync(
                WeatherEndpointTextBox.Text.Trim(),
                WeatherCityTextBox.Text.Trim(),
                WeatherApiKeyBox.Password);
            WeatherText.Text = $"{snapshot.Condition}  {snapshot.TemperatureCelsius:0.#}°C";
            WeatherStatusText.Text = $"更新于 {DateTime.Now:HH:mm}";
            if (!_weatherTimer.IsEnabled) _weatherTimer.Start();
        }
        catch (Exception exception)
        {
            WeatherText.Text = "天气不可用";
            WeatherStatusText.Text = exception.Message;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme) return;
        RootShell.RequestedTheme = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
    }

    private async void ExportDataButton_Click(object sender, RoutedEventArgs e) => await ShowMessageAsync("导出备份包", "完整功能阶段会把磁贴布局、文字、附件和可继续编辑的笔迹一起打包导出。", "完成");
    private async void ImportDataButton_Click(object sender, RoutedEventArgs e) => await ShowMessageAsync("导入备份包", "完整功能阶段会校验备份包，再恢复磁贴布局与内容。", "完成");
    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_isFullScreen);

    private void SetFullScreen(bool isFullScreen)
    {
        if (_appWindow is null || _isFullScreen == isFullScreen) return;
        _appWindow.SetPresenter(isFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        _isFullScreen = isFullScreen;
        FullScreenIcon.Glyph = isFullScreen ? "\uE73F" : "\uE740";
        FullScreenLabel.Text = isFullScreen ? "退出全屏" : "进入全屏";
        FullScreenLabel.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(FullScreenButton, isFullScreen ? "退出全屏" : "进入全屏");
    }

    private void RootShell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (_isEditing) FinishEditing(); else if (_isFullScreen) SetFullScreen(false);
        e.Handled = true;
    }

    private void RootShell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 900;
        if (compact)
        {
            DisplayGrid.ColumnDefinitions[0].MinWidth = 0;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(0);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(300);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(ClockPanel, 0); Grid.SetRow(ClockPanel, 0); Grid.SetColumn(BoardWorkspace, 0); Grid.SetRow(BoardWorkspace, 1);
            ClockPanel.BorderThickness = new Thickness(0, 0, 0, 1);
            MainTimeText.FontSize = 72;
        }
        else
        {
            DisplayGrid.ColumnDefinitions[0].MinWidth = 400;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(2, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(ClockPanel, 0); Grid.SetRow(ClockPanel, 0); Grid.SetColumn(BoardWorkspace, 1); Grid.SetRow(BoardWorkspace, 0);
            ClockPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            MainTimeText.FontSize = 112;
        }
    }

    private async Task<ContentDialogResult> ShowConfirmAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot, Title = title, Content = message,
            PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync();
    }

    private async Task ShowMessageAsync(string title, string message, string closeText)
    {
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = title, Content = message, CloseButtonText = closeText };
        await dialog.ShowAsync();
    }
}
