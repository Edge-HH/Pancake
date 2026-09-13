namespace Pancake.Platforms.Abstraction.Services;

/// <summary>用系统默认程序打开文件、目录或链接，并支持重启应用。</summary>
public interface IExternalLauncher
{
    void OpenPath(string path);

    void OpenUri(string uri);

    /// <summary>重启当前应用；便携更新在覆盖完成后使用。</summary>
    bool RestartApplication();
}
