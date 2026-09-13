using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pancake.Models;
using Pancake.RichText;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 科目磁贴：显示作业内容与笔迹，编辑态下提供标题改名、拖动移动、八方向缩放和增删作业。
/// 富文本编辑、图片框编辑与手写在后续里程碑接入同一套结构。
/// </summary>
public sealed class SubjectTileControl : Grid
{
    internal const double MinimumTileWidth = 280;
    internal const double MinimumTileHeight = 96;
    internal const double MaximumTileWidth = 900;
    internal const double MaximumTileHeight = 680;
    // 顶部边缘统一用于移动，命中区域比标题本身略高，方便触屏抓取。
    private const double EdgeHitTarget = 22;
    private const double ResizeHandleSize = 20;

    /// <summary>拖动移动时命中过的边；缩放时为空。</summary>
    private enum ResizeEdge
    {
        Top,
        TopLeft,
        TopRight,
        Left,
        Right,
        Bottom,
        BottomLeft,
        BottomRight
    }

    private readonly SubjectBoard _subject;
    private readonly Action<SubjectBoard> _deleteSubject;
    private readonly Action<SubjectBoard> _layoutChanged;
    private readonly Action<SubjectBoard> _layoutCommitted;
    private readonly Action<bool> _interactionChanged;
    private readonly Action _contentChanged;
    private readonly Func<HomeworkEntry, Task>? _addAttachment;
    private readonly AutofillService? _autofill;
    private readonly AutofillPopup? _autofillPopup;
    private readonly List<AutofillController> _entryAutofills = [];
    private AutofillController? _titleAutofill;
    private readonly Border _frame;
    private readonly BackgroundVisual _background = new();
    private readonly Grid _content;
    private readonly TextBlock _nameText;
    private readonly TextBox _nameEditor;
    private readonly TextBlock _countText;
    private readonly StackPanel _editingTools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly StackPanel _entriesPanel = new() { Spacing = 8 };
    private readonly InkCanvasLayer _inkLayer = new() { ClipToBounds = true };
    private readonly TextBlock _watermark;
    private readonly List<Thumb> _resizeHandles = [];
    private readonly List<(RichTextEditor Editor, HomeworkEntry Entry)> _entryEditors = [];
    // 附件控件也要跟着编辑态走：否则图片无法选中，缩放/裁切/旋转/拖动全都点不动。
    private readonly List<AttachmentImageControl> _attachmentControls = [];
    private readonly Thumb _headerMoveThumb;
    private double _dragX;
    private double _dragY;
    private bool _isEditing;

    public SubjectTileControl(
        SubjectBoard subject,
        Action<SubjectBoard> deleteSubject,
        Action<SubjectBoard> layoutChanged,
        Action<SubjectBoard> layoutCommitted,
        Action<bool> interactionChanged,
        Action contentChanged,
        Func<HomeworkEntry, Task>? addAttachment = null,
        AutofillService? autofill = null,
        AutofillPopup? autofillPopup = null)
    {
        _subject = subject;
        _deleteSubject = deleteSubject;
        _layoutChanged = layoutChanged;
        _layoutCommitted = layoutCommitted;
        _interactionChanged = interactionChanged;
        _contentChanged = contentChanged;
        _addAttachment = addAttachment;
        _autofill = autofill;
        _autofillPopup = autofillPopup;

        Width = subject.TileWidth;
        Height = subject.TileHeight;
        MinWidth = MinimumTileWidth;
        MinHeight = MinimumTileHeight;
        ClipToBounds = true;

        _frame = new Border
        {
            BorderBrush = new SolidColorBrush(subject.AccentColor.ToColor()),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor())
        };
        Children.Add(_frame);

        _content = new Grid
        {
            Margin = new Thickness(14, 10, 10, 10),
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        Grid layers = new();
        layers.Children.Add(_background);
        layers.Children.Add(_content);
        _frame.Child = layers;

        _nameText = new TextBlock
        {
            Text = subject.Name,
            FontSize = 29,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(subject.AccentColor.ToColor()),
            VerticalAlignment = VerticalAlignment.Center
        };
        _countText = new TextBlock
        {
            Text = subject.CountLabel,
            FontSize = 13,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor())
        };

