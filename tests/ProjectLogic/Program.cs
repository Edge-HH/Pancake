using System.IO.Compression;
using System.Text.Json;
using Pancake.Services;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException) { rejected = true; }
    Check(rejected, message);
}
string root = Path.Combine(Path.GetTempPath(), "Pancake-project-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    ProjectStore store = new(Path.Combine(root, "local"));
    ProjectLibrary library = store.Load();
    Check(library.Projects.Count == 0, "新安装不应填入示例作业");
    ProjectDocument first = ProjectStore.Create(library, false);
    first.Subjects.Add(new SubjectState
    {
        Name = "语文", Width = 430, Height = 320, X = 48, Y = 96, InkCoordinateVersion = 1,
        AccentHex = "#123456", IsAccentExplicit = true,
        Entries = [new HomeworkState { Content = "第一段\n第二段", RtfContent = @"{\rtf1\ansi text}", Attachments = [], FontFallbacks = [new FontFallbackState { Start = 0, Length = 2, Family = "Unavailable Classroom Font" }] }],
        InkStrokes = [new InkStrokeState { Color = "#FF112233", Thickness = 5, Points = [new PointState { X = 2, Y = 3 }, new PointState { X = 200, Y = 150 }] }]
    });
    string picture = Path.Combine(root, "source.png"); File.WriteAllBytes(picture, [1, 2, 3, 4]);
    first.Subjects[0].Entries[0].Attachments.Add(new AttachmentState { Name = "图片", Kind = "图片", Path = store.CopyAttachment(first.Id, picture), Rotation = 90, Scale = 2, OffsetX = 7, PositionY = 35 });
    File.Delete(picture);
    store.Save(library);
    ProjectLibrary restored = store.Load();
    Check(restored.ActiveProjectId == first.Id && restored.Projects[0].Subjects[0].X == 48, "项目与布局未恢复");
    Check(File.Exists(restored.Projects[0].Subjects[0].Entries[0].Attachments[0].Path), "附件未独立收纳");
    Check(!File.ReadAllText(store.LibraryPath).Contains(root.Replace("\\", "\\\\")), "本地附件应使用相对路径");
    ProjectDocument reset = ProjectStore.Create(library, true);
    Check(reset.Name != first.Name && reset.Subjects.Count == 1, "同日项目名或模板错误");
    Check(reset.Subjects[0].Entries.Count == 0 && reset.Subjects[0].InkStrokes.Count == 0 && reset.Subjects[0].AccentHex == "#123456", "重置作业误删配色或未清内容");
    Check(first.Subjects[0].Entries.Count == 1, "新项目污染原项目");
    Check(ProjectStore.Create(library, false).Subjects.Count == 0, "清空布局不应保留磁贴");
    string package = Path.Combine(root, "test.pch");
    PchPackageService service = new(store); service.Save(first, package);
    ProjectStore secondStore = new(Path.Combine(root, "other"));
    ProjectDocument imported = new PchPackageService(secondStore).Import(package);
    Check(imported.Id != first.Id && imported.LinkedFile == package, "导入应产生本地 ID 并关联原文件");
    AttachmentState image = imported.Subjects[0].Entries[0].Attachments[0];
    Check(File.ReadAllBytes(image.Path).SequenceEqual(new byte[] { 1, 2, 3, 4 }) && image.Rotation == 90 && image.OffsetX == 7, "图片或变换未往返");
    Check(imported.Subjects[0].Entries[0].RtfContent == first.Subjects[0].Entries[0].RtfContent && imported.Subjects[0].InkStrokes[0].Points[1].Y == 150, "RTF 或笔迹未往返");
    Check(imported.Subjects[0].Entries[0].FontFallbacks.Single().Family == "Unavailable Classroom Font", "缺失字体原始名称未随作业包往返");
    imported.Name = "重命名"; new PchPackageService(secondStore).Save(imported, package);
    Check(service.Import(package).Name == "重命名", "重复保存未更新包");
    byte[] before = File.ReadAllBytes(package);
    File.Delete(image.Path);
    Reject(() => new PchPackageService(secondStore).Save(imported, package), "缺失附件应阻止保存");
    Check(File.ReadAllBytes(package).SequenceEqual(before), "失败保存损坏原包");
    Reject(() => service.Save(first, store.DirectoryPath), "不可写目标应报告失败");
    Check(File.ReadAllBytes(package).SequenceEqual(before), "失败写入污染其他文件");
    foreach (string bad in new[] { "../escape", "assets/../../escape", "assets/.. ", "assets/x:stream", "assets/abc/def", "assets/CON", "assets/x\\..\\z" })
        Check(!PchPackageService.IsAssetPath(bad), "危险资源路径被接受：" + bad);
    string malicious = Path.Combine(root, "bad.pch");
    using (ZipArchive zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) zip.CreateEntry("../escape");
    Reject(() => service.Import(malicious), "ZIP 越界路径被接受");
    string broken = Path.Combine(root, "broken.pch"); File.WriteAllText(broken, "broken");
    Reject(() => service.Import(broken), "损坏包被接受");
    string incomplete = Path.Combine(root, "missing.pch");
    var noAssets = ProjectStore.Clone(first); noAssets.Subjects[0].Entries[0].Attachments[0].Path = "assets/" + new string('a', 32) + ".png";
    using (ZipArchive zip = ZipFile.Open(incomplete, ZipArchiveMode.Create))
    using (Stream s = zip.CreateEntry("project.json").Open()) JsonSerializer.Serialize(s, noAssets);
    Reject(() => service.Import(incomplete), "包内缺少附件仍然导入");
    noAssets.Version = 2;
    Reject(() => ProjectValidation.Validate(noAssets, true), "未知版本被接受");
    SubjectState oldInk = new() { Width = 430, Height = 326, InkStrokes = [new InkStrokeState { Thickness = 5, Points = [new PointState { X = 1000, Y = 600 }] }] };
    ProjectStore.MigrateInk(oldInk);
    Check(oldInk.InkStrokes[0].Points[0].X == 417 && oldInk.InkStrokes[0].Points[0].Y == 313 && oldInk.InkStrokes[0].TipScaleX == .4, "旧笔迹转换错误");
    ProjectStore.MigrateInk(oldInk);
    Check(oldInk.InkStrokes[0].Points[0].X == 417, "笔迹被重复迁移");
    Check(InkGeometry.HitTest([(0, 0), (100, 0)], 50, 3, 5) && !InkGeometry.HitTest([(0, 0), (100, 0)], 50, 30, 5), "橡皮擦线段距离错误");
    foreach (var size in new[] { (1920d, 1080d), (1080d, 1920d), (1600d, 1200d), (1200d, 1600d), (1080d, 1080d), (700d, 1400d) })
    {
        var layout = ExportLayout.Arrange([(430, 2000), (300, 500), (600, 360), (400, 750)], size.Item1, size.Item2, 100);
        Check(layout.All(p => ExportLayout.InBounds(p, size.Item1, size.Item2, 100)), "自动排版越界或遮住标题");
        Check(!layout.SelectMany((p, i) => layout.Skip(i + 1).Select(q => ExportLayout.Overlap(p, q))).Any(v => v), "自动排版重叠");
    }
    Check(ExportLayout.Arrange([], 1000, 1000, 100).Count == 0, "空导出错误");
    ProjectStore emptyAttachmentStore = new(Path.Combine(root, "legacy-empty-attachment"));
    Directory.CreateDirectory(emptyAttachmentStore.DirectoryPath);
    AppState legacyWithEmptyAttachment = new()
    {
        Subjects =
        [
            new SubjectState
            {
                Name = "旧科目", Width = 430, Height = 320,
                Entries = [new HomeworkState { Attachments = [new AttachmentState { Name = "未完成图片", Kind = "图片", Path = "" }] }]
            }
        ]
    };
    File.WriteAllText(Path.Combine(emptyAttachmentStore.DirectoryPath, "pancake.json"), JsonSerializer.Serialize(legacyWithEmptyAttachment));
    ProjectLibrary migratedWithEmptyAttachment = emptyAttachmentStore.Load();
    Check(migratedWithEmptyAttachment.Projects.Single().Subjects.Single().Entries.Single().Attachments.Single().Path == "", "空附件占位不应阻止旧看板迁移");
    ProjectStore legacyStore = new(Path.Combine(root, "legacy")); Directory.CreateDirectory(legacyStore.DirectoryPath);
    AppState legacy = new() { Subjects = [new SubjectState { Name = "旧科目", Width = 430, Height = 320 }] };
    File.WriteAllText(Path.Combine(legacyStore.DirectoryPath, "pancake.json"), JsonSerializer.Serialize(legacy));
    ProjectLibrary migrated = legacyStore.Load();
    Check(migrated.Projects.Count == 1 && migrated.Projects[0].Subjects[0].InkCoordinateVersion == 1, "旧看板迁移失败");
    Check(File.Exists(Path.Combine(legacyStore.DirectoryPath, "pancake.json.before-projects.bak")), "未保留旧备份");
    migrated.Projects.Clear(); migrated.ActiveProjectId = null; legacyStore.Save(migrated);
    Check(legacyStore.Load().Projects.Count == 0, "删除最后项目后重新迁入旧数据");
    Console.WriteLine($"PASS: {checks} project/package/migration/ink/layout checks.");
}
finally
{
    string tempRoot = Path.GetFullPath(Path.GetTempPath());
    if (Path.GetFullPath(root).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}
