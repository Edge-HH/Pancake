using System.Diagnostics;
using System.IO.Compression;

namespace Pancake.Services;

/// <summary>把 7z 和标准分卷 ZIP 转为现有更新器使用的已验证 ZIP，覆盖/回滚只有一条路径。</summary>
public static class SplitUpdatePackage
{
    public static async Task<string> NormalizeAsync(string package, CancellationToken cancellationToken = default)
    {
        if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.ChangeExtension(package, ".z01"))) return package;
        string executable = Path.Combine(AppContext.BaseDirectory, "Tools", "7zip", "x64", "7za.exe");
        string listing = await RunAsync(executable, ["l", "-slt", "-ba", "-sccUTF-8", "-p-", "--", package], cancellationToken);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (string block in listing.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = block.Split('\n').Where(l => l.Contains(" = ")).Select(l => l.Split(" = ", 2)).ToDictionary(p => p[0], p => p[1]);
            if (!fields.TryGetValue("Path", out string? path)) continue;
            PortableUpdateService.ValidateRelativePath(path.Replace('\\', '/').TrimEnd('/'));
            if (!names.Add(path) || names.Count > 10000) throw new InvalidDataException("更新包路径重复或文件过多。");
            if (fields.ContainsKey("Symbolic Link") || fields.ContainsKey("Hard Link") ||
                fields.GetValueOrDefault("Attributes", "").Contains('l') || fields.GetValueOrDefault("Encrypted") == "+")
                throw new InvalidDataException("更新包不能包含链接或加密文件。");
            if (long.TryParse(fields.GetValueOrDefault("Size"), out long size)) total = checked(total + size);
            if (total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("更新包解压体积过大。");
        }
        if (names.Count == 0) throw new InvalidDataException("更新包为空或分卷不完整。");
        string directory = Path.Combine(Path.GetDirectoryName(package)!, "normalized-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await RunAsync(executable, ["x", "-y", "-p-", "-o" + directory, "--", package], cancellationToken);
        foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新包不能包含链接。");
            PortableUpdateService.ValidateRelativePath(Path.GetRelativePath(directory, path).Replace('\\', '/'));
        }
        string normalized = directory + ".zip";
        ZipFile.CreateFromDirectory(directory, normalized, CompressionLevel.NoCompression, false);
        return normalized;
    }

    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        ProcessStartInfo start = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动解压程序。");
        Task<string> output = process.StandardOutput.ReadToEndAsync(token), error = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        string text = await output, errorText = await error;
        if (process.ExitCode != 0) throw new InvalidDataException("更新包解压失败或分卷缺失：" + errorText);
        return text;
    }
}