        // 标题区使用“只读文本 / 可编辑文本框”两套控件切换，避免每帧重建。
        // 水印先于标题编辑器创建：改名回调里会同步水印文本。
        _watermark = new TextBlock
        {
            Text = subject.Watermark,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -10),
            FontSize = 72,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(subject.AccentColor.ToColor()),
            Opacity = 0.16,
            IsHitTestVisible = false
        };

        _nameEditor = new TextBox
        {
            Text = subject.Name,
            FontSize = 29,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(subject.AccentColor.ToColor()),
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = 120,
            MaxWidth = 240,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        _nameEditor.TextChanged += (_, _) =>
        {
            _subject.Name = _nameEditor.Text ?? string.Empty;
            _nameText.Text = _subject.Name;
            _watermark.Text = _subject.Watermark;
            _contentChanged();
        };
        if (_autofill is not null && _autofillPopup is not null)
        {
            _titleAutofill = AutofillController.AttachSubject(
                _autofill,
                _nameEditor,
                _autofillPopup,
                () => _isEditing,
                suggestion =>
                {
                    // 采纳学科后连主题色一起套用，与旧版行为一致。
                    _subject.AccentHex = _autofill.ResolveSubjectColor(suggestion);
                    _subject.IsAccentExplicit = true;
                    ApplyAccent();
                    _contentChanged();
                });
        }

        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(_nameText);
        header.Children.Add(_nameEditor);
        Grid.SetColumn(_countText, 1);
        header.Children.Add(_countText);
        _editingTools.Children.Add(CreateIconButton(nameof(FluentGlyphs.Add), "添加一条作业", (_, _) => AddHomework()));
        _editingTools.Children.Add(CreateThemeButton());
        _editingTools.Children.Add(CreateIconButton(nameof(FluentGlyphs.Delete), "删除科目", (_, _) => _deleteSubject(_subject), mirrored: true));
        Grid.SetColumn(_editingTools, 3);
        header.Children.Add(_editingTools);
        _content.Children.Add(header);

        ScrollViewer entriesScroller = new()
        {
            Margin = new Thickness(0, 8, 4, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _entriesPanel
        };
        Grid.SetRow(entriesScroller, 1);
        _content.Children.Add(entriesScroller);

        // 笔迹层覆盖整块磁贴并裁剪到边界，越界部分不落笔。
        _inkLayer.StrokesChanged += () => _contentChanged();
        Grid.SetRowSpan(_inkLayer, 3);
        _content.Children.Add(_inkLayer);

        Grid.SetRow(_watermark, 1);
        _content.Children.Add(_watermark);

        // 顶部拖动条统一负责移动；它比左右缩放区更靠上，避免两类手势互相抢指针。
        _headerMoveThumb = ThumbVisuals.Apply(new Thumb
        {
            Height = EdgeHitTarget,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(BoardColor.FromArgb(1, 0, 0, 0).ToColor()),
            IsVisible = false
        });
        _headerMoveThumb.DragStarted += (_, _) =>
        {
            IsMoving = true;
            _dragX = _subject.X;
            _dragY = _subject.Y;
            _interactionChanged(true);
        };
        _headerMoveThumb.DragDelta += (_, args) =>
        {
            // 从按下时的原始坐标累计位移，避免小幅拖动始终粘在吸附线上。
            _dragX += args.Vector.X;
            _dragY += args.Vector.Y;
            _subject.X = Math.Max(0, _dragX);
            _subject.Y = Math.Max(0, _dragY);
            _layoutChanged(_subject);
        };
        _headerMoveThumb.DragCompleted += (_, _) =>
        {
            _layoutCommitted(_subject);
            IsMoving = false;
            _interactionChanged(false);
        };
        _headerMoveThumb.ZIndex = 70;
        Children.Add(_headerMoveThumb);

        AddResizeHandles();
        subject.Entries.CollectionChanged += Entries_CollectionChanged;
        subject.PropertyChanged += Subject_PropertyChanged;
        RebuildEntries();
        SetEditing(false, null, null);
    }

    /// <summary>正在拖动磁贴本体（而非缩放），用于决定是否显示对齐辅助线。</summary>
    public bool IsMoving { get; private set; }

    /// <summary>某条作业的富文本编辑器获得焦点；宿主据此在悬浮岛上显示格式工具。</summary>
    public event Action<SubjectTileControl, RichTextEditor>? EntryEditorFocused;

#if PANCAKE_UI_TESTS
    /// <summary>验证构建专用：磁贴里第一个作业编辑器。</summary>
    internal RichTextEditor? FirstEditorForVerification => _entryEditors.FirstOrDefault().Editor;

    /// <summary>验证脚本用：磁贴对应的科目模型（只读用途，预览与自检需要记录条目的 RTF）。</summary>
    internal SubjectBoard SubjectForVerification => _subject;

    /// <summary>验证脚本用：磁贴的手写层（自检需要确认它是否接收指针）。</summary>
    internal InkCanvasLayer InkLayerForVerification => _inkLayer;

    /// <summary>验证脚本用：磁贴顶部的拖动条（自检需要确认它接收指针）。</summary>
    internal Thumb HeaderThumbForVerification => _headerMoveThumb;

    /// <summary>验证脚本用：标题输入框（自检需要把焦点放上去验证候选浮层）。</summary>
    internal TextBox TitleEditorForVerification => _nameEditor;
#endif

    /// <summary>把模型的坐标、尺寸与主题色重新应用到控件上。</summary>
    public void ApplyModelLayout()
    {
        Width = _subject.TileWidth;
        Height = _subject.TileHeight;
        ApplyAccent();
    }

    public void SetEditing(bool editing) => SetEditing(editing, null, null);

    public void SetEditing(bool editing, Action<SubjectBoard>? layoutChanged, Action<SubjectBoard>? layoutCommitted)
    {
        _isEditing = editing;
        _ = layoutChanged;
        _ = layoutCommitted;
        if (!editing)
        {
            // 退出编辑时结算统计并收起候选，避免半截输入被计入。
            _titleAutofill?.Settle();
            _titleAutofill?.Hide();
            foreach (AutofillController controller in _entryAutofills)
            {
                controller.Settle();
                controller.Hide();
            }
        }

        _nameText.IsVisible = !editing;
        _nameEditor.IsVisible = editing;
        _editingTools.IsVisible = editing;
        _headerMoveThumb.IsVisible = editing;
        foreach (Thumb handle in _resizeHandles) handle.IsVisible = editing;
        foreach ((RichTextEditor editor, HomeworkEntry _) in _entryEditors) editor.IsEditing = editing;
        // 图片附件同样跟随编辑态：查看模式只显示画面，编辑模式才允许选中、拖动、缩放与旋转。
        foreach (AttachmentImageControl attachment in _attachmentControls) attachment.SetEditing(editing);
    }

    private void AddResizeHandles()
    {
        AddResizeHandle(ResizeEdge.TopLeft, HorizontalAlignment.Left, VerticalAlignment.Top);
        AddResizeHandle(ResizeEdge.TopRight, HorizontalAlignment.Right, VerticalAlignment.Top);
        AddResizeHandle(ResizeEdge.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        AddResizeHandle(ResizeEdge.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        AddResizeHandle(ResizeEdge.Top, HorizontalAlignment.Center, VerticalAlignment.Top, double.NaN, 14);
        AddResizeHandle(ResizeEdge.Bottom, HorizontalAlignment.Center, VerticalAlignment.Bottom, double.NaN, 14);
        AddResizeHandle(ResizeEdge.Left, HorizontalAlignment.Left, VerticalAlignment.Center, 14, double.NaN);
        AddResizeHandle(ResizeEdge.Right, HorizontalAlignment.Right, VerticalAlignment.Center, 14, double.NaN);
    }

    private void AddResizeHandle(
        ResizeEdge edge,
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        double width = ResizeHandleSize,
        double height = ResizeHandleSize)
    {
        Thumb thumb = ThumbVisuals.Apply(new Thumb
        {
            Width = width,
            Height = height,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Background = new SolidColorBrush(BoardColor.FromArgb(1, 0, 0, 0).ToColor()),
            IsVisible = false,
            Tag = edge
        });
        thumb.DragStarted += (_, _) => _interactionChanged(true);
        thumb.DragDelta += ResizeHandle_DragDelta;
        thumb.DragCompleted += (_, _) =>
        {
            _layoutCommitted(_subject);
            _interactionChanged(false);
        };
        // 下侧手柄需要压住上侧的命中区，否则右下角拖不到。
        thumb.ZIndex = edge is ResizeEdge.BottomLeft or ResizeEdge.BottomRight ? 60 : 50;
        _resizeHandles.Add(thumb);
        Children.Add(thumb);
    }

    /// <summary>八方向缩放：左侧拖动同时移动原点，保证右边缘不动。</summary>
    private void ResizeHandle_DragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is not Thumb { Tag: ResizeEdge edge }) return;
        double right = _subject.X + _subject.TileWidth;
        double nextX = _subject.X;
        double nextY = _subject.Y;
        double nextWidth = _subject.TileWidth;
        double nextHeight = _subject.TileHeight;

        if (edge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
        {
            nextWidth = Math.Clamp(_subject.TileWidth - e.Vector.X, MinimumTileWidth, MaximumTileWidth);
            nextX = Math.Max(0, right - nextWidth);
        }
        else if (edge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight)
        {
            nextWidth = Math.Clamp(_subject.TileWidth + e.Vector.X, MinimumTileWidth, MaximumTileWidth);
        }

        if (edge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight)
        {
            nextHeight = Math.Clamp(_subject.TileHeight + e.Vector.Y, MinimumTileHeight, MaximumTileHeight);
        }
        else if (edge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
        {
            // 顶部缩放保持下边缘不动。
            double bottom = _subject.Y + _subject.TileHeight;
            nextHeight = Math.Clamp(_subject.TileHeight - e.Vector.Y, MinimumTileHeight, MaximumTileHeight);
            nextY = Math.Max(0, bottom - nextHeight);
        }

        _subject.X = nextX;
        _subject.Y = nextY;
        _subject.TileWidth = nextWidth;
        _subject.TileHeight = nextHeight;
        ApplyModelLayout();
        _layoutChanged(_subject);
    }

    private void AddHomework()
    {
        HomeworkEntry entry = new() { Content = "在这里输入作业内容" };
        _subject.Entries.Add(entry);
        _subject.NotifyEntriesChanged();
        _contentChanged();
    }

    /// <summary>删除一条作业；磁贴内至少保留一条，避免出现完全空白的编辑器区域。</summary>
    private void DeleteHomework(HomeworkEntry entry)
    {
        _subject.Entries.Remove(entry);
        _subject.NotifyEntriesChanged();
        _contentChanged();
    }

    private void ApplyAccent()
    {
        SolidColorBrush accent = new(_subject.AccentColor.ToColor());
        _frame.BorderBrush = accent;
        _nameText.Foreground = accent;
        _nameEditor.Foreground = accent;
        _watermark.Foreground = accent;
    }

    private void Subject_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubjectBoard.Name))
        {
            _nameText.Text = _subject.Name;
            _nameEditor.Text = _subject.Name;
            _watermark.Text = _subject.Watermark;
        }
        else if (e.PropertyName is nameof(SubjectBoard.AccentHex))
        {
            ApplyAccent();
        }
        else if (e.PropertyName is nameof(SubjectBoard.CountLabel))
        {
            _countText.Text = _subject.CountLabel;
        }
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildEntries();

    /// <summary>作业内容与附件按模型重建；笔迹单独一层，不随内容重建。</summary>
    public void RebuildEntries()
    {
        _entriesPanel.Children.Clear();
        // 工具栏引用的是已重建的编辑器，必须一并清理，避免 SetEditing 更新到旧控件。
        _entryEditors.Clear();
        _attachmentControls.Clear();
        _entryAutofills.Clear();
        foreach (HomeworkEntry entry in _subject.Entries)
        {
            _entriesPanel.Children.Add(CreateEntryView(entry));
        }

        _countText.Text = _subject.CountLabel;
        _watermark.IsVisible = _subject.Entries.Count == 0 && _subject.InkStrokes.Count == 0;
        RenderStrokes();
    }

    private Control CreateEntryView(HomeworkEntry entry)
    {
        StackPanel panel = new() { Spacing = 6 };
        RichTextEditor editor = new()
        {
            Document = RichTextContent.ToDocument(entry),
            FontSize = 20,
            IsEditing = _isEditing
        };
        editor.ContentChanged = () =>
        {
            RichTextContent.Save(entry, editor.Document);
            _contentChanged();
        };
        if (_autofill is not null && _autofillPopup is not null)
        {
            // 作业补全挂在编辑器内部的输入框上：候选采纳后写回富文本模型并同步统计。
            _entryAutofills.Add(AutofillController.AttachHomework(
                _autofill,
                editor.Input,
                _autofillPopup,
                () => _subject.Name,
                () => _isEditing,
                () =>
                {
                    RichTextContent.Save(entry, editor.Document);
                    _contentChanged();
                }));
        }

        panel.Children.Add(editor);

        // 格式工具不放在磁贴里：编辑器获得焦点时由宿主在悬浮岛上显示（与旧版一致）。
        editor.Input.GotFocus += (_, _) => EntryEditorFocused?.Invoke(this, editor);
        _entryEditors.Add((editor, entry));

        // 每条作业自己的操作：添加图片与删除这条作业。
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 2, IsVisible = _isEditing };
        if (_addAttachment is not null)
        {
            actions.Children.Add(CreateIconButton(
                nameof(FluentGlyphs.ImageAdd),
                "添加图片",
                async (_, _) => await _addAttachment(entry)));
        }

        actions.Children.Add(CreateIconButton(nameof(FluentGlyphs.Delete), "删除这条作业", (_, _) => DeleteHomework(entry), mirrored: true));
        panel.Children.Add(actions);

        foreach (AttachmentItem attachment in entry.Attachments)
        {
            AttachmentImageControl view = new(
                attachment,
                () =>
                {
                    // 删除附件只从当前作业移除；项目资源目录里的文件由项目清理逻辑处理。
                    entry.Attachments.Remove(attachment);
                    entry.NotifyAttachmentsChanged();
                    _contentChanged();
                },
                _contentChanged,
                _interactionChanged);
            // 与富文本编辑器一样跟随磁贴的编辑态：查看模式只显示画面，编辑模式才允许选中与拖动。
            view.SetEditing(_isEditing);
            _attachmentControls.Add(view);
            panel.Children.Add(view);
        }

        return panel;
    }

    /// <summary>纯色色块按钮，用于调色板与磁贴主题色。</summary>
    private static Button CreateColorSwatch(string hex, double size)
    {
        BoardColor color = BoardColor.Parse(hex, BoardColor.White);
        return new Button
        {
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(color.ToColor()),
            BorderBrush = new SolidColorBrush(BoardColor.FromArgb(64, 255, 255, 255).ToColor()),
            BorderThickness = new Thickness(1)
        };
    }

    /// <summary>磁贴主题色：从预设色里选一个，立即应用到边框、标题与水印。</summary>
    private Button CreateThemeButton()
    {
        Button button = CreateIconButton(nameof(FluentGlyphs.Color), "磁贴主题色", (_, _) => { });
        WrapPanel colors = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        foreach (string hex in new[] { "#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF", "#F87171" })
        {
            Button swatch = CreateColorSwatch(ColorPalette.Resolve(hex), 32);
            swatch.Margin = new Thickness(3);
            swatch.Click += (_, _) =>
            {
                _subject.AccentHex = hex;
                _subject.IsAccentExplicit = true;
                ApplyAccent();
                button.Flyout?.Hide();
                _contentChanged();
            };
            colors.Children.Add(swatch);
        }

        button.Flyout = new Flyout { Content = colors };
        return button;
    }

    /// <summary>重画全部笔迹；缩放磁贴不会改变已有笔迹的坐标。</summary>
    internal void RenderStrokes() => _inkLayer.Attach(_subject.InkStrokes);

    /// <summary>
    /// 按内容测量磁贴需要的尺寸：标题与每条作业都按可用宽度量一遍，
    /// 出现换行时高度一起增长；笔迹超出内容时按笔迹范围补齐。
    /// 自动排列用它把磁贴收紧到刚好放下内容，并把过宽的磁贴收窄成换行。
    /// </summary>
    internal (double Width, double Height) MeasureContentSize(double maxWidth = double.PositiveInfinity)
    {
        _entriesPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _nameEditor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // 标题行左侧留白 + 计数文字 + 编辑按钮的占位宽度。
        double contentWidth = _nameEditor.DesiredSize.Width + 110;
        foreach (Control child in _entriesPanel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // 作业区左侧留白 + 滚动条与内边距。
            contentWidth = Math.Max(contentWidth, child.DesiredSize.Width + 28);
        }

        // maxWidth 由自动排列的列宽决定：需要一行多放时收窄磁贴，让文字换行而不是溢出。
        double limit = double.IsFinite(Width) ? Math.Min(Width, Math.Max(MinimumTileWidth, maxWidth)) : Math.Max(MinimumTileWidth, maxWidth);
        double width = Math.Max(MinimumTileWidth, Math.Min(limit, contentWidth));
        double inkRight = 0, inkBottom = 0;
        foreach (InkStrokeData stroke in _subject.InkStrokes)
        {
            foreach (BoardPoint point in stroke.Points)
            {
                inkRight = Math.Max(inkRight, point.X + stroke.Thickness * stroke.TipScaleX / 2);
                inkBottom = Math.Max(inkBottom, point.Y + stroke.Thickness * stroke.TipScaleY / 2);
            }
        }

        width = Math.Max(width, inkRight);
        _entriesPanel.Measure(new Size(Math.Max(1, width - 28), double.PositiveInfinity));
        _nameEditor.Measure(new Size(Math.Max(1, width - 110), double.PositiveInfinity));
        double height = Math.Max(MinimumTileHeight, Math.Max(inkBottom,
            _entriesPanel.DesiredSize.Height + _nameEditor.DesiredSize.Height + 38));
        // 测量用的是临时约束，测量完要让下一次真实布局重新计算。
        InvalidateMeasure();
        return (width, height);
    }

    /// <summary>应用磁贴背景（颜色层、图片与毛玻璃）。</summary>
    public void ApplyBackground(BackgroundSettings style) => _background.Apply(style);

    /// <summary>应用标题字号；编辑框与只读标题保持一致。</summary>
    public void ApplyTitleSize(double size)
    {
        double clamped = Math.Clamp(size <= 0 ? 29 : size, 14, 72);
        _nameText.FontSize = clamped;
        _nameEditor.FontSize = clamped;
    }

    /// <summary>
    /// 切换手写模式：只有笔迹层接收指针，其它编辑入口临时让位，
    /// 避免书写时误触按钮或选中文字。
    /// </summary>
    public void SetInkMode(bool drawing, InkToolSettings settings)
    {
        bool active = drawing && _isEditing;
        _inkLayer.SetInkMode(active, settings);
        _entriesPanel.IsHitTestVisible = !active;
        _nameEditor.IsHitTestVisible = !active;
        _editingTools.IsVisible = _isEditing && !active;
        foreach (Thumb handle in _resizeHandles) handle.IsVisible = _isEditing && !active;
        _headerMoveThumb.IsVisible = _isEditing && !active;
        foreach ((RichTextEditor editor, HomeworkEntry _) in _entryEditors) editor.IsEditing = _isEditing && !active;
        // 画笔模式下附件也退出可编辑状态，避免书写时误选中图片。
        foreach (AttachmentImageControl attachment in _attachmentControls) attachment.SetEditing(_isEditing && !active);
    }

    /// <summary>清空本磁贴的笔迹。</summary>
    public void ClearInk() => _inkLayer.ClearStrokes();

    /// <summary>撤销最后一笔，返回是否移除过内容。</summary>
    public bool UndoInk() => _inkLayer.UndoLastStroke();

    /// <summary>磁贴内的图标按钮：无边框、悬停高亮，触屏有足够命中面积。</summary>
    /// <summary>磁贴内的图标按钮；symbol 使用 FluentGlyphs 的语义名。</summary>
    private static Button CreateIconButton(string symbol, string tooltip, EventHandler<RoutedEventArgs> click, bool mirrored = false)
    {
        FluentIcon icon = new() { Symbol = symbol, FontSize = 16 };
        if (mirrored)
        {
            icon.RenderTransform = new ScaleTransform(-1, 1);
            icon.RenderTransformOrigin = RelativePoint.Center;
        }

        Button button = new()
        {
            Width = 36,
            Height = 34,
            MinWidth = 36,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()),
            Content = icon
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += click;
        return button;
    }
}
