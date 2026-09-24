using System.IO.Compression;
using System.Text.Json;
using Pancake.Services;

// 旧配置补全可见颜色层；显式清除和独立透明度必须跨保存保持，不得关闭模糊。
var legacySurface = JsonSerializer.Deserialize<BoardSettingsState>("{\"ToolbarGlass\":true,\"TileBackground\":{\"Glass\":true}}")!;
Check(legacySurface.ToolbarBackgroundOpacity == 0.8 && legacySurface.TileBackground.ColorOpacity == 0.8,
    "旧设置应补全控制窗和磁贴颜色层的默认透明度");
legacySurface.ToolbarBackgroundColorCleared = true;
legacySurface.ToolbarBackgroundOpacity = 0.25;
legacySurface.TileBackground.ColorCleared = true;
legacySurface.TileBackground.ColorOpacity = 0.65;
var restoredSurface = JsonSerializer.Deserialize<BoardSettingsState>(JsonSerializer.Serialize(legacySurface))!;
Check(restoredSurface.ToolbarBackgroundColorCleared && restoredSurface.TileBackground.ColorCleared
    && restoredSurface.ToolbarGlass && restoredSurface.TileBackground.Glass,
    "清除颜色后重新加载仍应保留清除状态和毛玻璃开关");
Check(restoredSurface.ToolbarBackgroundOpacity == 0.25 && restoredSurface.TileBackground.ColorOpacity == 0.65,
    "控制窗和磁贴透明度必须分别保存");
Console.WriteLine("PASS: surface color defaults, independent opacity, and clearing preserves glass across serialization.");
if (args.Contains("--surface-only")) return;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string[] vividColors = ["#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF", "#F87171", "#65D46E", "#7567FF"];
foreach (string vivid in vividColors)
{
    string pastel = ColorPalette.ConvertHex(vivid, true);
    Check(pastel != vivid, "每个彩色预设都应支持马卡龙色系");
    Check(ColorPalette.ConvertHex(pastel, true) == pastel, "重复应用不应累计变色");
    Check(ColorPalette.ConvertHex(pastel, false) == vivid, "切回鲜艳应恢复原始颜色");
}
Check(ColorPalette.ConvertHex("#123456", true) == "#123456", "保留自定义颜色");
Check(ColorPalette.ConvertHex("#F7F7F9", true) == "#F7F7F9", "保留中性文字颜色");
Check(ColorPalette.ResolveAccent("#4ADE80", true, true) == "#A8D5BA", "旧项目中误标为手动色的内置色仍应跟随色系");
Check(ColorPalette.ResolveAccent("#123456", true, true) == "#123456", "真正的自定义磁贴颜色不应跟随色系");
const string rtf = @"{\rtf1{\colortbl;\red74\green222\blue128;\red251\green191\blue36;}\cf1 hello\highlight2 world}";
string converted = ColorPalette.ConvertRtf(rtf, true);
Check(converted.Contains(@"\cf1 hello\highlight2 world"), "保留文字和高光格式索引");
Check(ColorPalette.ConvertRtf(converted, false) == rtf, "富文本色板往返应无损");
const string body = @"{\rtf1\red74\green222\blue128}";
Check(ColorPalette.ConvertRtf(body, true) == body, "不修改颜色表之外的内容");

const string current = "\"current\":{\"temperature\":{\"value\":\"29\"},\"weather\":\"1\"}";
using (JsonDocument json = JsonDocument.Parse("{" + current + "}"))
{
    WeatherSnapshot snapshot = XiaomiWeatherService.ParseSnapshot(json.RootElement);
    Check(snapshot.Alerts.Count == 0 && snapshot.Condition == "多云" && snapshot.TemperatureCelsius == 29,
        "无预警响应应正常显示实况");
}
using (JsonDocument json = JsonDocument.Parse("{" + current + ",\"alerts\":[{\"title\":\"强对流黄色预警\",\"detail\":\"防范雷暴大风\"},{\"type\":\"海区大风\",\"level\":\"橙色\"},null]}"))
{
    WeatherSnapshot snapshot = XiaomiWeatherService.ParseSnapshot(json.RootElement);
    Check(snapshot.Alerts.Count == 2 && snapshot.Alerts[0].Detail == "防范雷暴大风" && snapshot.Alerts[1].Title == "海区大风橙色",
        "强对流和海区大风均应保留，缺少标题时使用类型和级别");
}
Console.WriteLine("PASS: palette round trips, rich-text preservation, and extreme weather alerts.");
if (args.Contains("--live"))
{
    WeatherSnapshot live = await new XiaomiWeatherService().GetCurrentAsync("101280601");
    Console.WriteLine($"LIVE: {live.Condition}, {live.TemperatureCelsius} C, {live.Alerts.Count} alerts.");
}


const string whiteText = @"{\rtf1{\colortbl;\red247\green247\blue249;\red74\green222\blue128;}\cf1 old text\cf2 color}";
string lightText = DefaultContentColors.AdaptRtf(whiteText, true);
Check(lightText.Contains(@"\red0\green0\blue0"), "Existing default text becomes black");
Check(lightText.Contains(@"\red74\green222\blue128"), "Colored content remains unchanged");
Check(DefaultContentColors.AdaptRtf(lightText, true, saving: true) == whiteText, "Light editing preserves dark default text");
Check(DefaultContentColors.IsDefaultWhite(245, 245, 247), "Legacy white ink is recognized");
Check(!DefaultContentColors.IsDefaultWhite(248, 113, 113), "Colored ink is preserved");
var gate = new NoiseAlertGate();
Check(gate.ShouldPlay(70, 60, true, 0), "First noise triggers immediately");
Check(!gate.ShouldPlay(70, 60, true, 0.3), "Avoid playback feedback and overlapping sound");
Check(!gate.ShouldPlay(40, 60, true, 0.8), "Quiet input rearms without playing");
Check(gate.ShouldPlay(70, 60, true, 0.9), "New noise must not wait ten seconds");
Check(!gate.ShouldPlay(70, 60, true, 1.8), "Limit continuous reminders");
Check(gate.ShouldPlay(70, 60, true, 3), "Repeat after two seconds of persistent noise");
Check(!gate.ShouldPlay(70, 60, false, 4), "Disabled alert stays silent");
byte[] pcm = NoiseAlertTone.CreatePcm();
Check(pcm.Length == NoiseAlertTone.SampleRate * NoiseAlertTone.DurationSeconds * 2, "Tone is 520 ms");
int audibleRuns = 0;
bool previousAudible = false;
for (int offset = 0; offset < pcm.Length; offset += 480)
{
    bool audible = false;
    for (int i = offset; i < Math.Min(offset + 480, pcm.Length); i += 2)
        audible |= Math.Abs((int)BitConverter.ToInt16(pcm, i)) > 50;
    if (audible && !previousAudible) audibleRuns++;
    previousAudible = audible;
}
Check(audibleRuns == 3, "PCM has exactly three distinct beeps");
BoardSettingsState settings = JsonSerializer.Deserialize<BoardSettingsState>(JsonSerializer.Serialize(new BoardSettingsState { NoiseAlertVolume = .35 }))!;
Check(Math.Abs(settings.NoiseAlertVolume - .35) < .001, "提示音音量应持久化");
Check(GitHubUpdateService.IsInstallable("Pancake-win-x64-2.0.1.zip"), "便携版 Release 应被更新器识别");
Console.WriteLine("PASS: light theme content, immediate noise rearming, and three-beep PCM.");

