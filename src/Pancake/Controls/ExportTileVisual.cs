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
    private readonly Grid _surface = new() { IsHitTestVisible = false };
    private readonly Image _backdrop = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _tint = new();
    private readonly double _originalWidth, _originalHeight;
    private Color _background;
    private string _palette;

    public ExportTileVisual(SubjectBoard subject, Color background, string palette, double titleSize = 29)
    {
        _subject = subject; _background = background; _palette = palette;
        _originalWidth = subject.TileWidth; _originalHeight = subject.TileHeight;
        Width = subject.TileWidth;
        _surface.Children.Add(_backdrop); _surface.Children.Add(_tint);
        Children.Add(_surface);
        _layout = new Grid { Width = subject.TileWidth, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Children.Add(_layout);
        StackPanel content = new() { Spacing = 8, Padding = new Thickness(14, 10, 10, 10) };
        _frame = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(3), Child = content };
        _layout.Children.Add(_frame);
        _name = new TextBlock { Text = subject.Name, FontSize = titleSize, FontWeight = FontWeights.SemiBold, MinHeight = 34, TextWrapping = TextWrapping.Wrap };
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
        FitContent(false);
    }

    internal void FitContent(bool resize)
    {
        _layout.Height = double.NaN;
        _layout.Width = _originalWidth;
        _layout.Measure(new Size(_originalWidth, double.PositiveInfinity));
        double height = _layout.DesiredSize.Height;
        double width = _originalWidth;
        if (resize)
        {
            _layout.Width = double.NaN;
            _layout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double contentWidth = Math.Max(_name.DesiredSize.Width + 30,
                _editors.Select(pair => RichTextContentMetrics.Width(pair.Editor) + 65).DefaultIfEmpty(0).Max());
            contentWidth = Math.Max(contentWidth, _images.Select(image => image.DesiredSize.Width + 60).DefaultIfEmpty(0).Max());
            width = Math.Max(280, Math.Min(_originalWidth, contentWidth));
            double inkRight = 0, inkBottom = 0;
            foreach (var stroke in _subject.InkStrokes)
            foreach (var p in stroke.Points)
            {
                // 越界笔迹不扩张导出磁贴，裁切始终以原磁贴矩形为边界。
                inkRight = Math.Max(inkRight, Math.Min(_originalWidth, p.X + stroke.Thickness * stroke.TipScaleX / 2));
                inkBottom = Math.Max(inkBottom, Math.Min(_originalHeight, p.Y + stroke.Thickness * stroke.TipScaleY / 2));
            }
            width = Math.Max(width, inkRight);
            _layout.Width = width;
            _layout.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Max(_layout.DesiredSize.Height, inkBottom);
        }
        SetSize(width, resize ? Math.Max(96, height) : _originalHeight);
        _ink.Clip = new RectangleGeometry { Rect = new Rect(0, 0, _originalWidth, _originalHeight) };
    }

    internal void SetSize(double width, double height)
    {
        Width = _layout.Width = width; Height = _layout.Height = height;
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
        _surface.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
    }

    internal void SetSurface(ImageSource? backdrop, double canvasWidth, double canvasHeight,
        ExportTilePlacement placement, BackgroundSettings style, ImageSource? tileImage)
    {
        // 把同一张模糊后的画布按磁贴位置取景，预览与 PNG 都使用可栅格化的像素层。
        _backdrop.Source = style.Glass ? backdrop : null;
        _backdrop.Width = canvasWidth / placement.Scale; _backdrop.Height = canvasHeight / placement.Scale;
        _backdrop.RenderTransform = new TranslateTransform { X = -placement.X / placement.Scale, Y = -placement.Y / placement.Scale };
        _surface.Background = tileImage is null ? null : new ImageBrush { ImageSource = tileImage, Stretch = Stretch.UniformToFill };
        _tint.Background = SurfaceBackground.Create(style.Color, style.ColorOpacity, style.ColorCleared, false, 0);
    }

    public void SetAppearance(Color background, string palette)
    {
        _background = background; _palette = palette;
        bool light = (.2126 * background.R + .7152 * background.G + .0722 * background.B) > 145;
        Color foreground = light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        Background = _frame.Background = null;
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
