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
    string ownedVideo = await library.ImportAsync(video);
    Check(library.RecentImages().Count == 2 && !library.RecentImages().Contains(ownedVideo), "视频不混入最近图片缩略图");
    File.Delete(a);
    Check(File.Exists(ownedA), "原图片删除不影响历史副本");
    Check(new MediaLibrary(Path.Combine(mediaRoot, "data")).RecentImages()[0] == ownedA, "历史跨服务实例持久化");
    Check(MediaLibrary.DisplayName(ownedA) == "a.png", "资源内部标识不会显示成媒体名称");
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
    playback.Configure(new() { PlaylistEnabled = true, Playlist = [ownedA, b, video] });
    Check(playback.Advance(false, true) && playback.Current == b && playback.Advance(false, true) && playback.Current == video && !playback.Advance(false, true), "全部失败后停止换曲避免死循环");
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
    && !AutofillService.IsRecordable(AutofillService.PlaceholderHomework), "单字母、数字与占位文案不记录");
Check(AutofillService.IsRecordable("Unit") && AutofillService.IsRecordable("练习题"), "英文短语与中文词可记录");
Check(!AutofillService.IsRecordable("这是一个超过十二个字符的作业名称"), "过长片段视为句子不记录");

autofillSettings.Homework.Enabled = true;
Check(autofill.RecordHomework("完成 P30 练习题", "数学"), "首次输入累计待收录词条");
Check(autofillSettings.Homework.Items.Any(item => item.Text == "练习题" && item.Count == 1 && !item.Promoted), "首次输入只累计不收录");
Check(autofillSettings.Homework.Items.All(item => item.Text is not ("P" or "30")), "页码碎片不入库");
Check(autofillSettings.Homework.Items.All(item => item.Text != AutofillService.PlaceholderHomework), "占位文案不进入待收录列表");
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