var defaults = JsonSerializer.Deserialize<BoardSettingsState>("{}")!;
Check(defaults.LayoutMode == "Split" && defaults.ToolbarIconOnly && defaults.UpdateSource == "GitHub" && defaults.GridSize == 48, "旧配置应使用兼容的布局、无字模式与更新源默认值");
var placement = new RegionPlacement { X = 100, Y = 80, Width = 200, Height = 100 };
WidgetLayout.Move(placement, 50, 20, 1000, 800);
Check(placement.X == 150 && placement.Y == 100, "组件拖动应准确累计位移");
WidgetLayout.Resize(placement, 80, 40, 1000, 800);
Check(placement.Width == 280 && placement.Height == 140, "组件缩放应准确累计尺寸");
WidgetLayout.Move(placement, -1000, -1000, 1000, 800);
Check(placement.X == 0 && placement.Y == 0, "组件移动不能越过左上边界");
WidgetLayout.Resize(placement, -1000, -1000, 1000, 800);
Check(placement.Width == 80 && placement.Height == 48, "组件应保留最小可操作尺寸");
var centered = new RegionPlacement { X = 240, Y = 100, Width = 320, Height = 160 };
WidgetLayout.MoveVerticallyCentered(centered, 35, 1000, 800);
Check(centered.X == 340 && centered.Y == 135, "受约束组件只能沿中轴线上下移动");
WidgetLayout.ResizeCentered(centered, 80, 0, 2, 200, 1000, 800);
Check(centered.Width == 400 && centered.Height == 200 && centered.X == 300, "受约束组件应等比缩放并保持中轴居中");
Check(WidgetLayout.CompleteSplit(0) == "Board" && WidgetLayout.CompleteSplit(1) == "Clock" && WidgetLayout.CompleteSplit(.5) == "Split", "分隔条两端应切换单区，中间保留分屏");
defaults.Widgets["FutureComponent"] = placement;
defaults.TileBackground = new() { Color = "#123456", ImageMode = "Fit", Glass = true, Blur = 43.2 };
defaults.InfiniteBoard = true; defaults.ToolbarScale = 1.37; defaults.UpdateSource = "Gitee";
var restored = JsonSerializer.Deserialize<BoardSettingsState>(JsonSerializer.Serialize(defaults))!;
Check(restored.Widgets.ContainsKey("FutureComponent") && restored.TileBackground.Blur == 43.2 && restored.InfiniteBoard && restored.ToolbarScale == 1.37 && restored.UpdateSource == "Gitee", "组件扩展、外观、无限画板与更新源应完整持久化");
var widgetSnapshot = WidgetLayout.Copy(defaults.Widgets);
placement.X = 500;
Check(widgetSnapshot["FutureComponent"].X == 0, "放弃组件编辑所用的快照必须独立于当前模型");
Console.WriteLine("PASS: settings migration, widget movement/resizing, split endpoints, precision and persistence.");

Check(!defaults.ToolbarAutoHide && !defaults.PauseNoiseWhenMinimized && defaults.ToolbarAutoHideSeconds == 5,
    "旧配置保持控制窗常驻和最小化继续监测");
var presentationSettings = JsonSerializer.Deserialize<BoardSettingsState>(JsonSerializer.Serialize(new BoardSettingsState
{
    ToolbarAutoHide = true, ToolbarAutoHideSeconds = 17, ToolbarHideAnimation = "Fly", PauseNoiseWhenMinimized = true
}))!;
Check(presentationSettings.ToolbarAutoHide && presentationSettings.ToolbarAutoHideSeconds == 17 &&
    presentationSettings.ToolbarHideAnimation == "Fly" && presentationSettings.PauseNoiseWhenMinimized, "自动隐藏和最小化暂停设置往返持久化");
Check(!ToolbarAutoHidePolicy.ShouldHide(true, true, false, 4.99, 5) &&
    ToolbarAutoHidePolicy.ShouldHide(true, true, false, 5, 5), "空闲时间到达阈值才隐藏");
Check(!ToolbarAutoHidePolicy.ShouldHide(false, true, false, 100, 5) &&
    !ToolbarAutoHidePolicy.ShouldHide(true, false, false, 100, 5) &&
    !ToolbarAutoHidePolicy.ShouldHide(true, true, true, 100, 5), "关闭开关、设置和编辑模式、持续操作都不得隐藏");
Check(ToolbarAutoHidePolicy.NormalizeDelay(double.NaN) == 5 && ToolbarAutoHidePolicy.NormalizeDelay(-1) == 1 &&
    ToolbarAutoHidePolicy.NormalizeDelay(9999) == 600, "非法等待时间使用可用的默认值或边界");
Check(ToolbarAutoHidePolicy.ExitOffset(20, 300, 100, 100, 1000, 800) == (-121d, 0d), "最近左边框时完整飞出窗口");
Check(ToolbarAutoHidePolicy.ExitOffset(880, 300, 100, 100, 1000, 800) == (121d, 0d), "最近右边框时完整飞出窗口");
Check(ToolbarAutoHidePolicy.ExitOffset(450, 20, 100, 100, 1000, 800) == (0d, -121d), "最近上边框时完整飞出窗口");
Check(ToolbarAutoHidePolicy.ExitOffset(450, 680, 100, 100, 1000, 800) == (0d, 121d), "最近下边框时完整飞出窗口");
Console.WriteLine("PASS: presentation settings persistence, idle boundaries, interaction guards and nearest-edge animation geometry.");

