using System.Diagnostics;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>使用系统默认程序打开目标；三个桌面平台都由 .NET 的 ShellExecute 支持。</summary>
public sealed class DefaultExternalLauncher : IExternalLauncher
{
    public void OpenPath(string path) => Open(path);

    public void OpenUri(string uri) => Open(uri);

    public bool RestartApplication()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
