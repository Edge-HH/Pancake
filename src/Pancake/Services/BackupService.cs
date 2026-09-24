using System.IO.Compression;
using System.Text.Json;

namespace Pancake.Services;

public sealed record BackupManifest(int Version, BackupScopes Scopes, DateTime CreatedAt);

public sealed record BackupInfo(BackupManifest Manifest, IReadOnlyList<string> MediaEntries);

/// <summary>
/// 数据备份：把选中范围收进单个 .pbk 压缩包，或从 .pbk 还原。
/// 包内媒体统一改写为条目路径（backgrounds/文件名、web/目录、projects/项目/文件名），
/// 还原时按条目落回本地资源目录，与 MediaLibrary、ProjectStore 的收纳方式保持一致。
/// </summary>
public sealed class BackupService(string dataDirectory)
{
    public const string ManifestEntry = "manifest.json";
    public const string JobsEntry = "jobs.json";
    public const string SettingsEntry = "settings.json";
    public const int ManifestVersion = 1;
    private const long MaximumBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumEntryBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>打包备份。媒体按文件名去重；网页壁纸整个项目目录收进 web/ 条目。</summary>
    public void Create(string destination, BackupScopes scopes, ProjectLibrary library)
    {
        BackupScopes scope = scopes.Sanitize();
        if (scope == BackupScopes.None) throw new InvalidDataException("请至少选择一项备份范围。");
        ProjectLibrary jobs = ProjectStore.Clone(library);
        ProjectLibrary settingsSource = ProjectStore.Clone(library);
        jobs.Settings = new BoardSettingsState();

        Dictionary<string, string> byPath = new(StringComparer.OrdinalIgnoreCase);
        List<(string Source, string Entry)> files = [];
        List<(string Directory, string Entry)> web = [];
        string EntryOfPath(string full)
        {
            if (byPath.TryGetValue(full, out string? entry)) return entry;
            foreach ((string directory, string prefix) in web)
            {
                string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return prefix + "/" + Path.GetRelativePath(directory, full).Replace('\\', '/');
            }
            return "";
        }
        void AddFile(string full)
        {
            full = Path.GetFullPath(full);
            if (byPath.ContainsKey(full) || !File.Exists(full)) return;
            string entry = "backgrounds/" + Path.GetFileName(full);
            if (byPath.ContainsValue(entry)) return;
            byPath.Add(full, entry);
            files.Add((full, entry));
        }
        void AddWebDirectory(string full)
        {
            full = Path.GetFullPath(full);
            if (web.Any(item => string.Equals(item.Directory, full, StringComparison.OrdinalIgnoreCase))) return;
            web.Add((full, "web/" + Path.GetFileName(full)));
        }
        void AddMedia(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full = Path.GetFullPath(path);
            // 网页壁纸的引用是项目目录里的入口文件，整个项目目录都要带走。
            string? owner = WebWallpaperDirectories(dataDirectory)
                .FirstOrDefault(dir => full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (owner is not null) AddWebDirectory(owner);
            else AddFile(full);
        }
        IEnumerable<BackgroundSettings> Styles() =>
            [settingsSource.Settings.TileBackground, settingsSource.Settings.SharedBackground, settingsSource.Settings.ClockBackground, settingsSource.Settings.BoardBackground];
        IEnumerable<string> CurrentPaths()
        {
            foreach (BackgroundSettings style in Styles())
            {
                yield return style.ImagePath;
                foreach (string item in style.Playlist) yield return item;
            }
        }
        List<string> historyImages = [.. new MediaLibrary(dataDirectory).RecentImages()];
        List<string> historyMedia = [.. new MediaLibrary(dataDirectory).RecentMedia()];
        if (scope.Has(BackupScopes.RecentBackgrounds))
            foreach (string path in historyImages.Concat(historyMedia)) AddMedia(path);
        if (scope.Has(BackupScopes.CurrentBackground))
            foreach (string path in CurrentPaths()) AddMedia(path);

        // 项目附件收进 projects/项目/文件名；本机关联路径对还原没有意义，出包时丢弃。
        foreach (ProjectDocument project in jobs.Projects)
        {
            project.LinkedFile = null;
            foreach (AttachmentState asset in ProjectStore.Attachments(project))
            {
                if (string.IsNullOrWhiteSpace(asset.Path)) continue;
                string full = Path.GetFullPath(asset.Path);
                string entry = "projects/" + project.Id.ToString("N") + "/" + Path.GetFileName(full);
                asset.Path = entry;
                if (!files.Any(item => item.Entry == entry)) files.Add((full, entry));
            }
        }

        // 设置里的媒体引用只有随包带走时才改成条目路径；没选媒体范围时保留原路径，还原后仍指向原文件。
        if (scope.Has(BackupScopes.Settings))
            foreach (BackgroundSettings style in Styles())
            {
                // 默认设置没有背景媒体；空路径直接保留，不能送进路径解析。
                if (!string.IsNullOrWhiteSpace(style.ImagePath)) style.ImagePath = EntryOfPath(Path.GetFullPath(style.ImagePath));
                style.Playlist = style.Playlist.Select(item => string.IsNullOrWhiteSpace(item) ? item : EntryOfPath(Path.GetFullPath(item))).ToList();
            }

        ProjectStore.AtomicWrite(destination, stream =>
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Create, true);
            void WriteJson(string entry, object value)
            {
                using Stream target = archive.CreateEntry(entry, CompressionLevel.Fastest).Open();
                JsonSerializer.Serialize(target, value, ProjectStore.JsonOptions);
            }
            WriteJson(ManifestEntry, new BackupManifest(ManifestVersion, scope, DateTime.Now));
            if (scope.Has(BackupScopes.Jobs)) WriteJson(JobsEntry, jobs);
            if (scope.Has(BackupScopes.Settings)) WriteJson(SettingsEntry, settingsSource.Settings);
            if (scope.Has(BackupScopes.RecentBackgrounds))
            {
                // 历史索引存条目路径，还原时映射回本地资源；不在包内的旧引用出包时就丢掉。
                WriteJson("backgrounds/recent-images.json", historyImages.Select(path => EntryOfPath(Path.GetFullPath(path))).Where(entry => entry.Length > 0).ToList());
                WriteJson("backgrounds/recent-media.json", historyMedia.Select(path => EntryOfPath(Path.GetFullPath(path))).Where(entry => entry.Length > 0).ToList());
            }
            foreach ((string source, string entry) in files)
                if (File.Exists(source)) archive.CreateEntryFromFile(source, entry, CompressionLevel.Fastest);
                else throw new FileNotFoundException("背景媒体或图片附件不存在，无法完整备份。", source);
            foreach ((string directory, string entry) in web) CreateDirectoryEntry(archive, directory, entry);
        });
    }

    /// <summary>读取备份清单：创建时间、备份范围与媒体数量，供还原前确认。</summary>
    public BackupInfo Read(string source)
    {
        using ZipArchive archive = ZipFile.OpenRead(source);
        CheckEntries(archive);
        BackupManifest manifest = ReadJson<BackupManifest>(archive, ManifestEntry) ?? throw new InvalidDataException("备份缺少清单。");
        if (manifest.Version != ManifestVersion) throw new InvalidDataException("此备份需要其他版本的软件。");
        List<string> media = archive.Entries.Select(entry => entry.FullName)
            .Where(name => (name.StartsWith("backgrounds/") || name.StartsWith("web/")) && !name.EndsWith('/') && !name.EndsWith(".json"))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new BackupInfo(manifest, media);
    }

    /// <summary>还原备份。范围以存档为准：收了什么还原什么，缺的类别保留现状。</summary>
    public void Restore(string source)
    {
        using ZipArchive archive = ZipFile.OpenRead(source);
        CheckEntries(archive);
        BackupManifest manifest = ReadJson<BackupManifest>(archive, ManifestEntry) ?? throw new InvalidDataException("备份缺少清单。");
        if (manifest.Version != ManifestVersion) throw new InvalidDataException("此备份需要其他版本的软件。");
        BackupScopes scope = manifest.Scopes.Sanitize();
        if (scope == BackupScopes.None) throw new InvalidDataException("备份没有可还原的内容。");

        // 先把包内媒体落回本地资源目录，再解析清单和设置；引用按条目路径映射成新的本地路径。
        Dictionary<string, string> imported = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            if (name.EndsWith('/') || name is ManifestEntry or JobsEntry or SettingsEntry || name.EndsWith(".json") || name.StartsWith("projects/")) continue;
            string destination = name.StartsWith("web/")
                ? Path.Combine(dataDirectory, "web-wallpapers", name[4..].Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(dataDirectory, "backgrounds", Path.GetFileName(name));
            ExtractTo(entry, destination);
            imported[name] = destination;
        }
        string? Map(string? entry) => entry is not null && imported.TryGetValue(entry, out string? local) ? local : entry;

        ProjectStore store = new(dataDirectory);
        BoardSettingsState? settings = scope.Has(BackupScopes.Settings) ? ReadJson<BoardSettingsState>(archive, SettingsEntry) : null;
        if (settings is not null) RewriteMediaPaths(settings, Map);
        if (scope.Has(BackupScopes.Jobs) && ReadJson<ProjectLibrary>(archive, JobsEntry) is { } library)
        {
            library.Version = 1;
            library.Settings = settings ?? store.Load().Settings;
            foreach (ProjectDocument project in library.Projects)
            {
                string assets = store.AssetDirectory(project.Id);
                foreach (AttachmentState asset in ProjectStore.Attachments(project))
                {
                    if (string.IsNullOrWhiteSpace(asset.Path)) continue;
                    string entry = asset.Path.Replace('\\', '/');
                    ZipArchiveEntry? file = archive.GetEntry(entry) ?? throw new InvalidDataException($"备份缺少图片：{asset.Name}");
                    ExtractTo(file, Path.Combine(assets, Path.GetFileName(entry)));
                    asset.Path = Path.Combine(assets, Path.GetFileName(entry));
                }
            }
            store.Save(library);
        }
        else if (settings is not null)
        {
            ProjectLibrary existing = store.Load();
            existing.Settings = settings;
            store.Save(existing);
        }

        if (scope.Has(BackupScopes.RecentBackgrounds))
        {
            List<string> images = (ReadJson<List<string>>(archive, "backgrounds/recent-images.json") ?? []).Select(Map).OfType<string>().ToList();
            List<string> media = (ReadJson<List<string>>(archive, "backgrounds/recent-media.json") ?? []).Select(Map).OfType<string>().ToList();
            new MediaLibrary(dataDirectory).ReplaceHistory(images, media);
        }
    }

    /// <summary>自动备份的文件名按时间命名，字典序即时间序，便于按保留份数清理最旧备份。</summary>
    public static string AutoBackupFileName(DateTime now) => $"auto-{now:yyyyMMddHHmmss}.pbk";

    /// <summary>自动备份间隔小时数归一化：非法值回到 24 小时，范围 1 小时到 1 年。</summary>
    public static double NormalizeIntervalHours(double hours) =>
        double.IsFinite(hours) ? Math.Clamp(hours, 1, 24 * 365) : 24;

    /// <summary>自动备份是否到期：从未备份过立即执行，之后至少间隔设定的小时数。</summary>
    public static bool IsDue(DateTime? lastRunAt, double intervalHours, DateTime now) =>
        lastRunAt is not { } last || (now - last).TotalHours >= NormalizeIntervalHours(intervalHours);

    /// <summary>自动备份按容量上限清理：超出份数的最旧备份被删除，至少保留最新一份。</summary>
    public static int PruneAutoBackups(string directory, int maxCount)
    {
        if (maxCount <= 0 || !Directory.Exists(directory)) return 0;
        List<string> files = Directory.EnumerateFiles(directory, "auto-*.pbk").Order(StringComparer.OrdinalIgnoreCase).ToList();
        int removed = 0;
        while (files.Count > Math.Max(1, maxCount))
        {
            try { File.Delete(files[0]); removed++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { break; }
            files.RemoveAt(0);
        }
        return removed;
    }

    internal static IEnumerable<string> WebWallpaperDirectories(string dataDirectory)
    {
        string root = Path.Combine(dataDirectory, "web-wallpapers");
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root) : [];
    }

    private static void RewriteMediaPaths(BoardSettingsState settings, Func<string?, string?> map)
    {
        foreach (BackgroundSettings style in new[] { settings.TileBackground, settings.SharedBackground, settings.ClockBackground, settings.BoardBackground })
        {
            style.ImagePath = map(style.ImagePath) ?? "";
            style.Playlist = style.Playlist.Select(item => map(item) ?? item).ToList();
        }
    }

    private static void CheckEntries(ZipArchive archive)
    {
        if (archive.Entries.Count > 200000) throw new InvalidDataException("备份包含过多文件。");
        long total = 0;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!IsSafeEntry(entry.FullName)) throw new InvalidDataException("备份包含非法路径。");
            if (!seen.Add(entry.FullName)) throw new InvalidDataException("备份包含重复路径。");
            if (entry.Length > MaximumEntryBytes) throw new InvalidDataException("备份里有超大文件。");
            total += entry.Length;
            if (total > MaximumBytes) throw new InvalidDataException("备份超过支持的大小（4 GB）。");
        }
    }

    private static bool IsSafeEntry(string name) => name.Length > 0 && !name.Contains('\\') && !name.Contains(':') && !name.StartsWith('/')
        && name.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..")
        && (name is ManifestEntry or JobsEntry or SettingsEntry
            || name.StartsWith("backgrounds/", StringComparison.Ordinal)
            || name.StartsWith("web/", StringComparison.Ordinal)
            || name.StartsWith("projects/", StringComparison.Ordinal));

    private static void ExtractTo(ZipArchiveEntry entry, string destination)
    {
        string full = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        entry.ExtractToFile(full, true);
    }

    private static T? ReadJson<T>(ZipArchive archive, string entry) where T : class
    {
        ZipArchiveEntry? manifest = archive.GetEntry(entry);
        if (manifest is null) return null;
        if (manifest.Length > 64 * 1024 * 1024) throw new InvalidDataException("备份清单过大。");
        using Stream stream = manifest.Open();
        return JsonSerializer.Deserialize<T>(stream, ProjectStore.JsonOptions);
    }

    private static void CreateDirectoryEntry(ZipArchive archive, string source, string entry)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("背景媒体包含链接，无法完整备份。");
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("背景媒体包含非法路径。");
            archive.CreateEntryFromFile(file, entry + "/" + relative, CompressionLevel.Fastest);
        }
    }
}