string mediaRoot = Path.Combine(Path.GetTempPath(), "pancake-media-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(mediaRoot);
try
{
    string a = Path.Combine(mediaRoot, "a.png"), b = Path.Combine(mediaRoot, "b.jpg"), video = Path.Combine(mediaRoot, "clip.mp4");
    File.WriteAllText(a, "image a"); File.WriteAllText(b, "image b"); File.WriteAllText(video, "video");
    MediaLibrary library = new(Path.Combine(mediaRoot, "data"));
    string ownedA = await library.ImportAsync(a);
    string ownedB = await library.ImportAsync(b);
    Check(library.RecentImages().SequenceEqual(new[] { ownedB, ownedA }), "最近图片按选择顺序排列");
    Check(await library.ImportAsync(ownedA) == ownedA && library.RecentImages()[0] == ownedA, "重复选择复用文件并移到首位");
    string ownedVideo = await library.ImportAsync(video, MediaScope.Tile);
    Check(library.RecentMedia(MediaScope.Tile)[0] == ownedVideo, "视频导入后进入最近媒体首位");
    Check(new MediaLibrary(Path.Combine(mediaRoot, "data")).RecentMedia(MediaScope.Tile)[0] == ownedVideo, "视频历史跨服务实例持久化");
    Check(library.RecentImages().Count == 2 && !library.RecentImages().Contains(ownedVideo), "视频不混入最近图片缩略图");
    Check(!library.RecentMedia(MediaScope.Background).Contains(ownedVideo), "磁贴的视频不出现在背景板最近媒体");
    File.Delete(a);
    Check(File.Exists(ownedA), "原图片删除不影响历史副本");
    Check(new MediaLibrary(Path.Combine(mediaRoot, "data")).RecentImages()[0] == ownedA, "历史跨服务实例持久化");
    Check(MediaLibrary.DisplayName(ownedA) == "a.png", "资源内部标识不会显示成媒体名称");
    bool refusedVideo = false;
    try { await library.ImportAsync(video); } catch (InvalidDataException) { refusedVideo = true; }
    Check(refusedVideo, "图片入口拒绝视频");
    string c = Path.Combine(mediaRoot, "c.webp"); File.WriteAllText(c, "image c");
    string ownedC = await library.ImportAsync(c, MediaScope.Background);
    Check(library.RecentMedia(MediaScope.Background)[0] == ownedC && library.RecentImages()[0] == ownedC, "背景板导入进入背景板最近媒体，图片仍进图片历史");
    Check(!library.RecentMedia(MediaScope.Tile).Contains(ownedC), "背景板的图片不出现在磁贴最近媒体");
    bool refusedCrossScope = false;
    try { await library.UseRecentAsync(ownedVideo, MediaScope.Background); } catch (FileNotFoundException) { refusedCrossScope = true; }
    Check(refusedCrossScope, "另一使用面的媒体不能从本使用面复选");
    BackgroundSettings playlistSettings = new() { PlaylistEnabled = true, Playlist = [ownedA, b, video, b, Path.Combine(mediaRoot, "missing.png")],
        Shuffle = false, SwitchOnTimer = true, SwitchIntervalSeconds = 12, SwitchOnMediaEnded = false };
    BackgroundPlaylist playback = new();
    Check(playback.Configure(playlistSettings) && playback.Current == ownedA, "播放列表去重并忽略缺失文件");
    Check(playback.Advance(false) && playback.Current == b && playback.Advance(false) && playback.Current == video && playback.Advance(false) && playback.Current == ownedA, "顺序播放到末尾后回到首项");
    playback.Advance(false);
    playlistSettings.Color = "#112233";
    Check(!playback.Configure(playlistSettings) && playback.Current == b, "外观变化不重置播放进度");
    playlistSettings.Playlist = [video, b, ownedA];
    playback.Configure(playlistSettings);
    Check(playback.Current == b && playback.Advance(false) && playback.Current == ownedA, "排序保留当前媒体且下一项遵循新顺序");
    for (int i = 0; i < 40; i++)
    {
        string previous = playback.Current;
        Check(playback.Advance(true) && playback.Current != previous, "随机播放不连续重复同一项");
    }
    // 重新配置会保留当前项（见上面的外观变化断言），因此这里用独立实例确保从首项开始，断言不受随机播放的落点影响。
    BackgroundPlaylist exhausted = new();
    Check(exhausted.Configure(new() { PlaylistEnabled = true, Playlist = [ownedA, b, video] }) && exhausted.Current == ownedA, "新建播放列表从首项开始");
    Check(exhausted.Advance(false, true) && exhausted.Current == b && exhausted.Advance(false, true) && exhausted.Current == video && !exhausted.Advance(false, true), "全部失败后停止换曲避免死循环");
    BackgroundSettings roundTrip = JsonSerializer.Deserialize<BackgroundSettings>(JsonSerializer.Serialize(playlistSettings))!;
    Check(roundTrip.Playlist.SequenceEqual(playlistSettings.Playlist) && roundTrip.SwitchOnTimer && !roundTrip.SwitchOnMediaEnded && roundTrip.SwitchIntervalSeconds == 12, "队列顺序与切换开关往返保存");
    playback.Configure(new() { ImagePath = b });
    Check(playback.Current == b && !playback.Advance(true), "旧版单图设置兼容且单项不会随机越界");
    Check(BackgroundPlaylist.NormalizeInterval(double.NaN) == 60 && BackgroundPlaylist.NormalizeInterval(-1) == 1 && BackgroundPlaylist.NormalizeInterval(double.MaxValue) == 86400, "异常间隔归一化");
    for (int i = 0; i < 10; i++)
    {
        string image = Path.Combine(mediaRoot, $"recent-{i}.png"); File.WriteAllText(image, i.ToString());
        await library.ImportAsync(image);
    }
    Check(library.RecentImages().Count == 8 && MediaLibrary.DisplayName(library.RecentImages()[0]) == "recent-9.png", "历史最多保留八张最新图片");
    File.Delete(library.RecentImages()[0]);
    Check(library.RecentImages().Count == 7, "删除的历史文件不显示无效入口");
    string projectFile = Path.Combine(mediaRoot, "project.json");
    File.WriteAllText(projectFile, JsonSerializer.Serialize(new { type = "video", file = "clip.mp4", title = "测试壁纸" }));
    Check(WallpaperEngineLibrary.ReadProject(mediaRoot)?.Path == video, "识别 Wallpaper Engine 视频项目");
    File.WriteAllText(projectFile, JsonSerializer.Serialize(new { type = "scene", file = "clip.mp4" }));
    Check(WallpaperEngineLibrary.ReadProject(mediaRoot) is null, "场景壁纸不当成视频导入");
    File.WriteAllText(projectFile, JsonSerializer.Serialize(new { type = "video", file = "../outside.mp4" }));
    Check(WallpaperEngineLibrary.ReadProject(mediaRoot) is null, "拒绝项目目录逃逸");
    File.WriteAllText(projectFile, "{broken");
    Check(WallpaperEngineLibrary.ReadProject(mediaRoot) is null, "损坏项目不阻断扫描");
    int missingExceptions = 0;
    void CountMissing(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is FileNotFoundException) missingExceptions++;
    }
    AppDomain.CurrentDomain.FirstChanceException += CountMissing;
    try { Check(WallpaperEngineLibrary.ReadProject(Path.Combine(mediaRoot, "absent")) is null, "扫描忽略缺少清单的项目"); }
    finally { AppDomain.CurrentDomain.FirstChanceException -= CountMissing; }
    Check(missingExceptions == 0, "缺少项目清单不引发调试器首次机会异常");
    string webRoot = Path.Combine(mediaRoot, "web-source");
    Directory.CreateDirectory(webRoot);
    string webEntry = Path.Combine(webRoot, "index.html");
    File.WriteAllText(webEntry, "<html>wallpaper</html>");
    File.WriteAllText(Path.Combine(webRoot, "resource.js"), "// resource");
    File.WriteAllText(Path.Combine(webRoot, "project.json"), "{\"type\":\"web\",\"file\":\"index.html\"}");
    int changes = 0;
    library.Changed += () => changes++;
    string ownedWeb = await library.ImportWallpaperAsync(new("网页", webEntry, "web", webRoot, ""), MediaScope.Background);
    Check(changes == 1 && library.RecentMedia(MediaScope.Background)[0] == ownedWeb, "网页完整导入后更新最近媒体并通知视图");
    Check(!library.RecentMedia(MediaScope.Tile).Contains(ownedWeb), "背景板的动态壁纸不出现在磁贴最近媒体");
    File.Delete(webEntry);
    await library.UseRecentAsync(ownedVideo, MediaScope.Tile);
    Check(library.RecentMedia(MediaScope.Tile)[0] == ownedVideo, "再次选择视频移到首位");
    Check(await library.UseRecentAsync(ownedWeb, MediaScope.Background) == ownedWeb && library.RecentMedia(MediaScope.Background)[0] == ownedWeb
        && Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(ownedWeb))!).Length == 1
        && File.Exists(Path.Combine(Path.GetDirectoryName(ownedWeb)!, "resource.js")), "原网页入口删除后复用完整项目且不重复复制");
    Check(!library.RecentImages().Contains(ownedWeb) && !library.RecentImages().Contains(ownedVideo), "附件图片入口过滤网页和视频");
    bool refusedWallpaper = false;
    try { await library.ImportWallpaperAsync(new("网页", webEntry, "web", webRoot, ""), MediaScope.Images); } catch (InvalidDataException) { refusedWallpaper = true; }
    Check(refusedWallpaper, "图片入口拒绝动态壁纸");
    Check(new MediaLibrary(Path.Combine(mediaRoot, "data")).RecentMedia(MediaScope.Background)[0] == ownedWeb
        && new MediaLibrary(Path.Combine(mediaRoot, "data")).RecentMedia(MediaScope.Tile)[0] == ownedVideo, "磁贴与背景板最近媒体分别持久化");
    Console.WriteLine("PASS: recent image ownership/history, mixed playlists, shuffle, persistence and Wallpaper Engine import boundaries.");
}
finally { Directory.Delete(mediaRoot, true); }

