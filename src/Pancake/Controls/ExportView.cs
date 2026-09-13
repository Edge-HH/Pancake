using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Pancake.Models;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 图片导出：左侧选项、右侧预览，预览与输出共用同一块画布。
/// 排版只用于本次导出，不修改看板；磁贴重叠或越界会标红并禁止导出。
/// </summary>
public sealed class ExportView : Grid
{
    private static readonly (string Label, double Ratio)[] Ratios =
        [("16:9", 16d / 9d), ("9:16", 9d / 16d), ("4:3", 4d / 3d), ("3:4", 3d / 4d), ("1:1", 1d)];

    private readonly ProjectDocument _project;
    private readonly BoardSettingsState _settings;
    private readonly Window _window;
    private readonly Action _close;
    private readonly BackgroundSettings _tileStyle;
    private readonly Grid _stage = new();
    private readonly Canvas _canvas = new();
    private readonly Canvas _handles = new();
    private readonly Grid _renderHost = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _backgroundImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly List<SubjectTileControl> _tiles = [];
    // 导出用的科目副本：色系只改这里的主题色与富文本颜色，看板数据不受影响。
    private readonly List<SubjectBoard> _exportSubjects = [];
    private readonly List<Border> _handlesByTile = [];
    private readonly List<ExportTilePlacement> _placements = [];
    private readonly NumericUpDown _width = new() { Minimum = 256, Maximum = 8192, Increment = 16, Value = 1920, Width = 150 };
    private readonly NumericUpDown _height = new() { Minimum = 256, Maximum = 8192, Increment = 16, Value = 1080, Width = 150 };
    private readonly ComboBox _ratio = new() { MinWidth = 150 };
    private readonly CheckBox _showTitle = new() { Content = "显示标题", IsChecked = true };
    private readonly TextBox _titleInput = new() { MaxLength = 200, Width = 220 };
    private readonly ComboBox _alignment = new() { MinWidth = 150 };
    private readonly NumericUpDown _titleSize = new() { Minimum = 12, Maximum = 200, Increment = 2, Value = 48, Width = 150 };
    private readonly ComboBox _titleFont = new() { MinWidth = 220 };
    private readonly ComboBox _palette = new() { MinWidth = 150 };
    private readonly Slider _opacity = new() { Minimum = 0, Maximum = 100, Width = 220 };
    private readonly Slider _blur = new() { Minimum = 0, Maximum = 100, Width = 220 };
    private readonly Slider _scale = new() { Minimum = 1, Maximum = 1000, Width = 220, IsEnabled = false };
    private readonly Button _save = new() { Content = "导出 PNG", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private Color _background = Color.FromRgb(31, 31, 31);
    private string _backgroundPath = string.Empty;
    private double _titleHeight;
    private int _selected = -1;
    private IPointer? _pointer;
    private Point _dragStart;
    private double _dragX;
    private double _dragY;
    private bool _updating;
    private bool _saving;

    public ExportView(ProjectDocument project, BoardSettingsState settings, Window window, Action close)
    {
        _project = project;
        _settings = ProjectStore.Clone(settings);
        _tileStyle = _settings.TileBackground;
        _window = window;
        _close = close;
        ColumnDefinitions = new ColumnDefinitions("320,*");

        // 背景沿用看板当前的作业板/跨区底图设置，导出时仍可单独调整透明度与模糊。
        BackgroundSettings board = _settings.SharedBackgroundEnabled && _settings.LayoutMode == "Split"
            ? _settings.SharedBackground
            : _settings.BoardBackground;
        _background = BoardColor.Parse(board.Color, BoardColor.FromRgb(31, 31, 31)).ToColor();
        if (MediaLibrary.IsImage(board.ImagePath) && File.Exists(board.ImagePath)) _backgroundPath = board.ImagePath;
        _opacity.Value = (1 - Math.Clamp(_tileStyle.ColorOpacity, 0, 1)) * 100;
        _blur.Value = _tileStyle.Glass ? _tileStyle.Blur : 0;

        Children.Add(BuildOptions());
        Children.Add(BuildPreview());
        _canvas.Children.Add(_backgroundImage);
        _canvas.Children.Add(_title);
        _title.FontFamily = FontService.DefaultFamily;
        _title.FontWeight = FontWeight.SemiBold;
        _title.TextWrapping = TextWrapping.Wrap;
        _title.ZIndex = 100;
        _titleInput.Text = $"{project.CreatedAt:M月d日}作业";

        BuildTiles();
        UpdateTitleAndArrange();
        Loaded += (_, _) => RefreshBackgroundImage();
    }

    /// <summary>左侧选项面板。</summary>
    private Control BuildOptions()
    {
        StackPanel options = new() { Margin = new Thickness(20), Spacing = 12 };
        options.Children.Add(new TextBlock { Text = "导出图片", FontSize = 24 });
        options.Children.Add(new TextBlock
        {
            Text = "拖动预览中的磁贴调整位置，选中后可修改缩放。排版仅用于本次导出。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.7 }
        });

        _ratio.ItemsSource = Ratios.Select(item => item.Label).Append("自定义").ToArray();
        _ratio.SelectedIndex = 0;
        _ratio.SelectionChanged += (_, _) => ApplyRatio();
        _width.ValueChanged += (_, _) => DimensionsChanged(syncRatio: false);
        _height.ValueChanged += (_, _) => DimensionsChanged(syncRatio: false);
        options.Children.Add(_ratio);
        options.Children.Add(Labeled("宽度（像素）", _width));
        options.Children.Add(Labeled("高度（像素）", _height));

        options.Children.Add(_showTitle);
        options.Children.Add(Labeled("标题", _titleInput));
        _alignment.ItemsSource = new[] { "左上", "上方居中", "右上" };
        _alignment.SelectedIndex = 0;
        _alignment.SelectionChanged += (_, _) => UpdateTitleAndArrange();
        options.Children.Add(Labeled("标题位置", _alignment));
        _titleSize.ValueChanged += (_, _) => UpdateTitleAndArrange();
        options.Children.Add(Labeled("标题字号", _titleSize));
        // 标题字体：随包字体始终在列表首位，其余为系统已安装字体。
        List<string> families = [FontService.FamilyName];
        families.AddRange(PlatformServices.FontCatalog.AvailableFamilies
            .Where(name => !string.Equals(name, FontService.FamilyName, StringComparison.CurrentCultureIgnoreCase)));
        _titleFont.ItemsSource = families;
        _titleFont.SelectedIndex = 0;
        _titleFont.SelectionChanged += (_, _) =>
        {
            if (_titleFont.SelectedItem is not string family) return;
            _title.FontFamily = family == FontService.FamilyName ? FontService.DefaultFamily : new FontFamily(family);
            UpdateTitleAndArrange();
        };
        options.Children.Add(Labeled("标题字体", _titleFont));
        // 色系只作用于本次导出：预设主题色与富文本里的预设色按色系换算，自定义色保持不变。
        _palette.ItemsSource = new[] { "鲜明", "马卡龙" };
        _palette.SelectedIndex = ColorPalette.IsMacaron ? 1 : 0;
        _palette.SelectionChanged += (_, _) => ApplyPalette();
        options.Children.Add(Labeled("色系", _palette));
        _showTitle.IsCheckedChanged += (_, _) => UpdateTitleAndArrange();
        _titleInput.TextChanged += (_, _) => UpdateTitleAndArrange();

        options.Children.Add(Labeled("背景颜色", BuildBackgroundColors()));
        Button chooseImage = new() { Content = "选择背景图片", HorizontalAlignment = HorizontalAlignment.Stretch };
        chooseImage.Click += async (_, _) => await ChooseBackgroundAsync();
        options.Children.Add(chooseImage);
        Button clearImage = new() { Content = "移除背景图片", HorizontalAlignment = HorizontalAlignment.Stretch };
        clearImage.Click += (_, _) => { _backgroundPath = string.Empty; RefreshBackgroundImage(); };
        options.Children.Add(clearImage);

        _opacity.ValueChanged += (_, _) =>
        {
            // 滑条表示透明度，设置里保存的是不透明度，两者互为补数。
            _tileStyle.ColorOpacity = 1 - _opacity.Value / 100;
            _tileStyle.ColorCleared = false;
            ApplyTileSurfaces();
        };
        options.Children.Add(Labeled("磁贴背景透明度（%）", _opacity));
        _blur.ValueChanged += (_, _) =>
        {
            _tileStyle.Blur = _blur.Value;
            _tileStyle.Glass = _blur.Value > 0;
            ApplyTileSurfaces();
        };
        options.Children.Add(Labeled("磁贴背景模糊", _blur));

        _scale.ValueChanged += (_, _) => ResizeSelected();
        options.Children.Add(Labeled("选中磁贴缩放（%）", _scale));

        Button arrange = new() { Content = "自动排列磁贴", HorizontalAlignment = HorizontalAlignment.Stretch };
        arrange.Click += (_, _) => AutoArrange();
        options.Children.Add(arrange);
        options.Children.Add(_status);
        _save.Click += async (_, _) => await SaveAsync();
        options.Children.Add(_save);
        Button cancel = new() { Content = "返回看板", HorizontalAlignment = HorizontalAlignment.Stretch };
        cancel.Click += (_, _) => { if (!_saving) _close(); };
        options.Children.Add(cancel);

        return new ScrollViewer
        {
            Content = options,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor())
        };
    }

