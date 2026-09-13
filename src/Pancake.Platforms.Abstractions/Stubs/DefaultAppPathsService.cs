using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>
/// 通用数据目录实现：便携模式优先，否则使用系统用户数据目录。
/// Windows 与 Linux 直接使用该实现，macOS 由平台工程覆盖为 ~/Library/Application Support。
/// </summary>
public class DefaultAppPathsService : IAppPathsService
{
    private const string AppFolderName = "Pancake";

    public DefaultAppPathsService()
    {
        string programRoot = AppContext.BaseDirectory;
        string portableRoot = Path.Combine(programRoot, "data");

        // 旧版本把数据写在程序目录旁；只要该目录存在就继续沿用，保证升级后不丢数据。
        bool wantsPortable = File.Exists(Path.Combine(programRoot, "portable.txt")) || Directory.Exists(portableRoot);
        if (wantsPortable && IsWritable(portableRoot))
        {
            IsPortable = true;
            DataRoot = portableRoot;
            CacheRoot = Path.Combine(portableRoot, "cache");
            return;
        }

        string userData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(userData)) userData = Path.Combine(programRoot, "data");
        DataRoot = Path.Combine(userData, AppFolderName);
        CacheRoot = Path.Combine(Path.GetTempPath(), AppFolderName);
    }

    public string DataRoot { get; }

    public string CacheRoot { get; }

    public bool IsPortable { get; }

    /// <summary>目录可能只读（例如安装在受保护位置），写入探测失败时回退到用户目录。</summary>
    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
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