// 自动填充：学科库、拼音三档匹配、作业切词统计、阈值晋升、过期与屏蔽。
var autofillSettings = new AutofillSettings();
var autofill = new AutofillService(autofillSettings);
Check(autofill.EnsureBuiltIns() && autofillSettings.Subject.Subjects.Count == 16, "首次启动生成 16 个内置学科");
Check(!autofill.EnsureBuiltIns() && autofillSettings.Subject.Subjects.Count == 16, "重复初始化不重复生成内置学科");
Check(autofillSettings.Subject.Subjects.All(item => item.Color.Length == 7 && item.Enabled), "内置学科都有默认颜色并默认启用");
Check(autofill.MatchSubjects("语文").Count == 0, "总开关关闭时不返回学科候选");

const string customSubject = "道法";
autofillSettings.Subject.Subjects.Add(new SubjectSuggestion
{
    Name = customSubject, Enabled = true, Color = "#818CF8", Source = AutofillService.ManualSource
});
Check(autofillSettings.Subject.Subjects.Count(item => item.Name == customSubject) == 1, "手动添加的学科进入学科库");
Check(autofillSettings.Subject.Subjects.All(item => item.Source is AutofillService.BuiltInSource or AutofillService.ManualSource),
    "学科库只包含内置与手动添加的学科，不会自动收录项目科目");

autofillSettings.Subject.Enabled = true;
Check(autofill.MatchSubjects("语").First().Name == "语文", "中文前缀匹配");
Check(autofill.MatchSubjects("yw").First().Name == "语文", "拼音首字母匹配");
Check(autofill.MatchSubjects("yuw").First().Name == "语文", "拼音全拼前缀匹配");
Check(autofill.MatchSubjects("数学").Count == 0, "与输入完全相同的学科不再提示");
Check(!autofill.MatchSubjects("xjs").Any(item => item.Name == "信息技术"), "正常档不允许首字母跳字");
autofillSettings.Subject.MatchLevel = "Loose";
Check(autofill.MatchSubjects("xjs").Any(item => item.Name == "信息技术"), "宽松档允许首字母跳字");
Check(autofill.MatchSubjects("y").Count >= 2, "宽松档输入 1 个字母即提示");
autofillSettings.Subject.MatchLevel = "Strict";
Check(autofill.MatchSubjects("y").Count == 0 && autofill.MatchSubjects("yw").Any(item => item.Name == "语文"),
    "严格档至少 2 个字符并只认完整前缀");
