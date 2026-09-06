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
Console.WriteLine("PASS: light theme content, immediate noise rearming, and three-beep PCM.");
