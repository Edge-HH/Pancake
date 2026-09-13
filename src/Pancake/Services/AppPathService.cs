using Pancake.Platforms.Abstraction;

namespace Pancake.Services;

/// <summary>
/// 解析数据根目录，并把旧版本写在程序目录旁的 data 内容迁移到新位置。
/// 便携模式下新旧目录相同，迁移会被自动跳过。
/// </summary>
public static class AppPathService
{
    public static string ResolveDataRoot()
    {
        string root = PlatformServices.AppPaths.DataRoot;
        MigrateLegacyData(root);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 旧版本固定使用 <c>&lt;程序目录&gt;/data</c>。若新目录为空且旧目录存在内容，
    /// 则整体复制过去，用户升级后不会丢失项目。
    /// </summary>
    private static void MigrateLegacyData(string targetRoot)
    {
        string legacyRoot = Path.Combine(AppContext.BaseDirectory, "data");
        if (PathsEqual(legacyRoot, targetRoot) || !Directory.Exists(legacyRoot)) return;
        if (Directory.EnumerateFileSystemEntries(targetRoot).Any()) return;

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(legacyRoot, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(legacyRoot, directory)));
            }

            foreach (string file in Directory.EnumerateFiles(legacyRoot, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(targetRoot, Path.GetRelativePath(legacyRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 迁移失败不应阻止启动：用户仍可从旧目录手动复制数据。
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
