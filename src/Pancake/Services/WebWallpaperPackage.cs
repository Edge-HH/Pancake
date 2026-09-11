using System.Text.Json;

namespace Pancake.Services;

public sealed record WebWallpaperPackage(string Directory, string EntryPath, string PropertiesJson)
{
    public static WebWallpaperPackage Load(string entry)
    {
        string path = Path.GetFullPath(entry);
        var folder = new DirectoryInfo(Path.GetDirectoryName(path)!);
        // 只接受已导入的项目及其入口；不会把任意父目录暴露给网页。
        while (folder is not null && folder.Name != "web-wallpapers")
        {
            var project = WallpaperEngineLibrary.ReadProject(folder.FullName);
            if (project is { Kind: "web" } && string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder.FullName, "project.json")));
                string properties = json.RootElement.TryGetProperty("general", out var general)
                    && general.TryGetProperty("properties", out var values) && values.ValueKind == JsonValueKind.Object ? values.GetRawText() : "{}";
                return new(folder.FullName, Path.GetRelativePath(folder.FullName, path), properties);
            }
            folder = folder.Parent;
        }
        throw new InvalidDataException("网页壁纸项目已损坏，请从 Wallpaper Engine 重新导入。");
    }
}
