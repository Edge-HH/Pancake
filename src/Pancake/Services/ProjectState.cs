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
    public string ImagePath { get; set; } = "";
    public string ImageMode { get; set; } = "Zoom";
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