autofillSettings.Subject.MatchLevel = "Normal";
Check(autofill.MatchSubjects(new string('长', 30)).Count == 0, "超长输入不触发补全");

Check(AutofillService.Tokenize("背诵《赤壁赋》第二段").SequenceEqual(["背诵", "赤壁赋", "第二段"]), "按标点切分中文片段");
Check(AutofillService.Tokenize("数学双练一测P30").SequenceEqual(["数学双练一测"]), "页码字母不并入作业名称");
Check(AutofillService.Tokenize("双练一测第3页").SequenceEqual(["双练一测"]), "页码前的序号词与量词不并入作业名称");
Check(AutofillService.Tokenize("双练一测第3页答案").SequenceEqual(["双练一测", "答案"]), "页码之后的词仍然保留");
Check(AutofillService.Tokenize("Unit 5 单词").SequenceEqual(["Unit", "单词"]), "英文单词与数字分开切分");
Check(!AutofillService.IsRecordable("P") && !AutofillService.IsRecordable("30")
    && !AutofillService.IsRecordable(HomeworkState.PlaceholderText), "单字母、数字与占位文案不记录");
Check(AutofillService.IsRecordable("Unit") && AutofillService.IsRecordable("练习题"), "英文短语与中文词可记录");
Check(!AutofillService.IsRecordable("这是一个超过十二个字符的作业名称"), "过长片段视为句子不记录");

autofillSettings.Homework.Enabled = true;
Check(autofill.RecordHomework("完成 P30 练习题", "数学"), "首次输入累计待收录词条");
Check(autofillSettings.Homework.Items.Any(item => item.Text == "练习题" && item.Count == 1 && !item.Promoted), "首次输入只累计不收录");
Check(autofillSettings.Homework.Items.All(item => item.Text is not ("P" or "30")), "页码碎片不入库");
Check(autofillSettings.Homework.Items.All(item => item.Text != HomeworkState.PlaceholderText), "占位文案不进入待收录列表");
Check(autofill.MatchHomework("练习", "数学").Count == 0, "未达阈值的词条不参与补全");
autofill.RecordHomework("完成 P31 练习题", "数学");
autofill.RecordHomework("完成 P32 练习题", "数学");
Check(autofillSettings.Homework.Items.Single(item => item.Text == "练习题").Promoted, "达到默认 3 次后自动收录");
Check(autofill.MatchHomework("练习", "数学").Single().Text == "练习题", "已收录词按前缀补全");
Check(autofill.MatchHomework("lianxi", "数学").Single().Text == "练习题", "已收录词支持拼音全拼补全");
HomeworkSuggestion repeated = autofillSettings.Homework.Items.Single(item => item.Text == "练习题");
int repeatedBefore = repeated.Count;
autofill.RecordHomework("练习题 练习题 练习题", "数学");
Check(repeated.Count == repeatedBefore + 1, "同一条作业内重复出现的词只累计一次");
Check(autofill.MatchSubjects("yu'wen").Single().Name == "语文", "输入法撇号分隔的拼音可以命中学科");
Check(autofill.MatchSubjects("ｙｕ'ｗｅｎ").Single().Name == "语文", "全角拼音同样可以命中学科");
Check(autofill.RecommendSubjects(12).Count >= 12 && autofill.RecommendSubjects(12).All(item => item.Enabled),
    "空标题推荐列表只包含已启用的学科");

// 学科颜色：未开启随机配色时沿用学科颜色；开启后每次补全都从预设色板里取一个且不连续重复。
SubjectSuggestion randomSubject = autofillSettings.Subject.Subjects.First(item => item.Name == "语文");
Check(autofill.ResolveSubjectColor(randomSubject) == randomSubject.Color, "未开启随机配色时套用学科自身的颜色");
randomSubject.RandomColor = true;
List<string> randomPicks = Enumerable.Range(0, 24).Select(_ => autofill.ResolveSubjectColor(randomSubject)).ToList();
Check(randomPicks.All(pick => ColorPalette.Presets.Contains(pick)), "随机配色只从预设色板里取色");
Check(!randomPicks.Where((pick, index) => index > 0 && pick == randomPicks[index - 1]).Any(),
    "随机配色不连续重复同一个颜色");
Check(randomPicks.Distinct().Count() > 1, "随机配色会落在多个预设颜色上");
randomSubject.RandomColor = false;

HomeworkSuggestion renamed = autofillSettings.Homework.Items.Single(item => item.Text == "练习题");
Check(autofill.RenameHomework(renamed, "练习册") && renamed.Text == "练习册", "已收录的作业名称可以改写");
Check(autofill.MatchHomework("练习", "数学").Single().Text == "练习册", "改写后按新名称补全");
Check(autofill.RenameHomework(renamed, "练习册") == false, "名称未变化时不重复保存");
Check(autofill.RecordHomework("在这里输入作业内容", "数学") == false, "占位文案不触发统计");

// 新增作业只给灰色提示：正文为空，旧版本写进正文的提示文案在读取时还原成空内容。
HomeworkState blank = new();
Check(blank.Content.Length == 0 && blank.RtfContent.Length == 0, "新建作业的正文与富文本都是空的");
HomeworkState legacy = new() { Content = HomeworkState.PlaceholderText, RtfContent = @"{\rtf1 在这里输入作业内容}" };
legacy.ClearLegacyPlaceholder();
Check(legacy.Content.Length == 0 && legacy.RtfContent.Length == 0, "旧项目里的占位提示还原成空内容");
HomeworkState written = new() { Content = "在这里输入作业内容后交作业", RtfContent = "rtf" };
written.ClearLegacyPlaceholder();
Check(written.Content == "在这里输入作业内容后交作业" && written.RtfContent == "rtf", "含有提示字样的真实作业不被清空");

autofillSettings.Homework.Isolation = "Subject";
autofill.RecordHomework("同步练习册", "数学");
Check(autofillSettings.Homework.Items.Any(item => item.Text == "同步练习册" && !item.IsGlobal && item.Subjects.Contains("数学")),
    "分学科隔离写入学科桶");
Check(!autofill.MatchHomework("同步", "英语").Any(), "分学科隔离下其他学科看不到该词");
autofill.RecordHomework("同步练习册", "英语");
Check(autofillSettings.Homework.Items.Count(item => item.Text == "同步练习册") == 2, "不同学科分别计数");
autofillSettings.Homework.Isolation = "Global";