    /// <summary>右侧预览：画布放在 Viewbox 里等比缩放显示。</summary>
    private Control BuildPreview()
    {
        // 预览与导出共用画布；预览只做视觉缩放，导出时把画布交给 1:1 的渲染宿主。
        _stage.Children.Add(_canvas);
        _stage.Children.Add(_handles);
        Viewbox preview = new()
        {
            Child = _stage,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid host = new();
        host.Children.Add(preview);
        host.Children.Add(_renderHost);
        Grid.SetColumn(host, 1);
        return host;
    }

    private static Control Labeled(string label, Control control)
    {
        StackPanel panel = new() { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(control);
        return panel;
    }

    /// <summary>背景颜色：常用预设加十六进制输入，避免依赖额外的取色器控件。</summary>
    private Control BuildBackgroundColors()
    {
        WrapPanel swatches = new() { Orientation = Orientation.Horizontal };
        foreach (string hex in new[] { "#1F1F1F", "#000000", "#151515", "#FFFFFF", "#F5F5FA", "#20304050" })
        {
            Button swatch = new()
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BoardColor.Parse(hex, BoardColor.Black).ToColor())
            };
            string captured = hex;
            swatch.Click += (_, _) =>
            {
                _background = BoardColor.Parse(captured, BoardColor.Black).ToColor();
                RefreshBackgroundImage();
            };
            ToolTip.SetTip(swatch, captured);
            swatches.Children.Add(swatch);
        }

        TextBox hexBox = new() { Watermark = "#RRGGBB", Width = 110 };
        hexBox.LostFocus += (_, _) =>
        {
            _background = BoardColor.Parse(hexBox.Text, BoardColor.FromRgb(31, 31, 31)).ToColor();
            RefreshBackgroundImage();
        };
        swatches.Children.Add(hexBox);
        return swatches;
    }

