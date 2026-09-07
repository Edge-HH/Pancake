using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Pancake.Services;

/// <summary>先校验解压到隔离目录，再由独立进程等待主程序退出后覆盖和回滚。</summary>
public static class PortableUpdateService
{
    public static string Prepare(string package, string applicationDirectory, string stagingRoot, int processId)
    {
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
        string stage = Path.Combine(Path.GetFullPath(stagingRoot), Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(stage, "payload");
        Directory.CreateDirectory(payload);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive archive = ZipFile.OpenRead(package))
        {
            long total = 0;
            if (archive.Entries.Count > 10000) throw new InvalidDataException("更新包文件数量异常。");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
                ValidateRelativePath(relative);
                if (!paths.Add(relative)) throw new InvalidDataException("更新包中存在重复路径。");
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("更新包不能包含符号链接。");
                total = checked(total + entry.Length);
                if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("更新包解压体积过大。");
                ValidateTarget(target, relative);
                string destination = Path.Combine(payload, relative);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) Directory.CreateDirectory(destination);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
        }
        foreach (string required in new[] { "Pancake.exe", "Pancake.dll", "Pancake.pri", "Pancake.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(payload, required))) throw new InvalidDataException("更新包缺少必要文件：" + required);
        // 在关闭应用前发现权限问题，用户仍可继续使用当前版本。
        string probe = Path.Combine(target, ".pancake-update-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        File.Copy(Path.Combine(AppContext.BaseDirectory, "ApplyUpdate.ps1"), Path.Combine(stage, "ApplyUpdate.ps1"));
        string configuration = Path.Combine(stage, "update.json");
        File.WriteAllText(configuration, JsonSerializer.Serialize(new { Target = target, Payload = payload, ParentProcessId = processId }));
        return configuration;
    }

    public static void Launch(string configuration)
    {
        ProcessStartInfo start = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(Path.GetDirectoryName(configuration)!, "ApplyUpdate.ps1"), "-Configuration", configuration })
            start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new IOException("无法启动更新程序。");
    }

    internal static void ValidateRelativePath(string path)
    {
        string[] segments = path.Split('/');
        if (Path.IsPathRooted(path) || segments.Any(s => string.IsNullOrWhiteSpace(s) || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny("<>:\"|?*\\".ToCharArray()) >= 0 || s.Any(char.IsControl))
            || segments[0].Equals("data", StringComparison.OrdinalIgnoreCase)
            || segments.Any(s => System.Text.RegularExpressions.Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("更新包包含无效路径或试图覆盖用户数据。");
    }

    private static void ValidateTarget(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新路径越界。");
        while (path.Length >= root.Length)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新目录包含链接，无法安全覆盖。");
            path = Path.GetDirectoryName(path) ?? "";
        }
    }
}
