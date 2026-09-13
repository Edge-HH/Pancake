using System.Collections.ObjectModel;
using System.Text.Json;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.ViewModels;

/// <summary>
/// 看板状态与项目持久化的视图模型。界面只依赖这里暴露的状态，
/// 项目文件格式仍由核心层的 ProjectStore / AppDataStore 负责。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly string[] _accentPalette = ["#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF"];
    private ObservableCollection<SubjectBoard> _subjects = [];
    private ObservableCollection<SubjectBoard>? _editSnapshot;
    private SubjectBoard? _selectedSubject;
    private HomeworkEntry? _selectedHomework;
    private ProjectStore? _store;
    private ProjectLibrary? _library;
    private ProjectDocument? _project;
    private MediaLibrary? _mediaLibrary;
    private AutofillService? _autofill;
    private readonly List<InkStrokeData> _clockInk = [];
    private bool _storageReady = true;

    public ObservableCollection<SubjectBoard> Subjects
    {
        get => _subjects;
        private set
        {
            if (SetProperty(ref _subjects, value)) RaisePropertyChanged(nameof(SubjectCountText));
        }
    }

    public SubjectBoard? SelectedSubject
    {
        get => _selectedSubject;
        set
        {
            if (SetProperty(ref _selectedSubject, value))
            {
                SelectedHomework = value?.Entries.FirstOrDefault();
            }
        }
    }

    public HomeworkEntry? SelectedHomework
    {
        get => _selectedHomework;
        set => SetProperty(ref _selectedHomework, value);
    }

    public string ProjectName => _project?.Name ?? "未命名项目";

    public string SubjectCountText => $"{Subjects.Count} 个科目";

    /// <summary>仅时钟模式下写在整块屏幕上的笔迹，保存进当前项目。</summary>
    public IList<InkStrokeData> ClockInk => _clockInk;

    public BoardSettingsState Settings => _library?.Settings ?? new BoardSettingsState();

    /// <summary>上次加载失败的原因；为空表示正常。界面启动后据此提示用户。</summary>
    public string? LoadError { get; private set; }

    /// <summary>数据可以安全写入；加载失败后置为 false，避免用空项目覆盖用户数据。</summary>
    public bool IsStorageReady => _storageReady;

    /// <summary>项目库中的全部项目。</summary>
    public IReadOnlyList<ProjectDocument> Projects => _library?.Projects ?? [];

    public ProjectDocument? CurrentProject => _project;

    /// <summary>学科与作业补全服务；设置页与磁贴输入共用同一实例。</summary>
    public AutofillService? Autofill => _autofill;

    /// <summary>最近使用的图片（最多八张），背景与图片附件入口共用。</summary>
    public IReadOnlyList<string> RecentImages => _mediaLibrary?.RecentImages() ?? [];

    /// <summary>最近使用优先的项目排序，供最近项目菜单使用。</summary>
    public IEnumerable<ProjectDocument> RecentProjects =>
        Projects.OrderByDescending(project => project.LastUsedAt).ThenBy(project => project.Name);

    /// <summary>存储失败时通知界面显示横幅；参数为标题与异常。</summary>
    public event Action<string, Exception>? StorageFailed;

    /// <summary>加载数据目录中的项目库；首次运行时创建一个空项目，与旧版“新安装不填示例作业”一致。</summary>
    public void LoadFromDisk(string dataRoot)
    {
        _store = new ProjectStore(dataRoot);
        _mediaLibrary = new MediaLibrary(dataRoot);
        try
        {
            _library = _store.Load();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            // 数据损坏不能让程序打不开：保留原文件供排查，然后以空项目继续启动。
            LoadError = $"项目数据无法读取：{ex.Message}";
            QuarantineBrokenLibrary(dataRoot);
            _library = new ProjectLibrary();
        }

        MigrateSettings(_library.Settings);
        _autofill = new AutofillService(() => _library!.Settings.Autofill);
        _autofill.EnsureBuiltIns();
        _autofill.Prune(DateTime.Now);
        // 与旧版一致：优先恢复上次使用的项目；全新安装没有项目时显示空状态面板而不自动新建。
        ProjectDocument? active = _library.Projects.FirstOrDefault(project => project.Id == _library.ActiveProjectId)
            ?? _library.Projects.OrderByDescending(project => project.LastUsedAt).FirstOrDefault();
        ActivateProject(active);
        _storageReady = LoadError is null;
    }

    /// <summary>旧配置补齐与归一：缺少的集合字段补空、过期的停靠布局按新默认值重置。</summary>
    private static void MigrateSettings(BoardSettingsState settings)
    {
        settings.Widgets ??= [];
        settings.CustomizedDockedWidgets ??= [];
        if (settings.DockedWidgetLayoutVersion < 2)
        {
            foreach (string key in new[] { "SplitClock", "SplitComponents", "ClockModeClock", "ClockModeComponents" })
            {
                settings.Widgets.Remove(key);
            }

            settings.CustomizedDockedWidgets.Clear();
            settings.DockedWidgetLayoutVersion = 2;
        }

        settings.GridStyle = GridAppearance.EffectiveStyle(settings.GridStyle, false, false);
        settings.Autofill ??= new AutofillSettings();
        settings.Autofill.Subject ??= new SubjectCompletionSettings();
        settings.Autofill.Homework ??= new HomeworkCompletionSettings();
        settings.Autofill.Subject.Subjects ??= [];
        settings.Autofill.Homework.Items ??= [];
        settings.Autofill.Homework.Blocked ??= [];
        settings.GridLineThickness = Math.Clamp(settings.GridLineThickness, .5, 5);
        settings.GridDotDiameter = Math.Clamp(settings.GridDotDiameter, 1, 12);
    }

    /// <summary>
    /// 把图片登记到“最近使用”列表。登记失败只影响历史记录，不应阻止把图片加入作业，
    /// 因此这里退回原路径。
    /// </summary>
    public async Task<string> ImportRecentImageAsync(string path)
    {
        if (_mediaLibrary is null) return path;
        try
        {
            return await _mediaLibrary.ImportAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return path;
        }
    }

    /// <summary>把 Wallpaper Engine 本地项目导入媒体库；视频直接复制，网页项目打包为本地网页。</summary>
    public async Task<string> ImportWallpaperAsync(WallpaperProject project) =>
        _mediaLibrary is null ? project.Path : await _mediaLibrary.ImportWallpaperAsync(project);

    /// <summary>把图片复制进当前项目资源目录并加入作业条目，返回新建的附件。</summary>
    public AttachmentItem? AddAttachment(HomeworkEntry entry, string sourcePath)
    {
        if (_store is null || _project is null || string.IsNullOrWhiteSpace(sourcePath)) return null;
        string owned = _store.CopyAttachment(_project.Id, sourcePath);
        AttachmentItem item = new()
        {
            Name = MediaLibrary.DisplayName(sourcePath),
            Kind = "图片",
            Path = owned
        };
        entry.Attachments.Add(item);
        entry.NotifyAttachmentsChanged();
        return item;
    }

    /// <summary>把当前看板写回项目库；退出、定时保存与切换项目前调用。</summary>
    public void SaveToDisk() => TrySave("保存项目");

    /// <summary>
    /// 保存项目库。失败时不抛出，而是通过 <see cref="StorageFailed"/> 通知界面，
    /// 避免后台定时保存把异常抛到消息循环。
    /// </summary>
    public bool TrySave(string title)
    {
        if (_store is null || _library is null || _project is null) return true;
        try
        {
            if (!_storageReady) throw new IOException("原项目数据未能读取，已停止写入。请先恢复数据并重启软件。");
            CaptureCurrentProject();
            _store.Save(_library);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            StorageFailed?.Invoke(title, ex);
            return false;
        }
    }

    /// <summary>把界面上的磁贴、笔迹与项目外观写进当前项目对象。</summary>
    private void CaptureCurrentProject()
    {
        if (_project is null) return;
        _project.Subjects = AppDataStore.CaptureSubjects(Subjects);
        _project.ClockInkStrokes = AppDataStore.CaptureInk(_clockInk);
        _project.Theme = Settings.Theme;
        _project.Palette = Settings.Palette;
        _project.LastUsedAt = DateTime.Now;
    }

    /// <summary>切换到指定项目；返回是否成功。</summary>
    public bool SwitchProject(ProjectDocument project)
    {
        if (_library is null || project.Id == _project?.Id) return true;
        if (!TrySave("切换项目")) return false;
        ActivateProject(project);
        return true;
    }

    /// <summary>新建项目：保留布局或完全重置，其余内容一律清空。</summary>
    public ProjectDocument? CreateProject(bool keepLayout)
    {
        if (_library is null || !TrySave("新建项目")) return null;
        ProjectDocument project = ProjectStore.Create(_library, keepLayout);
        ActivateProject(project);
        TrySave("新建项目");
        return project;
    }

    /// <summary>重命名当前项目。</summary>
    public bool RenameProject(ProjectDocument project, string name)
    {
        if (_library is null || string.IsNullOrWhiteSpace(name)) return false;
        project.Name = name.Trim();
        return TrySave("重命名项目");
    }

    /// <summary>删除本地项目；关联的 .pch 文件保留。</summary>
    public bool DeleteProject(ProjectDocument project)
    {
        if (_library is null || _store is null) return false;
        _library.Projects.Remove(project);
        _library.ActiveProjectId = null;
        ActivateProject(RecentProjects.FirstOrDefault());
        bool saved = TrySave("删除项目");
        try
        {
            // 清单写入成功后才回收附件；清理失败只留下无引用资源，不恢复已删除的项目。
            _store.DeleteAssets(project.Id);
        }
        catch (IOException ex)
        {
            StorageFailed?.Invoke("项目已删除，附件目录未能清理", ex);
        }

        return saved;
    }

    /// <summary>导入 .pch 作业包；同一文件已导入时直接切换到已有项目。</summary>
    public ProjectDocument? ImportProject(string path)
    {
        if (_library is null || _store is null) return null;
        ProjectDocument? existing = Projects.FirstOrDefault(project =>
            string.Equals(project.LinkedFile, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SwitchProject(existing);
            return existing;
        }

        ProjectDocument imported = new PchPackageService(_store).Import(path);
        _library.Projects.Add(imported);
        ActivateProject(imported);
        TrySave("导入项目");
        return imported;
    }

    /// <summary>把项目导出成 .pch；返回是否成功。</summary>
    public bool SaveProjectPackage(ProjectDocument project, string path)
    {
        if (_store is null) return false;
        try
        {
            CaptureCurrentProject();
            ProjectDocument copy = ProjectStore.Clone(project);
            copy.Theme = Settings.Theme;
            copy.Palette = Settings.Palette;
            new PchPackageService(_store).Save(copy, path);
            project.LinkedFile = path;
            project.Theme = copy.Theme;
            project.Palette = copy.Palette;
            return TrySave("保存作业文件");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            StorageFailed?.Invoke("保存作业文件失败", ex);
            return false;
        }
    }

    /// <summary>当前项目的深拷贝，供图片导出等只读流程使用。</summary>
    public ProjectDocument? SnapshotCurrentProject()
    {
        if (_project is null) return null;
        CaptureCurrentProject();
        ProjectDocument copy = ProjectStore.Clone(_project);
        copy.Theme = Settings.Theme;
        copy.Palette = Settings.Palette;
        return copy;
    }

    /// <summary>把项目内容装载到界面状态。切换项目与导入都会经过这里。</summary>
    private void ActivateProject(ProjectDocument? project)
    {
        if (_library is null) return;
        _project = project;
        _library.ActiveProjectId = project?.Id;
        if (project is not null) project.LastUsedAt = DateTime.Now;
        ReplaceSubjects(AppDataStore.RestoreSubjects(project?.Subjects ?? []));
        _clockInk.Clear();
        if (project is not null) _clockInk.AddRange(AppDataStore.RestoreInk(project.ClockInkStrokes));
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(CurrentProject));
    }

    /// <summary>把无法解析的项目清单改名备份，避免随后的自动保存覆盖掉原始数据。</summary>
    private static void QuarantineBrokenLibrary(string dataRoot)
    {
        string path = Path.Combine(dataRoot, "projects.json");
        if (!File.Exists(path)) return;
        try
        {
            File.Move(path, Path.Combine(dataRoot, $"projects.json.corrupt-{DateTime.Now:yyyyMMddHHmmss}"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 备份失败时保持原文件不动，用户仍可手动处理。
        }
    }

    public void BeginEditing() => _editSnapshot = CloneSubjects(Subjects);

    public void PublishEditing() => _editSnapshot = null;

    public void ReplaceSubjects(IEnumerable<SubjectBoard> subjects)
    {
        Subjects = new ObservableCollection<SubjectBoard>(subjects);
        SelectedSubject = Subjects.FirstOrDefault();
        RaisePropertyChanged(nameof(SubjectCountText));
    }

    public void DiscardEditing()
    {
        if (_editSnapshot is null) return;
        Subjects = CloneSubjects(_editSnapshot);
        _editSnapshot = null;
        SelectedSubject = Subjects.FirstOrDefault();
        RaisePropertyChanged(nameof(SubjectCountText));
    }

    public SubjectBoard AddSubject(string name)
    {
        string hex = _accentPalette[Subjects.Count % _accentPalette.Length];
        int index = Subjects.Count;
        SubjectBoard subject = new()
        {
            Name = name,
            AccentHex = ColorPalette.ResolveAccent(hex, false, ColorPalette.IsMacaron),
            X = 36 + (index % 2) * 470,
            Y = 36 + (index / 2) * 360
        };
        Subjects.Add(subject);
        SelectedSubject = subject;
        RaisePropertyChanged(nameof(SubjectCountText));
        return subject;
    }

    public HomeworkEntry? AddHomework()
    {
        if (SelectedSubject is null) return null;
        HomeworkEntry homework = new() { Content = "在这里输入作业内容" };
        SelectedSubject.Entries.Add(homework);
        SelectedSubject.NotifyEntriesChanged();
        SelectedHomework = homework;
        return homework;
    }

    private static ObservableCollection<SubjectBoard> CloneSubjects(IEnumerable<SubjectBoard> source) =>
        new(source.Select(subject => subject.Clone()));
}
