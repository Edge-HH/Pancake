using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pancake.Services;

public sealed record GitHubReleaseUpdate(Version Version, string Tag, string AssetName, string DownloadUrl, string? Digest);

public sealed class GitHubUpdateService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

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
        JsonElement? asset = document.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => IsInstallable(item.GetProperty("name").GetString() ?? ""));
        if (asset is null || asset.Value.ValueKind == JsonValueKind.Undefined) return null;
        string? digest = asset.Value.TryGetProperty("digest", out JsonElement digestElement) ? digestElement.GetString() : null;
        return new GitHubReleaseUpdate(releaseVersion, tag, asset.Value.GetProperty("name").GetString()!, asset.Value.GetProperty("browser_download_url").GetString()!, digest);
    }

    public async Task<string> DownloadAsync(GitHubReleaseUpdate update, string dataDirectory, CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(dataDirectory, "updates", update.Tag);
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

    private static bool IsInstallable(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase);
}
