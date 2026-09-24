namespace Pancake.Services;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public BoardSettingsState Settings { get; set; } = new();
    public List<SubjectState> Subjects { get; set; } = [];
}

public sealed class BoardSettingsState
{
    public const string DefaultTileBodyFontFamily = "HarmonyOS Sans SC";

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
    public double TileBodyFontSize { get; set; } = 20;
    public string TileBodyFontFamily { get; set; } = DefaultTileBodyFontFamily;
    public bool TileBodyBold { get; set; }
    public bool TileBodyItalic { get; set; }
    public bool PastePlainTextOnly { get; set; }
    public BackgroundSettings TileBackground { get; set; } = new();
    public bool SharedBackgroundEnabled { get; set; }
    public BackgroundSettings SharedBackground { get; set; } = new();
    public BackgroundSettings ClockBackground { get; set; } = new();
    public BackgroundSettings BoardBackground { get; set; } = new();
    public bool ToolbarIconOnly { get; set; } = true;
    public string ToolbarPosition { get; set; } = "BottomCenter";
    public double ToolbarScale { get; set; } = 1;
    public double ToolbarRadius { get; set; } = 14;
    public double ToolbarBorderThickness { get; set; } = 1;
    // 空字符串表示跟随主题线条色；设为固定颜色后切换深浅主题不再变色。
    public string ToolbarBorderColor { get; set; } = "";
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
    /// <summary>允许多实例：默认关闭，关闭后同一安装目录只允许同时打开一个软件窗口。</summary>
    public bool AllowMultipleInstances { get; set; }
    /// <summary>已打开时再次启动的行为：Foreground（移至前台）、FullScreen（全屏）、None（不执行任何操作）。</summary>
    public string SecondLaunchAction { get; set; } = "Foreground";
    public AutofillSettings Autofill { get; set; } = new();
    public LockSettings Lock { get; set; } = new();
    public DataSettings Data { get; set; } = new();

    /// <summary>再次启动行为只认三种取值；旧配置或手改值一律回到“移至前台”。</summary>
    public static string NormalizeSecondLaunchAction(string? value) =>
        value is "Foreground" or "FullScreen" or "None" ? value : "Foreground";
}

/// <summary>数据页设置：作业保留、备份与自动备份属于软件设置，跨项目共享。</summary>
public sealed class DataSettings
{
    /// <summary>应用内作业保留天数，0 表示永久保留；创建时间超出该时间的作业项目会被清除。</summary>
    public int JobRetentionDays { get; set; } = 30;
    public BackupSettings Backup { get; set; } = new();
    public BackupSettings AutoBackup { get; set; } = new();
}

/// <summary>备份的范围与位置；手动备份只用 Scopes，自动备份还使用目录、间隔和保留份数。</summary>
public sealed class BackupSettings
{
    public BackupScopes Scopes { get; set; } = BackupScopes.All;
    public bool Enabled { get; set; }
    public string Directory { get; set; } = "";
    /// <summary>自动备份间隔小时数，至少 1。</summary>
    public double IntervalHours { get; set; } = 24;
    /// <summary>自动备份保留份数，超出后删除最旧的备份。</summary>
    public int KeepCount { get; set; } = 7;
}

/// <summary>备份范围：四类内容可任意组合，默认全开。</summary>
[Flags]
public enum BackupScopes
{
    None = 0,
    Jobs = 1,
    CurrentBackground = 2,
    RecentBackgrounds = 4,
    Settings = 8,
    All = Jobs | CurrentBackground | RecentBackgrounds | Settings
}

public static class BackupScopeExtensions
{
    public static bool Has(this BackupScopes scopes, BackupScopes flag) => (scopes & flag) != 0;
    /// <summary>存档里只保留合法位，旧版本或手改的数值不能带进新备份。</summary>
    public static BackupScopes Sanitize(this BackupScopes scopes) => scopes & BackupScopes.All;
}

/// <summary>
/// 锁定设置属于软件设置，跨项目共享；只保存密码哈希与 2FA 密钥，不保存明文。
/// 总开关开启但密码与 2FA 都未配置时不生效，具体判断见 LockService.IsEnforced。
/// </summary>
public sealed class LockSettings
{
    public bool Enabled { get; set; }
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    /// <summary>编辑看板前要求验证；默认关闭。</summary>
    public bool RequireAuthForEditing { get; set; }
    /// <summary>打开设置前要求验证；默认开启。</summary>
    public bool RequireAuthForSettings { get; set; } = true;
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
    /// <summary>新增作业时编辑框里的灰色占位提示：只用于显示，不写进作业正文。</summary>
    public const string PlaceholderText = "在这里输入作业内容";

    public string Content { get; set; } = string.Empty;
    public string RtfContent { get; set; } = string.Empty;
    public bool HasHandwriting { get; set; }
    public List<AttachmentState> Attachments { get; set; } = [];
    public List<FontFallbackState> FontFallbacks { get; set; } = [];

    /// <summary>旧版本把占位提示当成正文写入；读取项目时按空内容还原，让它只作为灰色提示显示。</summary>
    public void ClearLegacyPlaceholder()
    {
        if (!string.Equals((Content ?? string.Empty).Trim(), PlaceholderText, StringComparison.Ordinal)) return;
        Content = string.Empty;
        RtfContent = string.Empty;
    }
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

