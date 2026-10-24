using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pancake.Shared;

public enum SettingKind { Bool, Number, Enum, Color, Font, Text, Background, Widgets, Internal }

public sealed record SettingOption(string Value, string Label);

/// <summary>
/// 一项可同步的软件设置。Key 是 BoardSettingsState 上的属性名，嵌套设置用点号连接（例如 Autofill.Subject.Enabled）。
/// Hidden 的设置照常同步，但网页表单不单独显示（组件位置由布局编辑器处理，颜色清除标记由取色控件处理）。
/// </summary>
public sealed record SettingDescriptor(string Key, string Group, string Label, SettingKind Kind, JsonNode? Default,
    double? Min = null, double? Max = null, double? Step = null, IReadOnlyList<SettingOption>? Options = null,
    string? Hint = null, bool Hidden = false, string? ClearedKey = null);

/// <summary>
/// 班级设置清单：大屏、服务端和网页共用的唯一来源。服务端按它校验网页和设备上传的设置，
/// 网页按它生成表单，桌面端按它挑选要同步的字段。新增软件设置时必须在这里登记，或者写进 DeviceLocalKeys。
/// </summary>
public static class SettingsSchema
{
    public static readonly IReadOnlyList<string> Groups =
        ["布局", "网格与排版", "磁贴文字", "磁贴背景", "共享背景", "时钟区背景", "作业板背景", "控制窗", "组件卡", "时钟", "主题与色系", "天气", "班务条", "噪音提醒", "输入与补全", "保存"];

    private static readonly SettingOption[] ToolbarPositions =
    [
        new("TopLeft", "左上"), new("TopCenter", "上居中"), new("TopRight", "右上"),
        new("CenterLeft", "左居中（竖置）"), new("CenterRight", "右居中（竖置）"),
        new("BottomLeft", "左下"), new("BottomCenter", "下居中"), new("BottomRight", "右下")
    ];
    /// <summary>与桌面端 BackgroundSettings 的默认值一致。</summary>
    private static JsonObject BackgroundDefault() => new()
    {
        ["Color"] = "", ["ColorOpacity"] = 0.8, ["ColorCleared"] = false, ["ImagePath"] = "", ["ImageMode"] = "Zoom",
        ["PlaylistEnabled"] = false, ["Playlist"] = new JsonArray(), ["Shuffle"] = false, ["SwitchOnTimer"] = true,
        ["SwitchIntervalSeconds"] = 60.0, ["SwitchOnMediaEnded"] = true, ["Glass"] = false, ["Blur"] = 20.0
    };
    private static readonly SettingOption[] Levels = [new("Loose", "宽松"), new("Normal", "正常"), new("Strict", "严格")];

