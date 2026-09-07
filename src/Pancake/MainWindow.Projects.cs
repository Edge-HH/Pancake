using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;
using Windows.Storage.Pickers;

namespace Pancake;

public sealed partial class MainWindow
{
    private readonly ProjectStore _projectStore = new();
    private ProjectLibrary _library = new();
    private bool _storageReady = true;
    private bool _projectOperation;
    private bool _applyingProjectAppearance;
    private SubjectBoard? _activeInkSubject;
    private readonly TextBlock _inkSubjectLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly InkToolSettings _inkSettings = new();
    private readonly StackPanel _inkColors = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ComboBox _inkTool = new() { ItemsSource = new[] { "画笔", "橡皮擦" }, SelectedIndex = 0, Width = 105 };
    private ProjectDocument? CurrentProject => _library.Projects.FirstOrDefault(p => p.Id == _library.ActiveProjectId);

    private void InitializeProjectCommands()
    {
        GlobalInkTools.Children.Add(_inkSubjectLabel);
        _inkTool.SelectionChanged += (_, _) => _inkSettings.Eraser = _inkTool.SelectedIndex == 1;
        GlobalInkTools.Children.Add(_inkTool);
        GlobalInkTools.Children.Add(_inkColors);
        RefreshInkPalette();
        Slider thickness = new() { Minimum = 2, Maximum = 18, StepFrequency = 1, Value = 5, Width = 110, Header = "粗细" };
        thickness.ValueChanged += (_, args) => _inkSettings.Thickness = args.NewValue;
        GlobalInkTools.Children.Add(thickness);
        Button clear = new() { Content = "清空笔迹" };
        clear.Click += ClearInk_Click;
        GlobalInkTools.Children.Add(clear);
        UpdateProjectCommands();
    }

