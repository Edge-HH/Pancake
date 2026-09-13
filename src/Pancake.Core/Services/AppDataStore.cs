using System.Text.Json;
using Pancake.Models;

namespace Pancake.Services;

/// <summary>
/// 传统单文件数据存储（pancake.json）。目录由调用方传入：桌面端会传入平台数据目录或便携模式目录，
/// 未指定时退回可执行文件旁的 data，保持与旧版本一致的行为。
/// </summary>
public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppDataStore(string? dataDirectory = null) =>
        DataDirectory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data");

    public string DataDirectory { get; }

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
                Name = saved.Name, AccentHex = saved.AccentHex, IsAccentExplicit = saved.IsAccentExplicit,
                X = saved.X, Y = saved.Y, TileWidth = saved.Width, TileHeight = saved.Height
            };
            foreach (HomeworkState item in saved.Entries)
            {
                HomeworkEntry homework = new() { Content = item.Content, RtfContent = item.RtfContent, HasHandwriting = item.HasHandwriting, FontFallbacks = ProjectStore.Clone(item.FontFallbacks ?? []) };
                foreach (AttachmentState attachment in item.Attachments.Where(attachment =>
                    attachment.Kind != "图片" || !string.IsNullOrWhiteSpace(attachment.Path)))
                    homework.Attachments.Add(new AttachmentItem
                    {
                        Name = attachment.Name,
                        Kind = attachment.Kind,
                        Path = attachment.Path,
                        Scale = attachment.Scale <= 0 ? 1 : attachment.Scale,
                        OffsetX = attachment.OffsetX,
                        OffsetY = attachment.OffsetY,
                        ViewportHeight = attachment.ViewportHeight <= 0 ? 180 : attachment.ViewportHeight,
                        FrameWidth = attachment.FrameWidth <= 0 ? 360 : attachment.FrameWidth, AspectRatio = attachment.AspectRatio, Rotation = attachment.Rotation, PositionX = attachment.PositionX, PositionY = attachment.PositionY
                    });
                subject.Entries.Add(homework);
            }
            foreach (InkStrokeData stroke in RestoreInk(saved.InkStrokes)) subject.InkStrokes.Add(stroke);
            result.Add(subject);
        }
        return result;
    }

    /// <summary>把保存用的笔迹还原成可编辑笔迹；磁贴与仅时钟整屏笔迹共用同一套坐标。</summary>
    public static List<InkStrokeData> RestoreInk(IEnumerable<InkStrokeState> source)
    {
        List<InkStrokeData> result = [];
        foreach (InkStrokeState item in source)
        {
            InkStrokeData stroke = new() { Color = ParseColor(item.Color), Thickness = item.Thickness, TipScaleX = item.TipScaleX, TipScaleY = item.TipScaleY };
            stroke.Points.AddRange(item.Points.Select(point => new BoardPoint(point.X, point.Y)));
            result.Add(stroke);
        }
        return result;
    }

    /// <summary>把可编辑笔迹转成保存结构，颜色统一写成带透明度的十六进制。</summary>
    public static List<InkStrokeState> CaptureInk(IEnumerable<InkStrokeData> source) => source.Select(stroke => new InkStrokeState
    {
        Color = stroke.Color.ToHex(),
        Thickness = stroke.Thickness, TipScaleX = stroke.TipScaleX, TipScaleY = stroke.TipScaleY,
        Points = stroke.Points.Select(point => new PointState { X = point.X, Y = point.Y }).ToList()
    }).ToList();

    public static List<SubjectState> CaptureSubjects(IEnumerable<SubjectBoard> source) => source.Select(subject => new SubjectState
    {
        Name = subject.Name, AccentHex = subject.AccentHex, IsAccentExplicit = subject.IsAccentExplicit, InkCoordinateVersion = 1, X = subject.X, Y = subject.Y,
        Width = subject.TileWidth, Height = subject.TileHeight,
        Entries = subject.Entries.Select(item => new HomeworkState
        {
            Content = item.Content, RtfContent = item.RtfContent, HasHandwriting = item.HasHandwriting, FontFallbacks = ProjectStore.Clone(item.FontFallbacks ?? []),
            Attachments = item.Attachments.Select(a => new AttachmentState
            {
                Name = a.Name,
                Kind = a.Kind,
                Path = a.Path,
                Scale = a.Scale,
                OffsetX = a.OffsetX,
                OffsetY = a.OffsetY,
                ViewportHeight = a.ViewportHeight,
                FrameWidth = a.FrameWidth, AspectRatio = a.AspectRatio, Rotation = a.Rotation, PositionX = a.PositionX, PositionY = a.PositionY
            }).ToList()
        }).ToList(),
        InkStrokes = CaptureInk(subject.InkStrokes)
    }).ToList();

    private static BoardColor ParseColor(string value) => BoardColor.Parse(value, BoardColor.FromRgb(245, 245, 247));
}
