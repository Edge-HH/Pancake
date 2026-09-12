namespace Pancake.Services;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public BoardSettingsState Settings { get; set; } = new();
    public List<SubjectState> Subjects { get; set; } = [];
}

public sealed class BoardSettingsState
{
    public string LayoutMode { get; set; } = "Split";
    public double SplitRatio { get; set; } = 0.4;
    public int DockedWidgetLayoutVersion { get; set; }
    // 按布局模式和稳定组件标识保存位置，避免受约束布局覆盖自由布局坐标。
    public Dictionary<string, RegionPlacement> Widgets { get; set; } = new();
    public HashSet<string> CustomizedDockedWidgets { get; set; } = [];
    public bool InfiniteBoard { get; set; }
    public double GridSize { get; set; } = 48;
    public double AutoLayoutGap { get; set; } = 0;
    public bool AutoLayoutAlign { get; set; } = true;
    public bool AutoLayoutResize { get; set; } = true;
    public string GridStyle { get; set; } = "Grid";
    public string GridColor { get; set; } = "#6956565C";
    public double GridLineThickness { get; set; } = 1.6;
    public string GridDotColor { get; set; } = "#8F56565C";
    public double GridDotDiameter { get; set; } = 3;
    public bool ShowGridWhileEditing { get; set; } = true;
    public double TileTitleSize { get; set; } = 29;
    public BackgroundSettings TileBackground { get; set; } = new();
    public bool SharedBackgroundEnabled { get; set; }
    public BackgroundSettings SharedBackground { get; set; } = new();
    public BackgroundSettings ClockBackground { get; set; } = new();
    public BackgroundSettings BoardBackground { get; set; } = new();
    public bool ToolbarIconOnly { get; set; } = true;
    public string ToolbarPosition { get; set; } = "BottomCenter";
    public double ToolbarScale { get; set; } = 1;
    public double ToolbarRadius { get; set; } = 14;
    public double ToolbarHorizontalInset { get; set; } = 24;
    public double ToolbarVerticalInset { get; set; } = 24;
    public bool ToolbarGlass { get; set; }
    public double ToolbarBlur { get; set; } = 20;
    public string ToolbarBackgroundColor { get; set; } = "";
    public double ToolbarBackgroundOpacity { get; set; } = 0.8;
    public bool ToolbarBackgroundColorCleared { get; set; }
    public bool ToolbarAutoHide { get; set; }
    public double ToolbarAutoHideSeconds { get; set; } = 5;
    public string ToolbarHideAnimation { get; set; } = "Fade";
    public string UpdateSource { get; set; } = "GitHub";
    public string Theme { get; set; } = "Dark";
    public string Palette { get; set; } = "Vivid";
    public double NoiseIntervalSeconds { get; set; } = 0.1;
    public string MicrophoneDeviceId { get; set; } = "";
    public double NoiseThresholdDb { get; set; } = 60;
    public bool NoiseAlertEnabled { get; set; }
    public bool PauseNoiseWhenMinimized { get; set; }
    public double NoiseAlertVolume { get; set; } = 1;
    public double CalibrationTargetDb { get; set; } = 40;
    public bool ShowWeatherAlerts { get; set; } = true;
    // 保留旧配置字段以兼容已有数据；共享采集格式现在由 Windows 输入设备决定。
    public int MicrophoneSampleRate { get; set; } = 16000;
    public double MicrophoneCalibrationDb { get; set; }
    public string WeatherCityName { get; set; } = "北京";
    public string WeatherCityCode { get; set; } = "101010100";
    public bool GridSnappingEnabled { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; } = true;
    public AutofillSettings Autofill { get; set; } = new();
}

/// <summary>自动填充设置属于软件设置，跨项目共享；默认全部关闭，升级后不改变原有输入行为。</summary>
public sealed class AutofillSettings
{
    public SubjectCompletionSettings Subject { get; set; } = new();
    public HomeworkCompletionSettings Homework { get; set; } = new();
}

