using System.Net.Http;
using System.Text.Json;

namespace PancakeBoard.Services;

public sealed record WeatherSnapshot(string Condition, double TemperatureCelsius);

/// <summary>
/// Calls a user-supplied Xiaomi weather endpoint template without embedding private or undocumented endpoints.
/// Supported placeholders are {city} and {key}; response field discovery accepts common weather JSON names.
/// </summary>
public sealed class XiaomiWeatherService
{
    private static readonly string[] TemperatureKeys = ["temperature", "temp", "current_temperature", "currentTemperature"];
    private static readonly string[] ConditionKeys = ["weather", "condition", "text", "weatherText"];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<WeatherSnapshot> GetCurrentAsync(string endpointTemplate, string city, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(endpointTemplate))
        {
            throw new InvalidOperationException("请先填写小米天气 API 的官方端点模板。");
        }

        string requestUrl = endpointTemplate
            .Replace("{city}", Uri.EscapeDataString(city), StringComparison.Ordinal)
            .Replace("{key}", Uri.EscapeDataString(apiKey), StringComparison.Ordinal);
        using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!TryFindNumber(document.RootElement, TemperatureKeys, out double temperature) ||
            !TryFindString(document.RootElement, ConditionKeys, out string? condition))
        {
            throw new InvalidDataException("天气响应中没有找到温度或天气状态字段，请提供接口文档以补充精确映射。");
        }

        return new WeatherSnapshot(condition!, temperature);
    }

    private static bool TryFindNumber(JsonElement element, IReadOnlyCollection<string> keys, out double result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (property.Value.TryGetDouble(out result)) return true;
                    if (property.Value.ValueKind == JsonValueKind.String && double.TryParse(property.Value.GetString(), out result)) return true;
                }
                if (TryFindNumber(property.Value, keys, out result)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (TryFindNumber(item, keys, out result)) return true;
            }
        }
        result = 0;
        return false;
    }

    private static bool TryFindString(JsonElement element, IReadOnlyCollection<string> keys, out string? result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (keys.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    result = property.Value.GetString();
                    return !string.IsNullOrWhiteSpace(result);
                }
                if (TryFindString(property.Value, keys, out result)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (TryFindString(item, keys, out result)) return true;
            }
        }
        result = null;
        return false;
    }
}
