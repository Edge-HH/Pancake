using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pancake.Services;

public sealed record ReleaseAsset(string Name, string Url, string? Digest);
public sealed record GitHubReleaseUpdate(Version Version, string Tag, string AssetName, string DownloadUrl, string? Digest)
{
    public IReadOnlyList<ReleaseAsset> Parts { get; init; } = [];
}

public sealed class GitHubUpdateService
{
    /// <summary>更新包文件名里的平台标识，与发布流水线产出的命名保持一致。</summary>
    public static IReadOnlyList<string> PlatformKeywords { get; } = ["win", "linux", "macos"];

    /// <summary>当前运行平台的标识；用于只挑选本平台的更新资产。</summary>
    public static string CurrentPlatformKeyword { get; } =
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    /// <summary>当前运行架构的标识。</summary>
    public static string CurrentArchitectureKeyword { get; } = RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public GitHubUpdateService() => _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Pancake-Updater/1.0");

    public async Task<GitHubReleaseUpdate?> CheckAsync(string repository, CancellationToken cancellationToken = default)
    {
        string[] parts = repository.Trim().Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("更新仓库格式应为 owner/repository。 ");
        using HttpResponseMessage response = await _httpClient.GetAsync($"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        string tag = document.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? releaseVersion)) return null;
        Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        if (releaseVersion <= current) return null;
        // 三端共用同一份 Release，必须只挑出与当前平台和架构匹配的资产。
        JsonElement? asset = document.RootElement.GetProperty("assets").EnumerateArray()
            .Where(item =>
            {
                string name = item.GetProperty("name").GetString() ?? "";
                return IsInstallable(name)
                    && MatchesPlatform(name, CurrentPlatformKeyword)
                    && name.Contains(CurrentArchitectureKeyword, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(item => (item.GetProperty("name").GetString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (asset is null || asset.Value.ValueKind == JsonValueKind.Undefined) return null;
        string? digest = asset.Value.TryGetProperty("digest", out JsonElement digestElement) ? digestElement.GetString() : null;
        return new GitHubReleaseUpdate(releaseVersion, tag, asset.Value.GetProperty("name").GetString()!, asset.Value.GetProperty("browser_download_url").GetString()!, digest);
    }

    public async Task<string> DownloadAsync(GitHubReleaseUpdate update, string dataDirectory, CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(dataDirectory, "updates", update.Version.ToString());
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, Path.GetFileName(update.AssetName));
        await using Stream input = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        if (update.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
        {
            output.Position = 0;
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(output, cancellationToken));
            string expected = update.Digest[7..];
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                output.Close();
                File.Delete(destination);
                throw new InvalidDataException("下载的更新文件 SHA-256 校验失败。");
            }
        }
        return destination;
    }

    public static void LaunchInstaller(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>可安装的包类型；与平台无关，平台与架构由 MatchesPlatform 单独判断。</summary>
    public static bool IsInstallable(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断资产名是否属于指定平台。其它平台的标识一旦出现即视为不匹配，
    /// 这样 Linux/macOS 用户不会拿到 Windows 包，反之亦然。
    /// </summary>
    public static bool MatchesPlatform(string name, string platformKeyword)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(platformKeyword)) return false;
        foreach (string other in PlatformKeywords)
        {
            if (other.Equals(platformKeyword, StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains(other, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return name.Contains(platformKeyword, StringComparison.OrdinalIgnoreCase);
    }
}