    /// <summary>按画布比例同步宽高；选择自定义后不再联动。</summary>
    private void ApplyRatio()
    {
        if (_ratio.SelectedIndex < 0 || _ratio.SelectedIndex >= Ratios.Length) return;
        double ratio = Ratios[_ratio.SelectedIndex].Ratio;
        _updating = true;
        try
        {
            double height = (double)(_height.Value ?? 1080);
            _width.Value = (decimal)Math.Round(height * ratio);
        }
        finally
        {
            _updating = false;
        }

        DimensionsChanged(syncRatio: true);
    }

    private void DimensionsChanged(bool syncRatio)
    {
        if (_updating) return;
        if (syncRatio && _ratio.SelectedIndex >= 0 && _ratio.SelectedIndex < Ratios.Length)
        {
            // 手动改尺寸后如果比例不再匹配，就转到“自定义”，避免显示与画布不一致。
            double ratio = (double)(_width.Value ?? 1920) / Math.Max(1, (double)(_height.Value ?? 1080));
            if (Math.Abs(ratio - Ratios[_ratio.SelectedIndex].Ratio) > 0.001)
            {
                _updating = true;
                _ratio.SelectedIndex = Ratios.Length;
                _updating = false;
            }
        }

        UpdateTitleAndArrange();
    }

