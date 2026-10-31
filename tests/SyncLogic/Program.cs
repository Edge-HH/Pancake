using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Pancake.Server;
using Pancake.Services;
using Pancake.Shared;

// 两台模拟大屏 + 网页管理员，驱动桌面端 CloudSyncService 与真实服务端完成完整同步流程。
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; }

// 设置清单：每个键都对应桌面端真实存在的设置，默认值一致；桌面端每个设置都登记为“同步”或“本机设置”，新增设置漏登记时这里会提醒。
JsonObject defaults = JsonSerializer.SerializeToNode(new BoardSettingsState(), ProjectStore.JsonOptions)!.AsObject();
foreach (SettingDescriptor descriptor in SettingsSchema.All)
{
    Check(SettingsSchema.Get(defaults, descriptor.Key) is not null, $"设置 {descriptor.Key} 不存在");
    Check(SettingsSchema.Same(SettingsSchema.Get(defaults, descriptor.Key), descriptor.Default), $"设置 {descriptor.Key} 的默认值与桌面端不一致");
    Check(SettingsSchema.SanitizeValue(descriptor, descriptor.Default) is { } clean && SettingsSchema.Same(clean, descriptor.Default), $"设置 {descriptor.Key} 的默认值能通过校验");
}
void Cover(JsonObject node, string prefix)
{
    foreach (var (key, value) in node)
    {
        string path = prefix + key;
        if (SettingsSchema.Find(path) is not null || SettingsSchema.DeviceLocalKeys.ContainsKey(path)) continue;
        if (value is JsonObject child) Cover(child, path + ".");
        else Check(false, $"设置 {path} 既不同步也没有登记为本机设置");
    }
}
Cover(defaults, "");
var sanitized = SettingsSchema.Sanitize(JsonNode.Parse("""{"GridSize":999,"TileBodyFontSize":20.4,"Theme":"Neon","GridColor":"#abcdef","Lock":{"PasswordHash":"x"},"Unknown":1,"Autofill.Subject.Enabled":true}""")!.AsObject());
Check(sanitized["GridSize"]!.GetValue<double>() == 160 && sanitized["TileBodyFontSize"]!.GetValue<double>() == 20 && !sanitized.ContainsKey("Theme")
    && sanitized["GridColor"]!.GetValue<string>() == "#ABCDEF" && !sanitized.ContainsKey("Lock") && !sanitized.ContainsKey("Unknown") && sanitized["Autofill.Subject.Enabled"]!.GetValue<bool>(), "设置校验：限幅、取整、枚举、颜色与未知键");

