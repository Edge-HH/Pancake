using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PancakeBoard.Models;
using Windows.Foundation;

namespace PancakeBoard.Controls;

/// <summary>
/// A subject tile owns its inline text editor, normalized ink surface, and move/resize handles.
/// Keeping those interactions together prevents the board page from becoming a second editor screen.
/// </summary>
public sealed class SubjectTileControl : Grid
{
    private const double InkSurfaceWidth = 1000;
    private const double InkSurfaceHeight = 600;
    private const double MinimumTileWidth = 280;
    private const double MinimumTileHeight = 190;
    private const double MaximumTileWidth = 900;
    private const double MaximumTileHeight = 680;

    private readonly SubjectBoard _subject;
    private readonly Action<SubjectBoard> _deleteSubject;
    private readonly Action<SubjectBoard, double, double> _moveSubject;
    private readonly Func<HomeworkEntry, Task> _addAttachment;
    private readonly Canvas _inkCanvas = new() { Width = InkSurfaceWidth, Height = InkSurfaceHeight, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
    private readonly StackPanel _entriesPanel = new() { Spacing = 8 };
    private readonly StackPanel _editingTools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly TextBox _nameEditor;
    private readonly ToggleButton _drawButton;
    private readonly Thumb _resizeThumb;
    private readonly Grid _resizeHandle;
    private bool _isEditing;
    private bool _isDrawing;
    private InkStrokeData? _activeStrokeData;
    private Polyline? _activeStrokeShape;

    public SubjectTileControl(
        SubjectBoard subject,
        Action<SubjectBoard> deleteSubject,
        Action<SubjectBoard, double, double> moveSubject,
        Func<HomeworkEntry, Task> addAttachment)
    {
        _subject = subject;
        _deleteSubject = deleteSubject;
        _moveSubject = moveSubject;
        _addAttachment = addAttachment;

        Width = subject.TileWidth;
        Height = subject.TileHeight;
        MinWidth = MinimumTileWidth;
        MinHeight = MinimumTileHeight;

        Border frame = new()
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 31, 31)),
            BorderBrush = subject.AccentBrush,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(3)
        };
        Children.Add(frame);

        Grid content = new() { Padding = new Thickness(14, 10, 10, 10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = content;

        TextBlock watermark = new()
        {
            Text = subject.Watermark,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -10),
            FontSize = 72,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = subject.AccentBrush,
            Opacity = 0.16,
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(watermark, 3);
        content.Children.Add(watermark);

        Grid header = new() { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(header);

        _nameEditor = CreateInlineEditor(subject.Name, 29, subject.AccentBrush, true);
        _nameEditor.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _nameEditor.TextChanged += (_, _) =>
        {
            _subject.Name = _nameEditor.Text;
            watermark.Text = _subject.Watermark;
        };
        header.Children.Add(_nameEditor);
        Canvas.SetZIndex(header, 30);

        _drawButton = CreateIconToggle("\uED63", "直接在磁贴上涂画");
        _drawButton.Checked += (_, _) => SetDrawing(true);
        _drawButton.Unchecked += (_, _) => SetDrawing(false);
        _editingTools.Children.Add(_drawButton);
        _editingTools.Children.Add(CreateIconButton("\uE7A7", "撤销最后一笔", (_, _) => UndoLastStroke()));
        _editingTools.Children.Add(CreateIconButton("\uE74D", "清空笔迹", (_, _) => ClearStrokes()));
        _editingTools.Children.Add(CreateIconButton("\uE710", "添加一条作业", (_, _) => AddHomework()));

        Grid moveHandle = new() { Width = 38, Height = 38 };
        moveHandle.Children.Add(new FontIcon
        {
            Glyph = "\uE759",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 15,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 225)),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Thumb moveThumb = new() { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
        moveThumb.DragDelta += (_, args) => _moveSubject(_subject, args.HorizontalChange, args.VerticalChange);
        moveHandle.Children.Add(moveThumb);
        ToolTipService.SetToolTip(moveHandle, "拖动磁贴");
        _editingTools.Children.Add(moveHandle);
        _editingTools.Children.Add(CreateIconButton("\uE74D", "删除科目", (_, _) => _deleteSubject(_subject), true));
        Grid.SetColumn(_editingTools, 1);
        header.Children.Add(_editingTools);

        ScrollViewer entriesScroller = new()
        {
            Content = _entriesPanel,
            Margin = new Thickness(0, 8, 4, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(entriesScroller, 1);
        content.Children.Add(entriesScroller);

        TextBlock hint = new()
        {
            Text = "拖动右下角调整大小",
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 158)),
            Margin = new Thickness(2, 3, 0, 0)
        };
        Grid.SetRow(hint, 2);
        content.Children.Add(hint);

        Viewbox inkView = new()
        {
            Stretch = Stretch.Fill,
            Child = _inkCanvas,
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(inkView, 3);
        Canvas.SetZIndex(inkView, 20);
        content.Children.Add(inkView);
        _inkCanvas.PointerPressed += InkCanvas_PointerPressed;
        _inkCanvas.PointerMoved += InkCanvas_PointerMoved;
        _inkCanvas.PointerReleased += InkCanvas_PointerReleased;
        _inkCanvas.PointerCanceled += InkCanvas_PointerReleased;
        _drawButton.Tag = inkView;

        _resizeHandle = new Grid
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        _resizeHandle.Children.Add(new FontIcon
        {
            Glyph = "\uE740",
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 13,
            Foreground = subject.AccentBrush,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        _resizeThumb = new Thumb { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
        _resizeThumb.DragDelta += ResizeThumb_DragDelta;
        _resizeHandle.Children.Add(_resizeThumb);
        Canvas.SetZIndex(_resizeHandle, 30);
        Children.Add(_resizeHandle);

        _subject.Entries.CollectionChanged += Entries_CollectionChanged;
        RebuildEntries();
        RenderStoredStrokes();
        SetEditing(false);
    }

    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _editingTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _resizeThumb.IsHitTestVisible = editing;
        _resizeHandle.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _nameEditor.IsReadOnly = !editing;
        _nameEditor.IsHitTestVisible = editing;
        SetDrawing(false);
        RebuildEntries();
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildEntries();

    private void RebuildEntries()
    {
        _entriesPanel.Children.Clear();
        int index = 1;
        foreach (HomeworkEntry homework in _subject.Entries)
        {
            Grid row = new() { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = $"{index}.",
                FontSize = 20,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 247)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            TextBox editor = CreateInlineEditor(homework.Content, 20, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 247)), false);
            editor.AcceptsReturn = true;
            editor.TextWrapping = TextWrapping.Wrap;
            editor.IsReadOnly = !_isEditing;
            editor.IsHitTestVisible = _isEditing;
            editor.TextChanged += (_, _) => homework.Content = editor.Text;
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            if (_isEditing)
            {
                StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
                actions.Children.Add(CreateIconButton("\uE723", "添加附件", async (_, _) => await _addAttachment(homework)));
                actions.Children.Add(CreateIconButton("\uE74D", "删除这条作业", (_, _) => DeleteHomework(homework), true));
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
            }

            _entriesPanel.Children.Add(row);
            if (homework.Attachments.Count > 0)
            {
                TextBlock attachmentSummary = new()
                {
                    Text = $"{homework.Attachments.Count} 个附件",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 164, 164, 180)),
                    Margin = new Thickness(29, -5, 0, 2)
                };
                _entriesPanel.Children.Add(attachmentSummary);
            }

            index++;
        }
    }

    private static TextBox CreateInlineEditor(string text, double fontSize, Brush foreground, bool singleLine) => new()
    {
        Text = text,
        FontSize = fontSize,
        Foreground = foreground,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(2, 0, 2, 0),
        MinHeight = 34,
        AcceptsReturn = !singleLine,
        TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap
    };

    private static Button CreateIconButton(string glyph, string tooltip, RoutedEventHandler click, bool danger = false)
    {
        Button button = new()
        {
            Width = 38,
            Height = 38,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 15,
                Foreground = new SolidColorBrush(danger
                    ? Windows.UI.Color.FromArgb(255, 248, 113, 113)
                    : Windows.UI.Color.FromArgb(255, 235, 235, 240))
            }
        };
        button.Click += click;
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static ToggleButton CreateIconToggle(string glyph, string tooltip)
    {
        ToggleButton button = new()
        {
            Width = 38,
            Height = 38,
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 247))
            }
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private void AddHomework()
    {
        _subject.Entries.Add(new HomeworkEntry { Content = "在这里输入作业内容" });
        _subject.NotifyEntriesChanged();
    }

    private void DeleteHomework(HomeworkEntry homework)
    {
        _subject.Entries.Remove(homework);
        _subject.NotifyEntriesChanged();
    }

    private void SetDrawing(bool drawing)
    {
        _isDrawing = drawing && _isEditing;
        _drawButton.IsChecked = _isDrawing;
        if (_drawButton.Tag is Viewbox inkView)
        {
            inkView.IsHitTestVisible = _isDrawing;
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(Width + e.HorizontalChange, MinimumTileWidth, MaximumTileWidth);
        Height = Math.Clamp(Height + e.VerticalChange, MinimumTileHeight, MaximumTileHeight);
        _subject.TileWidth = Width;
        _subject.TileHeight = Height;
        _moveSubject(_subject, 0, 0);
    }

    private void InkCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing)
        {
            return;
        }

        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        _activeStrokeData = new InkStrokeData
        {
            Color = Windows.UI.Color.FromArgb(255, 247, 247, 249),
            Thickness = 5
        };
        _activeStrokeData.Points.Add(point);
        _subject.InkStrokes.Add(_activeStrokeData);

        _activeStrokeShape = CreateStrokeShape(_activeStrokeData);
        _activeStrokeShape.Points.Add(point);
        _inkCanvas.Children.Add(_activeStrokeShape);
        _inkCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void InkCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeStrokeData is null || _activeStrokeShape is null)
        {
            return;
        }

        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        _activeStrokeData.Points.Add(point);
        _activeStrokeShape.Points.Add(point);
        e.Handled = true;
    }

    private void InkCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activeStrokeData is null)
        {
            return;
        }

        _activeStrokeData = null;
        _activeStrokeShape = null;
        _inkCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void RenderStoredStrokes()
    {
        _inkCanvas.Children.Clear();
        foreach (InkStrokeData stroke in _subject.InkStrokes)
        {
            Polyline shape = CreateStrokeShape(stroke);
            foreach (Point point in stroke.Points)
            {
                shape.Points.Add(point);
            }

            _inkCanvas.Children.Add(shape);
        }
    }

    private static Polyline CreateStrokeShape(InkStrokeData stroke) => new()
    {
        Stroke = new SolidColorBrush(stroke.Color),
        StrokeThickness = stroke.Thickness,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round
    };

    private void UndoLastStroke()
    {
        if (_subject.InkStrokes.Count == 0)
        {
            return;
        }

        _subject.InkStrokes.RemoveAt(_subject.InkStrokes.Count - 1);
        RenderStoredStrokes();
    }

    private void ClearStrokes()
    {
        _subject.InkStrokes.Clear();
        _inkCanvas.Children.Clear();
    }
}