    public static readonly IReadOnlyList<SettingDescriptor> All =
    [
        // 布局
        new("LayoutMode", "布局", "布局模式", SettingKind.Enum, "Split", Options: [new("Split", "分屏（时钟 + 作业）"), new("Board", "仅作业"), new("Clock", "仅时钟"), new("Free", "自由布局")]),
        new("SplitRatio", "布局", "分屏时钟区占比", SettingKind.Number, 0.4, 0.1, 0.9, 0.01),
        new("InfiniteBoard", "布局", "无限作业板（可平移与缩放）", SettingKind.Bool, false),
        new("Widgets", "布局", "组件位置", SettingKind.Widgets, new JsonObject(), Hidden: true),
        new("CustomizedDockedWidgets", "布局", "手动调整过的停靠组件", SettingKind.Internal, new JsonArray(), Hidden: true),
        new("DockedWidgetLayoutVersion", "布局", "停靠布局版本", SettingKind.Internal, 0, Hidden: true),

        // 网格与排版
        new("GridSize", "网格与排版", "网格大小", SettingKind.Number, 48, 16, 160, 1),
        new("GridSnappingEnabled", "网格与排版", "网格吸附", SettingKind.Bool, true),
        new("GridStyle", "网格与排版", "网格样式", SettingKind.Enum, "Grid", Options: [new("Grid", "网格"), new("Dots", "点阵"), new("None", "不显示")]),
        new("GridColor", "网格与排版", "网格线颜色", SettingKind.Color, "#6956565C"),
        new("GridLineThickness", "网格与排版", "网格线粗细", SettingKind.Number, 1.6, 0.5, 5, 0.1),
        new("GridDotColor", "网格与排版", "点阵颜色", SettingKind.Color, "#8F56565C"),
        new("GridDotDiameter", "网格与排版", "点阵直径", SettingKind.Number, 3, 1, 12, 0.5),
        new("ShowGridWhileEditing", "网格与排版", "编辑时显示网格", SettingKind.Bool, true),
        new("AutoLayoutGap", "网格与排版", "自动排版磁贴间隔", SettingKind.Number, 0, 0, 120, 1),
        new("AutoLayoutAlign", "网格与排版", "自动排版对齐网格", SettingKind.Bool, true),
        new("AutoLayoutResize", "网格与排版", "自动排版调整大小", SettingKind.Bool, true),

        // 磁贴文字
        new("TileTitleSize", "磁贴文字", "标题大小", SettingKind.Number, 29, 16, 72, 1),
        new("TileBodyFontFamily", "磁贴文字", "正文默认字体", SettingKind.Font, "HarmonyOS Sans SC"),
        new("TileBodyFontSize", "磁贴文字", "正文默认字号", SettingKind.Number, 20, 12, 72, 1),
        new("TileBodyBold", "磁贴文字", "正文默认加粗", SettingKind.Bool, false),
        new("TileBodyItalic", "磁贴文字", "正文默认斜体", SettingKind.Bool, false),

        // 背景
        new("TileBackground", "磁贴背景", "磁贴背景", SettingKind.Background, BackgroundDefault()),
        new("SharedBackgroundEnabled", "共享背景", "时钟区与作业板共用一张背景（仅分屏）", SettingKind.Bool, false),
        new("SharedBackground", "共享背景", "共享背景", SettingKind.Background, BackgroundDefault()),
        new("ClockBackground", "时钟区背景", "时钟区背景", SettingKind.Background, BackgroundDefault()),
        new("BoardBackground", "作业板背景", "作业板背景", SettingKind.Background, BackgroundDefault()),

        // 控制窗
        new("ToolbarPosition", "控制窗", "位置", SettingKind.Enum, "BottomCenter", Options: ToolbarPositions),
        new("ToolbarIconOnly", "控制窗", "只显示图标", SettingKind.Bool, true),
        new("ToolbarScale", "控制窗", "缩放", SettingKind.Number, 1, 0.6, 2, 0.05),
        new("ToolbarRadius", "控制窗", "圆角", SettingKind.Number, 14, 0, 60, 1),
        new("ToolbarBorderThickness", "控制窗", "边框粗细", SettingKind.Number, 1, 0, 20, 0.5),
        new("ToolbarBorderColor", "控制窗", "边框颜色", SettingKind.Color, "", Hint: "留空跟随主题线条色"),
        new("ToolbarHorizontalInset", "控制窗", "距左右边框", SettingKind.Number, 24, 0, 200, 1),
        new("ToolbarVerticalInset", "控制窗", "距上下边框", SettingKind.Number, 24, 0, 200, 1),
        new("ToolbarBackgroundColor", "控制窗", "背景颜色", SettingKind.Color, "", Hint: "留空跟随主题", ClearedKey: "ToolbarBackgroundColorCleared"),
        new("ToolbarBackgroundColorCleared", "控制窗", "不使用背景颜色", SettingKind.Bool, false, Hidden: true),
        new("ToolbarBackgroundOpacity", "控制窗", "背景不透明度", SettingKind.Number, 0.8, 0, 1, 0.01),
        new("ToolbarGlass", "控制窗", "毛玻璃", SettingKind.Bool, false),
        new("ToolbarBlur", "控制窗", "模糊程度", SettingKind.Number, 20, 0, 100, 1),
        new("ToolbarAutoHide", "控制窗", "自动隐藏", SettingKind.Bool, false),
        new("ToolbarAutoHideSeconds", "控制窗", "自动隐藏时间（秒）", SettingKind.Number, 5, 1, 600, 1),
        new("ToolbarHideAnimation", "控制窗", "隐藏动画", SettingKind.Enum, "Fade", Options: [new("Fade", "渐入渐出"), new("Fly", "飞出")]),

        // 组件卡
        new("WidgetBackgroundColor", "组件卡", "背景颜色", SettingKind.Color, "", Hint: "留空跟随主题", ClearedKey: "WidgetBackgroundColorCleared"),
        new("WidgetBackgroundColorCleared", "组件卡", "不使用背景颜色", SettingKind.Bool, false, Hidden: true),
        new("WidgetBackgroundOpacity", "组件卡", "背景不透明度", SettingKind.Number, 1, 0, 1, 0.01),
        new("WidgetGlass", "组件卡", "毛玻璃", SettingKind.Bool, false),
        new("WidgetBlur", "组件卡", "模糊程度", SettingKind.Number, 20, 0, 100, 1),

        // 时钟
        new("ClockFontFamily", "时钟", "时钟字体", SettingKind.Font, "", Hint: "留空使用内置字体"),
        new("ClockFontWeight", "时钟", "时钟粗细", SettingKind.Enum, "Normal", Options: [new("Light", "细体"), new("Normal", "常规"), new("Medium", "中等"), new("Bold", "粗体")]),
        new("ClockColor", "时钟", "时钟颜色", SettingKind.Color, "", Hint: "留空或黑白时跟随深浅主题"),

        // 主题与色系
        new("Theme", "主题与色系", "主题", SettingKind.Enum, "Dark", Options: [new("Dark", "深色"), new("Light", "浅色"), new("Default", "跟随系统")]),
        new("Palette", "主题与色系", "色系", SettingKind.Enum, "Vivid", Options: [new("Vivid", "鲜明"), new("Macaron", "马卡龙")]),

        // 天气
        new("WeatherCityName", "天气", "城市", SettingKind.Text, "北京", Hidden: true),
        new("WeatherCityCode", "天气", "城市编码", SettingKind.Text, "101010100", Hidden: true),
        new("ShowWeatherAlerts", "天气", "显示天气预警", SettingKind.Bool, true),

        // 班务条
        new("BoardBannerLevels", "班务条", "显示的公告", SettingKind.Enum, "All", Options: [new("All", "全部生效公告"), new("ImportantOnly", "仅重要、紧急与置顶")]),

        // 噪音提醒
        new("NoiseAlertEnabled", "噪音提醒", "超过阈值时提醒", SettingKind.Bool, false),
        new("NoiseThresholdDb", "噪音提醒", "提醒阈值（dB）", SettingKind.Number, 60, 20, 120, 1),
        new("NoiseIntervalSeconds", "噪音提醒", "刷新间隔（秒）", SettingKind.Number, 0.1, 0.1, 2, 0.1),
        new("NoiseAlertVolume", "噪音提醒", "提醒音量", SettingKind.Number, 1, 0, 1, 0.01),
        new("PauseNoiseWhenMinimized", "噪音提醒", "最小化时暂停检测", SettingKind.Bool, false),

        // 输入与补全
        new("PastePlainTextOnly", "输入与补全", "粘贴时只保留文字", SettingKind.Bool, false),
        new("Autofill.Subject.Enabled", "输入与补全", "学科补全", SettingKind.Bool, false),
        new("Autofill.Subject.MatchLevel", "输入与补全", "学科匹配程度", SettingKind.Enum, "Normal", Options: Levels),
        new("Autofill.Homework.Enabled", "输入与补全", "作业补全", SettingKind.Bool, false),
        new("Autofill.Homework.AutoRecord", "输入与补全", "自动记录常用作业", SettingKind.Bool, true),
        new("Autofill.Homework.RecordLevel", "输入与补全", "自动记录阈值", SettingKind.Enum, "Normal", Options: [new("Loose", "宽松（2 次）"), new("Normal", "正常（3 次）"), new("Strict", "严格（5 次）")]),
        new("Autofill.Homework.Isolation", "输入与补全", "记录隔离", SettingKind.Enum, "Global", Options: [new("Global", "全局"), new("Subject", "分学科")]),

        // 保存
        new("AutoSaveEnabled", "保存", "自动保存", SettingKind.Bool, true)
    ];

