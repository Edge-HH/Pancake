using Microsoft.Win32;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pancake.Services;

public sealed record WallpaperProject(string Title, string Path, string Kind, string Directory, string PreviewPath)
{
    public string TypeLabel => Kind == "web" ? "网页壁纸" : "视频壁纸";
    public Uri? PreviewUri => string.IsNullOrEmpty(PreviewPath) ? null : new Uri(PreviewPath);
}
public sealed record WallpaperEngineInstallation(string Directory, IReadOnlyList<string> Libraries);

/// <summary>读取 Steam 本地视频和网页项目；场景及原生程序不交给媒体播放器执行。</summary>
public static class WallpaperEngineLibrary
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static WallpaperEngineInstallation? Detect()
    {
        HashSet<string> libraries = new(StringComparer.OrdinalIgnoreCase);
        string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        steam ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (!string.IsNullOrWhiteSpace(steam))
        {
            libraries.Add(steam);
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        string? installed = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 431960", "InstallLocation", null) as string;
        installed ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 431960", "InstallLocation", null) as string;
        var candidates = libraries.Select(root => Path.Combine(root, "steamapps", "common", "wallpaper_engine")).ToList();
        if (!string.IsNullOrWhiteSpace(installed)) candidates.Insert(0, installed);
        string? directory = candidates.FirstOrDefault(root => File.Exists(Path.Combine(root, "wallpaper64.exe")) || File.Exists(Path.Combine(root, "wallpaper32.exe")));
        if (directory is null) return null;
        var library = System.IO.Directory.GetParent(directory)?.Parent?.Parent;
        if (library is not null && System.IO.Directory.Exists(Path.Combine(library.FullName, "steamapps"))) libraries.Add(library.FullName);
        return new(directory, libraries.ToArray());
    }

    public static IReadOnlyList<WallpaperProject> ReadVideos(WallpaperEngineInstallation installation) =>
        ReadProjects(installation).Where(project => project.Kind == "video").ToArray();

    public static IReadOnlyList<WallpaperProject> ReadProjects(WallpaperEngineInstallation installation)
    {
        List<WallpaperProject> result = [];
        var roots = installation.Libraries.Select(root => Path.Combine(root, "steamapps", "workshop", "content", "431960"))
            .Append(Path.Combine(installation.Directory, "projects", "myprojects"))
            .Append(Path.Combine(installation.Directory, "projects", "defaultprojects"));
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase).Where(System.IO.Directory.Exists))
        {
            try
            {
                foreach (string folder in System.IO.Directory.EnumerateDirectories(root))
                {
                    var video = ReadProject(folder);
                    if (video is not null) result.Add(video);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 其他 Steam 库仍可读取。 */ }
        }
        return result.DistinctBy(video => video.Path, StringComparer.OrdinalIgnoreCase).OrderBy(video => video.Title).ToArray();
    }

    public static WallpaperProject? ReadProject(string folder)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "project.json")));
            var root = json.RootElement;
            if (!root.TryGetProperty("type", out var type)) return null;
            string kind = type.GetString()?.ToLowerInvariant() ?? "";
            if (kind is not ("video" or "web")) return null;
            string? file = root.GetProperty("file").GetString();
            if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) return null;
            string path = Path.GetFullPath(Path.Combine(folder, file));
            // 项目文件只可指向自己的目录，不允许通过相对路径导入目录外的文件。
            if (!path.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !(kind == "video" ? MediaLibrary.IsVideo(path) : MediaLibrary.IsWeb(path)) || !File.Exists(path)) return null;
            string title = root.TryGetProperty("title", out var name) ? name.GetString() ?? Path.GetFileName(folder) : Path.GetFileName(folder);
            string preview = "";
            if (root.TryGetProperty("preview", out var previewValue) && previewValue.ValueKind == JsonValueKind.String)
                preview = ResolveProjectFile(folder, previewValue.GetString() ?? "");
            if (!MediaLibrary.IsImage(preview)) preview = "";
            return new(title, path, kind, Path.GetFullPath(folder), preview);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }

    public static string ResolveProjectFile(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return "";
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        return path.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && File.Exists(path) ? path : "";
    }
}
