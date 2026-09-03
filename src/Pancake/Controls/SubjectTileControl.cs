using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Models;
using Pancake.ViewModels;
using Windows.Foundation;
using Microsoft.UI.Text;

namespace Pancake.Controls;

/// <summary>
/// Owns every direct manipulation of a subject tile so touch, ink, and layout changes
/// can explicitly lock the parent viewport instead of competing with page scrolling.
/// </summary>
public sealed class SubjectTileControl : Grid
{
    private const double InkSurfaceWidth = 1000;
    private const double InkSurfaceHeight = 600;
    private const double MinimumTileWidth = 280;
    internal const double MinimumTileHeight = 96;
    private const double MaximumTileWidth = 900;
    private const double MaximumTileHeight = 680;
    private const double EdgeHitTarget = 22;
    private const double CornerHitTarget = 34;

    private readonly SubjectBoard _subject;
    private readonly Action<SubjectBoard> _deleteSubject;
    private readonly Action<SubjectBoard> _layoutChanged;
    private readonly Action<SubjectBoard> _layoutCommitted;
    private readonly Action<bool> _interactionChanged;
    private readonly Func<HomeworkEntry, Task> _addAttachment;
    private readonly Action _contentChanged;
    private readonly Canvas _inkCanvas = new()
    {
        Width = InkSurfaceWidth,
        Height = InkSurfaceHeight,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
        ManipulationMode = ManipulationModes.None
    };
    private readonly Dictionary<Polyline, InkStrokeData> _renderedStrokes = [];
    private readonly StackPanel _entriesPanel = new() { Spacing = 8 };
    private readonly StackPanel _editingTools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Border _penModeToolbar;
    private readonly TextBox _nameEditor;
    private readonly Thumb _headerMoveThumb;
    private readonly Border _frame;
    private readonly TextBlock _watermark;
    private readonly ToggleButton _drawButton;
    private ToggleButton _penButton = null!;
    private ToggleButton _eraserButton = null!;
    private bool _isEditing;
    private bool _isDrawing;
    private bool _isErasing;
    private InkStrokeData? _activeStrokeData;
    private Polyline? _activeStrokeShape;
    private Windows.UI.Color InkColor = Windows.UI.Color.FromArgb(255, 247, 247, 249);
    private double InkThickness = 5;
    private InkTool _inkTool = InkTool.Pen;

