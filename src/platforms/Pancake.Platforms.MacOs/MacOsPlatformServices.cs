using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.MacOs;

/// <summary>
/// macOS 数据目录：~/Library/Application Support/Pancake。
/// 应用包内不允许写入，因此便携模式只在程序目录可写时启用。
/// </summary>
public sealed class MacOsAppPathsService : IAppPathsService
{
    public MacOsAppPathsService()
    {
        string programRoot = AppContext.BaseDirectory;
        string portableRoot = Path.Combine(programRoot, "data");

        if (Directory.Exists(portableRoot) && IsWritable(portableRoot))
        {
            IsPortable = true;
            DataRoot = portableRoot;
            CacheRoot = Path.Combine(portableRoot, "cache");
            return;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DataRoot = Path.Combine(home, "Library", "Application Support", "Pancake");
        CacheRoot = Path.Combine(home, "Library", "Caches", "Pancake");
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

/// <summary>macOS 不提供网页壁纸与壁纸引擎探测，更新只提示新版本。</summary>
public sealed class MacOsPlatformFeatures : IPlatformFeatures
{
    public bool WebWallpaper => false;

    public bool WallpaperEngine => false;

    public bool SelfUpdate => false;
}
