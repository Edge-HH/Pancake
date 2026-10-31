using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pancake.Shared;

namespace Pancake.Services;

/// <summary>同步过程中需要界面配合的地方；默认实现什么都不做，便于在测试里直接驱动同步。</summary>
public sealed class CloudSyncHooks
{
    /// <summary>正在编辑的项目不能被服务器版本替换，等编辑结束后的下一轮再处理。</summary>
    public Func<Guid, bool> IsProjectBusy { get; init; } = _ => false;
    /// <summary>项目内容被服务器版本替换（含冲突后的替换）；当前项目需要重新加载界面。</summary>
    public Action<Guid> ProjectReplaced { get; init; } = _ => { };
    /// <summary>服务器上已删除，本地项目已从清单移除。</summary>
    public Action<Guid> ProjectRemoved { get; init; } = _ => { };
    /// <summary>服务器设置已原地合并进 library.Settings，界面按 CloudSyncService.LastAppliedLayoutKeys 刷新受影响的部分。</summary>
    public Action<BoardSettingsState> LayoutApplied { get; init; } = _ => { };
    public string BackgroundDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "data", "backgrounds");
    public string AppVersion { get; init; } = "";
    public Func<string?> ActiveProjectName { get; init; } = () => null;
    /// <summary>本机已安装的字体，上报给服务端供网页选择字体。</summary>
    public Func<IReadOnlyList<string>> Fonts { get; init; } = () => [];
}

