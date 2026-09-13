using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 项目与文件：最近项目切换、新建、重命名、删除、导入与保存 .pch，
/// 以及存储失败时的提示横幅。所有写入都经过视图模型，界面不直接操作项目文件。
/// </summary>
public sealed partial class MainWindow
{
    private bool _projectOperation;

    /// <summary>构建项目选择器的飞出菜单；只在窗口打开时执行一次。</summary>
    private void BuildProjectCommands()
    {
        StackPanel host = new() { Spacing = 8, Width = 240 };
        ScrollViewer scroller = new() { MaxHeight = 420, Content = host };
        Flyout flyout = new() { Content = scroller };
        flyout.Opened += (_, _) => BuildRecentProjects(host);
        Button picker = this.FindControl<Button>("ProjectPickerButton")!;
        picker.Flyout = flyout;

        _viewModel.StorageFailed += (title, error) => ShowStorageInfo(title, error.Message, isError: true);
        UpdateProjectCommands();
    }

    /// <summary>最近项目列表按最后使用时间排序，点击即切换。</summary>
    private void BuildRecentProjects(StackPanel host)
    {
        host.Children.Clear();
        foreach (ProjectDocument project in _viewModel.RecentProjects)
        {
            Button button = new()
            {
                Content = project.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            ToolTip.SetTip(button, $"创建于 {project.CreatedAt:yyyy年M月d日 HH:mm}");
            ProjectDocument captured = project;
            button.Click += (_, _) =>
            {
                this.FindControl<Button>("ProjectPickerButton")!.Flyout?.Hide();
                SwitchProject(captured);
            };
            host.Children.Add(button);
        }

        if (host.Children.Count == 0)
        {
            host.Children.Add(new TextBlock { Text = "还没有项目", IsEnabled = false });
        }
    }

    /// <summary>按当前项目状态刷新顶栏、空状态面板与各入口的可用性。</summary>
    private void UpdateProjectCommands()
    {
        bool exists = _viewModel.CurrentProject is not null;
        bool settingsVisible = SettingsRoot.IsVisible;
        this.FindControl<StackPanel>("ProjectCommands")!.IsVisible = exists && !settingsVisible;
        this.FindControl<StackPanel>("EmptyProjectPanel")!.IsVisible = !exists && !settingsVisible;
        this.FindControl<TextBlock>("ProjectNameText")!.Text = _viewModel.CurrentProject?.Name ?? "选择项目";
        this.FindControl<TextBlock>("ProjectSubjectCountText")!.Text = exists ? _viewModel.SubjectCountText : string.Empty;
        bool ready = exists && _viewModel.IsStorageReady;
        this.FindControl<MenuItem>("RenameProjectMenu")!.IsEnabled = ready;
        this.FindControl<MenuItem>("DeleteProjectMenu")!.IsEnabled = ready;
        this.FindControl<MenuItem>("SaveProjectMenu")!.IsEnabled = ready;
        // 项目不可用时（没有当前项目或存储未就绪）隐藏导出入口，避免出现按了没有反应的菜单项。
        this.FindControl<MenuItem>("ExportProjectMenu")!.IsVisible = ready;
        if (this.FindControl<ToggleButton>("GlobalPenButton") is { } pen) pen.IsEnabled = exists;
        this.FindControl<Button>("EditBoardButton")!.IsEnabled = exists;
        this.FindControl<Grid>("BoardWorkspace")!.IsVisible = exists && Settings.LayoutMode != "Clock";
    }

    /// <summary>存储与项目操作的结果横幅。</summary>
    private void ShowStorageInfo(string title, string message, bool isError)
    {
        this.FindControl<TextBlock>("StorageInfoTitle")!.Text = title;
        this.FindControl<TextBlock>("StorageInfoMessage")!.Text = message;
        this.FindControl<Border>("StorageInfoBar")!.IsVisible = true;
        _ = isError;
    }

    private void StorageInfoClose_Click(object? sender, RoutedEventArgs e) =>
        this.FindControl<Border>("StorageInfoBar")!.IsVisible = false;

    /// <summary>
    /// 项目操作统一入口：操作期间暂停自动保存，失败时恢复操作前的内存状态并提示用户，
    /// 保证界面上看到的项目内容与磁盘一致。
    /// </summary>
    private async Task RunProjectAction(Func<Task> action)
    {
        if (_projectOperation) return;
        _projectOperation = true;
        try
        {
            await action();
        }
        finally
        {
            _projectOperation = false;
            RefreshAfterProjectChange();
        }
    }

    /// <summary>
    /// 切换到另一个项目。项目保存的主题或色系与当前软件设置不同时，
    /// 让用户分别选择是否套用，避免切项目时外观被静默改掉。
    /// </summary>
    private async void SwitchProject(ProjectDocument project)
    {
        await RunProjectAction(async () =>
        {
            if (_viewModel.CurrentProject?.Id == project.Id) return;
            if (_isEditing) FinishEditing();
            if (!_viewModel.SwitchProject(project)) return;
            await OfferProjectAppearanceAsync(project);
        });
    }

    private async Task OfferProjectAppearanceAsync(ProjectDocument project)
    {
        bool themeDiffers = project.Theme != Settings.Theme;
        bool paletteDiffers = project.Palette != Settings.Palette;
        if (!themeDiffers && !paletteDiffers) return;

        CheckBox theme = new()
        {
            Content = $"应用项目主题：{project.Theme switch { "Light" => "浅色", "Dark" => "深色", _ => "跟随系统" }}",
            IsChecked = themeDiffers,
            IsVisible = themeDiffers
        };
        CheckBox palette = new()
        {
            Content = $"应用项目色系：{(project.Palette == "Macaron" ? "马卡龙" : "鲜明")}",
            IsChecked = paletteDiffers,
            IsVisible = paletteDiffers
        };
        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(theme);
        content.Children.Add(palette);
        DialogResult result = await ShowChoiceAsync(
            "应用项目外观",
            "项目外观与软件当前设置不同，可分别选择要应用的设置。",
            "应用所选",
            string.Empty,
            "保持当前",
            content);
        if (result != DialogResult.Primary) return;
        if (themeDiffers && theme.IsChecked == true) Settings.Theme = project.Theme;
        if (paletteDiffers && palette.IsChecked == true) Settings.Palette = project.Palette;
        SettingChanged();
    }

    /// <summary>项目内容变化后重建看板与相关界面状态。</summary>
    private void RefreshAfterProjectChange()
    {
        _isEditing = false;
        BuildTiles();
        ClockInkLayer.Attach(_viewModel.ClockInk);
        ApplyDisplayLayout();
        UpdateBoardBounds();
        UpdateSubjectCount();
        ApplyEditingState();
        UpdateProjectCommands();
        if (this.FindControl<ToggleButton>("GlobalPenButton") is { } pen) pen.IsChecked = false;
        ApplyInkMode();
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        DialogResult choice = await ShowChoiceAsync(
            "新建作业项目",
            "原项目会保留。请选择新项目要保留的内容：保留当前科目与布局但清空内容，或完全从空白开始。",
            "重置作业",
            "重置作业和科目布局",
            "取消");
        if (choice == DialogResult.Close) return;
        _viewModel.CreateProject(keepLayout: choice == DialogResult.Primary);
        await Task.CompletedTask;
    });

