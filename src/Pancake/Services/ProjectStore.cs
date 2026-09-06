using System.IO.Compression;
using System.Text.Json;

namespace Pancake.Services;

public sealed class ProjectDocument
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    public string? LinkedFile { get; set; }
    public string Theme { get; set; } = "Dark";
    public string Palette { get; set; } = "Vivid";
    public List<SubjectState> Subjects { get; set; } = [];
}

public sealed class ProjectLibrary
{
    public int Version { get; set; } = 1;
    public Guid? ActiveProjectId { get; set; }
    public BoardSettingsState Settings { get; set; } = new();
    public List<ProjectDocument> Projects { get; set; } = [];
}

/// <summary>本地事务快照和项目资源。清单与所有项目一次替换，避免切换时留下半份状态。</summary>
public sealed class ProjectStore(string? directory = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DirectoryPath { get; } = directory ?? Path.Combine(AppContext.BaseDirectory, "data");
    public string LibraryPath => Path.Combine(DirectoryPath, "projects.json");
    public string AssetDirectory(Guid id) => Path.Combine(DirectoryPath, "projects", id.ToString("N"), "assets");

    public ProjectLibrary Load()
    {
        if (File.Exists(LibraryPath))
        {
            ProjectLibrary library = JsonSerializer.Deserialize<ProjectLibrary>(File.ReadAllText(LibraryPath), JsonOptions)
                ?? throw new InvalidDataException("项目清单为空。");
            if (library.Version != 1) throw new InvalidDataException("此项目清单需要其他版本的软件。");
            foreach (ProjectDocument project in library.Projects)
            {
                ProjectValidation.Validate(project, false);
                foreach (AttachmentState asset in Attachments(project))
                {
                    // 旧版可能留下尚未选择文件的图片占位；它没有文件可收纳，但不应阻止看板启动。
                    if (string.IsNullOrWhiteSpace(asset.Path)) continue;
                    if (Path.IsPathRooted(asset.Path)) continue;
                    string full = Path.GetFullPath(Path.Combine(DirectoryPath, asset.Path));
                    string root = Path.GetFullPath(AssetDirectory(project.Id)) + Path.DirectorySeparatorChar;
                    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("本地附件路径越界。");
                    asset.Path = full;
                }
            }
            if (library.Projects.Select(p => p.Id).Distinct().Count() != library.Projects.Count)
                throw new InvalidDataException("项目标识重复。");
            return library;
        }

        ProjectLibrary result = new();
        string legacyPath = Path.Combine(DirectoryPath, "pancake.json");
        if (!File.Exists(legacyPath)) return result;
        AppState legacy = JsonSerializer.Deserialize<AppState>(File.ReadAllText(legacyPath), JsonOptions)
            ?? throw new InvalidDataException("旧看板数据无法读取。");
        result.Settings = legacy.Settings;
        ProjectDocument migrated = Create(result, false);
        migrated.Subjects = legacy.Subjects;
        migrated.Theme = legacy.Settings.Theme;
        migrated.Palette = legacy.Settings.Palette;
        foreach (SubjectState subject in migrated.Subjects)
        {
            subject.IsAccentExplicit = true;
            MigrateInk(subject);
        }
        // 先保留原始文件；缺失附件会使迁移失败，旧文件仍可恢复，不能悄悄丢图。
        if (!File.Exists(legacyPath + ".before-projects.bak")) File.Copy(legacyPath, legacyPath + ".before-projects.bak");
        OwnAttachments(migrated);
        Save(result);
        return result;
    }

    public static void MigrateInk(SubjectState subject)
    {
        if (subject.InkCoordinateVersion >= 1) return;
        // 原画布位于 3px 边框和 (14,10,10,10) 内边距中，Viewbox 使用非等比拉伸。
        double sx = Math.Max(1, subject.Width - 30) / 1000;
        double sy = Math.Max(1, subject.Height - 26) / 600;
        foreach (InkStrokeState stroke in subject.InkStrokes)
        {
            foreach (PointState point in stroke.Points) { point.X = 17 + point.X * sx; point.Y = 13 + point.Y * sy; }
            stroke.TipScaleX *= sx;
            stroke.TipScaleY *= sy;
        }
        subject.InkCoordinateVersion = 1;
    }

    public static ProjectDocument Create(ProjectLibrary library, bool keepLayout)
    {
        ProjectDocument? current = library.Projects.FirstOrDefault(p => p.Id == library.ActiveProjectId);
        string basis = $"{DateTime.Now:M月d日}";
        string name = basis;
        for (int i = 2; library.Projects.Any(p => p.Name == name); i++) name = $"{basis} ({i})";
        ProjectDocument project = new() { Name = name, Theme = library.Settings.Theme, Palette = library.Settings.Palette };
        if (keepLayout && current is not null)
        {
            project.Theme = current.Theme;
            project.Palette = current.Palette;
            project.Subjects = Clone(current.Subjects);
            foreach (SubjectState subject in project.Subjects) { subject.Entries.Clear(); subject.InkStrokes.Clear(); }
        }
        library.Projects.Add(project);
        library.ActiveProjectId = project.Id;
        return project;
    }

    public void Save(ProjectLibrary library)
    {
        foreach (ProjectDocument project in library.Projects) ProjectValidation.Validate(project, false);
        ProjectLibrary snapshot = Clone(library);
        foreach (ProjectDocument project in snapshot.Projects)
            foreach (AttachmentState asset in Attachments(project))
            {
                if (string.IsNullOrWhiteSpace(asset.Path)) continue;
                if (Path.GetFullPath(asset.Path).StartsWith(Path.GetFullPath(AssetDirectory(project.Id)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    asset.Path = Path.GetRelativePath(DirectoryPath, asset.Path);
            }
        AtomicWrite(LibraryPath, stream => JsonSerializer.Serialize(stream, snapshot, JsonOptions));
    }

    public void OwnAttachments(ProjectDocument project)
    {
        foreach (AttachmentState asset in Attachments(project))
        {
            if (string.IsNullOrWhiteSpace(asset.Path)) continue;
            asset.Path = CopyAttachment(project.Id, asset.Path);
        }
    }

    public string CopyAttachment(Guid id, string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("图片附件路径为空。");
        string full = Path.GetFullPath(source);
        if (!File.Exists(full)) throw new FileNotFoundException("图片附件不存在，请恢复后重试。", full);
        string destinationDirectory = AssetDirectory(id);
        if (full.StartsWith(Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return full;
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(full));
        File.Copy(full, destination);
        return destination;
    }

    public void DeleteAssets(Guid id)
    {
        // 路径仅由受控根目录与 Guid 构成，绝不使用项目名称或包里的路径递归删除。
        string root = Path.GetFullPath(Path.Combine(DirectoryPath, "projects")) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, id.ToString("N")));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("无效项目目录。");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public static IEnumerable<AttachmentState> Attachments(ProjectDocument project) =>
        project.Subjects.SelectMany(s => s.Entries).SelectMany(e => e.Attachments);
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions)!;
    internal static void AtomicWrite(string path, Action<Stream> write)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write)) { write(stream); stream.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class ProjectValidation
{
    public static void Validate(ProjectDocument project, bool packaged)
    {
        if (project.Version != 1 || project.Id == Guid.Empty || string.IsNullOrWhiteSpace(project.Name) || project.Name.Length > 200)
            throw new InvalidDataException("不支持的作业文件版本或无效项目资料。");
        if (project.Theme is not ("Dark" or "Light" or "Default") || project.Palette is not ("Vivid" or "Macaron"))
            throw new InvalidDataException("项目主题或色系无效。");
        if (project.Subjects is null || project.Subjects.Count > 1000) throw new InvalidDataException("磁贴数量无效。");
        foreach (SubjectState subject in project.Subjects)
        {
            if (subject.InkCoordinateVersion != 1 || !Finite(subject.X, subject.Y, subject.Width, subject.Height) || subject.Width < 1 || subject.Height < 1)
                throw new InvalidDataException("磁贴布局或笔迹坐标版本无效。");
            Color(subject.AccentHex);
            if (subject.Entries is null || subject.InkStrokes is null) throw new InvalidDataException("磁贴内容缺失。");
            foreach (InkStrokeState stroke in subject.InkStrokes)
            {
                Color(stroke.Color);
                if (!Finite(stroke.Thickness, stroke.TipScaleX, stroke.TipScaleY) || stroke.Thickness <= 0 || stroke.TipScaleX <= 0 || stroke.TipScaleY <= 0 || stroke.Points is null || stroke.Points.Any(p => !Finite(p.X, p.Y)))
                    throw new InvalidDataException("笔迹内容无效。");
            }
            foreach (HomeworkState entry in subject.Entries)
            {
                if (entry.Attachments is null) throw new InvalidDataException("附件清单缺失。");
                if (entry.FontFallbacks is null || entry.FontFallbacks.Count > 10000 ||
                    entry.FontFallbacks.Any(f => f.Start < 0 || f.Length <= 0 || string.IsNullOrWhiteSpace(f.Family) || f.Family.Length > 200))
                    throw new InvalidDataException("字体回退资料无效。");
                foreach (AttachmentState asset in entry.Attachments)
                {
                    if (!Finite(asset.Scale, asset.OffsetX, asset.OffsetY, asset.FrameWidth, asset.ViewportHeight, asset.AspectRatio, asset.Rotation, asset.PositionX, asset.PositionY) || asset.Scale <= 0)
                        throw new InvalidDataException("附件布局无效。");
                    if (packaged && !PchPackageService.IsAssetPath(asset.Path)) throw new InvalidDataException("包内附件路径无效。");
                }
            }
        }
    }
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
    private static void Color(string value)
    {
        if (value is null || (value.Length != 7 && value.Length != 9) || value[0] != '#' || !value.AsSpan(1).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("无效颜色。");
    }
}

/// <summary>可移植作业包。只接受清单和直接位于 assets 下的资源，先完整验证再提交本地项目。</summary>
public sealed class PchPackageService(ProjectStore store)
{
    private const long MaximumBytes = 512L * 1024 * 1024;
    public static bool IsAssetPath(string path) => !string.IsNullOrEmpty(path) &&
        System.Text.RegularExpressions.Regex.IsMatch(path, @"\Aassets/[0-9a-fA-F]{32}(?:\.[a-zA-Z0-9]{1,10})?\z");

    public void Save(ProjectDocument project, string destination)
    {
        ProjectDocument copy = ProjectStore.Clone(project);
        copy.LinkedFile = null;
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (AttachmentState asset in ProjectStore.Attachments(copy))
        {
            if (string.IsNullOrWhiteSpace(asset.Path)) throw new InvalidDataException($"无法保存：图片附件路径为空：{asset.Name}");
            string full = Path.GetFullPath(asset.Path);
            if (!File.Exists(full)) throw new FileNotFoundException("无法保存：图片附件不存在。", full);
            if (!files.TryGetValue(full, out string? relative))
            {
                relative = "assets/" + Guid.NewGuid().ToString("N") + Path.GetExtension(full);
                files.Add(full, relative);
            }
            asset.Path = relative;
        }
        ProjectValidation.Validate(copy, true);
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(copy, ProjectStore.JsonOptions);
        if (manifest.Length > 16 * 1024 * 1024 || files.Count > 10000 || files.Keys.Sum(p => new FileInfo(p).Length) + manifest.Length > MaximumBytes)
            throw new InvalidDataException("作业包超过支持的大小（512 MB）。");
        if (files.ContainsKey(Path.GetFullPath(destination))) throw new IOException("不能用作业包覆盖图片附件。");
        ProjectStore.AtomicWrite(destination, stream =>
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Create, true);
            using (Stream target = archive.CreateEntry("project.json").Open()) target.Write(manifest);
            foreach ((string source, string relative) in files) archive.CreateEntryFromFile(source, relative, CompressionLevel.Fastest);
        });
    }

    public ProjectDocument Import(string source)
    {
        using ZipArchive archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > 10001 || archive.Entries.Sum(e => e.Length) > MaximumBytes)
            throw new InvalidDataException("作业包过大。");
        if (archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
            throw new InvalidDataException("作业包包含重复路径。");
        foreach (ZipArchiveEntry entry in archive.Entries)
            if (entry.FullName != "project.json" && !IsAssetPath(entry.FullName)) throw new InvalidDataException("作业包包含非法路径。");
        ZipArchiveEntry manifest = archive.GetEntry("project.json") ?? throw new InvalidDataException("缺少项目清单。");
        if (manifest.Length > 16 * 1024 * 1024) throw new InvalidDataException("项目清单过大。");
        ProjectDocument project;
        using (Stream stream = manifest.Open()) project = JsonSerializer.Deserialize<ProjectDocument>(stream, ProjectStore.JsonOptions)
            ?? throw new InvalidDataException("项目清单无效。");
        ProjectValidation.Validate(project, true);
        foreach (AttachmentState asset in ProjectStore.Attachments(project))
            if (archive.GetEntry(asset.Path) is null) throw new InvalidDataException($"缺少图片：{asset.Name}");
        project.Id = Guid.NewGuid();
        project.LinkedFile = Path.GetFullPath(source);
        project.LastUsedAt = DateTime.Now;
        string assetDirectory = store.AssetDirectory(project.Id);
        try
        {
            Directory.CreateDirectory(assetDirectory);
            foreach (ZipArchiveEntry entry in archive.Entries.Where(e => IsAssetPath(e.FullName)))
                entry.ExtractToFile(Path.Combine(assetDirectory, entry.FullName[7..]));
            foreach (AttachmentState asset in ProjectStore.Attachments(project)) asset.Path = Path.Combine(assetDirectory, asset.Path[7..]);
            return project;
        }
        catch { store.DeleteAssets(project.Id); throw; }
    }
}
