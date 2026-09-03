using System.Text.Json;

namespace Pancake.Services;

public sealed record WeatherCity(string Name, string Code)
{
    // Code 仅用于请求天气接口，地区选择界面不应向用户暴露内部编码。
    public override string ToString() => Name;
}

public sealed class WeatherCityCatalog
{
    private IReadOnlyList<WeatherCity>? _cities;

    public async Task<IReadOnlyList<WeatherCity>> SearchAsync(string query, int limit = 100)
    {
        _cities ??= await LoadAsync();
        string keyword = query.Trim();
        return _cities.Where(city => keyword.Length == 0 || city.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToList();
    }

    private static async Task<IReadOnlyList<WeatherCity>> LoadAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "xiaomi_weather_cities.json");
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<WeatherCity>>(stream) ?? [];
    }
}