/// <summary>学科补全：候选只来自内置清单与设置页手动添加项，程序不会自动收录项目科目。</summary>
public sealed class SubjectCompletionSettings
{
    public bool Enabled { get; set; }
    // 匹配程度：Loose（1 个字符即提示且允许首字母跳字）、Normal、Strict（只认完整前缀）。
    public string MatchLevel { get; set; } = "Normal";
    public List<SubjectSuggestion> Subjects { get; set; } = [];
}

public sealed class SubjectSuggestion
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = "#818CF8";
    // 随机配色：开启后每次补全都从预设色板里随机取一个，不再固定使用 Color。
    public bool RandomColor { get; set; }
    // 来源：BuiltIn（内置清单）、Manual（设置页添加）；Learned 仅为兼容旧版本已自动登记的条目。
    public string Source { get; set; } = "Manual";
    public DateTime LastUsedAt { get; set; }
}

/// <summary>作业补全：统计常输入的作业名称，达到阈值后进入候选。</summary>
public sealed class HomeworkCompletionSettings
{
    public bool Enabled { get; set; }
    public bool AutoRecord { get; set; } = true;
    // 收录阈值档位，与学科补全共用 Loose/Normal/Strict 命名，但表示出现次数（2/3/5）。
    public string RecordLevel { get; set; } = "Normal";
    // 记录隔离：Global（全部记进全局桶）或 Subject（自动学习写入当前学科桶，全局桶始终可用）。
    public string Isolation { get; set; } = "Global";
    public List<HomeworkSuggestion> Items { get; set; } = [];
    public List<string> Blocked { get; set; } = [];
}

public sealed class HomeworkSuggestion
{
    public string Text { get; set; } = string.Empty;
    // 来源：Auto（自动学习）、Manual（设置页添加，永不过期）。
    public string Source { get; set; } = "Auto";
    public bool IsGlobal { get; set; } = true;
    public List<string> Subjects { get; set; } = [];
    public int Count { get; set; }
    public bool Promoted { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class SubjectState
{
    public string Name { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#818CF8";
    public bool IsAccentExplicit { get; set; } = true;
    public int InkCoordinateVersion { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public List<HomeworkState> Entries { get; set; } = [];
    public List<InkStrokeState> InkStrokes { get; set; } = [];
}

public sealed class HomeworkState
{
    public string Content { get; set; } = string.Empty;
    public string RtfContent { get; set; } = string.Empty;
    public bool HasHandwriting { get; set; }
    public List<AttachmentState> Attachments { get; set; } = [];
    public List<FontFallbackState> FontFallbacks { get; set; } = [];
}

/// <summary>背景样式属于软件设置，资源复制到 data 后不依赖原图片位置。</summary>
public sealed class BackgroundSettings
{
    public string Color { get; set; } = "";
    // 磁贴的颜色层独立于媒体和模糊；空颜色仍表示主题色，显式清除单独保存。
    public double ColorOpacity { get; set; } = 0.8;
    public bool ColorCleared { get; set; }
    public string ImagePath { get; set; } = "";
    public string ImageMode { get; set; } = "Zoom";
    public bool PlaylistEnabled { get; set; }
    public List<string> Playlist { get; set; } = [];
    public bool Shuffle { get; set; }
    public bool SwitchOnTimer { get; set; } = true;
    public double SwitchIntervalSeconds { get; set; } = 60;
    public bool SwitchOnMediaEnded { get; set; } = true;
    public bool Glass { get; set; }
    public double Blur { get; set; } = 20;
}

public sealed class RegionPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>缺失字体的原始名称与 UTF-16 范围，显示回退不能抹掉用户的字体选择。</summary>
public sealed class FontFallbackState
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string Family { get; set; } = "";
}

public sealed class AttachmentState
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "文件";
    public string Path { get; set; } = string.Empty;
    public double Scale { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double ViewportHeight { get; set; } = 180;
    public double FrameWidth { get; set; } = 360;
    public double AspectRatio { get; set; }
    public double Rotation { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}

public sealed class InkStrokeState
{
    public string Color { get; set; } = "#FFF7F7F9";
    public double Thickness { get; set; }
    public double TipScaleX { get; set; } = 1;
    public double TipScaleY { get; set; } = 1;
    public List<PointState> Points { get; set; } = [];
}

public sealed class PointState
{
    public double X { get; set; }
    public double Y { get; set; }
}

