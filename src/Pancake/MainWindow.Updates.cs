using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private int _updateSourceRequest;
    private async Task RefreshUpdateSourceHintAsync()
    {
        int request = ++_updateSourceRequest;
        if (_settings.UpdateSource != "Gitee") { UpdateSourceHint.Text = "从 GitHub 获取最新发布。"; return; }
        UpdateSourceHint.Text = "正在比较 Gitee 与 GitHub 的发布版本…";
        try
        {
            var gitee = _updateService.GetLatestAsync("Gitee");
            var github = _updateService.GetLatestAsync("GitHub");
            await Task.WhenAll(gitee, github);
            if (request != _updateSourceRequest || _settings.UpdateSource != "Gitee") return;
            string message = ReleaseUpdateService.RecommendGitHub(gitee.Result, github.Result)
                ? $"Gitee 尚未同步 GitHub 的 {github.Result!.Tag}，建议将更新源切换到 GitHub。"
                : "Gitee 同步可能延迟，当前未发现比 GitHub 更旧的版本。";
            UpdateSourceHint.Text = message;
            if (message.Contains("尚未同步"))
            {
                StorageInfoBar.Title = "更新源提醒"; StorageInfoBar.Message = message;
                StorageInfoBar.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning; StorageInfoBar.IsOpen = true;
            }
        }
        catch (Exception)
        {
            if (request == _updateSourceRequest) UpdateSourceHint.Text = "暂时无法比较两个更新源；仍可检查所选源，Gitee 可能有同步延迟。";
        }
    }
}
