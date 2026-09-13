using Pancake.Platforms.Abstraction.Services;
using Pancake.Platforms.Abstraction.Stubs;

namespace Pancake.Platforms.Linux;

/// <summary>
/// Linux 数据目录：优先 XDG_DATA_HOME，未设置时用 ~/.local/share，与桌面环境约定一致。
/// </summary>
public sealed class LinuxAppPathsService : IAppPathsService
{
    public LinuxAppPathsService()
    {
        string programRoot = AppContext.BaseDirectory;
        string portableRoot = Path.Combine(programRoot, "data");

        // 与 Windows 相同：程序目录旁已有 data 时保持便携模式，方便整目录拷贝。
        if (Directory.Exists(portableRoot) && IsWritable(portableRoot))
        {
            IsPortable = true;
            DataRoot = portableRoot;
            CacheRoot = Path.Combine(portableRoot, "cache");
            return;
        }

        string? xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataHome = string.IsNullOrWhiteSpace(xdgData) ? Path.Combine(home, ".local", "share") : xdgData;
        DataRoot = Path.Combine(dataHome, "pancake");

        string? xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        CacheRoot = string.IsNullOrWhiteSpace(xdgCache) ? Path.Combine(home, ".cache", "pancake") : Path.Combine(xdgCache, "pancake");
    }

    public string DataRoot { get; }

    public string CacheRoot { get; }

    public bool IsPortable { get; }

    private static bool IsWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>Linux 不提供网页壁纸与壁纸引擎探测，更新只提示新版本。</summary>
public sealed class LinuxPlatformFeatures : IPlatformFeatures
{
    public bool WebWallpaper => false;

    public bool WallpaperEngine => false;

    public bool SelfUpdate => false;
}
