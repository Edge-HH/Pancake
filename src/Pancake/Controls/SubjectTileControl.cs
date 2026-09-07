using Pancake.Services;
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
    public event Action<FrameworkElement, bool>? FormattingToolbarChanged;
    private readonly Canvas _inkCanvas = new()
    {
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)),
        ManipulationMode = ManipulationModes.None
    };
    private readonly Dictionary<Polyline, InkStrokeData> _renderedStrokes = [];
    private readonly StackPanel _entriesPanel = new() { Spacing = 8 };
    private readonly StackPanel _editingTools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly TextBox _nameEditor;
    private readonly Thumb _headerMoveThumb;
    private readonly Border _frame;
    private readonly TextBlock _watermark;
    private InkToolSettings _inkSettings = new();
    private uint? _inkPointerId;
    public event Action<SubjectBoard>? InkActivated;
    private bool _isEditing;
    private bool _isDrawing;
    private bool _isErasing;
    private InkStrokeData? _activeStrokeData;
    private Polyline? _activeStrokeShape;

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
            Background = BoardTheme.SurfaceBrush,
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
        double dragX = 0, dragY = 0;
        _headerMoveThumb.DragStarted += (_, _) =>
        {
            IsMoving = true;
            dragX = _subject.X; dragY = _subject.Y;
            _interactionChanged(true);
        };
        _headerMoveThumb.DragDelta += (_, args) =>
        {
            // 从原始指针累计位移，避免小幅拖动始终粘在吸附线上。
            dragX += args.HorizontalChange; dragY += args.VerticalChange;
            _subject.X = Math.Max(0, dragX);
            _subject.Y = Math.Max(0, dragY);
            _layoutChanged(_subject);
        };
        _headerMoveThumb.DragCompleted += (_, _) =>
        {
            _layoutCommitted(_subject);
            IsMoving = false;
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

        _editingTools.Children.Add(CreateIconButton(FluentGlyphs.Add, "添加一条作业", (_, _) => AddHomework()));
        _editingTools.Children.Add(CreateThemeButton());

        _editingTools.Children.Add(CreateIconButton(FluentGlyphs.Delete, "删除科目", (_, _) => _deleteSubject(_subject), true));
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

        // 画布直接覆盖磁贴，以 DIP 保存坐标；调整磁贴只改变裁剪范围，不缩放笔迹。
        _inkCanvas.IsHitTestVisible = false;
        Canvas.SetZIndex(_inkCanvas, 80);
        Children.Add(_inkCanvas);
        _inkCanvas.SizeChanged += (_, args) => _inkCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
        _inkCanvas.PointerPressed += InkCanvas_PointerPressed;
        _inkCanvas.PointerMoved += InkCanvas_PointerMoved;
        _inkCanvas.PointerReleased += InkCanvas_PointerReleased;
        _inkCanvas.PointerCanceled += InkCanvas_PointerReleased;
        _inkCanvas.PointerCaptureLost += InkCanvas_PointerReleased;

        AddResizeHandles();
        _subject.Entries.CollectionChanged += Entries_CollectionChanged;
        RebuildEntries();
        RenderStoredStrokes();
        SetEditing(false);
    }

    public bool IsMoving { get; private set; }

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
        SetInkMode(false, _inkSettings);
        RebuildEntries();
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
                Foreground = BoardTheme.TextBrush,
                Margin = new Thickness(0, 4, 0, 0)
            });

            RichEditBox editor = CreateRichEditor(homework);
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);

            if (_isEditing)
            {
                StackPanel? formatting = null;
                editor.GotFocus += (_, _) =>
                {
                    // 工具栏由窗口统一承载，只在当前作业获得焦点时创建并切换。
                    formatting ??= BuildFormattingToolbar(editor, homework);
                    FormattingToolbarChanged?.Invoke(formatting, true);
                };
                editor.Unloaded += (_, _) =>
                {
                    if (formatting is not null) FormattingToolbarChanged?.Invoke(formatting, false);
                };
            }

            if (_isEditing)
            {
                StackPanel actions = new() { Orientation = Orientation.Horizontal };
                actions.Children.Add(CreateIconButton(FluentGlyphs.ImageAdd, "添加图片", async (_, _) => await _addAttachment(homework)));
                actions.Children.Add(CreateIconButton(FluentGlyphs.Delete, "删除这条作业", (_, _) => DeleteHomework(homework), true));
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
            }

            _entriesPanel.Children.Add(row);
            foreach (AttachmentItem attachment in homework.Attachments.Where(item => item.Kind == "图片" && !string.IsNullOrWhiteSpace(item.Path)))
            {
                AttachmentImageControl image = new(
                    attachment,
                    () => DeleteAttachment(homework, attachment),
                    _contentChanged,
                    _interactionChanged)
                {
                    Margin = new Thickness(29, -4, 4, 4)
                };
                image.SetEditing(_isEditing);
                _entriesPanel.Children.Add(image);
            }
            index++;
        }
    }

    private void DeleteAttachment(HomeworkEntry homework, AttachmentItem attachment)
    {
        homework.Attachments.Remove(attachment);
        homework.NotifyAttachmentsChanged();
        RebuildEntries();
        _contentChanged();
    }

    private RichEditBox CreateRichEditor(HomeworkEntry homework)
    {
        RichEditBox editor = new()
        {
            FontFamily = FontService.DefaultFamily,
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
            CaptureRichText(editor, homework);
        };
        editor.Loaded += (_, _) =>
        {
            if (documentReady) return;
            // WinUI 的只读 RichEdit 文档拒绝 SetText，初始化时短暂解除只读后再恢复。
            bool readOnly = editor.IsReadOnly;
            editor.IsReadOnly = false;
            editor.Document.SetText(string.IsNullOrWhiteSpace(homework.RtfContent) ? TextSetOptions.None : TextSetOptions.FormatRtf,
                string.IsNullOrWhiteSpace(homework.RtfContent) ? homework.Content : DefaultContentColors.AdaptRtf(homework.RtfContent, BoardTheme.IsLight));
            bool repairedTrailingParagraphs = RemoveGeneratedTrailingParagraphs(editor, homework.Content);
            if (string.IsNullOrWhiteSpace(homework.RtfContent) && homework.Content.Length > 0)
            {
                editor.Document.GetRange(0, homework.Content.Length).CharacterFormat.ForegroundColor =
                    BoardTheme.TextColor;
            }
            FontService.RebindBundledFont(editor, homework.FontFallbacks);
            editor.IsReadOnly = readOnly;
            documentReady = true;
            if (repairedTrailingParagraphs)
            {
                CaptureRichText(editor, homework);
            }
        };
        return editor;
    }

    private StackPanel BuildFormattingToolbar(RichEditBox editor, HomeworkEntry homework)
    {
        StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        tools.Children.Add(FontService.CreateFontPicker(editor, () => CaptureRichText(editor, homework)));
        tools.Children.Add(CreateIconButton(FluentGlyphs.Bold, "加粗", (_, _) =>
        {
            editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateIconButton(FluentGlyphs.Italic, "斜体", (_, _) =>
        {
            editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateIconButton(FluentGlyphs.Underline, "下划线", (_, _) =>
        {
            var format = editor.Document.Selection.CharacterFormat;
            format.Underline = format.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None;
            CaptureRichText(editor, homework);
        }));
        tools.Children.Add(CreateColorFlyoutButton(FluentGlyphs.Color, "文字颜色", editor, homework, false));
        tools.Children.Add(CreateColorFlyoutButton(FluentGlyphs.Highlight, "高光颜色", editor, homework, true));
        foreach (Button button in tools.Children.OfType<Button>())
        {
            // 点击顶栏时保持编辑器的焦点和选区；键盘仍可通过 Tab 访问按钮。
            button.AllowFocusOnInteraction = false;
        }
        return tools;
    }

    private void CaptureRichText(RichEditBox editor, HomeworkEntry homework)
    {
        var contentRange = editor.Document.GetRange(0, int.MaxValue);
        if (contentRange.EndPosition > 0)
        {
            // RichEditBox 的最后一个字符是控件维护的段落标记，不属于用户内容。
            contentRange.EndPosition -= 1;
        }

        contentRange.GetText(TextGetOptions.None, out string plain);
        contentRange.GetText(TextGetOptions.FormatRtf, out string rtf);
        homework.FontFallbacks = FontService.CaptureFallbacks(editor);
        homework.Content = plain;
        homework.RtfContent = DefaultContentColors.AdaptRtf(FontService.NormalizeRtf(rtf.TrimEnd('\0')), BoardTheme.IsLight, saving: true);
        _contentChanged();
    }

    private static bool RemoveGeneratedTrailingParagraphs(RichEditBox editor, string expectedPlainText)
    {
        editor.Document.GetText(TextGetOptions.None, out string loadedText);
        string loadedContent = loadedText.EndsWith('\r') ? loadedText[..^1] : loadedText;
        if (loadedContent.Length <= expectedPlainText.Length ||
            !loadedContent.StartsWith(expectedPlainText, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> unexpectedTail = loadedContent.AsSpan(expectedPlainText.Length);
        foreach (char character in unexpectedTail)
        {
            if (character is not ('\r' or '\n'))
            {
                return false;
            }
        }

        // 旧版本把 RichEditBox 自动段落写进 RTF；纯文本是可信边界，只删除其后的换行。
        var trailingRange = editor.Document.GetRange(expectedPlainText.Length, int.MaxValue);
        trailingRange.SetText(TextSetOptions.None, string.Empty);
        return true;
    }

    private Button CreateColorFlyoutButton(string glyph, string tooltip, RichEditBox editor, HomeworkEntry homework, bool isHighlight)
    {
        int selectionStart = 0;
        int selectionEnd = 0;
        StackPanel colors = new() { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8) };
        void RefreshSelection()
        {
            var format = editor.Document.GetRange(selectionStart, selectionEnd).CharacterFormat;
            Windows.UI.Color selected = isHighlight ? format.BackgroundColor : format.ForegroundColor;
            foreach (ColorSwatchButton swatch in colors.Children.OfType<ColorSwatchButton>())
                swatch.SetSelected(!selected.Equals(TextConstants.UndefinedColor) &&
                    (swatch.Tag as string == "clear" ? selected.Equals(TextConstants.AutoColor) : swatch.Color.Equals(selected)));
        }
        Button button = CreateIconButton(glyph, tooltip, (_, _) =>
        {
            // Flyout 会夺走编辑器焦点，必须在打开前保存文本选区。
            selectionStart = editor.Document.Selection.StartPosition;
            selectionEnd = editor.Document.Selection.EndPosition;
            RefreshSelection();
        });
        if (isHighlight)
        {
            Button clearHighlight = CreateColorSwatch("#FFFFFF", 30);
            clearHighlight.Tag = "clear";
            ((Grid)clearHighlight.Content).Children.Insert(0, new Line
            {
                X1 = 2, Y1 = 2, X2 = 28, Y2 = 28,
                Stroke = MainViewModel.BrushFromHex("#EF4444"), StrokeThickness = 2
            });
            ToolTipService.SetToolTip(clearHighlight, "取消高光");
            clearHighlight.Click += (_, _) =>
            {
                var range = editor.Document.GetRange(selectionStart, selectionEnd);
                // RichEdit 的自动背景色表示“无高光”；透明黑色会被当成黑色背景持久化。
                range.CharacterFormat.BackgroundColor = TextConstants.AutoColor;
                editor.Document.Selection.SetRange(selectionStart, selectionEnd);
                CaptureRichText(editor, homework);
                RefreshSelection();
                button.Flyout.Hide();
                editor.Focus(FocusState.Programmatic);
            };
            colors.Children.Add(clearHighlight);
        }

        foreach (string hex in new[] { "#F7F7F9", "#FBBF24", "#F87171", "#60A5FA", "#4ADE80", "#F472B6" }.Select(ColorPalette.Resolve))
        {
            Button swatch = CreateColorSwatch(hex, 30);
            swatch.Click += (_, _) =>
            {
                var range = editor.Document.GetRange(selectionStart, selectionEnd);
                if (isHighlight) range.CharacterFormat.BackgroundColor = BoardTheme.DisplayContentColor(MainViewModel.BrushFromHex(hex).Color);
                else range.CharacterFormat.ForegroundColor = BoardTheme.DisplayContentColor(MainViewModel.BrushFromHex(hex).Color);
                editor.Document.Selection.SetRange(selectionStart, selectionEnd);
                CaptureRichText(editor, homework);
                RefreshSelection();
                button.Flyout.Hide();
                editor.Focus(FocusState.Programmatic);
            };
            colors.Children.Add(swatch);
        }
        button.Flyout = new Flyout { Content = colors };
        button.Flyout.Opening += (_, _) =>
        {
            // 键盘、自动化及指针打开菜单的事件顺序不同，在 Opening 也捕获一次选区。
            selectionStart = editor.Document.Selection.StartPosition;
            selectionEnd = editor.Document.Selection.EndPosition;
            RefreshSelection();
        };
        return button;
    }

    private Button CreateThemeButton()
    {
        Button button = CreateIconButton(FluentGlyphs.Color, "磁贴主题色", (_, _) => { });
        StackPanel colors = new() { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(8) };
        foreach (string hex in new[] { "#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF", "#F87171" }.Select(ColorPalette.Resolve))
        {
            Button swatch = CreateColorSwatch(hex, 32);
            swatch.Click += (_, _) =>
            {
                _subject.IsAccentExplicit = true;
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
        button.Flyout.Opening += (_, _) =>
        {
            foreach (ColorSwatchButton swatch in colors.Children.OfType<ColorSwatchButton>())
                swatch.SetSelected(swatch.Color.Equals(_subject.AccentBrush.Color));
        };
        return button;
    }

    private static ColorSwatchButton CreateColorSwatch(string hex, double size) =>
        new(BoardTheme.DisplayContentColor(MainViewModel.BrushFromHex(hex).Color), size);

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
                Foreground = new SolidColorBrush(danger ? Windows.UI.Color.FromArgb(255, 248, 113, 113) : BoardTheme.TextColor)
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
                Foreground = BoardTheme.TextBrush
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

    public void SetInkMode(bool drawing, InkToolSettings settings)
    {
        _inkSettings = settings;
        _isDrawing = drawing && _isEditing;
        _inkCanvas.IsHitTestVisible = _isDrawing;
        _entriesPanel.IsHitTestVisible = !_isDrawing;
        _editingTools.Visibility = _isEditing && !_isDrawing ? Visibility.Visible : Visibility.Collapsed;
        _nameEditor.IsHitTestVisible = _isEditing && !_isDrawing;
        foreach (Thumb thumb in Children.OfType<Thumb>()) thumb.Visibility = _isEditing && !_isDrawing ? Visibility.Visible : Visibility.Collapsed;
        if (!_isDrawing)
        {
            _inkPointerId = null;
            _activeStrokeData = null;
            _activeStrokeShape = null;
            _isErasing = false;
            _inkCanvas.ReleasePointerCaptures();
        }
    }

    private bool IsInInkBounds(Point point) => point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;

    private void InkCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing || _inkPointerId is not null)
        {
            return;
        }

        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        if (!IsInInkBounds(point)) return;
        _inkPointerId = e.Pointer.PointerId;
        InkActivated?.Invoke(_subject);
        if (_inkSettings.Eraser)
        {
            _isErasing = true;
            EraseStrokeAt(point);
            _inkCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _activeStrokeData = new InkStrokeData { Color = _inkSettings.Color, Thickness = _inkSettings.Thickness };
        _activeStrokeData.Points.Add(point);
        _activeStrokeData.Points.Add(new Point(point.X + 0.01, point.Y));
        _subject.InkStrokes.Add(_activeStrokeData);
        _activeStrokeShape = CreateStrokeShape(_activeStrokeData);
        _activeStrokeShape.Points.Add(point);
        _activeStrokeShape.Points.Add(new Point(point.X + 0.01, point.Y));
        _inkCanvas.Children.Add(_activeStrokeShape);
        _renderedStrokes[_activeStrokeShape] = _activeStrokeData;
        _inkCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void InkCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_inkPointerId != e.Pointer.PointerId) return;
        Point point = e.GetCurrentPoint(_inkCanvas).Position;
        if (!IsInInkBounds(point))
        {
            // 越界即结束当前笔段，避免重新进入时画出跨越空隙的连接线。
            _activeStrokeData = null; _activeStrokeShape = null;
            return;
        }
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
        if (_inkPointerId != e.Pointer.PointerId) return;
        _inkPointerId = null;
        _activeStrokeData = null;
        _activeStrokeShape = null;
        _isErasing = false;
        _inkCanvas.ReleasePointerCapture(e.Pointer);
        _contentChanged();
        e.Handled = true;
    }

    private void EraseStrokeAt(Point point)
    {
        var hit = _renderedStrokes.LastOrDefault(pair => InkGeometry.HitTest(pair.Value.Points.Select(p => (p.X, p.Y)).ToList(), point.X, point.Y,
            Math.Max(8, pair.Value.Thickness * Math.Max(pair.Value.TipScaleX, pair.Value.TipScaleY))));
        Polyline? shape = hit.Key;
        if (shape is null) return;
        InkStrokeData stroke = hit.Value;
        _renderedStrokes.Remove(shape);
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
                shape.Points.Add(new Point(point.X / stroke.TipScaleX, point.Y / stroke.TipScaleY));
            }
            _inkCanvas.Children.Add(shape);
            _renderedStrokes[shape] = stroke;
        }
    }

    private static Polyline CreateStrokeShape(InkStrokeData stroke) => new()
    {
        Stroke = new SolidColorBrush(BoardTheme.DisplayContentColor(stroke.Color)), StrokeThickness = stroke.Thickness,
        RenderTransform = new ScaleTransform { ScaleX = stroke.TipScaleX, ScaleY = stroke.TipScaleY },
        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private void UndoLastStroke()
    {
        if (_subject.InkStrokes.Count == 0) return;
        _subject.InkStrokes.RemoveAt(_subject.InkStrokes.Count - 1);
        RenderStoredStrokes();
        _contentChanged();
    }

    public void ClearStrokes()
    {
        _subject.InkStrokes.Clear();
        RenderStoredStrokes();
        _contentChanged();
    }

    private enum ResizeEdge { Left, Right, Bottom, BottomLeft, BottomRight }
}