    private void RefreshInkPalette()
    {
        var current = _inkSettings.Color;
        _inkSettings.Color = ViewModels.MainViewModel.BrushFromHex(ColorPalette.Resolve($"#{current.R:X2}{current.G:X2}{current.B:X2}")).Color;
        _inkColors.Children.Clear();
        foreach (string hex in new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA" }.Select(ColorPalette.Resolve))
        {
            var color = ViewModels.MainViewModel.BrushFromHex(hex).Color;
            ColorSwatchButton swatch = new(BoardTheme.DisplayContentColor(color), 32);
            swatch.SetSelected(color.Equals(_inkSettings.Color));
            ToolTipService.SetToolTip(swatch, "笔迹颜色 " + hex);
            swatch.Click += (_, _) =>
            {
                _inkSettings.Color = color;
                _inkTool.SelectedIndex = 0;
                foreach (ColorSwatchButton candidate in _inkColors.Children)
                    candidate.SetSelected(ReferenceEquals(candidate, swatch));
            };
            _inkColors.Children.Add(swatch);
        }
    }

    private void CaptureCurrentProject()
    {
        if (CurrentProject is { } project) project.Subjects = AppDataStore.CaptureSubjects(ViewModel.Subjects);
    }

    private void PersistProjects()
    {
        if (!_storageReady) throw new IOException("原项目数据未能读取，已停止写入。请先恢复数据并重启软件。");
        CaptureCurrentProject();
        _library.Settings = _settings;
        _projectStore.Save(_library);
    }

    private void RememberAppearance()
    {
        if (!_isLoaded || _applyingProjectAppearance || CurrentProject is not { } project) return;
        project.Theme = _settings.Theme;
        project.Palette = _settings.Palette;
    }

    private void UpdateProjectCommands()
    {
        if (ProjectCommands is null || EmptyProjectPanel is null) return;
        bool exists = CurrentProject is not null;
        ProjectNameText.Text = CurrentProject?.Name ?? "选择项目";
        SubjectCountText.Text = exists ? $"{ViewModel.Subjects.Count} 个科目" : "";
        ProjectCommands.Visibility = SettingsRoot.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        RenameProjectMenu.IsEnabled = DeleteProjectMenu.IsEnabled = SaveProjectMenu.IsEnabled = ExportProjectMenu.IsEnabled = exists && _storageReady;
        GlobalPenButton.IsEnabled = exists;
        EmptyProjectPanel.Visibility = !exists && SettingsRoot.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        BoardWorkspace.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        EditBoardButton.IsEnabled = exists;
        UpdateInkSubjectLabel();
    }

    private void ReportStorageError(string title, Exception error)
    {
        StorageInfoBar.Title = title;
        StorageInfoBar.Message = error.Message;
        StorageInfoBar.Severity = InfoBarSeverity.Error;
        StorageInfoBar.IsOpen = true;
    }

    private async Task RunProjectAction(Func<Task> action)
    {
        if (_projectOperation) return;
        _projectOperation = true;
        _saveTimer.Stop();
        CaptureCurrentProject();
        ProjectLibrary before = ProjectStore.Clone(_library);
        try { await action(); }
        catch (Exception ex)
        {
            // 磁盘写入失败时恢复本轮操作前的内存内容；外部成功保存的文件不会被回滚或删除。
            _library = before;
            _settings = _library.Settings;
            ViewModel.ReplaceSubjects(AppDataStore.RestoreSubjects(CurrentProject?.Subjects ?? []));
            GlobalPenButton.IsChecked = false;
            _isEditing = false;
            BuildTiles();
            UpdateRichTextToolbar();
            ReportStorageError("操作未完成", ex);
        }
        finally { _projectOperation = false; UpdateProjectCommands(); }
    }

    private void RecentProjects_Opening(object sender, object e)
    {
        RecentProjectsPanel.Children.Clear();
        foreach (ProjectDocument project in _library.Projects.OrderByDescending(p => p.LastUsedAt))
        {
            Button button = new() { Content = project.Name, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            ToolTipService.SetToolTip(button, $"创建于 {project.CreatedAt:yyyy年M月d日 HH:mm}");
            button.Click += async (_, _) =>
            {
                ProjectPicker.Flyout.Hide();
                await RunProjectAction(async () => await SwitchProjectAsync(project));
            };
            RecentProjectsPanel.Children.Add(button);
        }
    }

    private async Task SwitchProjectAsync(ProjectDocument project)
    {
        if (CurrentProject?.Id == project.Id) return;
        if (_isEditing) FinishEditing();
        PersistProjects();
        await ActivateProjectAsync(project);
    }

    private async Task ActivateProjectAsync(ProjectDocument? project)
    {
        _library.ActiveProjectId = project?.Id;
        _activeInkSubject = null;
        if (project is not null) project.LastUsedAt = DateTime.Now;
        ViewModel.ReplaceSubjects(AppDataStore.RestoreSubjects(project?.Subjects ?? []));
        if (project is not null) await OfferProjectAppearanceAsync(project);
        ApplyPalette();
        BuildTiles();
        ShowBoard();
        if (project is not null) EnterEditing();
        PersistProjects();
        UpdateProjectCommands();
    }

    private async Task OfferProjectAppearanceAsync(ProjectDocument project)
    {
        bool themeDiffers = project.Theme != _settings.Theme;
        bool paletteDiffers = project.Palette != _settings.Palette;
        if (!themeDiffers && !paletteDiffers) return;
        CheckBox theme = new() { Content = $"应用项目主题：{project.Theme switch { "Light" => "浅色", "Dark" => "深色", _ => "跟随系统" }}", IsChecked = themeDiffers, Visibility = themeDiffers ? Visibility.Visible : Visibility.Collapsed };
        CheckBox palette = new() { Content = $"应用项目色系：{(project.Palette == "Macaron" ? "马卡龙" : "鲜明")}", IsChecked = paletteDiffers, Visibility = paletteDiffers ? Visibility.Visible : Visibility.Collapsed };
        StackPanel content = new() { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "项目外观与软件当前设置不同，可分别选择要应用的设置。", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(theme); content.Children.Add(palette);
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = "应用项目外观", Content = content, PrimaryButtonText = "应用所选", CloseButtonText = "保持当前" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _applyingProjectAppearance = true;
        try
        {
            if (themeDiffers && theme.IsChecked == true)
            {
                _settings.Theme = project.Theme;
                ThemeComboBox.SelectedIndex = project.Theme switch { "Light" => 1, "Default" => 2, _ => 0 };
                RootShell.RequestedTheme = project.Theme switch { "Light" => ElementTheme.Light, "Default" => ElementTheme.Default, _ => ElementTheme.Dark };
            }
            if (paletteDiffers && palette.IsChecked == true)
            {
                _settings.Palette = project.Palette;
                PaletteComboBox.SelectedIndex = project.Palette == "Macaron" ? 1 : 0;
            }
        }
        finally { _applyingProjectAppearance = false; }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot, Title = "新建作业项目", Content = "原项目会保留。请选择新项目要保留的内容。",
            PrimaryButtonText = "重置作业", SecondaryButtonText = "重置作业和科目布局", CloseButtonText = "取消"
        };
        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.None) return;
        if (_isEditing) FinishEditing();
        PersistProjects();
        ProjectDocument project = ProjectStore.Create(_library, choice == ContentDialogResult.Primary);
        await ActivateProjectAsync(project);
    });

