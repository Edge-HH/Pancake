using System.Globalization;
using System.Text.Json;

namespace PancakeBoard.Services;

public sealed record WeatherSnapshot(string Condition, double TemperatureCelsius);

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
        JsonElement current = document.RootElement.GetProperty("current");
        string temperatureText = current.GetProperty("temperature").GetProperty("value").GetString() ?? "";
        string weatherCode = current.GetProperty("weather").GetString() ?? "";
        if (!double.TryParse(temperatureText, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            throw new InvalidDataException("小米天气响应中的当前温度无效。");
        return new WeatherSnapshot(GetCondition(weatherCode), temperature);
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