string root = Path.Combine(Path.GetTempPath(), "Pancake-sync-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var app = PancakeServer.Build([
    "--Urls=http://127.0.0.1:0",
    $"--ConnectionStrings:Pancake=Data Source={Path.Combine(root, "server.db")};Pooling=False",
    $"--Pancake:AssetsPath={Path.Combine(root, "server-assets")}",
    "--Logging:LogLevel:Default=Warning"
]);
await app.StartAsync();
try
{
    string baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var admin = new HttpClient { BaseAddress = new Uri(baseUrl) };
    var login = await (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "admin"))).Content.ReadFromJsonAsync<JsonObject>();
    admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!["accessToken"]!.GetValue<string>());
    var created = await (await admin.PostAsJsonAsync("/api/admin/classes", new CreateClassRequest("初二（1）班"))).Content.ReadFromJsonAsync<JsonObject>();
    Guid classId = created!["id"]!.GetValue<Guid>();

    async Task<(ProjectStore Store, ProjectLibrary Library, CloudSyncService Sync)> Device(string name)
    {
        var code = await (await admin.PostAsync($"/api/classes/{classId}/pairing-code", null)).Content.ReadFromJsonAsync<JsonObject>();
        var store = new ProjectStore(Path.Combine(root, name, "data"));
        var library = store.Load();
        var sync = new CloudSyncService(store);
        var paired = await sync.PairAsync(baseUrl, code!["code"]!.GetValue<string>(), name);
        Check(paired is not null, "配对成功");
        library.Settings.CloudSync = new CloudSyncSettings { ServerUrl = baseUrl.TrimEnd('/'), ClassId = paired!.ClassId, ClassName = paired.ClassName, DeviceToken = paired.DeviceToken };
        return (store, library, sync);
    }
    CloudSyncHooks Hooks(string name, Func<Guid, bool>? busy = null, List<Guid>? replaced = null, List<Guid>? removed = null) => new()
    {
        IsProjectBusy = busy ?? (_ => false),
        ProjectReplaced = id => replaced?.Add(id),
        ProjectRemoved = id => removed?.Add(id),
        BackgroundDirectory = Path.Combine(root, name, "data", "backgrounds"),
        AppVersion = "test"
    };
    async Task<long> ServerRevision(Guid projectId) =>
        (await admin.GetFromJsonAsync<JsonObject>($"/api/classes/{classId}/projects/{projectId}"))!["revision"]!.GetValue<long>();

    // ---------------------------------------------------------------- A 上传带图片的项目
    var (storeA, libA, syncA) = await Device("大屏A");
    ProjectDocument project = ProjectStore.Create(libA, false);
    string picture = Path.Combine(root, "photo.png"); File.WriteAllBytes(picture, [9, 8, 7, 6, 5]);
    project.Subjects.Add(new SubjectState
    {
        Name = "语文", AccentHex = "#123456", InkCoordinateVersion = 1, X = 48, Y = 48, Width = 430, Height = 320,
        Entries = [new HomeworkState { Content = "背诵", RtfContent = "{\\rtf1 背诵}", Attachments = [new AttachmentState { Name = "课文", Kind = "图片", Path = storeA.CopyAttachment(project.Id, picture) }] }]
    });
    project.Subjects.Add(new SubjectState { Name = "数学", AccentHex = "#654321", InkCoordinateVersion = 1, X = 526, Y = 48, Width = 430, Height = 320 });
    CloudSyncService.Queue(libA, project);
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(syncA.LastSyncError is null, "A 首次同步无错误：" + syncA.LastSyncError);
    Check(project.SyncRevision == 1 && libA.SyncQueue.Count == 0 && project.SyncHash is not null, "A 上传后记录修订号与哈希");
    CloudSyncService.Queue(libA, project);
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(await ServerRevision(project.Id) == 1 && libA.SyncQueue.Count == 0, "内容没变时排队也不会产生新修订");
    var serverDoc = JsonNode.Parse((await admin.GetFromJsonAsync<JsonObject>($"/api/classes/{classId}/projects/{project.Id}"))!["payloadJson"]!.GetValue<string>())!;
    string attachmentPath = serverDoc["Subjects"]![0]!["Entries"]![0]!["Attachments"]![0]!["Path"]!.GetValue<string>();
    Check(attachmentPath.StartsWith("asset:") && !serverDoc.ToJsonString().Contains(root.Replace("\\", "\\\\")), "服务器内容只含资源引用，不含本机路径");
    Check(syncA.Board is { } boardA && boardA.ClassName == "初二（1）班" && libA.Settings.CloudSync.DeviceId == boardA.DeviceId, "同步后取得班务信息与设备 ID");

    // ---------------------------------------------------------------- B 下载，图片落到本地
    var (storeB, libB, syncB) = await Device("大屏B");
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    ProjectDocument copyB = libB.Projects.Single();
    string localImage = copyB.Subjects[0].Entries[0].Attachments[0].Path;
    Check(File.Exists(localImage) && File.ReadAllBytes(localImage).SequenceEqual(new byte[] { 9, 8, 7, 6, 5 }) && localImage.StartsWith(storeB.DirectoryPath), "B 下载图片到自己的项目目录");
    Check(copyB.Subjects[0].Entries[0].RtfContent == "{\\rtf1 背诵}" && copyB.SyncRevision == 1, "B 得到完整内容");
    storeB.Save(libB);
    Check(storeB.Load().Projects.Single().Subjects[0].Entries[0].Attachments[0].Path == localImage, "B 保存后可以重新加载");
    CloudSyncService.Queue(libB, copyB);
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check(await ServerRevision(project.Id) == 1, "B 未修改时不会回传");

    // ---------------------------------------------------------------- 网页编辑 → 设备收到；正在编辑的项目暂不替换
    await admin.PutAsJsonAsync($"/api/classes/{classId}/projects/{project.Id}/content", new
    {
        expectedRevision = 1,
        subjects = new object[]
        {
            new { index = 0, name = "语文", entries = new object[] { new { index = 0, content = "背诵" } } },
            new { index = 1, name = "数学", entries = new object[] { new { content = "练习册 P20" } } }
        }
    });
    List<Guid> replacedA = [];
    await syncA.SynchronizeAsync(libA, Hooks("大屏A", busy: id => id == project.Id, replaced: replacedA));
    Check(libA.Projects.Single(x => x.Id == project.Id).Subjects[1].Entries.Count == 0 && replacedA.Count == 0, "正在编辑的项目不被替换");
    await syncA.SynchronizeAsync(libA, Hooks("大屏A", replaced: replacedA));
    project = libA.Projects.Single(x => x.Id == project.Id);
    Check(project.Subjects[1].Entries.Single().Content == "练习册 P20" && project.SyncRevision == 2 && replacedA.SequenceEqual([project.Id]), "编辑结束后拉取网页修改并通知界面");
    Check(File.Exists(project.Subjects[0].Entries[0].Attachments[0].Path), "替换后附件仍可用");

    // ---------------------------------------------------------------- 冲突一：两台同时修改不同科目 → 自动合并
    project.Subjects[0].Entries[0].Content = "A 的修改"; project.Subjects[0].Entries[0].RtfContent = "";
    CloudSyncService.Queue(libA, project);
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    copyB = libB.Projects.Single(x => x.Id == project.Id);
    copyB.Subjects[1].Entries[0].Content = "B 的修改";
    CloudSyncService.Queue(libB, copyB);
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check(await ServerRevision(project.Id) == 3, "B 先上传");
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    var notices = syncA.TakeNotices();
    ProjectDocument merged = libA.Projects.Single(x => x.Id == project.Id);
    var mergedServer = JsonNode.Parse((await admin.GetFromJsonAsync<JsonObject>($"/api/classes/{classId}/projects/{project.Id}"))!["payloadJson"]!.GetValue<string>())!;
    Check(notices.Any(x => x.Contains("自动合并")) && !libA.Projects.Any(x => x.Name.Contains("本机副本"))
        && merged.Subjects[0].Entries[0].Content == "A 的修改" && merged.Subjects[1].Entries[0].Content == "B 的修改"
        && mergedServer["Subjects"]![0]!["Entries"]![0]!["Content"]!.GetValue<string>() == "A 的修改" && mergedServer["Subjects"]![1]!["Entries"]![0]!["Content"]!.GetValue<string>() == "B 的修改"
        && await ServerRevision(project.Id) == 4 && File.Exists(merged.Subjects[0].Entries[0].Attachments[0].Path), "不同科目的修改自动合并并上传");

    // ---------------------------------------------------------------- 冲突二：同一科目两边都改 → 采用服务器版本并保留本机副本
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    copyB = libB.Projects.Single(x => x.Id == project.Id);
    project = merged;
    project.Subjects[1].Entries[0].Content = "A 再改";
    CloudSyncService.Queue(libA, project);
    copyB.Subjects[1].Entries[0].Content = "B 再改";
    CloudSyncService.Queue(libB, copyB);
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    notices = syncA.TakeNotices();
    merged = libA.Projects.Single(x => x.Id == project.Id);
    ProjectDocument conflictCopy = libA.Projects.Single(x => x.Name.Contains("本机副本"));
    Check(merged.Subjects[1].Entries[0].Content == "B 再改" && conflictCopy.Subjects[1].Entries[0].Content == "A 再改", "同一科目冲突后采用服务器版本并保留本机副本");
    Check(notices.Any(x => x.Contains("本机副本")) && conflictCopy.SyncRevision == 1 && File.Exists(conflictCopy.Subjects[0].Entries[0].Attachments[0].Path), "冲突副本已上传且附件独立复制");

    // ---------------------------------------------------------------- 删除：A 删除 → B 移除
    libA.Projects.Remove(conflictCopy);
    CloudSyncService.QueueDelete(libA, conflictCopy);
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    var serverCopy = await admin.GetFromJsonAsync<JsonObject>($"/api/classes/{classId}/projects/{conflictCopy.Id}");
    Check(serverCopy!["deleted"]!.GetValue<bool>() && serverCopy["payloadJson"]!.GetValue<string>().Contains("A 的修改"), "删除同步到服务器且保留内容");
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check(libB.Projects.Any(x => x.Id == conflictCopy.Id) == false, "B 不会下载已删除的项目");
    await admin.DeleteAsync($"/api/classes/{classId}/projects/{project.Id}");
    List<Guid> removedB = [];
    await syncB.SynchronizeAsync(libB, Hooks("大屏B", removed: removedB));
    Check(libB.Projects.Count == 0 && removedB.SequenceEqual([project.Id]), "网页删除后设备移除本地项目");
    storeB.Save(libB);
    syncB.CleanupRemovedAssets();
    Check(!Directory.Exists(Path.GetDirectoryName(storeB.AssetDirectory(project.Id))), "保存后回收被删除项目的附件");
    await admin.PostAsJsonAsync($"/api/classes/{classId}/projects/{project.Id}/restore", new { });

    // ---------------------------------------------------------------- 布局：A 上传（含背景图）→ B 应用
    string wallpaper = Path.Combine(root, "wall.jpg"); File.WriteAllBytes(wallpaper, [1, 1, 2, 3, 5, 8]);
    libA.Settings.LayoutMode = "Board"; libA.Settings.SplitRatio = 0.3; libA.Settings.TileTitleSize = 33;
    libA.Settings.BoardBackground.ImagePath = wallpaper;
    libA.Settings.MicrophoneDeviceId = "A 的麦克风";
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    var layout = await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web);
    Check(layout!.Revision >= 1 && layout.PayloadJson.Contains("\"LayoutMode\":\"Board\"") && layout.PayloadJson.Contains("asset:") && !layout.PayloadJson.Contains("麦克风"), "布局上传含背景引用、不含本机设备设置");
    long layoutRevision = layout.Revision;
    libB.Settings.MicrophoneDeviceId = "B 的麦克风";
    string tokenB = libB.Settings.CloudSync.DeviceToken;
    BoardSettingsState? appliedB = null;
    await syncB.SynchronizeAsync(libB, new CloudSyncHooks { BackgroundDirectory = Path.Combine(root, "大屏B", "data", "backgrounds"), LayoutApplied = s => appliedB = s });
    Check(appliedB is not null && ReferenceEquals(libB.Settings, appliedB), "B 应用服务器布局并通知界面");
    Check(libB.Settings.LayoutMode == "Board" && libB.Settings.SplitRatio == 0.3 && libB.Settings.TileTitleSize == 33, "布局字段已合并");
    Check(libB.Settings.MicrophoneDeviceId == "B 的麦克风" && libB.Settings.CloudSync.DeviceToken == tokenB, "本机设置与集控凭据保持不变");
    Check(File.ReadAllBytes(libB.Settings.BoardBackground.ImagePath).SequenceEqual(new byte[] { 1, 1, 2, 3, 5, 8 }), "背景图下载到 B 的背景目录");
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check((await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!.Revision == layoutRevision, "B 应用后不会把同一布局再传回去");

    // 停靠组件位置按屏幕尺寸自动计算：未手动调整的不同步，手动调整的不带水平坐标；自由布局位置原样同步。
    libB.Settings.Widgets["SplitClock"] = new RegionPlacement { X = 123, Y = 50, Width = 400, Height = 200 };
    libB.Settings.Widgets["SplitComponents"] = new RegionPlacement { X = 77, Y = 300, Width = 300, Height = 60 };
    libB.Settings.CustomizedDockedWidgets = ["SplitComponents"];
    libB.Settings.Widgets["Clock"] = new RegionPlacement { X = 900, Y = 20, Width = 400, Height = 210 };
    string widgetsJson = LayoutSync.Extract(libB.Settings, _ => null);
    var widgets = JsonNode.Parse(widgetsJson)!["Widgets"]!.AsObject();
    Check(!widgets.ContainsKey("SplitClock") && widgets["SplitComponents"]!["X"]!.GetValue<double>() == 0 && widgets["SplitComponents"]!["Y"]!.GetValue<double>() == 300 && widgets["Clock"]!["X"]!.GetValue<double>() == 900, "只同步与屏幕尺寸无关的组件位置");
    libB.Settings.Widgets["SplitClock"].X = 456;  // 换一块屏幕重新居中
    libB.Settings.Widgets["SplitComponents"].X = 88;
    Check(LayoutSync.Extract(libB.Settings, _ => null) == widgetsJson, "自动计算的位置变化不算布局改动");
    libB.Settings.Widgets.Remove("SplitClock"); libB.Settings.Widgets.Remove("SplitComponents"); libB.Settings.Widgets.Remove("Clock");
    libB.Settings.CustomizedDockedWidgets = [];

    // 网页只下发部分字段时，设备补齐其余字段后不会立刻回传
    long beforePartial = (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!.Revision;
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout", new LayoutWriteRequest(beforePartial, """{"TileTitleSize":40}"""));
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check(libB.Settings.TileTitleSize == 40 && (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!.Revision == beforePartial + 1, "部分字段的布局应用后不回传");
    layout = (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!;
    layoutRevision = layout.Revision;
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout", new LayoutWriteRequest(layoutRevision, LayoutSync.Extract(libB.Settings, _ => null)));
    layout = (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!;
    layoutRevision = layout.Revision;
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));

    // 网页下发布局 → A 应用；统一管理时 A 的本地改动被还原
    var payload = JsonNode.Parse(layout.PayloadJson)!.AsObject();
    payload["SplitRatio"] = 0.6; payload["LayoutMode"] = "Split";
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout", new LayoutWriteRequest(layoutRevision, payload.ToJsonString()));
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout/policy", new { policy = LayoutPolicies.Managed });
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(libA.Settings.LayoutMode == "Split" && libA.Settings.SplitRatio == 0.6 && libA.Settings.CloudSync.LayoutPolicy == LayoutPolicies.Managed, "A 应用网页下发的布局");
    libA.Settings.SplitRatio = 0.2;
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(libA.Settings.SplitRatio == 0.6 && syncA.TakeNotices().Any(x => x.Contains("统一管理")), "统一管理时本地布局改动被还原");
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout/policy", new { policy = LayoutPolicies.Off });
    libA.Settings.SplitRatio = 0.25;
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(libA.Settings.SplitRatio == 0.25, "关闭布局同步后保留本机布局");

    // ---------------------------------------------------------------- 公告与实时通知
    using var realtime = new CloudRealtimeClient();
    var received = new TaskCompletionSource<SyncEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    realtime.ConnectionChanged += on => { if (on) connected.TrySetResult(); };
    realtime.EventReceived += e => { if (e.Type == SyncEventTypes.Board) received.TrySetResult(e); };
    realtime.Start(baseUrl, classId, libA.Settings.CloudSync.DeviceToken);
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await admin.PostAsJsonAsync($"/api/classes/{classId}/announcements", new { title = "下午停课", body = "", level = "urgent", pinned = true });
    var ev = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(ev.ClassId == classId, "实时收到公告通知");
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(syncA.Board!.Announcements.Single().Title == "下午停课", "同步后取得公告");
    var devices = await admin.GetFromJsonAsync<JsonArray>($"/api/classes/{classId}/devices");
    Check(devices!.Any(d => d!["deviceName"]!.GetValue<string>() == "大屏A" && d["online"]!.GetValue<bool>() && d["appVersion"]!.GetValue<string>() == "test"), "设备在线并上报版本");

    // 界面应用布局时抛出异常：错误被记录并上报，公告照常更新，不再静默吞掉。
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout/policy", new { policy = LayoutPolicies.Shared });
    long beforeThrow = (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!.Revision;
    await admin.PutAsJsonAsync($"/api/classes/{classId}/layout", new LayoutWriteRequest(beforeThrow, """{"TileTitleSize":44}"""));
    await admin.PostAsJsonAsync($"/api/classes/{classId}/announcements", new { title = "带好校服", body = "明天升旗", level = "info", pinned = false });
    await syncA.SynchronizeAsync(libA, new CloudSyncHooks { BackgroundDirectory = Path.Combine(root, "大屏A", "data", "backgrounds"), AppVersion = "test", LayoutApplied = _ => throw new InvalidOperationException("界面刷新失败") });
    Check(syncA.Board!.Announcements.Count == 2 && syncA.LastSyncError!.Contains("界面刷新失败"), "布局回调异常不影响公告，并记录错误：" + syncA.LastSyncError);
    devices = await admin.GetFromJsonAsync<JsonArray>($"/api/classes/{classId}/devices");
    Check(devices!.Any(d => d!["deviceName"]!.GetValue<string>() == "大屏A" && d["lastError"]!.GetValue<string>().Contains("界面刷新失败")), "错误随设备状态上报");

    // 班务通知只刷新公告：删除后立即撤下；普通公告默认显示，可改为只显示重要公告。
    var notes = await admin.GetFromJsonAsync<JsonArray>($"/api/classes/{classId}/announcements");
    Guid uniformId = notes!.Single(x => x!["title"]!.GetValue<string>() == "带好校服")!["id"]!.GetValue<Guid>();
    DateTime now = DateTime.UtcNow;
    Check(BoardNotices.Visible(syncA.Board!, now, BoardNotices.AllLevels).Select(x => x.Title).SequenceEqual(["下午停课", "带好校服"]), "全部公告按级别排序显示");
    Check(BoardNotices.Visible(syncA.Board!, now, BoardNotices.ImportantOnly).Select(x => x.Title).SequenceEqual(["下午停课"]), "只显示重要公告时过滤普通公告");
    await admin.DeleteAsync($"/api/classes/{classId}/announcements/{uniformId}");
    bool boardChanged = false; syncA.BoardChanged += () => boardChanged = true;
    await syncA.RefreshBoardAsync(libA);
    Check(boardChanged && syncA.Board!.Announcements.Single().Title == "下午停课", "刷新班务后撤下已删除的公告");

    // 定时公告：提前下发，到点由大屏自行显示与撤下。
    DateTime starts = DateTime.UtcNow.AddMinutes(2), ends = DateTime.UtcNow.AddMinutes(5);
    await admin.PostAsJsonAsync($"/api/classes/{classId}/announcements", new { title = "两分钟后", level = "important", pinned = false, startsAt = starts, expiresAt = ends });
    await syncA.RefreshBoardAsync(libA);
    BoardInfo timed = syncA.Board!;
    Check(timed.Upcoming!.Single().Title == "两分钟后" && timed.Announcements.All(x => x.Title != "两分钟后"), "未开始的定时公告放在 Upcoming");
    Check(BoardNotices.Visible(timed, DateTime.UtcNow, null).All(x => x.Title != "两分钟后") && BoardNotices.Visible(timed, starts.AddSeconds(1), null).Any(x => x.Title == "两分钟后")
        && BoardNotices.Visible(timed, ends.AddSeconds(1), null).All(x => x.Title != "两分钟后"), "定时公告按开始与结束时间显示");
    Check(Math.Abs((BoardNotices.NextBoundary(timed, DateTime.UtcNow)!.Value - starts).TotalSeconds) < 1 && Math.Abs((BoardNotices.NextBoundary(timed, starts.AddSeconds(1))!.Value - ends).TotalSeconds) < 1, "按最近的开始或结束时间重绘");
    realtime.Stop();

    // ---------------------------------------------------------------- 按键三方合并
    async Task<LayoutSnapshot> ServerLayout() => (await admin.GetFromJsonAsync<LayoutSnapshot>($"/api/classes/{classId}/layout", web))!;
    async Task<HttpResponseMessage> WebPatch(long baseRevision, Dictionary<string, object> set) =>
        await admin.PatchAsJsonAsync($"/api/classes/{classId}/layout", new { baseRevision, set });
    var (_, libC, syncC) = await Device("大屏C");
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    long r0 = (await ServerLayout()).Revision;
    Check(syncB.LastSyncError is null && syncC.LastSyncError is null && (await ServerLayout()).Revision == r0, "新设备首次同步采用服务器设置，不回传：" + syncB.LastSyncError + syncC.LastSyncError);

    using var layoutEvents = new CloudRealtimeClient();
    var layoutEvent = new TaskCompletionSource<SyncEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    var layoutConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    layoutEvents.ConnectionChanged += on => { if (on) layoutConnected.TrySetResult(); };
    layoutEvents.EventReceived += e => { if (e.Type == SyncEventTypes.Layout && e.SourceDeviceId is not null) layoutEvent.TrySetResult(e); };
    layoutEvents.Start(baseUrl, classId, libC.Settings.CloudSync.DeviceToken);
    await layoutConnected.Task.WaitAsync(TimeSpan.FromSeconds(10));

    libB.Settings.GridColor = "#11223344";
    Check((await WebPatch(r0, new() { ["ToolbarRadius"] = 30 })).IsSuccessStatusCode, "网页按键修改控制窗圆角");
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    var both = JsonNode.Parse((await ServerLayout()).PayloadJson)!;
    Check(libB.Settings.GridColor == "#11223344" && libB.Settings.ToolbarRadius == 30 && both["GridColor"]!.GetValue<string>() == "#11223344" && both["ToolbarRadius"]!.GetValue<double>() == 30,
        "大屏改网格颜色、网页改圆角：两边改动都保留");
    var echo = await layoutEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(echo.SourceDeviceId == libB.Settings.CloudSync.DeviceId && echo.Keys!.SequenceEqual(["GridColor"]), "布局通知带上修改来源设备与改动的键");
    layoutEvents.Stop();
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(libC.Settings.GridColor == "#11223344" && libC.Settings.ToolbarRadius == 30 && syncC.LastAppliedLayoutKeys.Order().SequenceEqual(["GridColor", "ToolbarRadius"]), "其他大屏只应用改动的键");

    // 同一个键两边都改：以服务器为准并提示
    libC.Settings.TileTitleSize = 50;
    Check((await WebPatch((await ServerLayout()).Revision, new() { ["TileTitleSize"] = 60 })).IsSuccessStatusCode, "网页修改标题大小");
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(libC.Settings.TileTitleSize == 60 && syncC.TakeNotices().Any(x => x.Contains("已被替换")), "同一个键两边都改时以服务器为准");

    // 网页用过期的修订号修改刚被别人改过的键：409 并指明冲突的键；改其他键不受影响
    long stale = (await ServerLayout()).Revision;
    libC.Settings.ToolbarScale = 1.5;
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    var conflict = await WebPatch(stale, new() { ["ToolbarScale"] = 0.8 });
    var conflictBody = await conflict.Content.ReadFromJsonAsync<LayoutConflictResponse>(web);
    Check(conflict.StatusCode == System.Net.HttpStatusCode.Conflict && conflictBody!.Keys.SequenceEqual(["ToolbarScale"]), "过期修订改同一个键返回冲突");
    Check((await WebPatch(stale, new() { ["ToolbarIconOnly"] = false })).IsSuccessStatusCode, "过期修订改其他键直接合并");

    // 嵌套设置与校验：补全开关同步，本机词库不动；超范围的值被限幅
    libC.Settings.Autofill.Subject.Subjects.Add(new SubjectSuggestion { Name = "本机科目" });
    Check((await WebPatch((await ServerLayout()).Revision, new() { ["Autofill.Subject.Enabled"] = true, ["GridSize"] = 999, ["Theme"] = "Light", ["Palette"] = "Macaron" })).IsSuccessStatusCode, "网页修改嵌套设置（主题色系随项目保存，班级设置里会被忽略）");
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(libC.Settings.Autofill.Subject.Enabled && libC.Settings.Autofill.Subject.Subjects.Single().Name == "本机科目" && libC.Settings.GridSize == 160
        && libC.Settings.Theme == "Dark" && libC.Settings.Palette == "Vivid" && libC.Settings.ToolbarIconOnly == false, "嵌套设置与限幅后的值都已应用，主题色系不随班级设置改变，本机词库保持不变");
    // 设置原地合并：设置页控件持有的子对象引用继续有效
    BoardSettingsState settingsRef = libC.Settings; BackgroundSettings tileRef = libC.Settings.TileBackground; var widgetsRef = libC.Settings.Widgets;
    Check((await WebPatch((await ServerLayout()).Revision, new() { ["TileBackground"] = JsonNode.Parse("""{"Glass":true,"Blur":35}""")!, ["Widgets"] = JsonNode.Parse("""{"Clock":{"X":10,"Y":20,"Width":300,"Height":160}}""")! })).IsSuccessStatusCode, "网页修改磁贴背景与组件位置");
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(ReferenceEquals(settingsRef, libC.Settings) && ReferenceEquals(tileRef, libC.Settings.TileBackground) && ReferenceEquals(widgetsRef, libC.Settings.Widgets)
        && tileRef.Glass && tileRef.Blur == 35 && tileRef.ImageMode == "Zoom" && widgetsRef["Clock"].Width == 300, "设置原地合并，背景只下发部分字段时其余字段保持不变");
    long settled = (await ServerLayout()).Revision;
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    await syncB.SynchronizeAsync(libB, Hooks("大屏B"));
    Check((await ServerLayout()).Revision == settled && libB.Settings.GridSize == 160, "多台大屏应用后不再互相回传");

    // ---------------------------------------------------------------- 定向同步：只读取收到通知的项目
    var newProject = await (await admin.PostAsJsonAsync($"/api/classes/{classId}/projects", new { name = "定向同步", date = (string?)null, useLatestTemplate = false })).Content.ReadFromJsonAsync<JsonObject>();
    Guid newId = newProject!["id"]!.GetValue<Guid>();
    await syncC.SyncProjectsOnlyAsync(libC, [newId], Hooks("大屏C"));
    Check(syncC.LastSyncError is null && libC.Projects.Any(x => x.Id == newId && x.Name == "定向同步"), "定向同步下载新项目：" + syncC.LastSyncError);
    await admin.DeleteAsync($"/api/classes/{classId}/projects/{newId}");
    List<Guid> removedC = [];
    await syncC.SyncProjectsOnlyAsync(libC, [newId], Hooks("大屏C", removed: removedC));
    Check(libC.Projects.All(x => x.Id != newId) && removedC.SequenceEqual([newId]), "定向同步移除已删除项目");
    await syncC.SyncLayoutOnlyAsync(libC, Hooks("大屏C"));
    Check(syncC.LastSyncError is null, "只同步班级设置不报错");

    // 大屏上报已安装字体，网页按并集提供字体选择
    await syncC.SynchronizeAsync(libC, new CloudSyncHooks { BackgroundDirectory = Path.Combine(root, "大屏C", "data", "backgrounds"), Fonts = () => ["楷体", "Segoe UI"] });
    var fonts = await admin.GetFromJsonAsync<List<string>>($"/api/classes/{classId}/fonts");
    Check(fonts!.Contains("楷体") && fonts.Contains("Segoe UI") && fonts.Contains("HarmonyOS Sans SC"), "网页取得大屏上报的字体");

    // ---------------------------------------------------------------- 编辑占用：大屏编辑时网页只读，网页编辑时大屏进不了编辑、上传留在队列
    var lockProject = await (await admin.PostAsJsonAsync($"/api/classes/{classId}/projects", new { name = "占用测试", date = (string?)null, useLatestTemplate = false })).Content.ReadFromJsonAsync<JsonObject>();
    Guid lockId = lockProject!["id"]!.GetValue<Guid>();
    string lockUrl = $"/api/classes/{classId}/projects/{lockId}/edit-lock";
    await syncC.SyncProjectsOnlyAsync(libC, [lockId], Hooks("大屏C"));
    ProjectDocument LockDoc() => libC.Projects.Single(x => x.Id == lockId);
    Check((await syncC.AcquireEditLockAsync(libC.Settings.CloudSync, lockId)).Acquired, "大屏进入编辑前申请到占用");
    Check((await admin.PutAsJsonAsync(lockUrl, new EditLockRequest("tab"))).StatusCode == System.Net.HttpStatusCode.Locked, "大屏编辑时网页拿不到占用");
    await syncC.ReleaseEditLockAsync(libC.Settings.CloudSync, lockId);
    Check((await admin.PutAsJsonAsync(lockUrl, new EditLockRequest("tab"))).IsSuccessStatusCode, "大屏释放后网页可以占用");
    var held = await syncC.AcquireEditLockAsync(libC.Settings.CloudSync, lockId);
    Check(!held.Acquired && held.HeldBy?.Holder == EditLockHolders.User && held.HeldBy.Name == "系统管理员", "网页编辑时大屏进不了编辑模式，并知道是谁在改");
    LockDoc().Name = "占用测试（大屏改）";
    CloudSyncService.Queue(libC, LockDoc());
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(syncC.LastSyncError?.Contains("云端修改中") == true && CloudSyncService.IsPending(libC, lockId), "网页编辑期间大屏的上传留在队列里：" + syncC.LastSyncError);
    await admin.DeleteAsync($"{lockUrl}?clientId=tab");
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(!CloudSyncService.IsPending(libC, lockId) && await ServerRevision(lockId) == 2, "网页结束编辑后大屏补传");

    // 项目配色随项目同步，不改动大屏的班级设置
    (await admin.PutAsJsonAsync($"/api/classes/{classId}/projects/{lockId}/content", new { expectedRevision = 2, subjects = Array.Empty<object>(), theme = "Light", palette = "Macaron" })).EnsureSuccessStatusCode();
    await syncC.SyncProjectsOnlyAsync(libC, [lockId], Hooks("大屏C"));
    Check(LockDoc().Theme == "Light" && LockDoc().Palette == "Macaron" && libC.Settings.Theme == "Dark", "网页改的项目配色随项目下载");

    // 编辑中跳过的更新记下来，结束编辑后补拉；排队但没改动的项目在同一轮拉到新版本
    (await admin.PutAsJsonAsync($"/api/classes/{classId}/projects/{lockId}/content", new { expectedRevision = 3, subjects = Array.Empty<object>(), theme = "Dark" })).EnsureSuccessStatusCode();
    await syncC.SyncProjectsOnlyAsync(libC, [lockId], Hooks("大屏C", busy: id => id == lockId));
    Check(LockDoc().Theme == "Light" && syncC.TakeDeferredProjects().SequenceEqual([lockId]), "编辑中的项目不被替换，并记下来等结束编辑后补拉");
    CloudSyncService.Queue(libC, LockDoc());
    await syncC.SynchronizeAsync(libC, Hooks("大屏C"));
    Check(LockDoc().Theme == "Dark" && !CloudSyncService.IsPending(libC, lockId), "排队但没改动的项目在同一轮补拉到服务器的新版本");

    // 屏幕与分区上报：网页设备列表能读到
    await syncC.ReportScreenAsync(libC.Settings.CloudSync, new DeviceScreenReport(1920, 1080, 1.5, "Split", .4, false, new ScreenRegion(0, 0, 768, 1080), new ScreenRegion(768, 0, 1152, 1080), lockId));
    var screens = await admin.GetFromJsonAsync<JsonArray>($"/api/classes/{classId}/devices");
    Check(screens!.Any(d => d!["screen"]?["board"]?["width"]?.GetValue<double>() == 1152 && d["activeProjectId"]?.GetValue<Guid>() == lockId), "大屏上报屏幕与分区");

    // ---------------------------------------------------------------- 吊销后提示重新配对
    Guid deviceA = libA.Settings.CloudSync.DeviceId!.Value;
    await admin.DeleteAsync($"/api/classes/{classId}/devices/{deviceA}");
    await syncA.SynchronizeAsync(libA, Hooks("大屏A"));
    Check(syncA.CredentialsRejected && syncA.LastSyncError!.Contains("重新配对"), "吊销后提示重新配对");

    Console.WriteLine($"PASS: {checks} sync checks.");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    try { Directory.Delete(root, true); } catch (IOException) { }
}
