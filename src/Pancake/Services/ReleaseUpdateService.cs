using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pancake.Services;

public sealed record ReleaseSnapshot(Version Version, string Tag, GitHubReleaseUpdate? Update);

/// <summary>统一两个更新源的版本、资产和分卷语义；安装仍由 PortableUpdateService 负责。</summary>
public sealed class ReleaseUpdateService
{
    private readonly HttpClient _http;
    public ReleaseUpdateService(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new() : new(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pancake-Updater/2.0");
    }
    public static bool IsNewer(Version remote, Version current) => Normalize(remote) > Normalize(current);
    public static bool RecommendGitHub(ReleaseSnapshot? gitee, ReleaseSnapshot? github) => github is not null && (gitee is null || IsNewer(github.Version, gitee.Version));
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    public async Task<ReleaseSnapshot?> GetLatestAsync(string source, CancellationToken token = default)
    {
        string url = source == "Gitee" ? "https://gitee.com/api/v5/repos/EdgeHH/pancake/releases/latest" : "https://api.github.com/repos/Edge-HH/Pancake/releases/latest";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(url, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return ParseRelease(json.RootElement);
    }

    public static ReleaseSnapshot? ParseRelease(JsonElement release)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version)) return null;
        if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        List<ReleaseAsset> assets = [];
        foreach (var item in release.GetProperty("assets").EnumerateArray())
        {
            string name = item.GetProperty("name").GetString() ?? "", url = item.GetProperty("browser_download_url").GetString() ?? "";
            // Gitee 将源码包也放入 assets，必须排除。
            if (!name.Contains("Pancake", StringComparison.OrdinalIgnoreCase) || name.Contains("arm64", StringComparison.OrdinalIgnoreCase) || url.Contains("/archive/", StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
            assets.Add(new(name, url, item.TryGetProperty("digest", out var digest) ? digest.GetString() : null));
        }
        var first = assets.Where(a => GitHubUpdateService.IsInstallable(a.Name) || Regex.IsMatch(a.Name, @"\.7z$|\.(zip|7z)\.001$", RegexOptions.IgnoreCase))
            .OrderByDescending(a => a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 4
                : a.Name.EndsWith(".zip.001", StringComparison.OrdinalIgnoreCase) ? 3
                : a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? 2
                : a.Name.EndsWith(".7z.001", StringComparison.OrdinalIgnoreCase) ? 1 : 0).FirstOrDefault();
        if (first is null) return new(version, tag, null);
        List<ReleaseAsset> volumes = [first];
        if (Regex.IsMatch(first.Name, @"\.(zip|7z)\.001$", RegexOptions.IgnoreCase))
        {
            string prefix = first.Name[..^3];
            volumes = assets.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(a.Name[prefix.Length..], @"^\d{3,}$"))
                .OrderBy(a => int.Parse(a.Name[prefix.Length..])).ToList();
            for (int i = 0; i < volumes.Count; i++)
                if (int.Parse(volumes[i].Name[prefix.Length..]) != i + 1) throw new InvalidDataException("Release 更新分卷缺失或重复。");
        }
        else if (first.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string prefix = first.Name[..^3];
            var split = assets.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(a.Name[prefix.Length..], @"^z\d{2,}$", RegexOptions.IgnoreCase))
                .OrderBy(a => int.Parse(a.Name[(prefix.Length + 1)..])).ToList();
            for (int i = 0; i < split.Count; i++)
                if (int.Parse(split[i].Name[(prefix.Length + 1)..]) != i + 1) throw new InvalidDataException("Release ZIP 分卷缺失。");
            if (split.Count > 0) volumes = split.Concat([first]).ToList();
        }
        return new(version, tag, new(version, tag, first.Name, first.Url, first.Digest) { Parts = volumes });
    }

    public async Task<string> DownloadAsync(GitHubReleaseUpdate update, string dataDirectory, CancellationToken token = default)
    {
        string directory = Path.Combine(dataDirectory, "updates", update.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var parts = update.Parts.Count == 0 ? new[] { new ReleaseAsset(update.AssetName, update.DownloadUrl, update.Digest) } : update.Parts;
        long total = 0;
        foreach (var part in parts)
        {
            if (Path.GetFileName(part.Name) != part.Name) throw new InvalidDataException("无效更新附件名称。");
            await using Stream input = await _http.GetStreamAsync(part.Url, token);
            await using FileStream output = new(Path.Combine(directory, part.Name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            byte[] buffer = new byte[81920]; int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read; if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("更新下载体积过大。");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            await output.FlushAsync(token);
            if (part.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            {
                output.Position = 0;
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(output, token));
                if (!actual.Equals(part.Digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("下载的更新文件 SHA-256 校验失败。");
            }
        }
        string package = Path.Combine(directory, update.AssetName);
        if (Regex.IsMatch(update.AssetName, @"\.(zip|7z)\.001$", RegexOptions.IgnoreCase))
        {
            package = package[..^4];
            await using FileStream combined = File.Create(package);
            foreach (var part in parts)
            {
                await using FileStream input = File.OpenRead(Path.Combine(directory, part.Name));
                await input.CopyToAsync(combined, token);
            }
        }
        return package;
    }
}