    private async void RenameProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (_viewModel.CurrentProject is not { } project) return;
        TextBox input = new() { Text = project.Name, MaxLength = 200, Watermark = "项目名称" };
        DialogResult result = await ShowChoiceAsync("重命名项目", string.Empty, "保存", string.Empty, "取消", input);
        if (result != DialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;
        _viewModel.RenameProject(project, input.Text);
    });

    private async void DeleteProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (_viewModel.CurrentProject is not { } project) return;
        bool confirmed = await ShowConfirmAsync(
            "删除本地项目",
            $"删除“{project.Name}”的本地内容？关联的 .pch 文件会保留。",
            "删除",
            "取消");
        if (!confirmed) return;
        _viewModel.DeleteProject(project);
    });

    private async void ImportProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入作业文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pancake 作业文件") { Patterns = ["*.pch"] }]
        });
        string? path = files.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        if (path is null) return;
        try
        {
            _viewModel.ImportProject(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowStorageInfo("导入失败", ex.Message, isError: true);
        }
    });

    private async void SaveProject_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (_viewModel.CurrentProject is not { } project) return;
        string? path = project.LinkedFile;
        bool createdByPicker = false;
        if (string.IsNullOrWhiteSpace(path))
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存作业文件",
                SuggestedFileName = SafeFileName(project.Name),
                DefaultExtension = "pch",
                FileTypeChoices = [new FilePickerFileType("Pancake 作业文件") { Patterns = ["*.pch"] }]
            });
            if (file is null) return;
            path = file.TryGetLocalPath();
            createdByPicker = true;
            if (string.IsNullOrWhiteSpace(path)) return;
        }
        else if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            ShowStorageInfo("保存失败", "关联文件目录不可用，请恢复该目录或设备后重试。", isError: true);
            return;
        }

        if (_viewModel.SaveProjectPackage(project, path))
        {
            ShowStorageInfo("已保存作业文件", path, isError: false);
            return;
        }

        // 文件选择器可能预先创建了空文件；保存失败时不留下看似有效的残缺作业文件。
        if (createdByPicker && File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
    });

    private async void ExportImage_Click(object? sender, RoutedEventArgs e) => await RunProjectAction(async () =>
    {
        if (_viewModel.SnapshotCurrentProject() is not { } snapshot) return;
        // 导出前先落盘，保证导出内容与已保存的项目一致。
        _viewModel.SaveToDisk();
        OpenExportView(snapshot);
        await Task.CompletedTask;
    });

    /// <summary>打开图片导出整页视图；关闭时返回看板。</summary>
    private void OpenExportView(ProjectDocument snapshot)
    {
        Grid overlay = this.FindControl<Grid>("ExportOverlay")!;
        overlay.Children.Clear();
        overlay.Children.Add(new Controls.ExportView(snapshot, Settings, this, () =>
        {
            overlay.Children.Clear();
            overlay.IsVisible = false;
        }));
        overlay.IsVisible = true;
    }

    /// <summary>把项目名转换成安全的建议文件名。</summary>
    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