    public SubjectTileControl(
        SubjectBoard subject,
        Action<SubjectBoard> deleteSubject,
        Action<SubjectBoard> layoutChanged,
        Action<SubjectBoard> layoutCommitted,
        Action<bool> interactionChanged,
        Func<HomeworkEntry, Task> addAttachment,
        Action contentChanged)
    {
        _subject = subject;
        _deleteSubject = deleteSubject;
        _layoutChanged = layoutChanged;
        _layoutCommitted = layoutCommitted;
        _interactionChanged = interactionChanged;
        _addAttachment = addAttachment;
        _contentChanged = contentChanged;

        Width = subject.TileWidth;
        Height = subject.TileHeight;
        MinWidth = MinimumTileWidth;
        MinHeight = MinimumTileHeight;
        ManipulationMode = ManipulationModes.None;

        _frame = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 31, 31)),
            BorderBrush = subject.AccentBrush,
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(3)
        };
        Children.Add(_frame);

        Grid content = new() { Padding = new Thickness(14, 10, 10, 10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _frame.Child = content;

        _watermark = new TextBlock
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
        Grid.SetRowSpan(_watermark, 3);
        content.Children.Add(_watermark);

        Grid header = new() { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(header);
        Canvas.SetZIndex(header, 30);

        _headerMoveThumb = new Thumb
        {
            Name = "HeaderMoveThumb",
            Height = EdgeHitTarget,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
            ManipulationMode = ManipulationModes.None
        };
        _headerMoveThumb.DragStarted += (_, _) => _interactionChanged(true);
        _headerMoveThumb.DragDelta += (_, args) =>
        {
            _subject.X = Math.Max(0, _subject.X + args.HorizontalChange);
            _subject.Y = Math.Max(0, _subject.Y + args.VerticalChange);
            _layoutChanged(_subject);
        };
        _headerMoveThumb.DragCompleted += (_, _) =>
        {
            _layoutCommitted(_subject);
            _interactionChanged(false);
        };
        // 顶部边缘统一用于移动，并覆盖左右缩放区在顶部的交叉部分。
        Canvas.SetZIndex(_headerMoveThumb, 70);
        Children.Add(_headerMoveThumb);

        _nameEditor = CreateInlineEditor(subject.Name, 29, subject.AccentBrush, true);
        _nameEditor.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _nameEditor.HorizontalAlignment = HorizontalAlignment.Left;
        _nameEditor.MinWidth = 120;
        _nameEditor.MaxWidth = 240;
        _nameEditor.TextChanged += (_, _) =>
        {
            _subject.Name = _nameEditor.Text;
            _watermark.Text = _subject.Watermark;
            _contentChanged();
        };
        header.Children.Add(_nameEditor);

        _drawButton = CreateIconToggle("\uED63", "开启笔模式");
        _drawButton.Checked += (_, _) => SetDrawing(true);
        _drawButton.Unchecked += (_, _) => SetDrawing(false);
        _editingTools.Children.Add(_drawButton);
        _editingTools.Children.Add(CreateIconButton("\uE7A7", "撤销最后一笔", (_, _) => UndoLastStroke()));
        _editingTools.Children.Add(CreateIconButton("\uE710", "添加一条作业", (_, _) => AddHomework()));
        _editingTools.Children.Add(CreateThemeButton());

        _editingTools.Children.Add(CreateIconButton("\uE74D", "删除科目", (_, _) => _deleteSubject(_subject), true));
        Grid.SetColumn(_editingTools, 1);
        header.Children.Add(_editingTools);

        ScrollViewer entriesScroller = new()
        {
            Content = _entriesPanel,
            Margin = new Thickness(0, 8, 4, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled
        };
        Grid.SetRow(entriesScroller, 1);
        content.Children.Add(entriesScroller);

        _penModeToolbar = BuildPenModeToolbar();
        _penModeToolbar.Name = "PenModeToolbar";
        Grid.SetRow(_penModeToolbar, 2);
        Canvas.SetZIndex(_penModeToolbar, 40);
        content.Children.Add(_penModeToolbar);

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

        AddResizeHandles();
        _subject.Entries.CollectionChanged += Entries_CollectionChanged;
        RebuildEntries();
        RenderStoredStrokes();
        SetEditing(false);
    }

    public void ApplyModelLayout()
    {
        Width = _subject.TileWidth;
        Height = _subject.TileHeight;
    }

    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _editingTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _headerMoveThumb.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        foreach (Thumb thumb in Children.OfType<Thumb>())
        {
            thumb.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }

        _nameEditor.IsReadOnly = !editing;
        _nameEditor.IsHitTestVisible = editing;
        SetDrawing(false);
        RebuildEntries();
    }

    private Border BuildPenModeToolbar()
    {
        StackPanel tools = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _penButton = CreateIconToggle("\uED63", "画笔");
        _penButton.IsChecked = true;
        _penButton.Checked += (_, _) => SelectInkTool(InkTool.Pen);
        tools.Children.Add(_penButton);

        foreach ((string hex, string name) in new[]
        {
            ("#F7F7F9", "白色"),
            ("#FBBF24", "黄色"),
            ("#F87171", "红色"),
            ("#60A5FA", "蓝色")
        })
        {
            Button colorButton = new()
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Content = new Ellipse { Width = 16, Height = 16, Fill = MainViewModel.BrushFromHex(hex) },
                Tag = hex
            };
            colorButton.Click += (_, _) =>
            {
                InkColor = MainViewModel.BrushFromHex((string)colorButton.Tag).Color;
                SelectInkTool(InkTool.Pen);
            };
            ToolTipService.SetToolTip(colorButton, $"{name}画笔");
            tools.Children.Add(colorButton);
        }

        Slider thicknessSlider = new()
        {
            Width = 108,
            Minimum = 2,
            Maximum = 18,
            Value = InkThickness,
            StepFrequency = 1,
            Header = "粗细"
        };
        thicknessSlider.ValueChanged += (_, args) => InkThickness = args.NewValue;
        tools.Children.Add(thicknessSlider);

        _eraserButton = CreateIconToggle("\uE75C", "橡皮擦");
        _eraserButton.Checked += (_, _) => SelectInkTool(InkTool.Eraser);
        tools.Children.Add(_eraserButton);
        tools.Children.Add(CreateIconButton("\uE74D", "清空笔迹", (_, _) => ClearStrokes(), true));

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(244, 9, 9, 11)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 70, 70, 74)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = tools,
            Visibility = Visibility.Collapsed
        };
    }

    private void AddResizeHandles()
    {
        AddResizeHandle(ResizeEdge.Left, HorizontalAlignment.Left, VerticalAlignment.Stretch, EdgeHitTarget, double.NaN);
        AddResizeHandle(ResizeEdge.Right, HorizontalAlignment.Right, VerticalAlignment.Stretch, EdgeHitTarget, double.NaN);
        AddResizeHandle(ResizeEdge.Bottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, EdgeHitTarget);
        AddResizeHandle(ResizeEdge.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom, CornerHitTarget, CornerHitTarget);
        AddResizeHandle(ResizeEdge.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom, CornerHitTarget, CornerHitTarget);
    }

    private void AddResizeHandle(ResizeEdge edge, HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
    {
        Thumb thumb = new()
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
            ManipulationMode = ManipulationModes.None,
            Tag = edge
        };
        thumb.DragStarted += (_, _) => _interactionChanged(true);
        thumb.DragDelta += ResizeHandle_DragDelta;
        thumb.DragCompleted += (_, _) =>
        {
            _layoutCommitted(_subject);
            _interactionChanged(false);
        };
        Canvas.SetZIndex(thumb, edge is ResizeEdge.BottomLeft or ResizeEdge.BottomRight ? 60 : 50);
        Children.Add(thumb);
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeEdge edge = (ResizeEdge)((Thumb)sender).Tag;
        double right = _subject.X + _subject.TileWidth;
        double bottom = _subject.Y + _subject.TileHeight;
        double nextX = _subject.X;
        double nextY = _subject.Y;
        double nextWidth = _subject.TileWidth;
        double nextHeight = _subject.TileHeight;

        if (edge is ResizeEdge.Left or ResizeEdge.BottomLeft)
        {
            nextWidth = Math.Clamp(_subject.TileWidth - e.HorizontalChange, MinimumTileWidth, MaximumTileWidth);
            nextX = Math.Max(0, right - nextWidth);
        }
        else if (edge is ResizeEdge.Right or ResizeEdge.BottomRight)
        {
            nextWidth = Math.Clamp(_subject.TileWidth + e.HorizontalChange, MinimumTileWidth, MaximumTileWidth);
        }

        if (edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight)
        {
            nextHeight = Math.Clamp(_subject.TileHeight + e.VerticalChange, MinimumTileHeight, MaximumTileHeight);
        }

        _subject.X = nextX;
        _subject.Y = nextY;
        _subject.TileWidth = nextWidth;
        _subject.TileHeight = nextHeight;
        ApplyModelLayout();
        _layoutChanged(_subject);
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
                Text = $"{index}.", FontSize = 20,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 247)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            RichEditBox editor = CreateRichEditor(homework);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            if (_isEditing)
            {
                StackPanel formatting = BuildFormattingToolbar(editor, homework);
                Grid.SetColumn(formatting, 1);
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(formatting, 1);
                row.Children.Add(formatting);
            }

            if (_isEditing)
            {
                StackPanel actions = new() { Orientation = Orientation.Horizontal };
                actions.Children.Add(CreateIconButton("\uE723", "添加附件", async (_, _) => await _addAttachment(homework)));
                actions.Children.Add(CreateIconButton("\uE74D", "删除这条作业", (_, _) => DeleteHomework(homework), true));
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
            }

            _entriesPanel.Children.Add(row);
            if (homework.Attachments.Count > 0)
            {
                _entriesPanel.Children.Add(new TextBlock
                {
                    Text = $"{homework.Attachments.Count} 个附件", FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 164, 164, 180)),
                    Margin = new Thickness(29, -5, 0, 2)
                });
            }
            index++;
        }
    }

    private RichEditBox CreateRichEditor(HomeworkEntry homework)
    {
        RichEditBox editor = new()
        {
            FontSize = 20,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            MinHeight = 42,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = !_isEditing,
            IsHitTestVisible = _isEditing
        };
        bool documentReady = false;
        editor.TextChanged += (_, _) =>
        {
            if (!documentReady) return;
            editor.Document.GetText(TextGetOptions.None, out string plain);
            editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            homework.Content = plain.TrimEnd('\r');
            homework.RtfContent = rtf;
            _contentChanged();
        };
        editor.Loaded += (_, _) =>
        {
            if (documentReady) return;
            // WinUI 的只读 RichEdit 文档拒绝 SetText，初始化时短暂解除只读后再恢复。
            bool readOnly = editor.IsReadOnly;
            editor.IsReadOnly = false;
            editor.Document.SetText(string.IsNullOrWhiteSpace(homework.RtfContent) ? TextSetOptions.None : TextSetOptions.FormatRtf,
                string.IsNullOrWhiteSpace(homework.RtfContent) ? homework.Content : homework.RtfContent);
            if (string.IsNullOrWhiteSpace(homework.RtfContent) && homework.Content.Length > 0)
            {
                editor.Document.GetRange(0, homework.Content.Length).CharacterFormat.ForegroundColor =
                    Windows.UI.Color.FromArgb(255, 245, 245, 247);
            }
            editor.IsReadOnly = readOnly;
            documentReady = true;
        };
        return editor;
    }

    private StackPanel BuildFormattingToolbar(RichEditBox editor, HomeworkEntry homework)
    {
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 2, 0, 4) };
        tools.Children.Add(CreateIconButton("\uE8DD", "加粗", (_, _) =>
        {
            editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateIconButton("\uE8DB", "斜体", (_, _) =>
        {
            editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateIconButton("\uE8DC", "下划线", (_, _) =>
        {
            var format = editor.Document.Selection.CharacterFormat;
            format.Underline = format.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateColorFlyoutButton("\uE790", "文字颜色", editor, homework, false));
        tools.Children.Add(CreateColorFlyoutButton("\uE7E6", "高光颜色", editor, homework, true));
        return tools;
    }

    private void CaptureRichText(RichEditBox editor, HomeworkEntry homework)
    {
        editor.Document.GetText(TextGetOptions.None, out string plain);
        editor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
        homework.Content = plain.TrimEnd('\r');
        homework.RtfContent = rtf;
        _contentChanged();
    }

    private Button CreateColorFlyoutButton(string glyph, string tooltip, RichEditBox editor, HomeworkEntry homework, bool isHighlight)
    {
        int selectionStart = 0;
        int selectionEnd = 0;
        Button button = CreateIconButton(glyph, tooltip, (_, _) =>
        {
            // Flyout 会夺走编辑器焦点，必须在打开前保存文本选区。
            selectionStart = editor.Document.Selection.StartPosition;
            selectionEnd = editor.Document.Selection.EndPosition;
        });
        StackPanel colors = new() { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8) };
        foreach (string hex in new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA", "#4ADE80", "#F472B6" })
        {
            Button swatch = CreateColorSwatch(hex, 30);
            swatch.Click += (_, _) =>
            {
                var range = editor.Document.GetRange(selectionStart, selectionEnd);
                if (isHighlight) range.CharacterFormat.BackgroundColor = MainViewModel.BrushFromHex(hex).Color;
                else range.CharacterFormat.ForegroundColor = MainViewModel.BrushFromHex(hex).Color;
                editor.Document.Selection.SetRange(selectionStart, selectionEnd);
                CaptureRichText(editor, homework);
                button.Flyout.Hide();
                editor.Focus(FocusState.Programmatic);
            };
            colors.Children.Add(swatch);
        }
        button.Flyout = new Flyout { Content = colors };
        return button;
    }

    private Button CreateThemeButton()
    {
        Button button = CreateIconButton("\uE790", "磁贴主题色", (_, _) => { });
        StackPanel colors = new() { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8) };
        foreach (string hex in new[] { "#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF", "#F87171" })
        {
            Button swatch = CreateColorSwatch(hex, 32);
            swatch.Click += (_, _) =>
            {
                _subject.AccentHex = hex;
                _subject.AccentBrush = MainViewModel.BrushFromHex(hex);
                _frame.BorderBrush = _subject.AccentBrush;
                _nameEditor.Foreground = _subject.AccentBrush;
                _watermark.Foreground = _subject.AccentBrush;
                button.Flyout.Hide();
                _contentChanged();
            };
            colors.Children.Add(swatch);
        }
        button.Flyout = new Flyout { Content = colors };
        return button;
    }

    private static Button CreateColorSwatch(string hex, double size) => new()
    {
        Width = size,
        Height = size,
        Padding = new Thickness(5),
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        Content = new Ellipse
        {
            Width = size - 12,
            Height = size - 12,
            Fill = MainViewModel.BrushFromHex(hex)
        }
    };

    private static TextBox CreateInlineEditor(string text, double fontSize, Brush foreground, bool singleLine) => new()
    {
        Text = text, FontSize = fontSize, Foreground = foreground,
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
        BorderThickness = new Thickness(0), Padding = new Thickness(2, 0, 2, 0), MinHeight = 34,
        AcceptsReturn = !singleLine,
        TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap
    };

    private static Button CreateIconButton(string glyph, string tooltip, RoutedEventHandler click, bool danger = false)
    {
        Button button = new()
        {
            Width = 38, Height = 38, Padding = new Thickness(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)), BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph, FontSize = 15,
                Foreground = new SolidColorBrush(danger ? Windows.UI.Color.FromArgb(255, 248, 113, 113) : Windows.UI.Color.FromArgb(255, 235, 235, 240))
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
            Width = 38, Height = 38, Padding = new Thickness(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)), BorderThickness = new Thickness(0),
            Content = new FontIcon
            {
                Glyph = glyph, FontSize = 16,
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
        _contentChanged();
    }

    private void DeleteHomework(HomeworkEntry homework)
    {
        _subject.Entries.Remove(homework);
        _subject.NotifyEntriesChanged();
        _contentChanged();
    }

    private void SetDrawing(bool drawing)
    {
        bool wasDrawing = _isDrawing;
        _isDrawing = drawing && _isEditing;
        _drawButton.IsChecked = _isDrawing;
        _penModeToolbar.Visibility = _isDrawing ? Visibility.Visible : Visibility.Collapsed;
        if (_drawButton.Tag is Viewbox inkView)
        {
            inkView.IsHitTestVisible = _isDrawing;
        }
        if (wasDrawing != _isDrawing)
        {
            _interactionChanged(_isDrawing);
        }
    }

    private void SelectInkTool(InkTool tool)
    {
        _inkTool = tool;
        _penButton.IsChecked = tool == InkTool.Pen;
        _eraserButton.IsChecked = tool == InkTool.Eraser;
    }

    private void InkCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing)
        {
            return;
        }

        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        if (_inkTool == InkTool.Eraser)
        {
            _isErasing = true;
            EraseStrokeAt(point);
            _inkCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _activeStrokeData = new InkStrokeData { Color = InkColor, Thickness = InkThickness };
        _activeStrokeData.Points.Add(point);
        _subject.InkStrokes.Add(_activeStrokeData);
        _activeStrokeShape = CreateStrokeShape(_activeStrokeData);
        _activeStrokeShape.Points.Add(point);
        _inkCanvas.Children.Add(_activeStrokeShape);
        _renderedStrokes[_activeStrokeShape] = _activeStrokeData;
        _inkCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void InkCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        if (_isErasing)
        {
            EraseStrokeAt(point);
        }
        else if (_activeStrokeData is not null && _activeStrokeShape is not null)
        {
            _activeStrokeData.Points.Add(point);
            _activeStrokeShape.Points.Add(point);
        }
        e.Handled = true;
    }

    private void InkCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _activeStrokeData = null;
        _activeStrokeShape = null;
        _isErasing = false;
        _inkCanvas.ReleasePointerCapture(e.Pointer);
        _contentChanged();
        e.Handled = true;
    }

    private void EraseStrokeAt(Point point)
    {
        Polyline? shape = VisualTreeHelper.FindElementsInHostCoordinates(point, _inkCanvas).OfType<Polyline>().FirstOrDefault();
        if (shape is null || !_renderedStrokes.Remove(shape, out InkStrokeData? stroke))
        {
            return;
        }
        _subject.InkStrokes.Remove(stroke);
        _inkCanvas.Children.Remove(shape);
        _contentChanged();
    }

    private void RenderStoredStrokes()
    {
        _inkCanvas.Children.Clear();
        _renderedStrokes.Clear();
        foreach (InkStrokeData stroke in _subject.InkStrokes)
        {
            Polyline shape = CreateStrokeShape(stroke);
            foreach (Point point in stroke.Points)
            {
                shape.Points.Add(point);
            }
            _inkCanvas.Children.Add(shape);
            _renderedStrokes[shape] = stroke;
        }
    }

    private static Polyline CreateStrokeShape(InkStrokeData stroke) => new()
    {
        Stroke = new SolidColorBrush(stroke.Color), StrokeThickness = stroke.Thickness,
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private void UndoLastStroke()
    {
        if (_subject.InkStrokes.Count == 0) return;
        _subject.InkStrokes.RemoveAt(_subject.InkStrokes.Count - 1);
        RenderStoredStrokes();
        _contentChanged();
    }

    private void ClearStrokes()
    {
        _subject.InkStrokes.Clear();
        RenderStoredStrokes();
        _contentChanged();
    }

    private enum InkTool { Pen, Eraser }
    private enum ResizeEdge { Left, Right, Bottom, BottomLeft, BottomRight }
}
