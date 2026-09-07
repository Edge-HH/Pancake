using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Models;
using Pancake.Services;
using Pancake.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace Pancake.Controls;

/// <summary>导出专用只读磁贴。按原宽度展开内容，布局调整只缩放这个完整视觉对象。</summary>
public sealed class ExportTileVisual : Grid
{
    private readonly SubjectBoard _subject;
    private readonly Grid _layout;
    private readonly Border _frame;
    private readonly TextBlock _name;
    private readonly TextBlock _watermark;
    private readonly List<(RichEditBox Editor, HomeworkEntry Entry)> _editors = [];
    private readonly List<AttachmentImageControl> _images = [];
    private readonly Canvas _ink = new() { IsHitTestVisible = false };
    private readonly TaskCompletionSource _loaded = new();
    private Color _background;
    private string _palette;

    public ExportTileVisual(SubjectBoard subject, Color background, string palette)
    {
        _subject = subject; _background = background; _palette = palette;
        Width = subject.TileWidth;
        _layout = new Grid { Width = subject.TileWidth, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Children.Add(_layout);
        StackPanel content = new() { Spacing = 8, Padding = new Thickness(14, 10, 10, 10) };
        _frame = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(3), Child = content };
        _layout.Children.Add(_frame);
        _name = new TextBlock { Text = subject.Name, FontSize = 29, FontWeight = FontWeights.SemiBold, MinHeight = 34 };
        content.Children.Add(_name);
        _watermark = new TextBlock { Text = subject.Name, FontSize = 72, FontWeight = FontWeights.Bold, Opacity = .16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        _layout.Children.Add(_watermark);
        int index = 0;
        foreach (HomeworkEntry homework in subject.Entries)
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = $"{++index}.", FontSize = 20, Margin = new Thickness(0, 4, 0, 0) });
            RichEditBox editor = new()
            {
                FontFamily = FontService.DefaultFamily, FontSize = 20, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Padding = new Thickness(2, 0, 2, 0), MinHeight = 42, IsHitTestVisible = false
            };
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Disabled);
            Grid.SetColumn(editor, 1); row.Children.Add(editor); content.Children.Add(row);
            _editors.Add((editor, homework));
            foreach (AttachmentItem attachment in homework.Attachments)
            {
                if (attachment.Kind != "图片") continue;
                AttachmentImageControl image = new(attachment, () => { }, () => { }, _ => { }) { Margin = new Thickness(29, -4, 4, 4) };
                image.SetEditing(false); _images.Add(image); content.Children.Add(image);
            }
        }
        _layout.Children.Add(_ink);
        Loaded += (_, _) => _loaded.TrySetResult();
    }

    public async Task PrepareAsync()
    {
        await _loaded.Task;
        SetAppearance(_background, _palette);
        foreach (AttachmentImageControl image in _images)
            if (!await image.ImageReady) throw new IOException("有图片无法解码，请恢复附件后重新导出。");
        _layout.Measure(new Size(_subject.TileWidth, double.PositiveInfinity));
        _layout.Height = _layout.DesiredSize.Height;
        Height = _layout.Height;
        UpdateLayout();
        double left = 0, top = 0, right = _subject.TileWidth, bottom = _layout.Height;
        foreach (AttachmentImageControl image in _images)
        {
            Rect bounds = image.TransformToVisual(_layout).TransformBounds(new Rect(0, 0, image.ActualWidth, image.ActualHeight));
            left = Math.Min(left, bounds.Left); top = Math.Min(top, bounds.Top);
            right = Math.Max(right, bounds.Right); bottom = Math.Max(bottom, bounds.Bottom);
        }
        foreach (InkStrokeData stroke in _subject.InkStrokes)
            foreach (Point point in stroke.Points)
            {
                left = Math.Min(left, point.X - stroke.Thickness * stroke.TipScaleX / 2);
                right = Math.Max(right, point.X + stroke.Thickness * stroke.TipScaleX / 2);
                top = Math.Min(top, point.Y - stroke.Thickness * stroke.TipScaleY / 2);
                bottom = Math.Max(bottom, point.Y + stroke.Thickness * stroke.TipScaleY / 2);
            }
        Width = right - left; Height = bottom - top;
        _layout.Height = Math.Max(_layout.Height, bottom);
        _layout.RenderTransform = new TranslateTransform { X = -left, Y = -top };
        UpdateLayout();
    }

    public void SetAppearance(Color background, string palette)
    {
        _background = background; _palette = palette;
        bool light = (.2126 * background.R + .7152 * background.G + .0722 * background.B) > 145;
        Color foreground = light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        Background = _frame.Background = new SolidColorBrush(background);
        string accent = ColorPalette.ResolveAccent(_subject.AccentHex, _subject.IsAccentExplicit, palette == "Macaron");
        Brush accentBrush = MainViewModel.BrushFromHex(accent);
        _name.Foreground = _watermark.Foreground = _frame.BorderBrush = accentBrush;
        foreach (var pair in _editors)
        {
            RichEditBox editor = pair.Editor;
            editor.IsReadOnly = false;
            editor.Document.SetText(string.IsNullOrWhiteSpace(pair.Entry.RtfContent) ? TextSetOptions.None : TextSetOptions.FormatRtf,
                string.IsNullOrWhiteSpace(pair.Entry.RtfContent) ? pair.Entry.Content : DefaultContentColors.AdaptRtf(pair.Entry.RtfContent, light));
            if (string.IsNullOrWhiteSpace(pair.Entry.RtfContent)) editor.Document.GetRange(0, int.MaxValue).CharacterFormat.ForegroundColor = foreground;
            FontService.RebindBundledFont(editor, pair.Entry.FontFallbacks);
            editor.Foreground = new SolidColorBrush(foreground);
            editor.IsReadOnly = true;
        }
        foreach (TextBlock text in Descendants<TextBlock>(_layout))
            if (text != _name && text != _watermark) text.Foreground = new SolidColorBrush(foreground);
        _ink.Children.Clear();
        foreach (InkStrokeData stroke in _subject.InkStrokes)
        {
            Color color = light && DefaultContentColors.IsDefaultWhite(stroke.Color.R, stroke.Color.G, stroke.Color.B) ? Microsoft.UI.Colors.Black : stroke.Color;
            Polyline line = new()
            {
                Stroke = new SolidColorBrush(color), StrokeThickness = stroke.Thickness,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new ScaleTransform { ScaleX = stroke.TipScaleX, ScaleY = stroke.TipScaleY }
            };
            foreach (Point p in stroke.Points) line.Points.Add(new Point(p.X / stroke.TipScaleX, p.Y / stroke.TipScaleY));
            _ink.Children.Add(line);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