    private void BuildTiles()
    {
        _exportSubjects.AddRange(AppDataStore.RestoreSubjects(_project.Subjects.OrderBy(s => s.Y).ThenBy(s => s.X)));
        foreach (SubjectBoard subject in _exportSubjects)
        {
            SubjectTileControl tile = new(
                subject,
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => { });
            tile.SetEditing(false);
            tile.ApplyBackground(_tileStyle);
            tile.ApplyTitleSize(_settings.TileTitleSize);
            _tiles.Add(tile);
            _canvas.Children.Add(tile);

            // 每块磁贴对应一个透明操作层，负责选中与拖动，不改变磁贴自身的命中逻辑。
            Border handle = new()
            {
                Background = new SolidColorBrush(BoardColor.Transparent.ToColor()),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(BoardColor.Transparent.ToColor())
            };
            int index = _tiles.Count - 1;
            handle.PointerPressed += (_, e) => BeginDrag(index, e);
            handle.PointerMoved += (_, e) => Drag(e);
            handle.PointerReleased += (_, e) => EndDrag(e);
            handle.PointerCaptureLost += (_, _) => _pointer = null;
            _handlesByTile.Add(handle);
            _handles.Children.Add(handle);
        }
    }

    /// <summary>
    /// 切换色系：预设主题色与富文本里的预设色按色系一一换算，自定义颜色保持不变。
    /// 只改导出用的副本，看板本身不受影响；换算是一一对应映射，来回切换不会累计误差。
    /// </summary>
    private void ApplyPalette()
    {
        bool macaron = _palette.SelectedIndex == 1;
        foreach (SubjectBoard subject in _exportSubjects)
        {
            subject.AccentHex = ColorPalette.ResolveAccent(subject.AccentHex, subject.IsAccentExplicit, macaron);
            foreach (HomeworkEntry entry in subject.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.RtfContent))
                {
                    entry.RtfContent = ColorPalette.ConvertRtf(entry.RtfContent, macaron);
                }
            }
        }

        // 富文本是画好后就不再重读的，颜色换算后重建内容才能反映到预览与导出。
        foreach (SubjectTileControl tile in _tiles) tile.RebuildEntries();
    }

    private void UpdateTitleAndArrange()
    {
        double width = (double)(_width.Value ?? 1920);
        double height = (double)(_height.Value ?? 1080);
        _canvas.Width = width;
        _canvas.Height = height;
        _stage.Width = width;
        _stage.Height = height;
        _renderHost.Width = width;
        _renderHost.Height = height;
        _backgroundImage.Width = width;
        _backgroundImage.Height = height;

        _title.IsVisible = _showTitle.IsChecked == true;
        _title.Text = _titleInput.Text ?? string.Empty;
        _title.FontSize = (double)(_titleSize.Value ?? 48);
        _titleHeight = 0;
        if (_title.IsVisible)
        {
            _title.Measure(new Size(width, height));
            _titleHeight = Math.Min(height * 0.35, _title.DesiredSize.Height + 32);
            _title.Width = width;
            _title.Height = _titleHeight;
            Canvas.SetTop(_title, 0);
            _title.TextAlignment = _alignment.SelectedIndex switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left
            };
            _title.Margin = new Thickness(24, 12, 24, 12);
        }

        AutoArrange();
    }

    /// <summary>按当前画布重新排版磁贴；空间不足时按比例缩小整块磁贴。</summary>
    private void AutoArrange()
    {
        _placements.Clear();
        if (_tiles.Count == 0)
        {
            UpdatePlacements();
            return;
        }

        double width = (double)(_width.Value ?? 1920);
        double height = (double)(_height.Value ?? 1080);
        try
        {
            _placements.AddRange(ExportLayout.Arrange(
                _tiles.Select(tile => (tile.Width, tile.Height)).ToList(),
                width,
                height,
                _titleHeight,
                _settings.AutoLayoutGap,
                _settings.AutoLayoutAlign));
        }
        catch (InvalidOperationException ex)
        {
            _status.Text = ex.Message;
        }

        UpdatePlacements();
    }

    /// <summary>把排版结果应用到磁贴与操作层，并检查重叠与越界。</summary>
    private void UpdatePlacements()
    {
        bool invalid = _placements.Count != _tiles.Count;
        for (int index = 0; index < _tiles.Count && index < _placements.Count; index++)
        {
            ExportTilePlacement placement = _placements[index];
            _tiles[index].RenderTransform = new ScaleTransform(placement.Scale, placement.Scale);
            _tiles[index].RenderTransformOrigin = RelativePoint.TopLeft;
            Canvas.SetLeft(_tiles[index], placement.X);
            Canvas.SetTop(_tiles[index], placement.Y);

            Border handle = _handlesByTile[index];
            handle.Width = placement.Width;
            handle.Height = placement.Height;
            Canvas.SetLeft(handle, placement.X);
            Canvas.SetTop(handle, placement.Y);
            bool overlap = _placements.Where((_, other) => other != index)
                .Any(other => ExportLayout.Overlap(placement, other));
            bool outside = !ExportLayout.InBounds(placement, _canvas.Width, _canvas.Height, _titleHeight);
            invalid |= overlap || outside;
            handle.BorderBrush = new SolidColorBrush(overlap || outside
                ? BoardColor.FromRgb(239, 68, 68).ToColor()
                : index == _selected ? BoardColor.FromRgb(96, 165, 250).ToColor() : Colors.Transparent);
        }

        _save.IsEnabled = !invalid && !_saving && _tiles.Count > 0;
        _status.Text = _tiles.Count == 0
            ? "没有可导出的科目磁贴。"
            : invalid
                ? "磁贴存在重叠或越界，请调整或点击自动排列。"
                : "预览已就绪，标题区域已预留。";
    }

    private void BeginDrag(int index, PointerPressedEventArgs e)
    {
        _selected = index;
        _updating = true;
        _scale.Value = Math.Clamp(_placements[index].Scale * 100, 1, 1000);
        _updating = false;
        _scale.IsEnabled = true;
        _pointer = e.Pointer;
        _dragStart = e.GetPosition(_canvas);
        _dragX = _placements[index].X;
        _dragY = _placements[index].Y;
        e.Pointer.Capture(_handlesByTile[index]);
        UpdatePlacements();
        e.Handled = true;
    }

    private void Drag(PointerEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer) || _selected < 0) return;
        Point current = e.GetPosition(_canvas);
        ExportTilePlacement placement = _placements[_selected];
        // 以 8 像素为步长吸附，避免拖动后产生半像素锯齿。
        placement.X = Math.Max(0, Math.Round((_dragX + current.X - _dragStart.X) / 8) * 8);
        placement.Y = Math.Max(_titleHeight, Math.Round((_dragY + current.Y - _dragStart.Y) / 8) * 8);
        UpdatePlacements();
        e.Handled = true;
    }

    private void EndDrag(PointerReleasedEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        e.Pointer.Capture(null);
        _pointer = null;
        e.Handled = true;
    }

    /// <summary>按滑条调整选中磁贴的缩放，并限制在画布能容纳的最大比例内。</summary>
    private void ResizeSelected()
    {
        if (_updating || _selected < 0 || _selected >= _placements.Count) return;
        ExportTilePlacement placement = _placements[_selected];
        SubjectTileControl tile = _tiles[_selected];
        double maximum = Math.Min(
            _canvas.Width / Math.Max(1, tile.Width),
            (_canvas.Height - _titleHeight) / Math.Max(1, tile.Height));
        placement.Scale = Math.Clamp(_scale.Value / 100, 0.001, maximum);
        placement.Width = tile.Width * placement.Scale;
        placement.Height = tile.Height * placement.Scale;
        placement.X = Math.Clamp(placement.X, 0, Math.Max(0, _canvas.Width - placement.Width));
        placement.Y = Math.Clamp(placement.Y, _titleHeight, Math.Max(_titleHeight, _canvas.Height - placement.Height));
        UpdatePlacements();
    }

    private void ApplyTileSurfaces()
    {
        foreach (SubjectTileControl tile in _tiles) tile.ApplyBackground(_tileStyle);
    }

    private async Task ChooseBackgroundAsync()
    {
        IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = MediaLibrary.ImageExtensions.Select(extension => "*" + extension).ToArray()
                }
            ]
        });
        string? path = files.Select(file => file.TryGetLocalPath()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (path is null) return;
        _backgroundPath = path;
        RefreshBackgroundImage();
    }

    /// <summary>背景由颜色层与可选图片叠加而成，与看板的分层方式一致。</summary>
    private void RefreshBackgroundImage()
    {
        _canvas.Background = new SolidColorBrush(_background);
        if (string.IsNullOrWhiteSpace(_backgroundPath) || !File.Exists(_backgroundPath))
        {
            _backgroundImage.Source = null;
            return;
        }

        try
        {
            _backgroundImage.Source = new Bitmap(_backgroundPath);
            _backgroundImage.Stretch = _settings.BoardBackground.ImageMode switch
            {
                "Stretch" => Stretch.Fill,
                "Fit" => Stretch.Uniform,
                _ => Stretch.UniformToFill
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"无法读取背景图片：{ex.Message}";
            _backgroundImage.Source = null;
        }
    }

    private async Task SaveAsync()
    {
        if (!_save.IsEnabled || _saving) return;
        IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 PNG",
            SuggestedFileName = string.Concat(_project.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)),
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }]
        });
        if (file is null) return;
        string? path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        _saving = true;
        _save.IsEnabled = false;
        _handles.IsVisible = false;
        try
        {
            RenderToFile(path);
            _status.Text = $"已导出 {(int)_canvas.Width} × {(int)_canvas.Height} PNG：{path}";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.Text = $"导出失败：{ex.Message}";
        }
        finally
        {
            _saving = false;
            _handles.IsVisible = true;
            UpdatePlacements();
        }
    }

    /// <summary>
    /// 把画布按 1:1 渲染成 PNG。预览里画布被 Viewbox 缩放显示，
    /// 因此先把画布临时交给离屏渲染宿主，保证输出分辨率与用户选择的一致。
    /// </summary>
    public void RenderToFile(string path)
    {
        _stage.Children.Remove(_canvas);
        _renderHost.Children.Add(_canvas);
        try
        {
            _canvas.Measure(new Size(_canvas.Width, _canvas.Height));
            _canvas.Arrange(new Rect(0, 0, _canvas.Width, _canvas.Height));
            PixelSize size = new((int)Math.Round(_canvas.Width), (int)Math.Round(_canvas.Height));
            RenderTargetBitmap bitmap = new(size, new Vector(96, 96));
            bitmap.Render(_canvas);
            bitmap.Save(path);
        }
        finally
        {
            _renderHost.Children.Remove(_canvas);
            _stage.Children.Insert(0, _canvas);
        }
    }

