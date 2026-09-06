using System.Globalization;
using System.Text.Json;

namespace Pancake.Services;

public sealed record WeatherAlert(string Title, string Detail);
public sealed record WeatherSnapshot(string Condition, double TemperatureCelsius, IReadOnlyList<WeatherAlert> Alerts);

/// <summary>按 XiaomiWeather.md 描述调用小米天气市场接口。</summary>
public sealed class XiaomiWeatherService
{
    private const string Endpoint = "https://weatherapi.market.xiaomi.com/wtr-v3/weather/all";
    private const string Signature = "zUFJoAR2ZVrDy1vF3D07";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<WeatherSnapshot> GetCurrentAsync(string cityCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cityCode)) throw new InvalidOperationException("请先选择地区。");
        string query = $"latitude=0&longitude=0&locationKey={Uri.EscapeDataString("weathercn:" + cityCode)}&days=5&appKey=weather20151024&sign={Signature}&isGlobal=false&locale=zh_cn";
        using HttpResponseMessage response = await _httpClient.GetAsync($"{Endpoint}?{query}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseSnapshot(document.RootElement);
    }

    public static WeatherSnapshot ParseSnapshot(JsonElement root)
    {
        JsonElement current = root.GetProperty("current");
        string temperatureText = current.GetProperty("temperature").GetProperty("value").GetString() ?? "";
        string weatherCode = current.GetProperty("weather").GetString() ?? "";
        if (!double.TryParse(temperatureText, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            throw new InvalidDataException("小米天气响应中的当前温度无效。");
        List<WeatherAlert> alerts = [];
        // 预警类型由服务端提供，不用普通天气代码白名单过滤强对流、海区大风等类型。
        if (root.TryGetProperty("alerts", out JsonElement alertList) && alertList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement alert in alertList.EnumerateArray())
            {
                if (alert.ValueKind != JsonValueKind.Object) continue;
                string Read(string name) => alert.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                string title = Read("title");
                if (string.IsNullOrWhiteSpace(title)) title = Read("type") + Read("level");
                if (!string.IsNullOrWhiteSpace(title)) alerts.Add(new(title, Read("detail")));
            }
        }
        return new WeatherSnapshot(GetCondition(weatherCode), temperature, alerts.Distinct().ToList());
    }

    private static string GetCondition(string code) => code switch
    {
        "0" => "晴", "1" => "多云", "2" => "阴", "3" => "阵雨", "4" => "雷阵雨",
        "5" => "雷阵雨伴冰雹", "6" => "雨夹雪", "7" => "小雨", "8" => "中雨", "9" => "大雨",
        "10" => "暴雨", "11" => "大暴雨", "12" => "特大暴雨", "13" => "阵雪", "14" => "小雪",
        "15" => "中雪", "16" => "大雪", "17" => "暴雪", "18" => "雾", "19" => "冻雨",
        "20" => "沙尘暴", "21" => "小到中雨", "22" => "中到大雨", "23" => "大到暴雨", "24" => "暴雨到大暴雨",
        "25" => "大暴雨到特大暴雨", "26" => "小到中雪", "27" => "中到大雪", "28" => "大到暴雪",
        "29" => "浮尘", "30" => "扬沙", "31" => "强沙尘暴", "53" => "霾", _ => $"天气代码 {code}"
    };
}
