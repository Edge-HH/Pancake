using System.Diagnostics;
using System.IO.Compression;
using Pancake.Services;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "危险或残缺更新包未被拒绝");
}
string root = Path.Combine(Path.GetTempPath(), "Pancake-update-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string target = Path.Combine(root, "程序 ' & 空格");
    Directory.CreateDirectory(Path.Combine(target, "data"));
    File.WriteAllText(Path.Combine(target, "data", "projects.json"), "user-projects");
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "old-version");
    File.WriteAllText(Path.Combine(target, "z-locked.dll"), "old-locked");
    string Package(string name, params (string Name, string Content)[] extra)
    {
        string path = Path.Combine(root, name + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in new[] { ("Pancake.exe", "exe"), ("Pancake.dll", "new-version"), ("Pancake.pri", "resources"), ("Pancake.runtimeconfig.json", "{}") }.Concat(extra))
        {
            using StreamWriter writer = new(zip.CreateEntry(file.Item1).Open()); writer.Write(file.Item2);
        }
        return path;
    }
    int Apply(string configuration)
    {
        ProcessStartInfo start = new("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "ApplyUpdate.ps1"), "-Configuration", configuration, "-SkipRestart" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(20000)) { process.Kill(); throw new Exception("更新脚本超时"); }
        if (process.ExitCode != 0 && !File.Exists(Path.Combine(Path.GetDirectoryName(configuration)!, "update.log")))
            throw new Exception("更新程序启动失败：" + process.StandardError.ReadToEnd());
        return process.ExitCode;
    }
    string valid = Package("valid", ("Assets/new.txt", "asset"));
    string config = PortableUpdateService.Prepare(valid, target, root, int.MaxValue);
    Check(File.ReadAllText(Path.Combine(target, "Pancake.dll")) == "old-version", "准备更新时不应覆盖正在运行的程序");
    Check(Apply(config) == 0, "更新执行失败");
    Check(File.ReadAllText(Path.Combine(target, "Pancake.dll")) == "new-version", "旧文件未被覆盖");
    Check(File.ReadAllText(Path.Combine(target, "data", "projects.json")) == "user-projects", "更新改变了项目数据");
    Check(File.ReadAllText(Path.Combine(Path.GetDirectoryName(config)!, "backup", "Pancake.dll")) == "old-version", "更新没有保留旧文件备份");
    string failure = PortableUpdateService.Prepare(Package("locked", ("z-locked.dll", "new-locked")), target, root, int.MaxValue);
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "before-failure");
    using (var locked = new FileStream(Path.Combine(target, "z-locked.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        Check(Apply(failure) != 0, "锁定文件应导致更新失败");
    Check(File.ReadAllText(Path.Combine(target, "Pancake.dll")) == "before-failure", "更新失败没有回滚之前覆盖的文件");
    foreach (string bad in new[] { "../escape", "data/projects.json", "C:/escape", "Assets/../../escape", "Assets/test:stream", "Assets/CON", "Assets/a. ", "Pancake.DLL" })
        Reject(() => PortableUpdateService.Prepare(Package(Guid.NewGuid().ToString("N"), (bad, "bad")), target, root, int.MaxValue));
    string missing = Path.Combine(root, "missing.zip");
    using (ZipArchive zip = ZipFile.Open(missing, ZipArchiveMode.Create)) zip.CreateEntry("Pancake.exe");
    Reject(() => PortableUpdateService.Prepare(missing, target, root, int.MaxValue));
    Check(!File.Exists(Path.Combine(root, "escape")), "解压路径越界");
    Console.WriteLine($"PASS: {checks} update extraction, overwrite, data preservation, rollback and invalid-package checks.");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}
