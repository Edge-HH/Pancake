using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Services;
using Pancake.Shared;

namespace Pancake;

/// <summary>
/// 项目编辑占用（集控）：进入编辑模式前向服务端申请，网页上有老师正在修改这个项目时提示“云端修改中”、不进入编辑；
/// 编辑期间每 15 秒续期，结束编辑并上传后释放。网页可以请求本机保存并退出编辑（end-edit 指令），
/// 本机没有响应时网页可以强制接管：续期被拒就按同样方式保存并退出编辑。没连集控或连不上时照常编辑。
/// </summary>
public sealed partial class MainWindow
{
    private readonly DispatcherTimer _editLockHeartbeat = new() { Interval = TimeSpan.FromSeconds(15) };
    // 已拿到占用的项目：EnterEditing 据此跳过申请。
    private Guid? _editLockGranted;
    private bool _editLockPending;
    // 结束编辑后等这次修改上传完再释放占用的项目。
    private Guid? _editLockReleaseAfterSync;

    private void InitializeCloudEditLock()
    {
        _editLockHeartbeat.Tick += async (_, _) => await RenewEditLockAsync();
        Closed += (_, _) => _editLockHeartbeat.Stop();
    }

    /// <summary>连了集控、当前项目还没拿到占用时，进入编辑前要先申请。</summary>
    private bool NeedsCloudEditLock() =>
        _settings.CloudSync.Enabled && _storageReady && CurrentProject is { } project && _editLockGranted != project.Id;

    /// <summary>申请到占用后再进入编辑；被网页或另一块大屏占着时提示“云端修改中”。同步调用方只管触发。</summary>
    private void RequestCloudLockThenEnter()
    {
        if (_editLockPending || CurrentProject is not { } project) return;
        _editLockPending = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                CloudSyncService.EditLockResult result = await CloudSync.AcquireEditLockAsync(_settings.CloudSync, project.Id);
                // 申请期间切换了项目：作废这次申请。
                if (CurrentProject?.Id != project.Id) { if (result.Acquired) _ = CloudSync.ReleaseEditLockAsync(_settings.CloudSync, project.Id); return; }
                if (!result.Acquired) { ShowCloudNotice(EditLockedText(project, result.HeldBy), InfoBarSeverity.Warning, "云端修改中"); return; }
                _editLockGranted = project.Id;
                EnterEditing();
            }
            finally { _editLockPending = false; }
        });
    }

    private static string EditLockedText(ProjectDocument project, EditLockInfo? holder) => holder?.Holder == EditLockHolders.Device
        ? $"另一块大屏“{holder.Name}”正在编辑“{project.Name}”，它结束编辑后才能在这里编辑。"
        : $"{holder?.Name ?? "其他老师"}正在网页上编辑“{project.Name}”，网页保存或离开后才能在大屏编辑。";

    /// <summary>进入编辑后开始续期。</summary>
    private void StartEditLockHeartbeat()
    {
        if (_editLockGranted is not null) _editLockHeartbeat.Start();
    }

    /// <summary>编辑期间续期；续期被网页拒绝说明网页强制接管了，按“请求结束编辑”处理。</summary>
    private async Task RenewEditLockAsync()
    {
        if (!_isEditing || CurrentProject is not { } project || _editLockGranted != project.Id) { _editLockHeartbeat.Stop(); return; }
        CloudSyncService.EditLockResult result = await CloudSync.AcquireEditLockAsync(_settings.CloudSync, project.Id);
        if (!result.Acquired && _isEditing && CurrentProject?.Id == project.Id && result.HeldBy?.Holder == EditLockHolders.User)
            EndEditingForCloud(project, result.HeldBy.Name);
    }

    /// <summary>
    /// 结束或放弃编辑时调用：停止续期，等这次修改上传完再释放占用，网页因此一定能载入大屏刚保存的内容。
    /// 关闭自动保存时项目不上传，立即释放。
    /// </summary>
    private void ReleaseCloudEditLock()
    {
        _editLockHeartbeat.Stop();
        if (_editLockGranted is not { } id) return;
        _editLockGranted = null;
        if (_settings.AutoSaveEnabled) { _editLockReleaseAfterSync = id; RequestCloudSync(immediate: true); }
        else { _ = CloudSync.ReleaseEditLockAsync(_settings.CloudSync, id); PullDeferredProjects(); }
    }

    /// <summary>申请到占用却没进入编辑（例如取消了解锁密码）：立即释放。</summary>
    private void ReleaseUnusedEditLock()
    {
        if (_isEditing || _editLockGranted is not { } id) return;
        _editLockGranted = null;
        _ = CloudSync.ReleaseEditLockAsync(_settings.CloudSync, id);
    }

    /// <summary>每次同步结束后：等待释放的项目已经上传（不在队列里了）就释放占用。</summary>
    private void ReleaseEditLockIfUploaded()
    {
        if (_editLockReleaseAfterSync is not { } id || _isEditing || CloudSyncService.IsPending(_library, id)) return;
        _editLockReleaseAfterSync = null;
        _ = CloudSync.ReleaseEditLockAsync(_settings.CloudSync, id);
    }

    /// <summary>编辑期间别处的修改没能替换进来：结束编辑后补拉。</summary>
    private void PullDeferredProjects()
    {
        IReadOnlyList<Guid> ids = CloudSync.TakeDeferredProjects();
        if (ids.Count == 0) return;
        foreach (Guid id in ids) _pendingCloudProjects.Add(id);
        _cloudProjectDebounce.Stop();
        _cloudProjectDebounce.Start();
    }

    /// <summary>网页请求结束编辑（end-edit 指令）：正在编辑这个项目就保存、上传并退出编辑；没在编辑就直接释放残留的占用。</summary>
    private void OnEndEditRequested(Guid projectId, string? requester)
    {
        if (_isEditing && CurrentProject is { } project && project.Id == projectId) { EndEditingForCloud(project, requester); return; }
        if (_editLockGranted == projectId) _editLockGranted = null;
        _ = CloudSync.ReleaseEditLockAsync(_settings.CloudSync, projectId);
    }

    private void EndEditingForCloud(ProjectDocument project, string? requester)
    {
        FinishEditing();
        string who = string.IsNullOrWhiteSpace(requester) ? "网页上的老师" : requester;
        ShowCloudNotice($"{who}请求在网页上编辑“{project.Name}”，已保存本机修改并退出编辑模式。", InfoBarSeverity.Informational);
    }
}
