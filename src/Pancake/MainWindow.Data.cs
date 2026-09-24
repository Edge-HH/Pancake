using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Services;
using Windows.Storage.Pickers;

namespace Pancake;

public sealed partial class MainWindow
{
    private TextBlock? _autoBackupStatus;
    // 还原备份后数据已在磁盘上被替换，关闭窗口时不能再把内存里的旧状态写回去。
    private bool _skipSaveOnClose;

    /// <summary>数据页：自动保存、数据导出（手动备份与还原）与自动备份；设置跨项目共享。</summary>
    private StackPanel BuildDataPage()
    {
        StackPanel page = SettingsStack();
        page.Children.Add(Heading("保存"));
        page.Children.Add(Toggle("自动保存", _settings.AutoSaveEnabled, value => _settings.AutoSaveEnabled = value,
            // 自动保存开关本身要立刻落盘：关闭动作若不写盘，重启后开关状态会丢失。
            () => { SettingChanged(); SaveStateNow(); }));
        page.Children.Add(Note("开启后，改动会在停止操作约 0.6 秒后写入 data 目录；关闭后仅在退出软件、切换项目、保存作业文件或点击「立即保存」时写入。"));
        Button saveNow = new() { Content = "立即保存", HorizontalAlignment = HorizontalAlignment.Left };
        saveNow.Click += (_, _) =>
        {
            SaveStateNow();
            StorageInfoBar.Title = "已保存";
            StorageInfoBar.Message = $"项目与设置已写入 {_projectStore.DirectoryPath}";
            StorageInfoBar.Severity = InfoBarSeverity.Success;
            StorageInfoBar.IsOpen = true;
        };
        page.Children.Add(saveNow);

        page.Children.Add(Heading("数据导出"));
        page.Children.Add(Note("导出为单个 .pbk 备份包，可在其他电脑上还原。手动导出与自动备份共用下方的备份范围。"));
        StackPanel scopeList = new() { Spacing = 4 };
        foreach ((BackupScopes flag, string text) in new (BackupScopes, string)[]
        {
            (BackupScopes.Jobs, "作业数据：项目、磁贴、图片附件与笔迹"),
            (BackupScopes.CurrentBackground, "当前背景：正在使用的背景媒体"),
            (BackupScopes.RecentBackgrounds, "最近背景：最近使用的背景媒体"),
            (BackupScopes.Settings, "软件设置：布局、外观、组件与补全等设置"),
        })
        {
            CheckBox box = new() { Content = text, IsChecked = _settings.Data.Backup.Scopes.Has(flag) };
            box.Checked += (_, _) => UpdateBackupScopes(flag, true);
            box.Unchecked += (_, _) => UpdateBackupScopes(flag, false);
            scopeList.Children.Add(box);
        }
        page.Children.Add(scopeList);

        Button export = new() { Content = "导出数据备份…", HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += async (_, _) =>
        {
            BackupScopes scope = _settings.Data.Backup.Scopes.Sanitize();
            if (scope == BackupScopes.None)
            {
                await ShowMessageAsync("无法导出", "请至少选择一项备份范围。", "知道了");
                return;
            }
            FileSavePicker picker = new() { SuggestedFileName = $"Pancake 备份 {DateTime.Now:yyyyMMdd-HHmmss}" };
            picker.FileTypeChoices.Add("Pancake 数据备份", new List<string> { ".pbk" });
            InitializePicker(picker);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            try
            {
                // 导出前把当前内存状态落盘并打进备份，导出内容与界面一致。
                SaveStateNow();
                new BackupService(_dataStore.DataDirectory).Create(file.Path, scope, _library);
                StorageInfoBar.Title = "已导出数据备份";
                StorageInfoBar.Message = file.Path;
                StorageInfoBar.Severity = InfoBarSeverity.Success;
                StorageInfoBar.IsOpen = true;
            }
            catch (Exception ex) { ReportStorageError("数据导出失败", ex); }
        };
        page.Children.Add(export);

        Button restore = new() { Content = "从备份还原…", HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) =>
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".pbk");
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            try
            {
                BackupService service = new(_dataStore.DataDirectory);
                BackupInfo info = service.Read(file.Path);
                if (await ShowConfirmAsync("从备份还原",
                    $"备份时间：{info.Manifest.CreatedAt:yyyy年M月d日 HH:mm}\n包含范围：{BackupScopeLabels(info.Manifest.Scopes)}\n媒体文件：{info.MediaEntries.Count} 个\n\n还原会覆盖当前的项目与设置，且无法撤销。继续吗？") != ContentDialogResult.Primary) return;
                service.Restore(file.Path);
                await ShowMessageAsync("还原完成", "备份已还原，软件将重新启动以加载还原后的数据。", "重新启动");
                RestartAfterRestore();
            }
            catch (Exception ex) { ReportStorageError("从备份还原失败", ex); }
        };
        page.Children.Add(restore);

        page.Children.Add(Heading("自动备份"));
        ToggleSwitch autoBackup = Toggle("自动备份", _settings.Data.AutoBackup.Enabled, value => _settings.Data.AutoBackup.Enabled = value,
            () => { SettingChanged(); SaveStateNow(); RefreshAutoBackupStatus(); });
        page.Children.Add(autoBackup);