    private async void RenameProject_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (CurrentProject is not { } project) return;
        TextBox input = new() { Text = project.Name, MaxLength = 200, Header = "项目名称" };
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = "重命名项目", Content = input, PrimaryButtonText = "保存", CloseButtonText = "取消" };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        project.Name = input.Text.Trim();
        PersistProjects();
    });

    private async void DeleteProject_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (CurrentProject is not { } project) return;
        if (await ShowConfirmAsync("删除本地项目", $"删除“{project.Name}”的本地内容？关联的 .pch 文件会保留。") != ContentDialogResult.Primary) return;
        if (_isEditing) FinishEditing();
        _library.Projects.Remove(project);
        _library.ActiveProjectId = null;
        await ActivateProjectAsync(_library.Projects.OrderByDescending(p => p.LastUsedAt).FirstOrDefault());
        // 清单提交后才回收附件；清理失败仅留下无引用资源，不恢复已经删除的项目。
        try { _projectStore.DeleteAssets(project.Id); }
        catch (IOException ex) { ReportStorageError("项目已删除，附件目录未能清理", ex); }
    });

    private void InitializePicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

    private async void ImportProject_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        FileOpenPicker picker = new(); picker.FileTypeFilter.Add(".pch"); InitializePicker(picker);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ProjectDocument? existing = _library.Projects.FirstOrDefault(p => string.Equals(p.LinkedFile, file.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { await SwitchProjectAsync(existing); return; }
        ProjectDocument imported = new PchPackageService(_projectStore).Import(file.Path);
        if (_isEditing) FinishEditing();
        PersistProjects();
        _library.Projects.Add(imported);
        await ActivateProjectAsync(imported);
    });

    private async void SaveProject_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (CurrentProject is not { } project) return;
        PersistProjects();
        string? path = project.LinkedFile;
        bool removeEmptyPickerFileOnFailure = false;
        if (string.IsNullOrEmpty(path))
        {
            FileSavePicker picker = new() { SuggestedFileName = SafeFileName(project.Name) };
            picker.FileTypeChoices.Add("Pancake 作业文件", new List<string> { ".pch" }); InitializePicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            path = file.Path;
            removeEmptyPickerFileOnFailure = true;
        }
        else if (!Directory.Exists(Path.GetDirectoryName(path))) throw new IOException("关联文件目录不可用，请恢复该目录或设备后重试。");
        ProjectDocument copy = ProjectStore.Clone(project);
        copy.Theme = _settings.Theme; copy.Palette = _settings.Palette;
        try
        {
            new PchPackageService(_projectStore).Save(copy, path);
        }
        catch
        {
            // FileSavePicker 可能预先创建空文件；打包失败时不留下看似有效的残缺作业文件。
            if (removeEmptyPickerFileOnFailure && File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
            throw;
        }
        project.Theme = copy.Theme; project.Palette = copy.Palette; project.LinkedFile = path;
        if (_isEditing) ViewModel.BeginEditing();
        PersistProjects();
        StorageInfoBar.Title = "已保存作业文件"; StorageInfoBar.Message = path; StorageInfoBar.Severity = InfoBarSeverity.Success; StorageInfoBar.IsOpen = true;
    });

    private static string SafeFileName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void GlobalPen_Changed(object sender, RoutedEventArgs e) => ApplyGlobalInkMode();
    private void ApplyGlobalInkMode()
    {
        if (GlobalPenButton is null || GlobalInkToolbar is null) return;
        bool drawing = _isEditing && GlobalPenButton.IsChecked == true;
        GlobalInkToolbar.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        RichTextToolbar.Visibility = _isEditing && !drawing ? Visibility.Visible : Visibility.Collapsed;
        AddSubjectButton.IsEnabled = GridSnapToggleButton.IsEnabled = !drawing;
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>()) tile.SetInkMode(drawing, _inkSettings);
        UpdateInkSubjectLabel();
    }

    private void UpdateInkSubjectLabel()
    {
        if (_activeInkSubject is not null && !ViewModel.Subjects.Contains(_activeInkSubject)) _activeInkSubject = null;
        _inkSubjectLabel.Text = _activeInkSubject is null ? "在任意磁贴上书写" : $"当前：{_activeInkSubject.Name}";
    }

    private async void ClearInk_Click(object sender, RoutedEventArgs e)
    {
        UpdateInkSubjectLabel();
        ContentDialog dialog = new()
        {
            XamlRoot = RootShell.XamlRoot, Title = "清空笔迹", Content = "选择要清空的范围，下一步确认。",
            PrimaryButtonText = _activeInkSubject is null ? "当前磁贴" : _activeInkSubject.Name,
            IsPrimaryButtonEnabled = _activeInkSubject is not null, SecondaryButtonText = "全部磁贴", CloseButtonText = "取消"
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        string scope = result == ContentDialogResult.Primary ? _activeInkSubject?.Name ?? "当前磁贴" : "全部磁贴";
        if (await ShowConfirmAsync("确认清空笔迹", $"清空{scope}的笔迹？") != ContentDialogResult.Primary) return;
        foreach (SubjectTileControl tile in BoardCanvas.Children.OfType<SubjectTileControl>())
            if (result == ContentDialogResult.Secondary || ReferenceEquals(tile.DataContext, _activeInkSubject)) tile.ClearStrokes();
    }

    private async void ExportImage_Click(object sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (CurrentProject is not { } project) return;
        GlobalPenButton.IsChecked = false;
        PersistProjects();
        var snapshot = ProjectStore.Clone(project);
        snapshot.Theme = _settings.Theme; snapshot.Palette = _settings.Palette;
        ExportImageView view = new(snapshot, this, () => { ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed; });
        ExportOverlay.Children.Clear(); ExportOverlay.Children.Add(view); ExportOverlay.Visibility = Visibility.Visible;
        await view.InitializeAsync();
    });
}
