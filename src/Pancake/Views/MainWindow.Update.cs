using System.Reflection;
using Avalonia.Controls;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 应用更新：检查发布、下载更新包、校验并覆盖安装。
/// Windows 保留覆盖式自动更新（解压后退出、由脚本替换文件并重启）；
/// Linux 与 macOS 只提示新版本并打开发布页，由用户手动替换。
/// </summary>
public sealed partial class MainWindow
{
    private bool _checkingForUpdates;

    private string ReleasePageUrl => Settings.UpdateSource == "Gitee"
        ? "https://gitee.com/EdgeHH/pancake/releases"
        : "https://github.com/Edge-HH/Pancake/releases";

    /// <summary>
    /// 检查更新。<paramref name="interactive"/> 为真时失败会弹出对话框，
    /// 启动时的自动检查只写状态文字，避免开机就被弹窗打断。
    /// </summary>
    private async Task CheckForUpdatesAsync(TextBlock? status, bool interactive)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            if (status is not null) status.Text = $"正在检查 {Settings.UpdateSource} Release…";
            ReleaseUpdateService service = new();
            ReleaseSnapshot? latest = await service.GetLatestAsync(Settings.UpdateSource);
            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            GitHubReleaseUpdate? update = latest is not null && ReleaseUpdateService.IsNewer(latest.Version, current)
                ? latest.Update
                : null;
            if (update is null)
            {
                if (status is not null) status.Text = "当前已是最新版，或最新 Release 没有可安装资产。";
                return;
            }

            if (!PlatformServices.AppUpdate.SupportsSelfUpdate)
            {
                // 非 Windows 平台不做文件替换，只把用户带到发布页。
                if (status is not null) status.Text = $"发现 {update.Tag}，当前平台请手动下载替换。";
                PlatformServices.ExternalLauncher.OpenUri(ReleasePageUrl);
                return;
            }

            if (status is not null) status.Text = $"发现 {update.Tag}，正在下载 {update.AssetName}…";
            string dataRoot = AppPathService.ResolveDataRoot();
            string package = await service.DownloadAsync(update, dataRoot);
            bool portable = Path.GetExtension(package).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetExtension(package).Equals(".7z", StringComparison.OrdinalIgnoreCase);
            if (portable) package = await SplitUpdatePackage.NormalizeAsync(package);

            if (status is not null)
            {
                status.Text = portable
                    ? $"{update.Tag} 便携版已下载，可以自动更新。"
                    : $"{update.Tag} 已下载，等待安装。";
            }

            string prompt = portable
                ? $"已下载 {update.Tag} 便携版。现在保存项目并重启更新吗？程序会自动解压、覆盖旧版本并重新启动，项目和设置都会保留。"
                : $"已将 {update.Tag} 下载到软件目录。现在启动安装程序吗？";
            bool confirmed = await ShowConfirmAsync("发现新版本", prompt, portable ? "立即更新" : "启动安装", "稍后");
            if (!confirmed) return;

            if (!portable)
            {
                PlatformServices.AppUpdate.TryLaunchInstaller(package);
                return;
            }

            if (status is not null) status.Text = "正在校验并解压更新…";
            string configuration = await Task.Run(() => PortableUpdateService.Prepare(
                package,
                AppContext.BaseDirectory,
                Path.Combine(dataRoot, "updates"),
                Environment.ProcessId));
            // 覆盖前先把项目落盘，更新脚本只替换程序文件、不会动数据目录。
            _viewModel.SaveToDisk();
            PortableUpdateService.Launch(configuration);
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (status is not null) status.Text = $"更新检查失败：{ex.Message}";
            if (interactive) await ShowMessageAsync("无法检查更新", ex.Message, "知道了");
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    /// <summary>启动时按设置自动检查一次更新。</summary>
    private void StartAutoUpdateCheck()
    {
        if (!Settings.AutoUpdateEnabled) return;
        _ = CheckForUpdatesAsync(null, interactive: false);
    }
}
