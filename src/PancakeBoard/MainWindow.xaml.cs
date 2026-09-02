using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PancakeBoard.Controls;
using PancakeBoard.Models;
using PancakeBoard.ViewModels;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace PancakeBoard;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _startFullScreen;
    private readonly string _initialView;
    private AppWindow? _appWindow;
    private bool _isEditing;
    private bool _isFullScreen;

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
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        RootShell.Loaded += (_, _) =>
        {
            BuildTiles();
            ShowInitialView();
            RootShell.Focus(FocusState.Programmatic);
            if (_startFullScreen)
            {
                DispatcherQueue.TryEnqueue(() => SetFullScreen(true));
            }
        };
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

        double simulatedNoise = 42 + Math.Sin(now.Second / 8d) * 3;
        string state = simulatedNoise < 45 ? "安静" : simulatedNoise < 60 ? "适中" : "偏吵";
        NoiseText.Text = $"{simulatedNoise:0} dB · {state}";
    }

    private static string GetChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日"
    };

    private void ShowInitialView()
    {
        if (_initialView == "settings")
        {
            ShowSettings();
            return;
        }

        ShowBoard();
        if (_initialView is "editor" or "ink")
        {
            EnterEditing();
        }
    }

    private void ShowBoard()
    {
        DisplayRoot.Visibility = Visibility.Visible;
        SettingsRoot.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Collapsed;
        EditBoardButton.Visibility = Visibility.Visible;
    }

    private void ShowSettings()
    {
        DisplayRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Visible;
        BackToBoardButton.Visibility = Visibility.Visible;
        EditBoardButton.Visibility = Visibility.Collapsed;
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
    }

    private void EditBoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            FinishEditing();
        }
        else
        {
            EnterEditing();
        }
    }

    private void EnterEditing()
    {
        if (_isEditing)
        {
            return;
        }

        ViewModel.BeginEditing();
        _isEditing = true;
        EditBoardText.Text = "完成编辑";
        EditBoardIcon.Glyph = "\uE73E";
        AddSubjectButton.Visibility = Visibility.Visible;
        DiscardEditButton.Visibility = Visibility.Visible;
        BoardModeHint.Text = "可直接改字、涂画，并拖动磁贴右下角调整大小";
        SetTilesEditing(true);
    }

    private void FinishEditing()
    {
        ViewModel.PublishEditing();
        _isEditing = false;
        EditBoardText.Text = "直接编辑";
        EditBoardIcon.Glyph = "\uE70F";
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
        BoardModeHint.Text = "所有文字和笔迹都完整显示在磁贴上";
        SetTilesEditing(false);
    }

    private void DiscardEditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DiscardEditing();
        _isEditing = false;
        BuildTiles();
        EditBoardText.Text = "直接编辑";
        EditBoardIcon.Glyph = "\uE70F";
        AddSubjectButton.Visibility = Visibility.Collapsed;
        DiscardEditButton.Visibility = Visibility.Collapsed;
        BoardModeHint.Text = "所有文字和笔迹都完整显示在磁贴上";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            FinishEditing();
        }

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
        foreach (SubjectBoard subject in ViewModel.Subjects)
        {
            AddTile(subject);
        }

        UpdateBoardBounds();
        UpdateSubjectCount();
    }

    private void AddTile(SubjectBoard subject)
    {
        SubjectTileControl tile = new(subject, DeleteSubject, MoveSubject, AddAttachmentAsync);
        tile.DataContext = subject;
        tile.SetEditing(_isEditing);
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        BoardCanvas.Children.Add(tile);
    }

    private void SetTilesEditing(bool editing)
    {
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>())
        {
            tile.SetEditing(editing);
        }
    }

    private void AddSubjectButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectBoard subject = ViewModel.AddSubject("新科目");
        AddTile(subject);
        UpdateBoardBounds();
        UpdateSubjectCount();
    }

    private async void DeleteSubject(SubjectBoard subject)
    {
        if (await ShowConfirmAsync("删除这个科目", $"确定删除“{subject.Name}”以及其中的文字、附件和笔迹吗？") != ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.Subjects.Remove(subject);
        BuildTiles();
    }

    private void MoveSubject(SubjectBoard subject, double horizontalChange, double verticalChange)
    {
        SubjectTileControl? tile = BoardCanvas.Children.OfType<SubjectTileControl>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, subject));

        // Tiles created in code do not need DataContext for binding, so match by their current canvas coordinates.
        tile ??= BoardCanvas.Children.OfType<SubjectTileControl>()
            .FirstOrDefault(candidate => Math.Abs(Canvas.GetLeft(candidate) - subject.X) < 0.1 && Math.Abs(Canvas.GetTop(candidate) - subject.Y) < 0.1);
        if (tile is null)
        {
            return;
        }

        subject.X = Math.Max(12, subject.X + horizontalChange);
        subject.Y = Math.Max(12, subject.Y + verticalChange);
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        UpdateBoardBounds();
    }

    private async Task AddAttachmentAsync(HomeworkEntry homework)
    {
        FileOpenPicker picker = new();
        picker.FileTypeFilter.Add("*");
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

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
        double requiredWidth = ViewModel.Subjects.Count == 0
            ? 1100
            : ViewModel.Subjects.Max(subject => subject.X + subject.TileWidth + 48);
        double requiredHeight = ViewModel.Subjects.Count == 0
            ? 780
            : ViewModel.Subjects.Max(subject => subject.Y + subject.TileHeight + 48);
        BoardCanvas.Width = Math.Max(1100, requiredWidth);
        BoardCanvas.Height = Math.Max(780, requiredHeight);
    }

    private void UpdateSubjectCount() => SubjectCountText.Text = $"{ViewModel.Subjects.Count} 个科目";

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        RootShell.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private async void ExportDataButton_Click(object sender, RoutedEventArgs e) =>
        await ShowMessageAsync("导出备份包", "完整功能阶段会把磁贴布局、文字、附件和可继续编辑的笔迹一起打包导出。", "完成");

    private async void ImportDataButton_Click(object sender, RoutedEventArgs e) =>
        await ShowMessageAsync("导入备份包", "完整功能阶段会校验备份包，再恢复磁贴布局与内容。", "完成");

    private void FullScreenButton_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_isFullScreen);

    private void SetFullScreen(bool isFullScreen)
    {
        if (_appWindow is null || _isFullScreen == isFullScreen)
        {
            return;
        }

        _appWindow.SetPresenter(isFullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        _isFullScreen = isFullScreen;
        FullScreenIcon.Glyph = isFullScreen ? "\uE73F" : "\uE740";
    }

    private void RootShell_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
        {
            return;
        }

        if (_isEditing)
        {
            FinishEditing();
        }
        else if (_isFullScreen)
        {
            SetFullScreen(false);
        }

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
            Grid.SetColumn(ClockPanel, 0);
            Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(BoardWorkspace, 0);
            Grid.SetRow(BoardWorkspace, 1);
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
            Grid.SetColumn(ClockPanel, 0);
            Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(BoardWorkspace, 1);
            Grid.SetRow(BoardWorkspace, 0);
            ClockPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            MainTimeText.FontSize = 112;
        }
    }

    private async Task<ContentDialogResult> ShowConfirmAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync();
    }

    private async Task ShowMessageAsync(string title, string message, string closeText)
    {
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = title, Content = message, CloseButtonText = closeText };
        await dialog.ShowAsync();
    }
}