/// <summary>
/// 集控同步。一轮同步依次：读取公告与值日 → 拉取项目 → 上传本地队列 → 再拉取 → 同步班级布局 → 上报设备状态。
/// 内容哈希与上次同步一致时不发请求；附件和背景按 SHA-256 上传下载；冲突时保留本机副本再采用服务器版本。
/// 所有方法都在调用方线程（界面线程）上继续执行，库对象不会被并发修改。
/// </summary>
public sealed class CloudSyncService(ProjectStore store)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _files = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly CloudAssetHasher _hasher = new();
    private readonly HashSet<string> _uploaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Guid> _removedProjects = [];
    private readonly List<string> _notices = [];
    // 每个项目最后一次与服务器一致的规范化内容（只在内存里），冲突时据此按科目三方合并。
    private readonly Dictionary<Guid, string> _projectBases = [];
    private string _reportedFonts = "";
    private string _reportedScreen = "";
    private bool _screenUnsupported;
    private ExamScheduleStore? _examStore;
    private ExamScheduleStore ExamStore => _examStore ??= new ExamScheduleStore(store.DirectoryPath);
    // 正在编辑而没能替换的项目：结束编辑后由界面补拉一次。
    private readonly HashSet<Guid> _deferred = [];
    // 拉取时因为排队上传而跳过、但服务器上更新了的项目：上传时发现本机其实没改动，就在同一轮补拉。
    private readonly HashSet<Guid> _newerWhilePending = [];
    private bool _running, _again;

    public string? LastSyncError { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public BoardInfo? Board { get; private set; }
    /// <summary>服务器下发的考试安排；为空表示当前没有启用的考试计划。</summary>
    public ExamScheduleDto? Exam { get; private set; }
    /// <summary>服务器时间与本机时间的差（UTC 口径）；服务器下发的考试安排按它校正到点判断。</summary>
    public TimeSpan ServerClockOffset { get; private set; }
    /// <summary>最近一次合并进本机的班级设置键，界面据此只刷新受影响的部分。</summary>
    public IReadOnlyList<string> LastAppliedLayoutKeys { get; private set; } = [];
    /// <summary>设备令牌被拒绝（吊销或班级删除），需要重新配对。</summary>
    public bool CredentialsRejected { get; private set; }
    public bool IsRunning => _running;
    public event Action? BoardChanged;
    public event Action? ExamChanged;

    /// <summary>取走上一轮同步产生的提示（冲突副本、布局被覆盖等），界面展示一次即可。</summary>
    public IReadOnlyList<string> TakeNotices() { var list = _notices.ToList(); _notices.Clear(); return list; }

    /// <summary>取走因为正在编辑而跳过拉取的项目，结束编辑后补拉。</summary>
    public IReadOnlyList<Guid> TakeDeferredProjects() { var list = _deferred.ToList(); _deferred.Clear(); return list; }

    public async Task<DevicePairResponse?> PairAsync(string serverUrl, string pairingCode, string deviceName, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            $"{serverUrl.TrimEnd('/')}/api/devices/pair", new DevicePairRequest(pairingCode.Trim(), deviceName), cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<DevicePairResponse>(cancellationToken: cancellationToken);
    }

    /// <summary>连接到新班级前清空旧的同步记录：修订号只在同一个班级内有意义。</summary>
    public static void ResetSyncState(ProjectLibrary library)
    {
        foreach (ProjectDocument project in library.Projects) { project.SyncRevision = 0; project.SyncHash = null; project.SyncDeleted = false; }
        library.SyncQueue.Clear();
        library.Settings.CloudSync.LayoutRevision = 0;
        library.Settings.CloudSync.LayoutBaseJson = "";
        library.Settings.CloudSync.DeviceId = null;
    }

    public static void Queue(ProjectLibrary library, ProjectDocument project)
    {
        if (!library.Settings.CloudSync.Enabled) return;
        if (library.SyncQueue.Any(x => x.ProjectId == project.Id && x.Kind == "upsert")) return;
        library.SyncQueue.Add(new SyncOperationState { ProjectId = project.Id, ExpectedRevision = project.SyncRevision, Name = project.Name });
    }

    /// <summary>本地删除项目：已上传过的项目需要通知服务器，从未上传的直接丢弃队列。</summary>
    public static void QueueDelete(ProjectLibrary library, ProjectDocument project)
    {
        library.SyncQueue.RemoveAll(x => x.ProjectId == project.Id);
        if (!library.Settings.CloudSync.Enabled || project.SyncRevision <= 0) return;
        library.SyncQueue.Add(new SyncOperationState { ProjectId = project.Id, ExpectedRevision = project.SyncRevision, Name = project.Name, Kind = "delete" });
    }

    public static bool IsPending(ProjectLibrary library, Guid projectId) => library.SyncQueue.Any(x => x.ProjectId == projectId);

    /// <summary>项目已与服务器一致：上传过、没有排队，并且内容哈希没有变化。</summary>
    public static bool IsSynced(ProjectLibrary library, ProjectDocument project) => project.SyncRevision > 0 && !IsPending(library, project.Id);

    /// <summary>在清单保存之后回收被远端删除项目的附件目录，顺序与本地删除一致。</summary>
    public void CleanupRemovedAssets()
    {
        foreach (Guid id in _removedProjects.ToList())
        {
            try { store.DeleteAssets(id); } catch (IOException) { }
            _removedProjects.Remove(id);
        }
    }

    /// <summary>执行一轮同步；正在同步时只做标记，当前一轮结束后立即再来一轮。</summary>
    public async Task SynchronizeAsync(ProjectLibrary library, CloudSyncHooks? hooks = null, CancellationToken cancellationToken = default)
    {
        if (!library.Settings.CloudSync.Enabled) return;
        if (_running) { _again = true; return; }
        _running = true;
        try
        {
            do
            {
                _again = false;
                await RunOnceAsync(library, hooks ?? new CloudSyncHooks(), cancellationToken);
            }
            while (_again && library.Settings.CloudSync.Enabled);
        }
        finally { _running = false; }
    }

    /// <summary>
    /// 班务、项目、布局、状态四步互不阻断：任何一步出错（含界面回调抛出的异常）都记进 LastSyncError 并随状态上报，
    /// 其余步骤照常执行。只有连不上服务器时才放弃本轮剩余步骤。
    /// </summary>
    private async Task RunOnceAsync(ProjectLibrary library, CloudSyncHooks hooks, CancellationToken cancellationToken)
    {
        LastSyncError = null;
        CredentialsRejected = false;
        _newerWhilePending.Clear();
        bool reachable = await StepAsync("读取班务", () => FetchBoardAsync(library, cancellationToken), cancellationToken);
        if (reachable) reachable = await StepAsync("读取考试安排", () => FetchExamAsync(library, cancellationToken), cancellationToken);
        if (reachable) reachable = await StepAsync("同步项目", async () =>
        {
            if (!await PullAsync(library, hooks, cancellationToken)) return;
            if (await PushAsync(library, hooks, cancellationToken)) await PullAsync(library, hooks, cancellationToken);
        }, cancellationToken);
        if (CredentialsRejected) return;
        if (reachable && library.Settings.CloudSync.SyncLayout)
            reachable = await StepAsync("同步班级布局", () => SyncLayoutAsync(library, hooks, cancellationToken, 0), cancellationToken);
        if (reachable) await StepAsync("上报设备状态", () => ReportStatusAsync(library, hooks, cancellationToken), cancellationToken);
        if (LastSyncError is null) LastSyncAt = DateTime.Now;
    }

    /// <summary>执行一步同步并记录错误；返回 false 表示服务器不可达，本轮后续步骤不必再试。</summary>
    private async Task<bool> StepAsync(string name, Func<Task> action, CancellationToken cancellationToken)
    {
        try { await action(); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException ex) { LastSyncError ??= $"无法连接集控服务器：{ex.Message}"; return false; }
        catch (TaskCanceledException) { LastSyncError ??= "集控请求超时，请检查服务器地址和网络。"; return false; }
        catch (JsonException) { LastSyncError ??= $"{name}失败：服务器返回的数据无法识别，请确认服务端版本。"; return true; }
        catch (Exception ex) { LastSyncError ??= $"{name}失败：{ex.Message}"; return true; }
    }

    /// <summary>只刷新公告与值日：收到班务通知时调用，不必跑完整一轮同步。</summary>
    public async Task RefreshBoardAsync(ProjectLibrary library, CancellationToken cancellationToken = default)
    {
        if (!library.Settings.CloudSync.Enabled) return;
        await StepAsync("读取班务", () => FetchBoardAsync(library, cancellationToken), cancellationToken);
    }

    /// <summary>只刷新考试安排：收到 exam 通知时调用，不必跑完整一轮同步。</summary>
    public async Task RefreshExamAsync(ProjectLibrary library, CancellationToken cancellationToken = default)
    {
        if (!library.Settings.CloudSync.Enabled) return;
        await StepAsync("读取考试安排", () => FetchExamAsync(library, cancellationToken), cancellationToken);
    }

    /// <summary>启动时从本地缓存恢复考试安排（断网也能按时显示）；缓存按班级隔离，换班后失效。</summary>
    public void LoadExamCache(ProjectLibrary library)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        ApplyExam(cloud is { Enabled: true, ClassId: { } classId } ? ExamStore.Load(classId) : null, cacheClassId: null);
    }

    /// <summary>停用集控或切换班级时清空考试安排与本地缓存。</summary>
    public void ClearExam()
    {
        ExamStore.Clear();
        ServerClockOffset = TimeSpan.Zero;
        if (Exam is null) return;
        Exam = null;
        ExamChanged?.Invoke();
    }

    // ------------------------------------------------------------------ 项目

    private async Task<bool> PullAsync(ProjectLibrary library, CloudSyncHooks hooks, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        using HttpResponseMessage listResponse = await _http.SendAsync(Request(cloud, HttpMethod.Get, "/projects"), cancellationToken);
        if (listResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            CredentialsRejected = true;
            LastSyncError = "设备凭据已失效（可能已被吊销或班级已删除），请在设置中重新配对。";
            return false;
        }
        if (!listResponse.IsSuccessStatusCode) { LastSyncError = $"读取集控项目失败：服务器返回 {(int)listResponse.StatusCode}。"; return false; }
        List<ProjectSummary> summaries = await listResponse.Content.ReadFromJsonAsync<List<ProjectSummary>>(Web, cancellationToken) ?? [];
        foreach (ProjectSummary summary in summaries) await PullOneAsync(library, hooks, summary, null, cancellationToken);
        return true;
    }

    /// <summary>按服务器摘要更新一个本地项目：已删除的移除，修订更新的下载替换；排队上传中或正在编辑的项目留到之后处理。</summary>
    private async Task PullOneAsync(ProjectLibrary library, CloudSyncHooks hooks, ProjectSummary summary, ProjectSnapshot? snapshot, CancellationToken cancellationToken)
    {
        ProjectDocument? local = library.Projects.FirstOrDefault(x => x.Id == summary.Id);
        if (IsPending(library, summary.Id))
        {
            if (local is not null && summary.Revision > local.SyncRevision) _newerWhilePending.Add(summary.Id);
            return;
        }
        if (summary.Deleted)
        {
            if (local is null) return;
            if (hooks.IsProjectBusy(local.Id)) { _deferred.Add(local.Id); return; }
            library.Projects.Remove(local);
            if (library.ActiveProjectId == local.Id) library.ActiveProjectId = null;
            _removedProjects.Add(local.Id);
            hooks.ProjectRemoved(local.Id);
            return;
        }
        if (local is not null && local.SyncRevision >= summary.Revision) return;
        if (local is not null && hooks.IsProjectBusy(local.Id)) { _deferred.Add(local.Id); return; }
        CloudSyncSettings cloud = library.Settings.CloudSync;
        ProjectDocument? remote = snapshot is not null
            ? await MaterializeAsync(cloud, snapshot, cancellationToken)
            : await DownloadProjectAsync(cloud, summary.Id, summary.Name, cancellationToken);
        if (remote is null) return;
        Install(library, local, remote);
        if (local is not null) hooks.ProjectReplaced(remote.Id);
    }

    /// <summary>
    /// 只同步收到通知的项目：直接读取这些项目，不列出全部项目。本机有待上传的修改时改走完整一轮（需要先上传再确认）。
    /// </summary>
    public async Task SyncProjectsOnlyAsync(ProjectLibrary library, IReadOnlyCollection<Guid> projectIds, CloudSyncHooks? hooks = null, CancellationToken cancellationToken = default)
    {
        if (!library.Settings.CloudSync.Enabled) return;
        hooks ??= new CloudSyncHooks();
        if (_running) { _again = true; return; }
        if (projectIds.Any(id => IsPending(library, id))) { await SynchronizeAsync(library, hooks, cancellationToken); return; }
        _running = true;
        try
        {
            LastSyncError = null;
            foreach (Guid id in projectIds.Distinct())
            {
                bool reachable = await StepAsync("同步项目", async () =>
                {
                    CloudSyncSettings cloud = library.Settings.CloudSync;
                    using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Get, $"/projects/{id}"), cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NotFound) return;
                    if (!response.IsSuccessStatusCode) { LastSyncError ??= $"读取项目失败：服务器返回 {(int)response.StatusCode}。"; return; }
                    ProjectSnapshot snapshot = await response.Content.ReadFromJsonAsync<ProjectSnapshot>(Web, cancellationToken) ?? throw new JsonException();
                    await PullOneAsync(library, hooks, new ProjectSummary(snapshot.Id, snapshot.Name, snapshot.Date, snapshot.Revision, snapshot.Deleted, snapshot.UpdatedAt), snapshot, cancellationToken);
                }, cancellationToken);
                if (!reachable) break;
            }
            if (LastSyncError is null) LastSyncAt = DateTime.Now;
        }
        finally { _running = false; }
        if (_again) await SynchronizeAsync(library, hooks, cancellationToken);
    }

    /// <summary>下载项目并把附件落到本地；任何附件下载失败都放弃本次替换，保留本地版本等下一轮。</summary>
    private async Task<ProjectDocument?> DownloadProjectAsync(CloudSyncSettings cloud, Guid id, string name, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Get, $"/projects/{id}"), cancellationToken);
        if (!response.IsSuccessStatusCode) { LastSyncError = $"读取项目“{name}”失败：服务器返回 {(int)response.StatusCode}。"; return null; }
        ProjectSnapshot? snapshot = await response.Content.ReadFromJsonAsync<ProjectSnapshot>(Web, cancellationToken);
        return snapshot is null ? null : await MaterializeAsync(cloud, snapshot, cancellationToken);
    }

    private async Task<ProjectDocument?> MaterializeAsync(CloudSyncSettings cloud, ProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        ProjectDocument? remote;
        try { remote = JsonSerializer.Deserialize<ProjectDocument>(snapshot.PayloadJson, ProjectStore.JsonOptions); }
        catch (JsonException) { remote = null; }
        if (remote is null) { LastSyncError = $"项目“{snapshot.Name}”的内容无法识别。"; return null; }
        remote.Id = snapshot.Id; remote.Name = snapshot.Name; remote.AssignmentDate = snapshot.Date;
        remote.SyncRevision = snapshot.Revision; remote.SyncDeleted = false; remote.LinkedFile = null;
        remote.ClockInkStrokes ??= [];
        foreach (HomeworkState entry in remote.Subjects.SelectMany(s => s.Entries))
        {
            entry.Attachments ??= [];
            entry.FontFallbacks ??= [];
        }
        try { ProjectValidation.Validate(remote, false); }
        catch (InvalidDataException ex) { LastSyncError = $"项目“{snapshot.Name}”未通过校验：{ex.Message}"; return null; }
        foreach (AttachmentState asset in ProjectStore.Attachments(remote))
        {
            if (string.IsNullOrWhiteSpace(asset.Path)) continue;
            if (!AssetReference.TryParse(asset.Path, out string sha, out string extension)) { asset.Path = ""; continue; }
            string target = Path.Combine(store.AssetDirectory(remote.Id), sha + extension);
            if (!await DownloadAssetAsync(cloud, sha, target, cancellationToken))
            {
                LastSyncError = $"项目“{snapshot.Name}”的图片“{asset.Name}”下载失败，稍后重试。";
                return null;
            }
            asset.Path = target;
        }
        string payload = CloudPayload.BuildProjectPayload(remote, MapKnown);
        remote.SyncHash = CloudPayload.Hash(payload);
        _projectBases[remote.Id] = payload;
        return remote;
    }

    private static void Install(ProjectLibrary library, ProjectDocument? local, ProjectDocument remote)
    {
        if (local is null) { library.Projects.Add(remote); return; }
        // 关联的 .pch 文件和最近使用时间是本机信息，替换内容时保留。
        remote.LinkedFile = local.LinkedFile;
        remote.LastUsedAt = local.LastUsedAt;
        library.Projects[library.Projects.IndexOf(local)] = remote;
    }

    /// <summary>
    /// 上传队列；返回是否需要再拉取一次：有内容真正写到了服务器，或者丢弃了一个没改动的排队项，
    /// 而它在拉取时因为排队被跳过、服务器上其实已经更新。
    /// </summary>
    private async Task<bool> PushAsync(ProjectLibrary library, CloudSyncHooks hooks, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        bool wrote = false;
        foreach (SyncOperationState operation in library.SyncQueue.ToArray())
        {
            if (operation.Kind == "delete")
            {
                wrote |= await PushDeleteAsync(library, cloud, operation, cancellationToken);
                continue;
            }
            ProjectDocument? project = library.Projects.FirstOrDefault(x => x.Id == operation.ProjectId);
            if (project is null) { library.SyncQueue.Remove(operation); continue; }

            string payload = CloudPayload.BuildProjectPayload(project, MapKnown);
            string hash = CloudPayload.Hash(payload);
            if (project.SyncRevision > 0 && hash == project.SyncHash) { library.SyncQueue.Remove(operation); wrote |= _newerWhilePending.Remove(project.Id); continue; }

            // 只有内容确实变了才上传附件；上传失败的附件在服务器版本里留空，本机仍保留原图。
            Dictionary<string, string?> mapped = [];
            foreach (AttachmentState asset in ProjectStore.Attachments(project))
            {
                if (string.IsNullOrWhiteSpace(asset.Path) || mapped.ContainsKey(asset.Path)) continue;
                mapped[asset.Path] = await UploadAssetAsync(cloud, asset.Path, cancellationToken);
            }
            if (mapped.Values.Any(x => x is null))
            {
                payload = CloudPayload.BuildProjectPayload(project, path => mapped.GetValueOrDefault(path));
                hash = CloudPayload.Hash(payload);
            }

            using HttpRequestMessage message = Request(cloud, HttpMethod.Put, $"/projects/{project.Id}");
            message.Content = JsonContent.Create(new ProjectWriteRequest(project.SyncRevision, project.Name, project.AssignmentDate, false, payload));
            using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                wrote |= await ResolveConflictAsync(library, cloud, hooks, project, operation, response, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                LastSyncError = $"项目“{project.Name}”上传失败：{await ErrorText(response)}";
                continue;
            }
            ProjectSnapshot? snapshot = await response.Content.ReadFromJsonAsync<ProjectSnapshot>(Web, cancellationToken);
            if (snapshot is null) continue;
            project.SyncRevision = snapshot.Revision;
            project.SyncDeleted = false;
            project.SyncHash = hash;
            _projectBases[project.Id] = payload;
            library.SyncQueue.Remove(operation);
            wrote = true;
        }
        return wrote;
    }

    private async Task<bool> PushDeleteAsync(ProjectLibrary library, CloudSyncSettings cloud, SyncOperationState operation, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = Request(cloud, HttpMethod.Put, $"/projects/{operation.ProjectId}");
        message.Content = JsonContent.Create(new ProjectWriteRequest(operation.ExpectedRevision, string.IsNullOrWhiteSpace(operation.Name) ? "已删除项目" : operation.Name, null, true, ""));
        using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
        // 冲突说明别处刚改过它：放弃删除，下一次拉取会把新版本带回来，不会误删别人的修改。
        if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            library.SyncQueue.Remove(operation);
            if (response.StatusCode == HttpStatusCode.Conflict) _notices.Add($"“{operation.Name}”在服务器上刚被修改，已取消删除并重新下载。");
            return response.IsSuccessStatusCode;
        }
        LastSyncError = $"删除“{operation.Name}”失败：{await ErrorText(response)}";
        return false;
    }

    /// <summary>
    /// 修订冲突：先把服务器版本准备好（含附件），成功后把本机内容另存为“冲突副本”并排队上传，
    /// 再用服务器版本替换原项目。两边的修改都不会丢。
    /// </summary>
    private async Task<bool> ResolveConflictAsync(ProjectLibrary library, CloudSyncSettings cloud, CloudSyncHooks hooks, ProjectDocument project, SyncOperationState operation, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ConflictResponse? conflict = null;
        try { conflict = await response.Content.ReadFromJsonAsync<ConflictResponse>(Web, cancellationToken); } catch (JsonException) { }
        if (conflict?.ServerVersion is null)
        {
            // 服务器上没有这个项目（例如服务端数据被重置）：按新项目重新上传。
            project.SyncRevision = 0; project.SyncHash = null;
            _again = true;
            return false;
        }
        if (hooks.IsProjectBusy(project.Id))
        {
            LastSyncError = $"“{project.Name}”在其他地方被修改，结束编辑后会自动处理冲突。";
            return false;
        }
        string? basePayload = _projectBases.GetValueOrDefault(project.Id);
        ProjectDocument? remote = await MaterializeAsync(cloud, conflict.ServerVersion, cancellationToken);
        if (remote is null) return false;

        // 两边改的是不同科目：合并后以服务器修订为基础重新上传，不另存副本。
        if (basePayload is not null && _projectBases.GetValueOrDefault(remote.Id) is { } remotePayload
            && ProjectMerge.Merge(basePayload, project, CloudPayload.BuildProjectPayload(project, MapKnown), remote, remotePayload) is { } merged)
        {
            library.SyncQueue.Remove(operation);
            Install(library, project, merged);
            Queue(library, merged);
            hooks.ProjectReplaced(merged.Id);
            _notices.Add($"“{project.Name}”同时在其他地方被修改，两边改的是不同科目，已自动合并。");
            _again = true;
            return true;
        }

        ProjectDocument copy = ProjectStore.Clone(project);
        copy.Id = Guid.NewGuid();
        copy.Name = $"{project.Name}（本机副本 {DateTime.Now:M-d HH:mm}）";
        copy.SyncRevision = 0; copy.SyncHash = null; copy.LinkedFile = null;
        foreach (AttachmentState asset in ProjectStore.Attachments(copy))
            asset.Path = !string.IsNullOrWhiteSpace(asset.Path) && File.Exists(asset.Path) ? store.CopyAttachment(copy.Id, asset.Path) : "";
        library.Projects.Add(copy);
        library.SyncQueue.Remove(operation);
        Queue(library, copy);
        Install(library, project, remote);
        hooks.ProjectReplaced(remote.Id);
        _notices.Add($"“{project.Name}”同时在其他地方被修改：已采用服务器版本，本机内容另存为“{copy.Name}”。");
        _again = true;
        return true;
    }

    // ------------------------------------------------------------------ 布局

    /// <summary>只同步班级设置：收到布局通知时调用，不必跑完整一轮同步。</summary>
    public async Task SyncLayoutOnlyAsync(ProjectLibrary library, CloudSyncHooks? hooks = null, CancellationToken cancellationToken = default)
    {
        if (!library.Settings.CloudSync.Enabled || !library.Settings.CloudSync.SyncLayout) return;
        if (_running) { _again = true; return; }
        _running = true;
        try
        {
            LastSyncError = null;
            await StepAsync("同步班级布局", () => SyncLayoutAsync(library, hooks ?? new CloudSyncHooks(), cancellationToken, 0), cancellationToken);
            if (LastSyncError is null) LastSyncAt = DateTime.Now;
        }
        finally { _running = false; }
        if (_again) await SynchronizeAsync(library, hooks, cancellationToken);
    }

    /// <summary>
    /// 按键三方合并班级设置。Base 是上次与服务器一致时的设置 JSON：
    /// 服务器相对 Base 改过的键采用服务器值；本机相对 Base 改过的键按 PATCH 上传；同一个键两边都改了以服务器为准。
    /// 统一管理时只采用服务器值，本机改动被还原。
    /// </summary>
    private async Task SyncLayoutAsync(ProjectLibrary library, CloudSyncHooks hooks, CancellationToken cancellationToken, int depth)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Get, "/layout"), cancellationToken);
        if (!response.IsSuccessStatusCode) { LastSyncError ??= $"读取班级布局失败：服务器返回 {(int)response.StatusCode}。"; return; }
        LayoutSnapshot snapshot = await response.Content.ReadFromJsonAsync<LayoutSnapshot>(Web, cancellationToken) ?? throw new JsonException();
        cloud.LayoutPolicy = snapshot.Policy;
        if (snapshot.Policy == LayoutPolicies.Off) return;

        JsonObject remote = ParseLayout(snapshot.PayloadJson);
        // 从按哈希比较的旧版本升级：修订号一致说明当时已与服务器同步，以服务器内容为 Base。
        if (cloud.LayoutBaseJson.Length == 0 && cloud.LayoutRevision > 0 && snapshot.Revision == cloud.LayoutRevision) cloud.LayoutBaseJson = snapshot.PayloadJson;
        bool firstSync = cloud.LayoutBaseJson.Length == 0;
        JsonObject baseline = ParseLayout(cloud.LayoutBaseJson);
        JsonObject local = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, null));

        if (snapshot.Revision > 0 && (snapshot.Revision > cloud.LayoutRevision || firstSync))
        {
            List<string> apply = (firstSync || snapshot.Policy == LayoutPolicies.Managed
                    ? SettingsSchema.ChangedKeys(local, remote)
                    : SettingsSchema.ChangedKeys(baseline, remote))
                .Where(remote.ContainsKey).ToList();
            List<string> overwritten = apply.Where(k => !firstSync && !SettingsSchema.Same(local[k], baseline[k]) && !SettingsSchema.Same(local[k], remote[k])).ToList();
            if (overwritten.Count > 0) _notices.Add($"班级设置已在其他地方更新，本机未上传的 {overwritten.Count} 项改动已被替换。");
            await ApplyLayoutAsync(library, hooks, snapshot, apply, cancellationToken);
            cloud = library.Settings.CloudSync;
            // 服务器没有的键（网页只下发了部分设置、旧版服务器不认识的新设置）：本机没改过就记入 Base，不主动上传。
            JsonObject rebased = ParseLayout(cloud.LayoutBaseJson);
            JsonObject applied = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, null));
            foreach (string key in LayoutSync.Keys)
                if (!remote.ContainsKey(key) && applied[key] is { } value && (firstSync || SettingsSchema.Same(local[key], baseline[key])))
                    rebased[key] = value.DeepClone();
            cloud.LayoutBaseJson = rebased.ToJsonString();
        }

        local = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, null));
        baseline = ParseLayout(cloud.LayoutBaseJson);
        List<string> diff = SettingsSchema.ChangedKeys(baseline, local);
        if (diff.Count == 0) return;

        if (snapshot.Policy == LayoutPolicies.Managed)
        {
            // 统一管理：本机改动还原为服务器值（服务器没有的键保留本机值）。
            List<string> revert = diff.Where(remote.ContainsKey).ToList();
            if (revert.Count == 0) return;
            _notices.Add("本班布局由网页统一管理，已恢复为服务器布局。");
            await ApplyLayoutAsync(library, hooks, snapshot, revert, cancellationToken);
            return;
        }

        // 先上传改动引用到的背景文件；超过服务器上限的文件改为保留本机路径，其他设备沿用自己的背景。
        Dictionary<string, string> refs = [];
        JsonObject current = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, refs));
        HashSet<string> needed = diff.Where(k => LayoutSync.BackgroundKeys.Contains(k) && current[k] is not null)
            .SelectMany(k => LayoutSync.AssetReferences(new JsonObject { [k] = current[k]!.DeepClone() }.ToJsonString())).ToHashSet();
        HashSet<string> failed = [];
        foreach (var (reference, path) in refs)
            if (needed.Contains(reference) && await UploadAssetAsync(cloud, path, cancellationToken) is null) failed.Add(reference);
        if (failed.Count > 0) current = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, null) is { } r && !failed.Contains(r) ? r : null);

        Dictionary<string, JsonElement> set = [];
        List<string> unset = [];
        foreach (string key in diff)
        {
            if (current[key] is { } value) set[key] = JsonSerializer.SerializeToElement(value);
            else unset.Add(key);
        }
        using HttpRequestMessage patch = Request(cloud, HttpMethod.Patch, "/layout");
        patch.Content = JsonContent.Create(new LayoutPatchRequest(cloud.LayoutRevision, set, unset));
        using HttpResponseMessage result = await _http.SendAsync(patch, cancellationToken);
        if (result.IsSuccessStatusCode)
        {
            LayoutSnapshot saved = await result.Content.ReadFromJsonAsync<LayoutSnapshot>(Web, cancellationToken) ?? throw new JsonException();
            cloud.LayoutRevision = saved.Revision;
            cloud.LayoutBaseJson = saved.PayloadJson;
            // 服务器会校验并规范化取值（限幅、取整、颜色大写）；以服务器为准写回本机，避免下一轮再次上传。
            JsonObject stored = ParseLayout(saved.PayloadJson);
            List<string> normalized = diff.Where(k => stored.ContainsKey(k) && !SettingsSchema.Same(stored[k], current[k])
                && !(LayoutSync.BackgroundKeys.Contains(k) && failed.Count > 0)).ToList();
            if (normalized.Count > 0) await ApplyLayoutAsync(library, hooks, saved, normalized, cancellationToken);
        }
        else if (result.StatusCode == HttpStatusCode.Conflict && depth == 0)
            await SyncLayoutAsync(library, hooks, cancellationToken, depth + 1);
        else if (result.StatusCode == HttpStatusCode.Locked) { }
        else LastSyncError ??= $"上传班级布局失败：{await ErrorText(result)}";
    }

    /// <summary>
    /// 把服务器设置中 keys 指定的键合并进本机。修订号与 Base 在通知界面之前就更新，界面刷新抛异常也不会重复应用。
    /// 背景文件下载失败的键保留本机值，并把本机值记入 Base，避免把旧背景当成本机改动传回去；
    /// 界面应用时做的规范化（例如按屏幕尺寸调整组件位置）同样记入 Base，不算本机改动。
    /// </summary>
    private async Task ApplyLayoutAsync(ProjectLibrary library, CloudSyncHooks hooks, LayoutSnapshot snapshot, List<string> keys, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        JsonObject remote = ParseLayout(snapshot.PayloadJson);
        JsonObject subset = new();
        foreach (string key in keys) if (remote[key] is { } value) subset[key] = value.DeepClone();
        Dictionary<string, string> resolved = [];
        foreach (string reference in LayoutSync.AssetReferences(subset.ToJsonString()).Distinct())
        {
            if (!AssetReference.TryParse(reference, out string sha, out string extension)) continue;
            string target = Path.Combine(hooks.BackgroundDirectory, $"{sha[..16]}_{sha[16..32]}{extension}");
            if (await DownloadAssetAsync(cloud, sha, target, cancellationToken)) resolved[reference] = target;
            else LastSyncError ??= "部分背景文件下载失败，已暂时保留本机背景。";
        }
        BoardSettingsState merged;
        HashSet<string> unresolved = [];
        try { merged = LayoutSync.Merge(library.Settings, snapshot.PayloadJson, reference => resolved.GetValueOrDefault(reference), keys, unresolved); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { LastSyncError = $"班级布局无法应用：{ex.Message}"; return; }

        JsonObject baseline = remote;
        void Rebase(BoardSettingsState settings, IEnumerable<string> localKeys)
        {
            JsonObject now = LayoutSync.ExtractNode(settings, path => LayoutRef(path, null));
            foreach (string key in localKeys)
            {
                if (now[key] is { } value) baseline[key] = value.DeepClone();
                else baseline.Remove(key);
            }
            settings.CloudSync.LayoutBaseJson = baseline.ToJsonString();
        }
        merged.CloudSync.LayoutRevision = Math.Max(merged.CloudSync.LayoutRevision, snapshot.Revision);
        merged.CloudSync.LayoutPolicy = snapshot.Policy;
        // 合并后的提取结果与服务器值表示不同（本机路径换成资源引用、校验后的取值）的键，以本机结果为 Base。
        JsonObject mergedNode = LayoutSync.ExtractNode(merged, path => LayoutRef(path, null));
        Rebase(merged, keys.Where(k => !SettingsSchema.Same(mergedNode[k], remote[k])).Union(unresolved).ToList());
        // 原地写入：设置页控件持有的子对象（背景样式等）继续有效。
        SettingsCopy.Into(library.Settings, merged);
        merged = library.Settings;
        LastAppliedLayoutKeys = keys;
        JsonObject before = LayoutSync.ExtractNode(merged, path => LayoutRef(path, null));
        // 界面刷新失败不影响已合并的设置：记录错误并随状态上报，下次重绘时界面会以新设置为准。
        try { hooks.LayoutApplied(merged); }
        catch (Exception ex) { LastSyncError ??= $"应用班级布局时界面刷新失败：{ex.Message}"; }
        JsonObject after = LayoutSync.ExtractNode(library.Settings, path => LayoutRef(path, null));
        List<string> normalizedByUi = SettingsSchema.ChangedKeys(before, after).Where(keys.Contains).ToList();
        if (normalizedByUi.Count > 0) Rebase(library.Settings, normalizedByUi);
    }

    private static JsonObject ParseLayout(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// <summary>背景文件换成资源引用（只算哈希，不上传）；目录和网页壁纸无法作为单个文件同步，返回 null。</summary>
    private string? LayoutRef(string path, Dictionary<string, string>? refs)
    {
        if (!File.Exists(path) || _hasher.Sha256Of(path) is not { } sha) return null;
        string reference = AssetReference.Create(sha, Path.GetExtension(path));
        refs?.TryAdd(reference, path);
        return reference;
    }

    // ------------------------------------------------------------------ 班务与状态

    private async Task FetchBoardAsync(ProjectLibrary library, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Get, "/board"), cancellationToken);
        if (!response.IsSuccessStatusCode) return;
        BoardInfo? board = await response.Content.ReadFromJsonAsync<BoardInfo>(Web, cancellationToken);
        if (board is null) return;
        UpdateServerClock(board.ServerTime);
        if (board.DeviceId != Guid.Empty) cloud.DeviceId = board.DeviceId;
        if (!string.IsNullOrWhiteSpace(board.ClassName)) cloud.ClassName = board.ClassName;
        bool changed = JsonSerializer.Serialize(board with { ServerTime = default }) != JsonSerializer.Serialize((Board ?? board) with { ServerTime = default }) || Board is null;
        Board = board;
        if (changed) BoardChanged?.Invoke();
    }

    /// <summary>拉取本班的考试安排。旧服务端没有该接口（404）或没有启用计划（204）时都视为无安排。</summary>
    private async Task FetchExamAsync(ProjectLibrary library, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Get, "/exam"), cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            ApplyExam(null, cloud.ClassId);
            return;
        }
        if (!response.IsSuccessStatusCode) return;
        ExamScheduleDto? schedule = await response.Content.ReadFromJsonAsync<ExamScheduleDto>(Web, cancellationToken);
        if (schedule is null) { ApplyExam(null, cloud.ClassId); return; }
        UpdateServerClock(schedule.ServerTime);
        ApplyExam(schedule, cloud.ClassId);
    }

    /// <summary>更新考试安排；内容变化时写本地缓存并通知界面。ServerTime 每次刷新都不同，不参与比较。</summary>
    private void ApplyExam(ExamScheduleDto? schedule, Guid? cacheClassId)
    {
        bool changed = ExamSignature(schedule) != ExamSignature(Exam);
        Exam = schedule;
        if (cacheClassId is { } classId)
        {
            if (schedule is null) ExamStore.Clear();
            else ExamStore.Save(classId, schedule);
        }
        if (changed) ExamChanged?.Invoke();
    }

    private static string ExamSignature(ExamScheduleDto? schedule) =>
        schedule is null ? "" : JsonSerializer.Serialize(schedule with { ServerTime = default }, Web);

    /// <summary>服务器时间（UTC）与本机 UTC 的差；同校内网同时区，直接加到本地时间上用于到点判断。</summary>
    private void UpdateServerClock(DateTime serverUtc) => ServerClockOffset = serverUtc - DateTime.UtcNow;

    private async Task ReportStatusAsync(ProjectLibrary library, CloudSyncHooks hooks, CancellationToken cancellationToken)
    {
        CloudSyncSettings cloud = library.Settings.CloudSync;
        using HttpRequestMessage message = Request(cloud, HttpMethod.Post, "/devices/me/status");
        // 字体列表较长，内容没变时不重复上报。
        List<string> fonts = hooks.Fonts().ToList();
        string fontsHash = CloudPayload.Hash(fonts.ToArray());
        bool sendFonts = fonts.Count > 0 && fontsHash != _reportedFonts;
        message.Content = JsonContent.Create(new DeviceStatusRequest(hooks.AppVersion, library.SyncQueue.Count, LastSyncError, library.ActiveProjectId, hooks.ActiveProjectName(), sendFonts ? fonts : null));
        using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
        if (sendFonts && response.IsSuccessStatusCode) _reportedFonts = fontsHash;
    }

    // ------------------------------------------------------------------ 编辑占用与屏幕上报

    /// <summary>申请编辑占用的结果：Acquired 为可以编辑（拿到了占用，或服务器不支持、连不上时照常编辑）；HeldBy 是占着的一方。</summary>
    public sealed record EditLockResult(bool Acquired, EditLockInfo? HeldBy = null);

    /// <summary>
    /// 进入编辑模式前（以及编辑期间每 15 秒）申请或续期项目的编辑占用。老师正在网页上编辑时返回占着的一方；
    /// 旧服务端没有这个接口、连不上或 3 秒内没有回应时照常编辑，冲突仍由修订号和三方合并处理。
    /// </summary>
    public async Task<EditLockResult> AcquireEditLockAsync(CloudSyncSettings cloud, Guid projectId, CancellationToken cancellationToken = default)
    {
        if (!cloud.Enabled) return new(true);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using HttpRequestMessage message = Request(cloud, HttpMethod.Put, $"/projects/{projectId}/edit-lock");
            message.Content = JsonContent.Create(new EditLockRequest());
            using HttpResponseMessage response = await _http.SendAsync(message, timeout.Token);
            if (response.StatusCode != HttpStatusCode.Locked) return new(true);
            return new(false, await response.Content.ReadFromJsonAsync<EditLockInfo>(Web, timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { return new(true); }
    }

    /// <summary>结束编辑并上传后释放占用；失败也不要紧，服务端 45 秒没有续期会自动释放。</summary>
    public async Task ReleaseEditLockAsync(CloudSyncSettings cloud, Guid projectId)
    {
        if (!cloud.Enabled) return;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            using HttpResponseMessage response = await _http.SendAsync(Request(cloud, HttpMethod.Delete, $"/projects/{projectId}/edit-lock"), timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    /// <summary>上报屏幕与分区，内容没变时不重复上报。旧服务端没有这个接口时停止上报，直到软件重启。</summary>
    public async Task ReportScreenAsync(CloudSyncSettings cloud, DeviceScreenReport report, CancellationToken cancellationToken = default)
    {
        if (!cloud.Enabled || _screenUnsupported) return;
        string json = JsonSerializer.Serialize(report, Web);
        if (json == _reportedScreen) return;
        try
        {
            using HttpRequestMessage message = Request(cloud, HttpMethod.Put, "/devices/me/screen");
            message.Content = JsonContent.Create(report, options: Web);
            using HttpResponseMessage response = await _http.SendAsync(message, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) _screenUnsupported = true;
            else if (response.IsSuccessStatusCode) _reportedScreen = json;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }

    /// <summary>实时连接恢复后重新上报一次：服务端可能刚重启，或者别的网页刚打开。</summary>
    public void ForgetReportedScreen() => _reportedScreen = "";

    // ------------------------------------------------------------------ 资源

    /// <summary>附件路径换成资源引用（只算哈希）；文件不存在时返回 null。</summary>
    private string? MapKnown(string path)
    {
        if (AssetReference.TryParse(path, out _, out _)) return path;
        return _hasher.Sha256Of(path) is { } sha ? AssetReference.Create(sha, Path.GetExtension(path)) : null;
    }

    /// <summary>确保文件已在服务器上；返回资源引用，失败返回 null 并记录原因。</summary>
    private async Task<string?> UploadAssetAsync(CloudSyncSettings cloud, string path, CancellationToken cancellationToken)
    {
        if (_hasher.Sha256Of(path) is not { } sha) return null;
        string reference = AssetReference.Create(sha, Path.GetExtension(path));
        string key = $"{cloud.ClassId}/{sha}";
        if (_uploaded.Contains(key)) return reference;
        using (HttpResponseMessage head = await _http.SendAsync(Request(cloud, HttpMethod.Head, $"/assets/{sha}"), cancellationToken))
            if (head.IsSuccessStatusCode) { _uploaded.Add(key); return reference; }
        await using FileStream stream = File.OpenRead(path);
        using HttpRequestMessage put = Request(cloud, HttpMethod.Put, $"/assets/{sha}?name={Uri.EscapeDataString(Path.GetFileName(path))}");
        put.Content = new StreamContent(stream);
        using HttpResponseMessage response = await _files.SendAsync(put, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LastSyncError ??= $"文件“{Path.GetFileName(path)}”上传失败：{await ErrorText(response)}";
            return null;
        }
        _uploaded.Add(key);
        return reference;
    }

    private async Task<bool> DownloadAssetAsync(CloudSyncSettings cloud, string sha, string target, CancellationToken cancellationToken)
    {
        if (File.Exists(target) && _hasher.Sha256Of(target) == sha) return true;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using HttpResponseMessage response = await _files.SendAsync(Request(cloud, HttpMethod.Get, $"/assets/{sha}"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write))
                await response.Content.CopyToAsync(output, cancellationToken);
            if (_hasher.Sha256Of(temporary) != sha) return false;
            File.Move(temporary, target, true);
            _hasher.Remember(target, sha);
            _uploaded.Add($"{cloud.ClassId}/{sha}");
            return true;
        }
        catch (IOException) { return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static HttpRequestMessage Request(CloudSyncSettings cloud, HttpMethod method, string path)
    {
        HttpRequestMessage message = new(method, $"{cloud.ServerUrl.TrimEnd('/')}/api/classes/{cloud.ClassId}{path}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cloud.DeviceToken);
        return message;
    }

    private static async Task<string> ErrorText(HttpResponseMessage response)
    {
        string body = "";
        try { body = await response.Content.ReadAsStringAsync(); } catch (HttpRequestException) { }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.String) body = doc.RootElement.GetString() ?? "";
            else if (doc.RootElement.TryGetProperty("detail", out var detail)) body = detail.GetString() ?? "";
            else if (doc.RootElement.TryGetProperty("error", out var error)) body = error.GetString() ?? "";
        }
        catch (JsonException) { }
        return body.Length is > 0 and < 200 ? body : $"服务器返回 {(int)response.StatusCode}";
    }
}
