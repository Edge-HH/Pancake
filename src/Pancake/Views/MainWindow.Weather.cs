using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pancake.Controls;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 天气组件：看板显示、十分钟自动刷新、地区搜索选择与极端天气预警。
/// 数据来自小米天气接口，地区库随包分发。
/// </summary>
public sealed partial class MainWindow
{
    private readonly XiaomiWeatherService _weatherService = new();
    private readonly WeatherCityCatalog _weatherCityCatalog = new();
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private WeatherSnapshot? _lastWeather;
    private TextBlock? _weatherStatusText;
    private TextBlock? _weatherAlertsText;
    private TextBox? _weatherCityBox;

    /// <summary>启动天气刷新；没有配置地区时只显示提示。</summary>
    private void StartWeather()
    {
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        if (string.IsNullOrWhiteSpace(Settings.WeatherCityCode)) return;
        _ = RefreshWeatherAsync();
    }

    /// <summary>拉取当前天气并刷新看板与设置页显示。</summary>
    private async Task RefreshWeatherAsync()
    {
        if (_weatherStatusText is not null) _weatherStatusText.Text = "正在刷新…";
        if (string.IsNullOrWhiteSpace(Settings.WeatherCityCode))
        {
            if (_weatherStatusText is not null) _weatherStatusText.Text = "尚未选择地区";
            return;
        }

        try
        {
            _lastWeather = await _weatherService.GetCurrentAsync(Settings.WeatherCityCode);
            DisplayWeather();
            if (_weatherStatusText is not null) _weatherStatusText.Text = $"更新于 {DateTime.Now:HH:mm}";
            _weatherTimer.Start();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            _lastWeather = null;
            this.FindControl<TextBlock>("WeatherText")!.Text = "天气不可用";
            if (_weatherStatusText is not null) _weatherStatusText.Text = $"刷新失败：{ex.Message}";
            if (_weatherAlertsText is not null) _weatherAlertsText.Text = "预警更新失败，请稍后重试。";
        }
    }

    /// <summary>把最近一次天气结果显示到看板与设置页；预警按开关决定是否显示。</summary>
    private void DisplayWeather()
    {
        if (_lastWeather is not { } snapshot) return;
        string alerts = Settings.ShowWeatherAlerts
            ? string.Join("；", snapshot.Alerts.Select(alert => alert.Title))
            : string.Empty;
        TextBlock weatherText = this.FindControl<TextBlock>("WeatherText")!;
        weatherText.Text = $"{snapshot.Condition}  {snapshot.TemperatureCelsius:0.#}°C" +
                           (alerts.Length > 0 ? $" · {alerts}" : string.Empty);
        ToolTip.SetTip(weatherText, snapshot.Alerts.Count == 0
            ? null
            : string.Join("\n\n", snapshot.Alerts.Select(alert => $"{alert.Title}\n{alert.Detail}")));

        if (_weatherAlertsText is not null)
        {
            _weatherAlertsText.Text = !Settings.ShowWeatherAlerts
                ? "已关闭预警显示"
                : snapshot.Alerts.Count == 0
                    ? "当前地区暂无预警"
                    : string.Join("\n\n", snapshot.Alerts.Select(alert => $"{alert.Title}\n{alert.Detail}"));
        }
    }

    /// <summary>地区选择对话框：输入名称搜索，选中后写入设置并立即刷新。</summary>
    private async Task ChooseWeatherCityAsync()
    {
        TextBox search = new() { Watermark = "搜索地区名称" };
        ListBox results = new() { Height = 320, SelectionMode = SelectionMode.Single, Width = 520 };
        StackPanel content = new() { Width = 520, Spacing = 10 };
        content.Children.Add(search);
        content.Children.Add(results);

        async Task RefreshResultsAsync() =>
            results.ItemsSource = await _weatherCityCatalog.SearchAsync(search.Text ?? string.Empty);
        search.TextChanged += async (_, _) => await RefreshResultsAsync();
        await RefreshResultsAsync();

        DialogResult result = await ShowChoiceAsync(
            "选择天气地区",
            "输入地区名称（支持中文或拼音）后选择结果。",
            "选择",
            string.Empty,
            "取消",
            content);
        if (result != DialogResult.Primary || results.SelectedItem is not WeatherCity city) return;
        Settings.WeatherCityName = city.Name;
        Settings.WeatherCityCode = city.Code;
        RefreshWeatherSettingsRow();
        ScheduleSave();
        await RefreshWeatherAsync();
    }

    /// <summary>设置页里的地区文本框内容；页面未打开时为空操作。</summary>
    private void RefreshWeatherSettingsRow()
    {
        if (_weatherCityBox is not null) _weatherCityBox.Text = Settings.WeatherCityName;
    }

    /// <summary>组件 · 天气设置页。</summary>
    private void BuildWeatherSettings()
    {
        ToggleSwitch alertsToggle = CreateToggle(Settings.ShowWeatherAlerts);
        alertsToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.ShowWeatherAlerts = alertsToggle.IsChecked == true;
            DisplayWeather();
            ScheduleSave();
        };

        TextBox city = new() { Text = Settings.WeatherCityName, IsReadOnly = true, Width = 260 };
        _weatherCityBox = city;
        Button choose = CreateActionButton("选择地区", async () => await ChooseWeatherCityAsync());
        Grid cityRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 12 };
        cityRow.Children.Add(city);
        Grid.SetColumn(choose, 1);
        choose.VerticalAlignment = VerticalAlignment.Center;
        cityRow.Children.Add(choose);

        TextBlock status = new()
        {
            Text = "等待配置",
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        };
        _weatherStatusText = status;
        Button refresh = CreateActionButton("测试并刷新", async () => await RefreshWeatherAsync());
        StackPanel refreshRow = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
        refreshRow.Children.Add(refresh);
        refreshRow.Children.Add(status);

        TextBlock alerts = new()
        {
            Text = "预警信息将在刷新后显示。",
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        };
        _weatherAlertsText = alerts;

        SettingsContent.Children.Add(CreateCard(
            "天气",
            new TextBlock
            {
                Text = "使用小米天气接口，地区库随程序分发。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
            },
            CreateRow("地区", "选择后立即刷新，并每十分钟自动更新一次。", cityRow),
            CreateRow("显示极端天气预警", "关闭后看板与设置页都不再显示预警标题与详情。", alertsToggle),
            alerts,
            refreshRow));

        if (_lastWeather is not null) DisplayWeather();
    }
}