autofill.BlockHomework("同步练习册");
Check(autofill.IsBlocked("同步练习册") && autofillSettings.Homework.Items.All(item => item.Text != "同步练习册"),
    "不再收录会移除词条并记住屏蔽");
autofill.RecordHomework("同步练习册", "数学");
Check(autofillSettings.Homework.Items.All(item => item.Text != "同步练习册"), "被屏蔽的词不会再次入库");
autofill.UnblockHomework("同步练习册");
Check(!autofill.IsBlocked("同步练习册") && autofillSettings.Homework.Blocked.Count == 0, "恢复收录清除屏蔽");

Check(AutofillService.RecordThreshold("Loose") == 2 && AutofillService.RecordThreshold("Strict") == 5
    && AutofillService.RemainingCount(new HomeworkSuggestion { Source = AutofillService.AutoSource, Count = 1 }, "Normal") == 2,
    "三档阈值与剩余次数计算");
DateTime staleTime = DateTime.Now.AddDays(-20);
autofillSettings.Homework.Items.Add(new HomeworkSuggestion
{
    Text = "过期候选", Source = AutofillService.AutoSource, IsGlobal = true, Count = 1,
    FirstSeenAt = staleTime, LastSeenAt = staleTime
});
autofillSettings.Homework.Items.Add(new HomeworkSuggestion
{
    Text = "过期收录", Source = AutofillService.AutoSource, IsGlobal = true, Count = 3, Promoted = true,
    FirstSeenAt = DateTime.Now.AddDays(-120), LastSeenAt = DateTime.Now.AddDays(-120)
});
autofillSettings.Homework.Items.Add(new HomeworkSuggestion
{
    Text = "手动长期项", Source = AutofillService.ManualSource, IsGlobal = true, Count = 1, Promoted = true,
    FirstSeenAt = DateTime.Now.AddDays(-400), LastSeenAt = DateTime.Now.AddDays(-400)
});
Check(autofill.Prune(DateTime.Now), "过期清理会移除超期词条");
Check(autofillSettings.Homework.Items.All(item => item.Text is not ("过期候选" or "过期收录")),
    "未收录 14 天与已收录 90 天的自动词条被清理");
Check(autofillSettings.Homework.Items.Any(item => item.Text == "手动长期项"), "手动添加的作业类型永不过期");
Check(AutofillService.RemainingDays(new HomeworkSuggestion
{
    Source = AutofillService.AutoSource, LastSeenAt = DateTime.Now
}, DateTime.Now) == AutofillService.PendingExpiryDays, "剩余过期天数按未收录上限计算");

BoardSettingsState autofillRoundTrip = JsonSerializer.Deserialize<BoardSettingsState>(
    JsonSerializer.Serialize(new BoardSettingsState { Autofill = autofillSettings }))!;
Check(autofillRoundTrip.Autofill.Homework.Isolation == "Global"
    && autofillRoundTrip.Autofill.Subject.Subjects.Count == autofillSettings.Subject.Subjects.Count
    && autofillRoundTrip.Autofill.Homework.Items.Count == autofillSettings.Homework.Items.Count,
    "自动填充设置往返保存");
Check(autofillRoundTrip.Autofill.Subject.Subjects.All(item => !item.RandomColor), "随机配色开关随学科一起保存");
BoardSettingsState legacyAutofill = JsonSerializer.Deserialize<BoardSettingsState>("{}")!;
Check(!legacyAutofill.Autofill.Subject.Enabled && !legacyAutofill.Autofill.Homework.Enabled
    && legacyAutofill.Autofill.Subject.MatchLevel == "Normal" && legacyAutofill.Autofill.Homework.RecordLevel == "Normal"
    && legacyAutofill.Autofill.Homework.Items.Count == 0
    && legacyAutofill.Autofill.Subject.Subjects.All(item => !item.RandomColor),
    "旧配置缺少自动填充字段时使用默认值并保持关闭");
Console.WriteLine("PASS: autofill subject library, pinyin matching levels, homework tokenizing, thresholds, isolation, expiry and blocking.");

// 锁定：密码哈希校验、两步验证生效条件、Base32 与 TOTP 时间窗。
var lockState = new LockSettings();
Check(!LockService.IsEnforced(lockState), "未配置密码或两步验证时不锁定");
lockState.Enabled = true;
Check(!LockService.IsEnforced(lockState), "只开总开关不锁定，避免把自己关在门外");
LockService.SetPassword(lockState, "课堂密码123");
Check(lockState.PasswordSalt.Length > 0 && lockState.PasswordHash.Length > 0, "密码只保存盐与哈希");
Check(LockService.IsEnforced(lockState) && LockService.VerifyPassword(lockState, "课堂密码123"), "设置密码并开启总开关后锁定生效");
Check(!LockService.VerifyPassword(lockState, "课堂密码124") && !LockService.VerifyPassword(lockState, "")
    && !LockService.VerifyPassword(lockState, "课堂密码123 "), "错误、空白或带尾随空格的密码不能解锁");
Check(LockService.HashPassword("课堂密码123", lockState.PasswordSalt) == lockState.PasswordHash, "同一密码与盐得到相同哈希");
string firstSalt = lockState.PasswordSalt;
LockService.SetPassword(lockState, "课堂密码123");
Check(lockState.PasswordSalt != firstSalt, "修改密码更换盐，避免哈希比对泄露相同密码");
LockService.ClearPassword(lockState);
Check(!LockService.HasPassword(lockState) && !LockService.IsEnforced(lockState), "删除密码后不再锁定");
lockState.TotpSecret = LockService.CreateTotpSecret();
Check(LockService.IsEnforced(lockState), "绑定验证器后锁定生效");
lockState.Enabled = false;
Check(!LockService.IsEnforced(lockState), "总开关关闭后不锁定");
BoardSettingsState lockRoundTrip = JsonSerializer.Deserialize<BoardSettingsState>(
    JsonSerializer.Serialize(new BoardSettingsState { Lock = lockState }))!;
Check(lockRoundTrip.Lock.TotpSecret == lockState.TotpSecret && !lockRoundTrip.Lock.Enabled
    && lockRoundTrip.Lock.RequireAuthForSettings && !lockRoundTrip.Lock.RequireAuthForEditing,
    "锁定设置与两步验证密钥往返持久化");

