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
// 构造标准 PKZIP 多磁盘包，区别于 7-Zip 的 .zip.001 二进制切片。
static Dictionary<string, byte[]> CreateStandardSplitZip(byte[] zip)
{
    const int size = 65536;
    byte[] data = new byte[zip.Length + 4];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, 0x08074b50);
    zip.CopyTo(data, 4);
    int end = data.Length - 22;
    int central = BitConverter.ToInt32(data, end + 16) + 4;
    int count = BitConverter.ToUInt16(data, end + 10);
    void U16(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)value);
    void U32(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), (uint)value);
    int cursor = central;
    for (int i = 0; i < count; i++)
    {
        int local = BitConverter.ToInt32(data, cursor + 42) + 4;
        U16(cursor + 34, local / size); U32(cursor + 42, local % size);
        cursor += 46 + BitConverter.ToUInt16(data, cursor + 28) + BitConverter.ToUInt16(data, cursor + 30) + BitConverter.ToUInt16(data, cursor + 32);
    }
    U16(end + 4, end / size); U16(end + 6, central / size); U32(end + 16, central % size);
    Dictionary<string, byte[]> parts = [];
    for (int offset = 0, disk = 1; offset < data.Length; offset += size, disk++)
        parts["Pancake-standard." + (offset + size >= data.Length ? "zip" : "z" + disk.ToString("00"))] = data[offset..Math.Min(offset + size, data.Length)];
    return parts;
}
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
    Check(!ReleaseUpdateService.IsNewer(new Version(2, 0, 3), new Version(2, 0, 3, 0)), "相同发布与程序集版本不能误报更新");
    Check(ReleaseUpdateService.IsNewer(new Version(2, 0, 4), new Version(2, 0, 3, 0)), "较新发布未识别");
    var githubRelease = new ReleaseSnapshot(new Version(2, 0, 4), "v2.0.4", null);
    Check(ReleaseUpdateService.RecommendGitHub(new(new Version(2, 0, 3), "v2.0.3", null), githubRelease), "Gitee 版本落后时必须建议切换 GitHub");
    Check(!ReleaseUpdateService.RecommendGitHub(githubRelease, githubRelease), "同版本不应误报 Gitee 延迟");
    Check(ReleaseUpdateService.RecommendGitHub(null, githubRelease), "Gitee 无发布时应提示已有 GitHub 版本");
    GitHubReleaseUpdate Parse(params string[] names)
    {
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = "v2.0.4", assets = names.Select(name => new { name, browser_download_url = "https://example.test/" + name })
        }));
        return ReleaseUpdateService.ParseRelease(json.RootElement)!.Update!;
    }
    var splitUpdate = Parse("Pancake-win-x64.zip.003", "Pancake-win-x64.zip.001", "Pancake-win-x64.zip.002", "v2.0.4.zip");
    Check(splitUpdate.Parts.Count == 3 && splitUpdate.Parts[0].Name.EndsWith("001"), "分卷应按数字排序且排除源码包");
    Reject(() => Parse("Pancake-win-x64.zip.001", "Pancake-win-x64.zip.003"));
    Check(Parse("Pancake-win-x64.z02", "Pancake-win-x64.zip", "Pancake-win-x64.z01").Parts.Count == 3, "标准 ZIP 分卷未识别");
    byte[] zipBytes = File.ReadAllBytes(valid);
    var chunks = new Dictionary<string, byte[]>();
    for (int i = 0; i < 3; i++) chunks[splitUpdate.Parts[i].Name] = zipBytes[(zipBytes.Length * i / 3)..(zipBytes.Length * (i + 1) / 3)];
    var downloader = new ReleaseUpdateService(new AssetHandler(chunks));
    string joined = await downloader.DownloadAsync(splitUpdate, root);
    Check(File.ReadAllBytes(joined).SequenceEqual(zipBytes), "下载合并的 ZIP 与原包不一致");
    string splitConfig = PortableUpdateService.Prepare(joined, target, root, int.MaxValue);
    Check(Apply(splitConfig) == 0 && File.ReadAllText(Path.Combine(target, "data", "projects.json")) == "user-projects", "分卷覆盖应保留配置");
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "old-version");
    string sevenSource = Path.Combine(root, "seven-source");
    ZipFile.ExtractToDirectory(valid, sevenSource);
    string seven = Path.Combine(root, "Pancake-win-x64.7z");
    string sevenTool = Path.Combine(AppContext.BaseDirectory, "Tools", "7zip", "x64", "7za.exe");
    await SplitUpdatePackage.RunAsync(sevenTool, ["a", "-t7z", seven, Path.Combine(sevenSource, "*")], default);
    string normalized = await SplitUpdatePackage.NormalizeAsync(seven);
    Check(Apply(PortableUpdateService.Prepare(normalized, target, root, int.MaxValue)) == 0, "7z 转换与覆盖失败");
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "old-version");
    byte[] sevenBytes = File.ReadAllBytes(seven);
    var sevenParts = Parse("Pancake-win-x64.7z.002", "Pancake-win-x64.7z.001");
    var sevenDownloads = new ReleaseUpdateService(new AssetHandler(new()
    {
        ["Pancake-win-x64.7z.001"] = sevenBytes[..(sevenBytes.Length / 2)],
        ["Pancake-win-x64.7z.002"] = sevenBytes[(sevenBytes.Length / 2)..]
    }));
    string sevenJoined = await sevenDownloads.DownloadAsync(sevenParts, root);
    Check(Apply(PortableUpdateService.Prepare(await SplitUpdatePackage.NormalizeAsync(sevenJoined), target, root, int.MaxValue)) == 0, "7z 分卷下载合并与覆盖失败");
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "old-version");
    string unsafeSource = Path.Combine(root, "unsafe-source"); Directory.CreateDirectory(Path.Combine(unsafeSource, "data"));
    File.WriteAllText(Path.Combine(unsafeSource, "data", "projects.json"), "forbidden");
    string unsafeSeven = Path.Combine(root, "unsafe.7z");
    await SplitUpdatePackage.RunAsync(sevenTool, ["a", "-t7z", unsafeSeven, Path.Combine(unsafeSource, "*")], default);
    Reject(() => SplitUpdatePackage.NormalizeAsync(unsafeSeven).GetAwaiter().GetResult());
    var standardParts = CreateStandardSplitZip(File.ReadAllBytes(Package("large", ("Assets/random.txt", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(100000))))));
    var standardUpdate = Parse(standardParts.Keys.ToArray());
    string standard = await new ReleaseUpdateService(new AssetHandler(standardParts)).DownloadAsync(standardUpdate, root);
    Check(Apply(PortableUpdateService.Prepare(await SplitUpdatePackage.NormalizeAsync(standard), target, root, int.MaxValue)) == 0, "标准 z01 ZIP 分卷下载解压与覆盖失败");
    File.WriteAllText(Path.Combine(target, "Pancake.dll"), "old-version");
    // 最后一卷缺失没有序号空洞，必须由解压校验拒绝。
    string truncated = Path.Combine(root, "truncated.zip"); File.WriteAllBytes(truncated, zipBytes[..(zipBytes.Length / 2)]);
    Reject(() => PortableUpdateService.Prepare(truncated, target, root, int.MaxValue));
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

sealed class AssetHandler(Dictionary<string, byte[]> assets) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(assets[Path.GetFileName(request.RequestUri!.AbsolutePath)]) });
}
