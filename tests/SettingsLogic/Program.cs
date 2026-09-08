using System.Text.Json;
using Pancake.Services;

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