#if PANCAKE_UI_TESTS
    /// <summary>验证构建专用：标题字体候选列表（首位是随包字体）。</summary>
    internal IReadOnlyList<string> TitleFontsForVerification =>
        (_titleFont.ItemsSource as IEnumerable<string>)?.ToArray() ?? [];

    /// <summary>验证构建专用：切换标题字体，走与界面相同的回调。</summary>
    internal void ApplyTitleFontForVerification(int index) => _titleFont.SelectedIndex = index;

    /// <summary>验证用：切换导出色系并取回导出副本的主题色，用于确认换算真实生效且不动看板数据。</summary>
    internal void ApplyPaletteForVerification(bool macaron) => _palette.SelectedIndex = macaron ? 1 : 0;

    internal IReadOnlyList<string> ExportAccentsForVerification =>
        _exportSubjects.Select(subject => subject.AccentHex).ToArray();

    /// <summary>验证构建专用：把画布尺寸固定成便于快速渲染的小尺寸。</summary>
    internal void SetCanvasForVerification(int width, int height)
    {
        _updating = true;
        try
        {
            _width.Value = width;
            _height.Value = height;
        }
        finally
        {
            _updating = false;
        }

        UpdateTitleAndArrange();
    }

    internal int TileCount => _tiles.Count;

    internal string Status => _status.Text ?? string.Empty;
#endif
}
