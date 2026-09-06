using System.Text.Json;
using Pancake.Models;
using Pancake.ViewModels;
using Windows.Foundation;

namespace Pancake.Services;

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
                Name = saved.Name, AccentHex = saved.AccentHex, IsAccentExplicit = saved.IsAccentExplicit, AccentBrush = MainViewModel.BrushFromHex(saved.AccentHex, !saved.IsAccentExplicit),
                X = saved.X, Y = saved.Y, TileWidth = saved.Width, TileHeight = saved.Height
            };
            foreach (HomeworkState item in saved.Entries)
            {
                HomeworkEntry homework = new() { Content = item.Content, RtfContent = item.RtfContent, HasHandwriting = item.HasHandwriting, FontFallbacks = ProjectStore.Clone(item.FontFallbacks ?? []) };
                foreach (AttachmentState attachment in item.Attachments)
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
            foreach (InkStrokeState item in saved.InkStrokes)
            {
                InkStrokeData stroke = new() { Color = ParseColor(item.Color), Thickness = item.Thickness, TipScaleX = item.TipScaleX, TipScaleY = item.TipScaleY };
                stroke.Points.AddRange(item.Points.Select(point => new Point(point.X, point.Y)));
                subject.InkStrokes.Add(stroke);
            }
            result.Add(subject);
        }
        return result;
    }

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
        InkStrokes = subject.InkStrokes.Select(stroke => new InkStrokeState
        {
            Color = $"#{stroke.Color.A:X2}{stroke.Color.R:X2}{stroke.Color.G:X2}{stroke.Color.B:X2}",
            Thickness = stroke.Thickness, TipScaleX = stroke.TipScaleX, TipScaleY = stroke.TipScaleY,
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