        TextBox directory = new() { Header = "备份目录", IsReadOnly = true, Text = AutoBackupDirectory() };
        Button chooseDirectory = new() { Content = "选择目录…", HorizontalAlignment = HorizontalAlignment.Left };
        chooseDirectory.Click += async (_, _) =>
        {
            FolderPicker picker = new() { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            _settings.Data.AutoBackup.Directory = folder.Path;
            directory.Text = folder.Path;
            SettingChanged();
            RefreshAutoBackupStatus();
        };
        page.Children.Add(directory);
        page.Children.Add(chooseDirectory);

        NumberBox interval = new()
        {
            Header = "备份间隔（小时）", Minimum = 1, Maximum = 24 * 365,
            Value = BackupService.NormalizeIntervalHours(_settings.Data.AutoBackup.IntervalHours),
            SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        interval.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue)) return;
            _settings.Data.AutoBackup.IntervalHours = BackupService.NormalizeIntervalHours(args.NewValue);
            SettingChanged();
            RefreshAutoBackupStatus();
        };
        page.Children.Add(interval);

        NumberBox keepCount = new()
        {
            Header = "保留份数", Minimum = 1, Maximum = 100,
            Value = Math.Clamp(_settings.Data.AutoBackup.KeepCount, 1, 100),
            SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        keepCount.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue)) return;
            _settings.Data.AutoBackup.KeepCount = Math.Clamp((int)Math.Round(args.NewValue), 1, 100);
            SettingChanged();
        };
        page.Children.Add(keepCount);

        _autoBackupStatus = new TextBlock { TextWrapping = TextWrapping.Wrap };
        page.Children.Add(_autoBackupStatus);
        page.Children.Add(Note("自动备份按“上次备份时间 + 间隔”判断是否到期，软件运行期间每 30 分钟检查一次；到期后立即执行一次。超出保留份数时删除最旧的备份。"));
        page.Children.Add(Note("备份范围与上方导出共用；还原会整体覆盖当前项目与设置，建议先导出一份当前数据再还原。"));
        RefreshAutoBackupStatus();
        return page;
    }

    /// <summary>备份范围改动同时写入手动与自动备份两份配置，导出与自动备份内容保持一致。</summary>
    private void UpdateBackupScopes(BackupScopes flag, bool enabled)
    {
        foreach (BackupSettings target in new[] { _settings.Data.Backup, _settings.Data.AutoBackup })
            target.Scopes = (enabled ? target.Scopes | flag : target.Scopes & ~flag).Sanitize();
        SettingChanged();
    }

    private static string BackupScopeLabels(BackupScopes scope)
    {
        List<string> parts = [];
        if (scope.Has(BackupScopes.Jobs)) parts.Add("作业数据");
        if (scope.Has(BackupScopes.CurrentBackground)) parts.Add("当前背景");
        if (scope.Has(BackupScopes.RecentBackgrounds)) parts.Add("最近背景");
        if (scope.Has(BackupScopes.Settings)) parts.Add("软件设置");
        return parts.Count == 0 ? "（空）" : string.Join("、", parts);
    }

    /// <summary>自动备份目录：未指定时使用软件数据目录下的 backups。</summary>
    private string AutoBackupDirectory() => string.IsNullOrWhiteSpace(_settings.Data.AutoBackup.Directory)
        ? Path.Combine(_dataStore.DataDirectory, "backups")
        : _settings.Data.AutoBackup.Directory;

    private void RefreshAutoBackupStatus()
    {
        if (_autoBackupStatus is null) return;
        BackupSettings auto = _settings.Data.AutoBackup;
        if (!auto.Enabled) { _autoBackupStatus.Text = "自动备份已关闭。"; return; }
        _autoBackupStatus.Text = auto.LastRunAt is { } last
            ? $"上次自动备份：{last:yyyy年M月d日 HH:mm}，间隔 {BackupService.NormalizeIntervalHours(auto.IntervalHours):0.#} 小时。"
            : "尚未执行过自动备份，将在软件运行后立即执行第一次。";
    }

    /// <summary>到期检查：由 30 分钟定时器和启动时触发；是否执行由上次备份时间与间隔决定。</summary>
    private void RunAutoBackupIfDue()
    {
        BackupSettings auto = _settings.Data.AutoBackup;
        if (!auto.Enabled || !BackupService.IsDue(auto.LastRunAt, auto.IntervalHours, DateTime.Now)) return;
        DateTime now = DateTime.Now;
        try
        {
            // 备份内容取当前内存状态，与界面显示一致。
            CaptureCurrentProject();
            _library.Settings = _settings;
            string directory = AutoBackupDirectory();
            Directory.CreateDirectory(directory);
            new BackupService(_dataStore.DataDirectory).Create(Path.Combine(directory, BackupService.AutoBackupFileName(now)), auto.Scopes.Sanitize(), _library);
            auto.LastRunAt = now;
            BackupService.PruneAutoBackups(directory, Math.Clamp(auto.KeepCount, 1, 100));
            // LastRunAt 属于设置，写盘后重启才不会重复备份。
            SaveStateNow();
        }
        catch (Exception ex)
        {
            // 自动备份失败不打断使用，也不覆盖上次成功时间，下次检查继续重试。
            ReportStorageError("自动备份失败", ex);
        }
        RefreshAutoBackupStatus();
    }

    /// <summary>还原备份后重启软件加载新数据；先释放启动闸门，重启进程才不会被当成重复启动拦下。</summary>
    private void RestartAfterRestore()
    {
        _skipSaveOnClose = true;
        _saveTimer.Stop();
        _autoBackupTimer.Stop();
        _instanceGate?.Dispose();
        string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pancake.exe");
        System.Diagnostics.ProcessStartInfo start = new(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = _startFullScreen ? "" : "--windowed"
        };
        try
        {
            _ = System.Diagnostics.Process.Start(start) ?? throw new IOException("无法启动软件。");
        }
        catch (Exception ex)
        {
            // 重启失败时保留还原后的数据并报告错误，用户可自行重新打开软件。
            _skipSaveOnClose = false;
            ReportStorageError("还原已完成，但自动重启失败", ex);
            return;
        }
        Close();
    }
}
