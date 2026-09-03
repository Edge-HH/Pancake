using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace PancakeBoard.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AttachmentItem : ObservableObject
{
    public required string Name { get; init; }
    public string Kind { get; init; } = "文件";
    public string Path { get; init; } = string.Empty;
    public string Glyph => Kind == "图片" ? "\uEB9F" : Kind == "PDF" ? "\uEA90" : "\uE8A5";
}

public sealed class HomeworkEntry : ObservableObject
{
    private string _content = string.Empty;
    private string _rtfContent = string.Empty;
    private bool _hasHandwriting;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public string RtfContent
    {
        get => _rtfContent;
        set => SetProperty(ref _rtfContent, value);
    }

    public bool HasHandwriting
    {
        get => _hasHandwriting;
        set
        {
            if (SetProperty(ref _hasHandwriting, value))
            {
                RaisePropertyChanged(nameof(SupplementSummary));
            }
        }
    }

    public ObservableCollection<AttachmentItem> Attachments { get; } = [];

    public string SupplementSummary
    {
        get
        {
            List<string> details = [];
            if (HasHandwriting)
            {
                details.Add("手写笔记");
            }

            if (Attachments.Count > 0)
            {
                details.Add($"{Attachments.Count} 个附件");
            }

            return details.Count == 0 ? "仅文字" : string.Join(" · ", details);
        }
    }

    public void NotifyAttachmentsChanged() => RaisePropertyChanged(nameof(SupplementSummary));

    public HomeworkEntry Clone()
    {
        HomeworkEntry clone = new() { Content = Content, RtfContent = RtfContent, HasHandwriting = HasHandwriting };
        foreach (AttachmentItem attachment in Attachments)
        {
            clone.Attachments.Add(new AttachmentItem
            {
                Name = attachment.Name,
                Kind = attachment.Kind,
                Path = attachment.Path
            });
        }

        return clone;
    }
}

public sealed class InkStrokeData
{
    public Windows.UI.Color Color { get; init; } = Windows.UI.Color.FromArgb(255, 245, 245, 247);
    public double Thickness { get; init; } = 5;
    public List<Point> Points { get; } = [];

    public InkStrokeData Clone()
    {
        InkStrokeData clone = new() { Color = Color, Thickness = Thickness };
        clone.Points.AddRange(Points);
        return clone;
    }
}

public sealed class SubjectBoard : ObservableObject
{
    private string _name = string.Empty;
    private double _x;
    private double _y;
    private double _tileWidth = 430;
    private double _tileHeight = 320;
    private string _accentHex = "#818CF8";
    private SolidColorBrush _accentBrush = new(Windows.UI.Color.FromArgb(255, 129, 140, 248));

    public string AccentHex
    {
        get => _accentHex;
        set => SetProperty(ref _accentHex, value);
    }

    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        set => SetProperty(ref _accentBrush, value);
    }
    public ObservableCollection<HomeworkEntry> Entries { get; } = [];
    public ObservableCollection<InkStrokeData> InkStrokes { get; } = [];

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                RaisePropertyChanged(nameof(Watermark));
                RaisePropertyChanged(nameof(CountLabel));
            }
        }
    }

    public string Watermark => Name;
    public string CountLabel => $"{Entries.Count} 条内容";

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double TileWidth
    {
        get => _tileWidth;
        set => SetProperty(ref _tileWidth, value);
    }

    public double TileHeight
    {
        get => _tileHeight;
        set => SetProperty(ref _tileHeight, value);
    }

    public void NotifyEntriesChanged() => RaisePropertyChanged(nameof(CountLabel));

    public SubjectBoard Clone()
    {
        SubjectBoard clone = new()
        {
            Name = Name,
            AccentHex = AccentHex,
            AccentBrush = new SolidColorBrush(AccentBrush.Color),
            X = X,
            Y = Y,
            TileWidth = TileWidth,
            TileHeight = TileHeight
        };

        foreach (HomeworkEntry entry in Entries)
        {
            clone.Entries.Add(entry.Clone());
        }

        foreach (InkStrokeData stroke in InkStrokes)
        {
            clone.InkStrokes.Add(stroke.Clone());
        }

        return clone;
    }
}