string secret = LockService.CreateTotpSecret();
Check(secret.Length >= 32 && secret.All(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".Contains(c)), "密钥使用 Base32 标准字母表");
Check(LockService.Base32Decode(secret).Length == 20, "160 位密钥解码为 20 字节");
Check(LockService.TotpProvisioningUri(secret).StartsWith("otpauth://totp/Pancake?secret=" + secret), "otpauth 链接可直接导入验证器");
foreach (byte[] data in new[] { Array.Empty<byte>(), new byte[] { 0 }, new byte[] { 255, 0, 128, 7 }, new byte[] { 1, 2, 3, 4, 5 } })
    Check(LockService.Base32Decode(LockService.Base32Encode(data)).SequenceEqual(data), "Base32 编解码往返无损");
DateTimeOffset moment = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
long totpStep = moment.ToUnixTimeSeconds() / LockService.TotpStepSeconds;
string totpCode = LockService.ComputeTotp(LockService.Base32Decode(secret), totpStep);
Check(totpCode.Length == 6 && totpCode.All(char.IsDigit), "验证码为 6 位数字");
Check(LockService.VerifyTotp(secret, totpCode, moment), "当前时间步的验证码通过");
Check(LockService.VerifyTotp(secret, totpCode, moment.AddSeconds(LockService.TotpStepSeconds)), "容忍一步时钟偏差");
Check(!LockService.VerifyTotp(secret, totpCode, moment.AddSeconds(LockService.TotpStepSeconds * 3)), "偏差过大不通过");
Check(!LockService.VerifyTotp(secret, "12345", moment) && !LockService.VerifyTotp(secret, "123456x", moment), "位数不对的验证码不通过");
Check(LockService.VerifyTotp(secret, totpCode.Insert(3, " "), moment), "验证码里的空格被忽略");
Check(!LockService.VerifyTotp("0", totpCode, moment), "非法 Base32 密钥不通过而不是抛出");
Console.WriteLine("PASS: lock password hashing, enforcement conditions, Base32 round trips and TOTP time windows.");

// 数据备份：范围打包、清单读取、跨目录还原与自动备份清理。
string backupRoot = Path.Combine(Path.GetTempPath(), "pancake-backup-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(backupRoot);
try
{
    string sourceData = Path.Combine(backupRoot, "source");
    Directory.CreateDirectory(sourceData);
    ProjectStore sourceStore = new(sourceData);
    ProjectLibrary library = sourceStore.Load();
    ProjectDocument project = ProjectStore.Create(library, false);
    project.Subjects.Add(new SubjectState
    {
        Name = "数学", Width = 300, Height = 200, X = 10, Y = 20, InkCoordinateVersion = 1,
        AccentHex = "#123456", IsAccentExplicit = true,
        Entries = [new HomeworkState { Content = "完成练习", Attachments = [], FontFallbacks = [] }],
        InkStrokes = []
    });
    string picture = Path.Combine(backupRoot, "attachment.png");
    File.WriteAllBytes(picture, [9, 9, 9]);
    project.Subjects[0].Entries[0].Attachments.Add(new AttachmentState
    {
        Name = "图片", Kind = "图片", Path = sourceStore.CopyAttachment(project.Id, picture)
    });
    library.Settings.InfiniteBoard = true;
    library.Settings.Theme = "Light";
    string wallpaper = Path.Combine(backupRoot, "wall.png");
    File.WriteAllBytes(wallpaper, [1, 2, 3]);
    library.Settings.BoardBackground.ImagePath = await new MediaLibrary(sourceData).ImportAsync(wallpaper, MediaScope.Background);
    sourceStore.Save(library);

    BackupService exporter = new(sourceData);
    string fullArchive = Path.Combine(backupRoot, "manual.pbk");
    exporter.Create(fullArchive, BackupScopes.All, library);
    BackupInfo info = exporter.Read(fullArchive);
    Check(info.Manifest.Version == BackupService.ManifestVersion && info.Manifest.Scopes == BackupScopes.All, "清单记录版本与完整范围");
    Check(info.MediaEntries.Any(entry => entry.StartsWith("backgrounds/")), "背景媒体收进备份");

    string targetData = Path.Combine(backupRoot, "target");
    new BackupService(targetData).Restore(fullArchive);
    ProjectLibrary restoredLibrary = new ProjectStore(targetData).Load();
    Check(restoredLibrary.Projects.Count == 1 && restoredLibrary.Projects[0].Subjects[0].Entries[0].Content == "完成练习", "作业数据完整还原到另一数据目录");
    Check(restoredLibrary.Settings.InfiniteBoard && restoredLibrary.Settings.Theme == "Light", "软件设置完整还原");
    Check(File.Exists(restoredLibrary.Projects[0].Subjects[0].Entries[0].Attachments[0].Path), "图片附件落回本地资源目录");
    Check(File.Exists(restoredLibrary.Settings.BoardBackground.ImagePath), "背景媒体落回本地资源目录并改写引用");

    // 只收作业范围时设置不随包走，还原后保留目标机器现有设置。
    string jobsArchive = Path.Combine(backupRoot, "jobs.pbk");
    exporter.Create(jobsArchive, BackupScopes.Jobs, library);
    Check(exporter.Read(jobsArchive).Manifest.Scopes == BackupScopes.Jobs, "按范围导出只收选中的类别");
    ProjectStore targetStore = new(targetData);
    ProjectLibrary target = targetStore.Load();
    target.Settings.Theme = "Dark";
    target.Settings.InfiniteBoard = false;
    targetStore.Save(target);
    new BackupService(targetData).Restore(jobsArchive);
    ProjectLibrary partial = targetStore.Load();
    Check(partial.Projects[0].Subjects[0].Entries[0].Content == "完成练习" && !partial.Settings.InfiniteBoard && partial.Settings.Theme == "Dark",
        "只含作业的备份还原后保留现有设置");

    bool refusedEmptyScope = false;
    try { exporter.Create(Path.Combine(backupRoot, "empty.pbk"), BackupScopes.None, library); }
    catch (InvalidDataException) { refusedEmptyScope = true; }
    Check(refusedEmptyScope, "空备份范围被拒绝");

    void WriteArchive(string path, string entryName, string? manifest)
    {
        ProjectStore.AtomicWrite(path, stream =>
        {
            using ZipArchive archive = new(stream, ZipArchiveMode.Create, true);
            if (manifest is not null)
                using (Stream target = archive.CreateEntry("manifest.json").Open())
                    target.Write(System.Text.Encoding.UTF8.GetBytes(manifest));
            if (entryName.Length > 0) archive.CreateEntry(entryName);
        });
    }
    bool rejected = false;
    WriteArchive(Path.Combine(backupRoot, "escape.pbk"), "backgrounds/../evil.txt", "{\"Version\":1,\"Scopes\":1,\"CreatedAt\":\"2026-01-01T00:00:00\"}");
    try { exporter.Read(Path.Combine(backupRoot, "escape.pbk")); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "备份里的路径穿越条目被拒绝");
    rejected = false;
    WriteArchive(Path.Combine(backupRoot, "loose.pbk"), "evil.txt", "{\"Version\":1,\"Scopes\":1,\"CreatedAt\":\"2026-01-01T00:00:00\"}");
    try { exporter.Read(Path.Combine(backupRoot, "loose.pbk")); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "备份里的越界条目被拒绝");
    rejected = false;
    WriteArchive(Path.Combine(backupRoot, "nomanifest.pbk"), "backgrounds/x.png", null);
    try { exporter.Read(Path.Combine(backupRoot, "nomanifest.pbk")); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "缺少清单的备份被拒绝");
    rejected = false;
    WriteArchive(Path.Combine(backupRoot, "future.pbk"), "backgrounds/x.png", "{\"Version\":2,\"Scopes\":1,\"CreatedAt\":\"2026-01-01T00:00:00\"}");
    try { exporter.Read(Path.Combine(backupRoot, "future.pbk")); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "其他版本的备份被拒绝");

    string autoDirectory = Path.Combine(backupRoot, "auto");
    Directory.CreateDirectory(autoDirectory);
    foreach (int hours in new[] { 5, 4, 3, 2, 1 })
        File.WriteAllText(Path.Combine(autoDirectory, BackupService.AutoBackupFileName(DateTime.Now.AddHours(-hours))), "backup");
    Check(Directory.EnumerateFiles(autoDirectory, "auto-*.pbk").Count() == 5, "自动备份按时间命名，字典序即时间序");
    Check(BackupService.PruneAutoBackups(autoDirectory, 3) == 2 && Directory.EnumerateFiles(autoDirectory, "auto-*.pbk").Count() == 3,
        "超出保留份数时删除最旧的备份");
    Check(BackupService.PruneAutoBackups(autoDirectory, 0) == 0 && Directory.EnumerateFiles(autoDirectory, "auto-*.pbk").Count() == 3,
        "非法保留份数不清理");
    Check(BackupService.NormalizeIntervalHours(0) == 1 && BackupService.NormalizeIntervalHours(5.5) == 5.5
        && BackupService.NormalizeIntervalHours(double.NaN) == 24 && BackupService.NormalizeIntervalHours(99999) == 24 * 365,
        "自动备份间隔归一化到可用范围");
    DateTime moment2 = new(2026, 9, 24, 12, 0, 0);
    Check(BackupService.IsDue(null, 24, moment2), "从未备份过立即执行");
    Check(!BackupService.IsDue(moment2.AddHours(-23), 24, moment2) && BackupService.IsDue(moment2.AddHours(-24), 24, moment2),
        "到期按上次备份时间与间隔判断");
    Check(BackupService.IsDue(moment2.AddDays(-400), double.NaN, moment2), "非法间隔按 24 小时判断到期");
}
finally { Directory.Delete(backupRoot, true); }

// 自动保存与数据页设置：开关默认开启，旧配置补全默认值并完整往返。
BoardSettingsState dataDefaults = JsonSerializer.Deserialize<BoardSettingsState>("{}")!;
Check(dataDefaults.AutoSaveEnabled, "旧配置默认开启自动保存");
Check(dataDefaults.Data.JobRetentionDays == 30 && dataDefaults.Data.Backup.Scopes == BackupScopes.All
    && dataDefaults.Data.AutoBackup.IntervalHours == 24 && dataDefaults.Data.AutoBackup.KeepCount == 7
    && dataDefaults.Data.AutoBackup.LastRunAt is null, "旧配置缺少数据字段时使用默认值");
Check(!dataDefaults.Lock.Enabled && dataDefaults.Lock.RequireAuthForSettings && !dataDefaults.Lock.RequireAuthForEditing,
    "旧配置缺少锁定字段时保持未启用");
BoardSettingsState dataRoundTrip = JsonSerializer.Deserialize<BoardSettingsState>(JsonSerializer.Serialize(new BoardSettingsState
{
    AutoSaveEnabled = false,
    Lock = new LockSettings { Enabled = true, RequireAuthForEditing = true, RequireAuthForSettings = false },
    Data = new DataSettings
    {
        JobRetentionDays = 7,
        Backup = new BackupSettings { Scopes = BackupScopes.Jobs | BackupScopes.Settings },
        AutoBackup = new BackupSettings
        {
            Enabled = true, Directory = "D:\\auto", IntervalHours = 6, KeepCount = 3,
            LastRunAt = new DateTime(2026, 1, 2, 3, 4, 5), Scopes = BackupScopes.CurrentBackground
        }
    }
}))!;
Check(!dataRoundTrip.AutoSaveEnabled, "自动保存开关往返持久化");
Check(dataRoundTrip.Data.JobRetentionDays == 7 && dataRoundTrip.Data.Backup.Scopes == (BackupScopes.Jobs | BackupScopes.Settings),
    "备份范围按位组合往返持久化");
Check(dataRoundTrip.Data.AutoBackup.Enabled && dataRoundTrip.Data.AutoBackup.IntervalHours == 6
    && dataRoundTrip.Data.AutoBackup.KeepCount == 3 && dataRoundTrip.Data.AutoBackup.LastRunAt == new DateTime(2026, 1, 2, 3, 4, 5)
    && dataRoundTrip.Data.AutoBackup.Scopes == BackupScopes.CurrentBackground,
    "自动备份目录、间隔、保留份数与上次备份时间往返持久化");
Check(dataRoundTrip.Lock.Enabled && dataRoundTrip.Lock.RequireAuthForEditing && !dataRoundTrip.Lock.RequireAuthForSettings,
    "锁定开关与验证范围往返持久化");
Check((BackupScopes.All | BackupScopes.Jobs).Sanitize() == BackupScopes.All && ((BackupScopes)255).Sanitize() == BackupScopes.All,
    "备份范围只保留合法位");
Console.WriteLine("PASS: backup pack/restore boundaries, auto-backup pruning and auto-save/data-page persistence.");