    /// <summary>
    /// 不同步的设置及原因：安全凭据、本机硬件、本机路径、安装行为与本机学习数据。
    /// 测试会检查 BoardSettingsState 的每个属性都在 All 或这里登记过。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DeviceLocalKeys = new Dictionary<string, string>
    {
        ["CloudSync"] = "集控连接凭据",
        ["Lock"] = "锁定密码与两步验证密钥",
        ["MicrophoneDeviceId"] = "本机麦克风",
        ["MicrophoneSampleRate"] = "本机麦克风",
        ["MicrophoneCalibrationDb"] = "本机麦克风校准",
        ["CalibrationTargetDb"] = "本机麦克风校准",
        ["Data"] = "备份目录等本机路径",
        ["UpdateSource"] = "本机更新方式",
        ["AutoUpdateEnabled"] = "本机更新方式",
        ["AllowMultipleInstances"] = "本机启动行为",
        ["SecondLaunchAction"] = "本机启动行为",
        ["Autofill.Subject.Subjects"] = "本机补全词库",
        ["Autofill.Homework.Items"] = "本机学习的作业词库",
        ["Autofill.Homework.Blocked"] = "本机补全屏蔽词"
    };

    private static readonly Dictionary<string, SettingDescriptor> ByKey = All.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public static SettingDescriptor? Find(string key) => ByKey.GetValueOrDefault(key);

