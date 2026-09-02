using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PancakeBoard.Models;
using PancakeBoard.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace PancakeBoard;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stack<Polyline> _redoStrokes = new();
    private readonly List<Polyline> _strokeHistory = [];
    private readonly bool _startFullScreen;
    private readonly string _initialView;
    private AppWindow? _appWindow;
    private Polyline? _activeStroke;
    private Windows.UI.Color _inkColor = Windows.UI.Color.FromArgb(255, 32, 32, 40);
    private InkTool _inkTool = InkTool.Pen;
    private bool _isFullScreen;
    private HomeworkEntry? _lastDeletedHomework;
    private SubjectBoard? _lastDeletedSubject;
    private int _lastDeletedIndex = -1;

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
            RootShell.Focus(FocusState.Programmatic);
            ShowInitialView();
            DispatcherQueue.TryEnqueue(UpdateSubjectGridWidth);
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

        // UI 阶段使用平滑的模拟值，后续替换为本机麦克风采样服务。
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

    private void ShowDisplay()
    {
        DisplayRoot.Visibility = Visibility.Visible;
        EditorRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Collapsed;
        EditBoardButton.Visibility = Visibility.Visible;
        BackToBoardButton.Visibility = Visibility.Collapsed;
    }

    private void ShowInitialView()
    {
        switch (_initialView)
        {
            case "editor":
                ShowEditor(createSnapshot: true);
                break;
            case "settings":
                ShowSettings();
                break;
            case "ink":
                ShowEditor(createSnapshot: true);
                InkOverlay.Visibility = Visibility.Visible;
                break;
            default:
                ShowDisplay();
                break;
        }
    }

    private void ShowEditor(bool createSnapshot)
    {
        if (createSnapshot)
        {
            ViewModel.BeginEditing();
        }

        DisplayRoot.Visibility = Visibility.Collapsed;
        EditorRoot.Visibility = Visibility.Visible;
        SettingsRoot.Visibility = Visibility.Collapsed;
        EditBoardButton.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Visible;

        SubjectList.SelectedItem = ViewModel.SelectedSubject;
        HomeworkList.SelectedItem = ViewModel.SelectedHomework;
        RefreshEditor();
    }

    private void ShowSettings()
    {
        DisplayRoot.Visibility = Visibility.Collapsed;
        EditorRoot.Visibility = Visibility.Collapsed;
        SettingsRoot.Visibility = Visibility.Visible;
        EditBoardButton.Visibility = Visibility.Collapsed;
        BackToBoardButton.Visibility = Visibility.Visible;
    }

    private void EditBoardButton_Click(object sender, RoutedEventArgs e) => ShowEditor(createSnapshot: true);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void BackToBoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditorRoot.Visibility == Visibility.Visible)
        {
            ViewModel.PublishEditing();
        }

        ShowDisplay();
    }

    private void PublishEditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.PublishEditing();
        ShowDisplay();
    }

    private void DiscardEditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DiscardEditing();
        ShowDisplay();
    }

    private void ManageSubjectsButton_Click(object sender, RoutedEventArgs e) => ShowEditor(createSnapshot: true);

    private void SubjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectList.SelectedItem is SubjectBoard subject)
        {
            ViewModel.SelectedSubject = subject;
            HomeworkList.SelectedItem = ViewModel.SelectedHomework;
            RefreshEditor();
        }
    }

    private void HomeworkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HomeworkList.SelectedItem is HomeworkEntry homework)
        {
            ViewModel.SelectedHomework = homework;
            RefreshEditor();
        }
    }

    private void RefreshEditor()
    {
        HomeworkTextBox.IsEnabled = ViewModel.SelectedHomework is not null;
        HomeworkTextBox.Text = ViewModel.SelectedHomework?.Content ?? string.Empty;
    }

    private void HomeworkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel.SelectedHomework is not null && ViewModel.SelectedHomework.Content != HomeworkTextBox.Text)
        {
            ViewModel.SelectedHomework.Content = HomeworkTextBox.Text;
        }
    }

    private void AddHomeworkButton_Click(object sender, RoutedEventArgs e)
    {
        HomeworkEntry? homework = ViewModel.AddHomework();
        if (homework is not null)
        {
            HomeworkList.SelectedItem = homework;
            RefreshEditor();
            HomeworkTextBox.Focus(FocusState.Programmatic);
            HomeworkTextBox.SelectAll();
        }
    }

    private async void AddSubjectButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox nameBox = new()
        {
            PlaceholderText = "例如：化学",
            Header = "科目名称",
            MinWidth = 320
        };

        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot,
            Title = "添加科目",
            Content = nameBox,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string name = string.IsNullOrWhiteSpace(nameBox.Text) ? "新科目" : nameBox.Text.Trim();
            SubjectBoard subject = ViewModel.AddSubject(name);
            SubjectList.SelectedItem = subject;
        }
    }

    private async void DeleteSubjectButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectBoard? subject = ViewModel.SelectedSubject;
        if (subject is null)
        {
            return;
        }

        if (subject.Entries.Count > 0)
        {
            await ShowMessageAsync("暂时不能删除", "该科目仍有作业内容，请先移除其中的内容。", "知道了");
            return;
        }

        ContentDialogResult result = await ShowConfirmAsync("删除科目", $"确定删除“{subject.Name}”吗？");
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.Subjects.Remove(subject);
            ViewModel.SelectedSubject = ViewModel.Subjects.FirstOrDefault();
        }
    }

    private async void DeleteHomeworkButton_Click(object sender, RoutedEventArgs e)
    {
        SubjectBoard? subject = ViewModel.SelectedSubject;
        HomeworkEntry? homework = ViewModel.SelectedHomework;
        if (subject is null || homework is null)
        {
            return;
        }

        ContentDialogResult result = await ShowConfirmAsync("删除这条作业", "文字、附件和手写笔记会一起移除。");
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _lastDeletedHomework = homework;
        _lastDeletedSubject = subject;
        _lastDeletedIndex = subject.Entries.IndexOf(homework);
        subject.Entries.Remove(homework);
        subject.NotifyEntriesChanged();
        ViewModel.SelectedHomework = subject.Entries.FirstOrDefault();
        HomeworkList.SelectedItem = ViewModel.SelectedHomework;
        RefreshEditor();
        UndoInfoBar.IsOpen = true;
    }

    private void UndoDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDeletedHomework is null || _lastDeletedSubject is null)
        {
            return;
        }

        int index = Math.Clamp(_lastDeletedIndex, 0, _lastDeletedSubject.Entries.Count);
        _lastDeletedSubject.Entries.Insert(index, _lastDeletedHomework);
        _lastDeletedSubject.NotifyEntriesChanged();
        ViewModel.SelectedSubject = _lastDeletedSubject;
        ViewModel.SelectedHomework = _lastDeletedHomework;
        SubjectList.SelectedItem = _lastDeletedSubject;
        HomeworkList.SelectedItem = _lastDeletedHomework;
        _lastDeletedHomework = null;
        _lastDeletedSubject = null;
        _lastDeletedIndex = -1;
        UndoInfoBar.IsOpen = false;
    }

    private void BulletedListButton_Click(object sender, RoutedEventArgs e) => InsertListPrefix("• ");

    private void NumberedListButton_Click(object sender, RoutedEventArgs e) => InsertListPrefix("1. ");

    private void InsertListPrefix(string prefix)
    {
        int selectionStart = HomeworkTextBox.SelectionStart;
        HomeworkTextBox.Text = HomeworkTextBox.Text.Insert(selectionStart, prefix);
        HomeworkTextBox.SelectionStart = selectionStart + prefix.Length;
        HomeworkTextBox.Focus(FocusState.Programmatic);
    }

    private async void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHomework is null)
        {
            return;
        }

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
        ViewModel.SelectedHomework.Attachments.Add(new AttachmentItem
        {
            Name = file.Name,
            Kind = kind,
            Path = file.Path
        });
        ViewModel.SelectedHomework.NotifyAttachmentsChanged();
    }

    private static bool IsImageExtension(string extension) => extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";

    private async void OpenAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentItem attachment)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(attachment.Path) && File.Exists(attachment.Path))
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(attachment.Path);
            await Launcher.LaunchFileAsync(file);
            return;
        }

        await ShowMessageAsync(attachment.Name, $"这是用于完整 UI 演示的{attachment.Kind}附件。导入真实文件后可从这里打开。", "关闭");
    }

    private async void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentItem attachment || ViewModel.SelectedHomework is null)
        {
            return;
        }

        ContentDialogResult result = await ShowConfirmAsync("移除附件", $"确定移除“{attachment.Name}”吗？");
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.SelectedHomework.Attachments.Remove(attachment);
            ViewModel.SelectedHomework.NotifyAttachmentsChanged();
        }
    }

    private async void SubjectCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SubjectBoard subject)
        {
            return;
        }

        StackPanel content = new() { Spacing = 14, MinWidth = 560 };
        foreach (HomeworkEntry entry in subject.Entries)
        {
            StackPanel entryContent = new() { Spacing = 8 };
            entryContent.Children.Add(new TextBlock
            {
                Text = entry.Content,
                FontSize = 20,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 30
            });
            entryContent.Children.Add(new TextBlock
            {
                Text = entry.SupplementSummary,
                FontSize = 12,
                Foreground = GetThemeBrush("BoardTextMutedBrush", Windows.UI.Color.FromArgb(255, 115, 115, 134))
            });
            content.Children.Add(new Border
            {
                Background = GetThemeBrush("BoardSurfaceSecondaryBrush", Windows.UI.Color.FromArgb(255, 30, 30, 40)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18),
                Child = entryContent
            });
        }

        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot,
            Title = $"{subject.Name}作业",
            Content = new ScrollViewer { MaxHeight = 600, Content = content },
            CloseButtonText = "关闭"
        };
        await dialog.ShowAsync();
    }

    private void OpenInkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHomework is null)
        {
            return;
        }

        InkOverlay.Visibility = Visibility.Visible;
        DrawingCanvas.Focus(FocusState.Programmatic);
    }

    private void CancelInkButton_Click(object sender, RoutedEventArgs e) => InkOverlay.Visibility = Visibility.Collapsed;

    private void SaveInkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedHomework is not null)
        {
            ViewModel.SelectedHomework.HasHandwriting = _strokeHistory.Count > 0;
        }

        InkOverlay.Visibility = Visibility.Collapsed;
    }

    private void PenToolButton_Click(object sender, RoutedEventArgs e) => SelectInkTool(InkTool.Pen);

    private void HighlighterToolButton_Click(object sender, RoutedEventArgs e) => SelectInkTool(InkTool.Highlighter);

    private void EraserToolButton_Click(object sender, RoutedEventArgs e) => SelectInkTool(InkTool.Eraser);

    private void SelectInkTool(InkTool tool)
    {
        _inkTool = tool;
        PenToolButton.IsChecked = tool == InkTool.Pen;
        HighlighterToolButton.IsChecked = tool == InkTool.Highlighter;
        EraserToolButton.IsChecked = tool == InkTool.Eraser;
    }

    private void InkColorButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string hex)
        {
            _inkColor = MainViewModel.BrushFromHex(hex).Color;
        }
    }

    private void DrawingCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Touch or PointerDeviceType.Pen))
        {
            return;
        }

        Point point = e.GetCurrentPoint(DrawingCanvas).Position;
        if (_inkTool == InkTool.Eraser)
        {
            EraseStrokeAt(point);
            e.Handled = true;
            return;
        }

        SolidColorBrush strokeBrush = new(_inkColor)
        {
            Opacity = _inkTool == InkTool.Highlighter ? 0.42 : 1
        };
        _activeStroke = new Polyline
        {
            Stroke = strokeBrush,
            StrokeThickness = _inkTool == InkTool.Highlighter ? Math.Max(14, InkThicknessSlider.Value * 2) : InkThicknessSlider.Value,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        _activeStroke.Points.Add(point);
        DrawingCanvas.Children.Add(_activeStroke);
        _strokeHistory.Add(_activeStroke);
        _redoStrokes.Clear();
        DrawingCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DrawingCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is not (PointerDeviceType.Touch or PointerDeviceType.Pen))
        {
            return;
        }

        Point point = e.GetCurrentPoint(DrawingCanvas).Position;
        if (_inkTool == InkTool.Eraser && e.GetCurrentPoint(DrawingCanvas).Properties.IsLeftButtonPressed)
        {
            EraseStrokeAt(point);
        }
        else if (_activeStroke is not null)
        {
            _activeStroke.Points.Add(point);
        }

        e.Handled = true;
    }

    private void DrawingCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activeStroke is not null)
        {
            _activeStroke = null;
            DrawingCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void EraseStrokeAt(Point point)
    {
        Polyline? stroke = VisualTreeHelper.FindElementsInHostCoordinates(point, DrawingCanvas)
            .OfType<Polyline>()
            .FirstOrDefault();
        if (stroke is null)
        {
            return;
        }

        DrawingCanvas.Children.Remove(stroke);
        _strokeHistory.Remove(stroke);
        _redoStrokes.Push(stroke);
    }

    private void UndoInkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokeHistory.Count == 0)
        {
            return;
        }

        Polyline stroke = _strokeHistory[^1];
        _strokeHistory.RemoveAt(_strokeHistory.Count - 1);
        DrawingCanvas.Children.Remove(stroke);
        _redoStrokes.Push(stroke);
    }

    private void RedoInkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_redoStrokes.TryPop(out Polyline? stroke))
        {
            return;
        }

        DrawingCanvas.Children.Add(stroke);
        _strokeHistory.Add(stroke);
    }

    private async void ClearInkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokeHistory.Count == 0)
        {
            return;
        }

        if (await ShowConfirmAsync("清空画布", "这会移除当前画布上的所有笔迹。") != ContentDialogResult.Primary)
        {
            return;
        }

        foreach (Polyline stroke in _strokeHistory)
        {
            DrawingCanvas.Children.Remove(stroke);
        }

        _strokeHistory.Clear();
        _redoStrokes.Clear();
    }

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
        await ShowMessageAsync("导出备份包", "完整功能阶段会把科目、文字、附件和手写笔记打包导出。当前按钮用于确认完整 UI 流程。", "完成");

    private async void ImportDataButton_Click(object sender, RoutedEventArgs e) =>
        await ShowMessageAsync("导入备份包", "完整功能阶段会先校验备份包，再展示即将导入的科目和附件摘要。", "完成");

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
        if (e.Key == VirtualKey.Escape)
        {
            if (InkOverlay.Visibility == Visibility.Visible)
            {
                InkOverlay.Visibility = Visibility.Collapsed;
            }
            else if (_isFullScreen)
            {
                SetFullScreen(false);
            }

            e.Handled = true;
        }
    }

    private void RootShell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 780;
        if (compact)
        {
            DisplayGrid.ColumnDefinitions[0].MinWidth = 0;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(0);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(390);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(ClockPanel, 0);
            Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(CardsPanel, 0);
            Grid.SetRow(CardsPanel, 1);
            ClockPanel.BorderThickness = new Thickness(0, 0, 0, 1);
            MainTimeText.FontSize = 78;
        }
        else
        {
            DisplayGrid.ColumnDefinitions[0].MinWidth = 420;
            DisplayGrid.ColumnDefinitions[0].Width = new GridLength(2, GridUnitType.Star);
            DisplayGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
            DisplayGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            DisplayGrid.RowDefinitions[1].Height = new GridLength(0);
            Grid.SetColumn(ClockPanel, 0);
            Grid.SetRow(ClockPanel, 0);
            Grid.SetColumn(CardsPanel, 1);
            Grid.SetRow(CardsPanel, 0);
            ClockPanel.BorderThickness = new Thickness(0, 0, 1, 0);
            MainTimeText.FontSize = 116;
        }
    }

    private void SubjectGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateSubjectGridWidth);
    }

    private void SubjectGridView_LayoutUpdated(object? sender, object e) => UpdateSubjectGridWidth();

    private void UpdateSubjectGridWidth()
    {
        if (SubjectGridView.ItemsPanelRoot is not ItemsWrapGrid panel || SubjectGridView.ActualWidth <= 0)
        {
            return;
        }

        int columns = SubjectGridView.ActualWidth >= 820 ? 2 : 1;
        double itemWidth = Math.Max(340, (SubjectGridView.ActualWidth - 4) / columns);
        if (Math.Abs(panel.ItemWidth - itemWidth) > 1)
        {
            panel.ItemWidth = itemWidth;
        }

        if (Math.Abs(panel.ItemHeight - 300) > 1)
        {
            panel.ItemHeight = 300;
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
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = closeText
        };
        await dialog.ShowAsync();
    }

    private static Brush GetThemeBrush(string key, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }

    private enum InkTool
    {
        Pen,
        Highlighter,
        Eraser
    }
}
