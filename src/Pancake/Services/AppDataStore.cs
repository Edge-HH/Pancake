using System.Text.Json;
using Pancake.Models;
using Pancake.ViewModels;
using Windows.Foundation;

namespace Pancake.Services;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public BoardSettingsState Settings { get; set; } = new();
    public List<SubjectState> Subjects { get; set; } = [];
}

public sealed class BoardSettingsState
{
    public string Theme { get; set; } = "Dark";
    public int MicrophoneSampleRate { get; set; } = 16000;
    public double MicrophoneCalibrationDb { get; set; }
    public string WeatherCityName { get; set; } = "北京";
    public string WeatherCityCode { get; set; } = "101010100";
    public bool GridSnappingEnabled { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; } = true;
    public string UpdateRepository { get; set; } = "Edge-HH/Pancake";
}

public sealed class SubjectState
{
    public string Name { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#818CF8";
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
}

public sealed class AttachmentState
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "文件";
    public string Path { get; set; } = string.Empty;
}

public sealed class InkStrokeState
{
    public string Color { get; set; } = "#FFF7F7F9";
    public double Thickness { get; set; }
    public List<PointState> Points { get; set; } = [];
}

public sealed class PointState
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>把可迁移数据固定存放在可执行文件旁的 data 目录。</summary>
public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");
    public string StatePath => Path.Combine(DataDirectory, "pancake.json");

    public AppState? Load()
    {
        if (!File.Exists(StatePath)) return null;
        return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), JsonOptions);
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);
        string temporaryPath = StatePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, StatePath, true);
    }

    public static List<SubjectBoard> RestoreSubjects(IEnumerable<SubjectState> source)
    {
        List<SubjectBoard> result = [];
        foreach (SubjectState saved in source)
        {
            SubjectBoard subject = new()
            {
                Name = saved.Name, AccentHex = saved.AccentHex, AccentBrush = MainViewModel.BrushFromHex(saved.AccentHex),
                X = saved.X, Y = saved.Y, TileWidth = saved.Width, TileHeight = saved.Height
            };
            foreach (HomeworkState item in saved.Entries)
            {
                HomeworkEntry homework = new() { Content = item.Content, RtfContent = item.RtfContent, HasHandwriting = item.HasHandwriting };
                foreach (AttachmentState attachment in item.Attachments)
                    homework.Attachments.Add(new AttachmentItem { Name = attachment.Name, Kind = attachment.Kind, Path = attachment.Path });
                subject.Entries.Add(homework);
            }
            foreach (InkStrokeState item in saved.InkStrokes)
            {
                InkStrokeData stroke = new() { Color = ParseColor(item.Color), Thickness = item.Thickness };
                stroke.Points.AddRange(item.Points.Select(point => new Point(point.X, point.Y)));
                subject.InkStrokes.Add(stroke);
            }
            result.Add(subject);
        }
        return result;
    }

    public static List<SubjectState> CaptureSubjects(IEnumerable<SubjectBoard> source) => source.Select(subject => new SubjectState
    {
        Name = subject.Name, AccentHex = subject.AccentHex, X = subject.X, Y = subject.Y,
        Width = subject.TileWidth, Height = subject.TileHeight,
        Entries = subject.Entries.Select(item => new HomeworkState
        {
            Content = item.Content, RtfContent = item.RtfContent, HasHandwriting = item.HasHandwriting,
            Attachments = item.Attachments.Select(a => new AttachmentState { Name = a.Name, Kind = a.Kind, Path = a.Path }).ToList()
        }).ToList(),
        InkStrokes = subject.InkStrokes.Select(stroke => new InkStrokeState
        {
            Color = $"#{stroke.Color.A:X2}{stroke.Color.R:X2}{stroke.Color.G:X2}{stroke.Color.B:X2}",
            Thickness = stroke.Thickness,
            Points = stroke.Points.Select(point => new PointState { X = point.X, Y = point.Y }).ToList()
        }).ToList()
    }).ToList();

    private static Windows.UI.Color ParseColor(string value)
    {
        string hex = value.TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        return Windows.UI.Color.FromArgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16), Convert.ToByte(hex.Substring(6, 2), 16));
    }
}
