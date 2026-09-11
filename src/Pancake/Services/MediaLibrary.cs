using System.Security.Cryptography;
using System.Text.Json;

namespace Pancake.Services;

/// <summary>图片历史与背景共用自有资源副本，原文件或项目删除后仍可再次选择。</summary>
public sealed class MediaLibrary(string dataDirectory)
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif"];
    public static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".webm"];
    private readonly string _directory = Path.Combine(dataDirectory, "backgrounds");
    private string HistoryPath => Path.Combine(_directory, "recent-images.json");
    private readonly SemaphoreSlim _gate = new(1);
    public event Action? Changed;
    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsWeb(string path) => Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".htm", StringComparison.OrdinalIgnoreCase);
    public static bool IsSupported(string path) => IsImage(path) || IsVideo(path) || IsWeb(path);
    public static string DisplayName(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length > 17 && name[16] == '_' && name.Take(16).All(Uri.IsHexDigit) ? name[17..] : name;
    }

    public IReadOnlyList<string> RecentImages()
    {
        try { return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryPath)) ?? [])
            .Where(path => IsImage(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public async Task<string> ImportAsync(string source)
    {
        if (IsWeb(source)) throw new InvalidDataException("请从 Wallpaper Engine 导入完整网页壁纸项目。");
        if (!IsSupported(source)) throw new InvalidDataException("不支持此媒体格式。");
        await _gate.WaitAsync();
        string owned;
        try
        {
            // 哈希和复制在后台执行，大视频不阻塞设置页；同内容复用一份资源。
            owned = await Task.Run(() =>
            {
                Directory.CreateDirectory(_directory);
                using var input = File.OpenRead(source);
                string hash = Convert.ToHexString(SHA256.HashData(input));
                string destination = Path.Combine(_directory, hash[..16] + "_" + DisplayName(source));
                if (!File.Exists(destination))
                {
                    string temporary = destination + ".tmp";
                    try
                    {
                        input.Position = 0;
                        using (var output = File.Create(temporary)) input.CopyTo(output);
                        File.Move(temporary, destination, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                return destination;
            });
            if (IsImage(owned))
            {
                var history = RecentImages().Where(path => !string.Equals(path, owned, StringComparison.OrdinalIgnoreCase)).Prepend(owned).Take(8);
                string temporary = HistoryPath + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(history));
                File.Move(temporary, HistoryPath, true);
            }
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
        return owned;
    }

    public async Task<string> ImportWallpaperAsync(WallpaperProject project)
    {
        if (project.Kind != "web") return await ImportAsync(project.Path);
        // 网页需要完整相对目录；先在临时目录复制完成，再发布入口，避免队列读到半个项目。
        string directory = Path.Combine(dataDirectory, "web-wallpapers");
        string destination = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        string temporary = destination + ".tmp";
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            try
            {
                CopyDirectory(project.Directory, temporary);
                string relative = Path.GetRelativePath(project.Directory, project.Path);
                string entry = Path.GetFullPath(Path.Combine(temporary, relative));
                if (!entry.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
                    throw new InvalidDataException("网页壁纸入口不在项目目录内。");
                Directory.Move(temporary, destination);
                return Path.Combine(destination, relative);
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        });
    }

    private static void CopyDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("网页壁纸项目包含目录链接，无法完整导入。");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("网页壁纸项目包含文件链接，无法完整导入。");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string child in Directory.EnumerateDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
    }
}