    public static IEnumerable<string> Keys => All.Select(x => x.Key);

    public static IEnumerable<string> BackgroundKeys => All.Where(x => x.Kind == SettingKind.Background).Select(x => x.Key);

    // ------------------------------------------------------------------ 路径读写

    /// <summary>按点号路径读取嵌套设置；路径不存在时返回 null。</summary>
    public static JsonNode? Get(JsonObject root, string key)
    {
        JsonNode? node = root;
        foreach (string part in key.Split('.'))
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node)) return null;
        }
        return node;
    }

    /// <summary>按点号路径写入嵌套设置，缺失的中间对象自动补齐。</summary>
    public static void Set(JsonObject root, string key, JsonNode? value)
    {
        string[] parts = key.Split('.');
        JsonObject current = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next) { next = new JsonObject(); current[parts[i]] = next; }
            current = next;
        }
        current[parts[^1]] = value;
    }

    // ------------------------------------------------------------------ 比较

    /// <summary>逐键比较两份扁平设置 JSON，返回值不同的已登记键（含一边缺失的键）。</summary>
    public static List<string> ChangedKeys(JsonObject from, JsonObject to) =>
        All.Select(x => x.Key).Where(key => from.TryGetPropertyValue(key, out JsonNode? a) != to.TryGetPropertyValue(key, out JsonNode? b) || !Same(a, b)).ToList();

    /// <summary>JSON 值语义相等：数字按数值比较（48 与 48.0 相同），对象不看键顺序。</summary>
    public static bool Same(JsonNode? a, JsonNode? b)
    {
        switch (a, b)
        {
            case (null, null): return true;
            case (JsonObject x, JsonObject y):
                return x.Count == y.Count && x.All(p => y.TryGetPropertyValue(p.Key, out JsonNode? other) && Same(p.Value, other));
            case (JsonArray x, JsonArray y):
                return x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second));
            case (JsonValue x, JsonValue y):
                if (x.GetValueKind() == JsonValueKind.Number && y.GetValueKind() == JsonValueKind.Number) return double.Parse(x.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) == double.Parse(y.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
                return x.ToJsonString() == y.ToJsonString();
            default: return false;
        }
    }

    // ------------------------------------------------------------------ 校验

    /// <summary>
    /// 校验一组扁平设置（键为描述符 Key）：丢弃未知键和类型不符的值，数字限幅并按步长取整，枚举只接受登记的取值。
    /// 返回新对象，原对象不变。
    /// </summary>
    public static JsonObject Sanitize(JsonObject values)
    {
        JsonObject result = new();
        foreach (var (key, value) in values)
        {
            if (Find(key) is not { } descriptor) continue;
            if (SanitizeValue(descriptor, value) is { } clean) result[key] = clean;
        }
        return result;
    }

    public static JsonNode? SanitizeValue(SettingDescriptor descriptor, JsonNode? value) => descriptor.Kind switch
    {
        SettingKind.Bool => BoolOf(value) is { } b ? JsonValue.Create(b) : null,
        SettingKind.Number => Number(value, descriptor.Min, descriptor.Max, descriptor.Step) is { } n ? JsonValue.Create(n) : null,
        SettingKind.Enum => Text(value, 40) is { } e && descriptor.Options!.Any(o => o.Value == e) ? JsonValue.Create(e) : null,
        SettingKind.Color => Text(value, 9) is { } c && IsColor(c) ? JsonValue.Create(c.ToUpperInvariant()) : null,
        SettingKind.Font => Text(value, 100) is { } f && !f.Any(char.IsControl) ? JsonValue.Create(f.Trim()) : null,
        SettingKind.Text => Text(value, 100) is { } t && !t.Any(char.IsControl) ? JsonValue.Create(t.Trim()) : null,
        SettingKind.Background => value is JsonObject o ? Background(o) : null,
        SettingKind.Widgets => value is JsonObject w ? Widgets(w) : null,
        SettingKind.Internal => value?.DeepClone(),
        _ => null
    };

    /// <summary>颜色：空字符串（跟随主题）或 #RRGGBB / #AARRGGBB。</summary>
    public static bool IsColor(string value) =>
        value.Length == 0 || ((value.Length is 7 or 9) && value[0] == '#' && value[1..].All(Uri.IsHexDigit));

    // 按 JSON 类型读取，不依赖 JsonValue 是从文本解析的还是由 CLR 值创建的。
    private static bool? BoolOf(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false, _ => null
    };

    private static string? Text(JsonNode? node, int max) =>
        node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { } s && s.Length <= max ? s : null;

    private static double? Number(JsonNode? node, double? min, double? max, double? step)
    {
        if (node?.GetValueKind() != JsonValueKind.Number) return null;
        double d = double.Parse(node.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(d)) return null;
        if (min is { } lo) d = Math.Max(lo, d);
        if (max is { } hi) d = Math.Min(hi, d);
        // 只对整数步长取整：大屏滑块的步长更细，不能把用户拖出来的值改掉。
        if (step is { } s and >= 1) d = Math.Round(d / s) * s;
        return d;
    }

    private static readonly string[] ImageModes = ["Zoom", "Stretch", "Fit"];

    /// <summary>背景设置的字段与桌面端 BackgroundSettings 一一对应；路径只接受 asset: 引用或本机路径文本，长度受限。</summary>
    private static JsonObject Background(JsonObject source)
    {
        JsonObject result = new();
        void Copy(string key, Func<JsonNode?, JsonNode?> clean) { if (source.TryGetPropertyValue(key, out JsonNode? v) && clean(v) is { } c) result[key] = c; }
        static JsonNode? Bool(JsonNode? n) => BoolOf(n) is { } b ? JsonValue.Create(b) : null;
        Copy("Color", n => Text(n, 9) is { } c && IsColor(c) ? JsonValue.Create(c.ToUpperInvariant()) : null);
        Copy("ColorOpacity", n => Number(n, 0, 1, null) is { } d ? JsonValue.Create(d) : null);
        Copy("ColorCleared", Bool);
        Copy("ImagePath", n => Text(n, 1024) is { } p ? JsonValue.Create(p) : null);
        Copy("ImageMode", n => Text(n, 20) is { } m && ImageModes.Contains(m) ? JsonValue.Create(m) : null);
        Copy("PlaylistEnabled", Bool);
        Copy("Playlist", n => n is JsonArray a ? new JsonArray(a.Select(x => Text(x, 1024)).OfType<string>().Take(200).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) : null);
        Copy("Shuffle", Bool);
        Copy("SwitchOnTimer", Bool);
        Copy("SwitchIntervalSeconds", n => Number(n, 1, 86400, null) is { } d ? JsonValue.Create(d) : null);
        Copy("SwitchOnMediaEnded", Bool);
        Copy("Glass", Bool);
        Copy("Blur", n => Number(n, 0, 100, null) is { } d ? JsonValue.Create(d) : null);
        return result;
    }

    private static JsonObject Widgets(JsonObject source)
    {
        JsonObject result = new();
        foreach (var (key, value) in source.Take(100))
        {
            if (key.Length > 60 || value is not JsonObject rect) continue;
            double? x = Number(rect["X"], -100000, 100000, null), y = Number(rect["Y"], -100000, 100000, null);
            double? w = Number(rect["Width"], 0, 100000, null), h = Number(rect["Height"], 0, 100000, null);
            if (x is null || y is null || w is null || h is null) continue;
            result[key] = new JsonObject { ["X"] = x, ["Y"] = y, ["Width"] = w, ["Height"] = h };
        }
        return result;
    }

    // ------------------------------------------------------------------ 网页使用的描述

    /// <summary>给网页的清单：分组、控件类型、范围与选项。字体列表由服务端按班级设备上报合并后另行提供。</summary>
    public static JsonObject Describe()
    {
        JsonArray items = new();
        foreach (SettingDescriptor d in All)
        {
            JsonObject item = new()
            {
                ["key"] = d.Key, ["group"] = d.Group, ["label"] = d.Label, ["kind"] = d.Kind.ToString(),
                ["default"] = d.Default?.DeepClone(), ["hidden"] = d.Hidden
            };
            if (d.Min is { } min) item["min"] = min;
            if (d.Max is { } max) item["max"] = max;
            if (d.Step is { } step) item["step"] = step;
            if (d.Hint is { } hint) item["hint"] = hint;
            if (d.ClearedKey is { } cleared) item["clearedKey"] = cleared;
            if (d.Options is { } options) item["options"] = new JsonArray(options.Select(o => (JsonNode?)new JsonObject { ["value"] = o.Value, ["label"] = o.Label }).ToArray());
            items.Add(item);
        }
        return new JsonObject { ["groups"] = new JsonArray(Groups.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray()), ["settings"] = items };
    }
}
