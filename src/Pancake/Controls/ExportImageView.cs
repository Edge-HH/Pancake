using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace Pancake.Controls;

/// <summary>预览和输出共用干净画布；选择边框置于同级覆盖层，永不进入 PNG。</summary>
public sealed class ExportImageView : Grid
{
    private readonly ProjectDocument _project;
    private readonly Window _window;
    private readonly Action _close;
    private readonly Canvas _canvas = new();
    private readonly Canvas _handles = new();
    private readonly Canvas _guides = new() { IsHitTestVisible = false };
    private readonly Grid _stage = new();
    private readonly Canvas _renderHost = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly NumberBox _width = new() { Header = "宽度（像素）", Minimum = 256, Maximum = 8192, Value = 1920, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly NumberBox _height = new() { Header = "高度（像素）", Minimum = 256, Maximum = 8192, Value = 1080, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly ComboBox _ratio = new() { Header = "画布比例", ItemsSource = new[] { "16:9", "9:16", "4:3", "3:4", "1:1", "自定义" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _titleInput = new() { Header = "标题", MaxLength = 200 };
    private readonly CheckBox _showTitle = new() { Content = "显示标题", IsChecked = true };
    private readonly ComboBox _alignment = new() { Header = "标题位置", ItemsSource = new[] { "左上", "上方居中", "右上" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _palette = new() { Header = "色系", ItemsSource = new[] { "鲜明", "马卡龙" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumberBox _scale = new() { Header = "选中磁贴缩放（%）", Minimum = 1, Maximum = 1000, Value = 100, IsEnabled = false, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly Button _save = new() { Content = "导出 PNG", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
    private readonly List<ExportTileVisual> _tiles = [];
    private List<ExportTilePlacement> _placements = [];
    private Color _background;
    private bool _ready, _updating, _saving;
    private int _selected = -1;
    private double _titleHeight;
    private uint? _pointer;
    private Point _start;
    private double _startX, _startY;
    private readonly TaskCompletionSource _loaded = new();

    public ExportImageView(ProjectDocument project, Window window, Action close)
    {
        _project = project; _window = window; _close = close;
        _background = BoardTheme.IsLight ? Microsoft.UI.Colors.White : Color.FromArgb(255, 31, 31, 31);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel options = new() { Padding = new Thickness(20), Spacing = 12 };
        ScrollViewer optionsScroll = new() { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(optionsScroll);
        options.Children.Add(new TextBlock { Text = "导出图片", FontSize = 24 });
        options.Children.Add(new TextBlock { Text = "拖动磁贴调整位置，选中后修改缩放。排版仅用于本次导出。", TextWrapping = TextWrapping.Wrap });
        options.Children.Add(_ratio); options.Children.Add(_width); options.Children.Add(_height);
        _titleInput.Text = $"{project.CreatedAt:M月d日}作业";
        options.Children.Add(_showTitle); options.Children.Add(_titleInput); options.Children.Add(_alignment);
        _palette.SelectedIndex = project.Palette == "Macaron" ? 1 : 0;
        options.Children.Add(_palette);
        Button backgroundButton = new() { Content = "背景颜色", HorizontalAlignment = HorizontalAlignment.Stretch };
        ColorPicker picker = new() { Color = _background, IsAlphaEnabled = false, IsColorChannelTextInputVisible = true };
        backgroundButton.Flyout = new Flyout { Content = picker };
        picker.ColorChanged += (_, args) => { _background = args.NewColor; RefreshAppearance(); };
        options.Children.Add(backgroundButton); options.Children.Add(_scale);
        Button arrange = new() { Content = "自动排列磁贴", HorizontalAlignment = HorizontalAlignment.Stretch };
        arrange.Click += (_, _) => AutoArrange(); options.Children.Add(arrange);
        options.Children.Add(_status); options.Children.Add(_save);
        Button cancel = new() { Content = "返回编辑", HorizontalAlignment = HorizontalAlignment.Stretch };
        cancel.Click += (_, _) => { if (!_saving) _close(); }; options.Children.Add(cancel);
        _save.Click += async (_, _) => await SaveAsync();

        _stage.Children.Add(_canvas); _stage.Children.Add(_handles); _stage.Children.Add(_guides);
        Viewbox preview = new() { Child = _stage, Stretch = Stretch.Uniform, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(preview, 1); Children.Add(preview);
        Grid.SetColumn(_renderHost, 1); Children.Add(_renderHost);
        _canvas.Children.Add(_title);
        _title.FontFamily = FontService.DefaultFamily;
        _title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _title.TextWrapping = TextWrapping.Wrap;
        Canvas.SetZIndex(_title, 100);
        Loaded += (_, _) => _loaded.TrySetResult();
        _ratio.SelectionChanged += (_, _) => ChangeDimensions(true, true);
        _width.ValueChanged += (_, _) => ChangeDimensions(true, false);
        _height.ValueChanged += (_, _) => ChangeDimensions(false, false);
        _showTitle.Checked += (_, _) => UpdateTitleAndArrange();
        _showTitle.Unchecked += (_, _) => UpdateTitleAndArrange();
        _titleInput.TextChanged += (_, _) => UpdateTitleAndArrange();
        _alignment.SelectionChanged += (_, _) => UpdateTitleAndArrange();
        _palette.SelectionChanged += (_, _) => RefreshAppearance();
        _scale.ValueChanged += (_, _) => ResizeSelected();
        SetDimensions();
    }

    public async Task InitializeAsync()
    {
        await _loaded.Task;
        try
        {
            var subjects = AppDataStore.RestoreSubjects(_project.Subjects.OrderBy(s => s.Y).ThenBy(s => s.X));
            foreach (var subject in subjects)
            {
                ExportTileVisual tile = new(subject, _background, _project.Palette);
                _tiles.Add(tile); _canvas.Children.Add(tile);
            }
            foreach (ExportTileVisual tile in _tiles) await tile.PrepareAsync();
            _ready = true;
            RefreshAppearance();
            AutoArrange();
        }
        catch (Exception ex) { _status.Text = "无法生成预览：" + ex.Message; _save.IsEnabled = false; }
    }

    private void ChangeDimensions(bool widthChanged, bool presetChanged)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            if (!double.IsFinite(_width.Value) || !double.IsFinite(_height.Value)) return;
            _width.Value = Math.Clamp(Math.Round(_width.Value), 256, 8192);
            _height.Value = Math.Clamp(Math.Round(_height.Value), 256, 8192);
            if (_ratio.SelectedIndex < 5)
            {
                double ratio = new[] { 16d / 9, 9d / 16, 4d / 3, 3d / 4, 1d }[_ratio.SelectedIndex];
                if (widthChanged || presetChanged)
                {
                    double targetHeight = Math.Round(_width.Value / ratio);
                    _height.Value = Math.Clamp(targetHeight, 256, 8192);
                    if (targetHeight != _height.Value) _width.Value = Math.Round(_height.Value * ratio);
                }
                else
                {
                    double targetWidth = Math.Round(_height.Value * ratio);
                    _width.Value = Math.Clamp(targetWidth, 256, 8192);
                    if (targetWidth != _width.Value) _height.Value = Math.Round(_width.Value / ratio);
                }
            }
            SetDimensions();
            AutoArrange();
        }
        finally { _updating = false; }
    }

    private void SetDimensions()
    {
        _canvas.Width = _handles.Width = _stage.Width = _width.Value;
        _canvas.Height = _handles.Height = _stage.Height = _height.Value;
        _canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, _width.Value, _height.Value) };
        _canvas.Background = new SolidColorBrush(_background);
        MeasureTitle();
    }

    private void MeasureTitle()
    {
        double margin = Math.Min(_width.Value, _height.Value) * .025;
        _title.Text = _titleInput.Text;
        _title.FontSize = Math.Max(12, Math.Min(_width.Value, _height.Value) * .045);
        _title.Width = _width.Value - 2 * margin;
        _title.TextAlignment = _alignment.SelectedIndex switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left };
        _title.Visibility = _showTitle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _title.Measure(new Size(_title.Width, double.PositiveInfinity));
        _titleHeight = _showTitle.IsChecked == true ? _title.DesiredSize.Height + margin : 0;
        Canvas.SetLeft(_title, margin); Canvas.SetTop(_title, margin);
    }

    private void UpdateTitleAndArrange() { MeasureTitle(); AutoArrange(); }
    private void RefreshAppearance()
    {
        _canvas.Background = new SolidColorBrush(_background);
        bool light = .2126 * _background.R + .7152 * _background.G + .0722 * _background.B > 145;
        _title.Foreground = new SolidColorBrush(light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        if (!_ready) return;
        foreach (ExportTileVisual tile in _tiles) tile.SetAppearance(_background, _palette.SelectedIndex == 1 ? "Macaron" : "Vivid");
    }

    private void AutoArrange()
    {
        if (!_ready) return;
        try
        {
            _placements = ExportLayout.Arrange(_tiles.Select(t => (t.Width, t.Height)).ToList(), _width.Value, _height.Value, _titleHeight);
            _selected = -1; _scale.IsEnabled = false;
            RebuildHandles();
            UpdatePlacements();
        }
        catch (Exception ex) { _status.Text = ex.Message; _save.IsEnabled = false; }
    }

    private void RebuildHandles()
    {
        _guides.Children.Clear();
        _handles.Children.Clear();
        for (int i = 0; i < _tiles.Count; i++)
        {
            int index = i;
            Border handle = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(2), ManipulationMode = ManipulationModes.None };
            ToolTipService.SetToolTip(handle, "拖动位置；点击后在左侧调整缩放");
            handle.PointerPressed += (_, e) =>
            {
                if (_saving || _pointer is not null) return;
                _selected = index; _pointer = e.Pointer.PointerId;
                _start = e.GetCurrentPoint(_handles).Position; _startX = _placements[index].X; _startY = _placements[index].Y;
                _updating = true; _scale.IsEnabled = true; _scale.Value = _placements[index].Scale * 100; _updating = false;
                handle.CapturePointer(e.Pointer); UpdatePlacements(); e.Handled = true;
            };
            handle.PointerMoved += (_, e) =>
            {
                if (_pointer != e.Pointer.PointerId || _selected != index) return;
                Point point = e.GetCurrentPoint(_handles).Position;
                ExportTilePlacement tile = _placements[index];
                tile.X = Math.Clamp(_startX + point.X - _start.X, 0, Math.Max(0, _width.Value - tile.Width));
                tile.Y = Math.Clamp(_startY + point.Y - _start.Y, _titleHeight, Math.Max(_titleHeight, _height.Value - tile.Height));
                var transform = _stage.TransformToVisual(this);
                double previewScale = Math.Max(.01, transform.TransformPoint(new Point(1, 0)).X - transform.TransformPoint(new Point(0, 0)).X);
                var snap = BoardLayout.Snap(new(tile.X, tile.Y, tile.Width, tile.Height),
                    _placements.Where((_, j) => j != index).Select(p => new LayoutRect(p.X, p.Y, p.Width, p.Height)),
                    _width.Value, _height.Value, _titleHeight, 8 / previewScale);
                tile.X = snap.X; tile.Y = snap.Y;
                AlignmentGuides.Draw(_guides, snap, _width.Value, _height.Value, 1 / previewScale);
                UpdatePlacements(); e.Handled = true;
            };
            void Release(object sender, PointerRoutedEventArgs e)
            {
                if (_pointer != e.Pointer.PointerId) return;
                _pointer = null; _guides.Children.Clear(); handle.ReleasePointerCapture(e.Pointer); e.Handled = true;
            }
            handle.PointerReleased += Release; handle.PointerCanceled += Release; handle.PointerCaptureLost += Release;
            _handles.Children.Add(handle);
        }
    }

    private void ResizeSelected()
    {
        if (_updating || _selected < 0 || !_ready || !double.IsFinite(_scale.Value)) return;
        ExportTileVisual source = _tiles[_selected];
        ExportTilePlacement tile = _placements[_selected];
        double maximum = Math.Min(_width.Value / source.Width, (_height.Value - _titleHeight) / source.Height);
        tile.Scale = Math.Min(Math.Max(.001, _scale.Value / 100), maximum);
        tile.Width = source.Width * tile.Scale; tile.Height = source.Height * tile.Scale;
        tile.X = Math.Clamp(tile.X, 0, Math.Max(0, _width.Value - tile.Width));
        tile.Y = Math.Clamp(tile.Y, _titleHeight, Math.Max(_titleHeight, _height.Value - tile.Height));
        _updating = true; _scale.Value = tile.Scale * 100; _updating = false;
        UpdatePlacements();
    }

    private void UpdatePlacements()
    {
        bool invalid = false;
        for (int i = 0; i < _placements.Count; i++)
        {
            ExportTilePlacement p = _placements[i];
            _tiles[i].RenderTransform = new ScaleTransform { ScaleX = p.Scale, ScaleY = p.Scale };
            Canvas.SetLeft(_tiles[i], p.X); Canvas.SetTop(_tiles[i], p.Y);
            Border handle = (Border)_handles.Children[i];
            handle.Width = p.Width; handle.Height = p.Height;
            Canvas.SetLeft(handle, p.X); Canvas.SetTop(handle, p.Y);
            bool overlap = _placements.Where((_, j) => i != j).Any(other => ExportLayout.Overlap(p, other));
            bool outside = !ExportLayout.InBounds(p, _width.Value, _height.Value, _titleHeight);
            invalid |= overlap || outside;
            handle.BorderBrush = new SolidColorBrush(overlap || outside ? Microsoft.UI.Colors.Red : i == _selected ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.Transparent);
        }
        _save.IsEnabled = _ready && !invalid && !_saving && _tiles.Count > 0;
        _status.Text = _tiles.Count == 0 ? "没有可导出的科目磁贴。" : invalid ? "磁贴存在重叠或越界，请调整或点击自动排列。" :
            _placements.Any(p => p.Scale * 20 < 12) ? "内容较多，导出文字可能偏小；可增大输出像素尺寸。" : "预览已就绪，标题区域已预留。";
    }

    private async Task SaveAsync()
    {
        if (!_save.IsEnabled || _saving) return;
        _saving = true; _save.IsEnabled = false; _handles.IsHitTestVisible = false;
        try
        {
            FileSavePicker picker = new() { SuggestedFileName = string.Concat(_project.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) };
            picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await RenderToFileAsync(file.Path);
            int width = (int)_width.Value, height = (int)_height.Value;
            _status.Text = $"已导出 {width} × {height} PNG：{file.Path}";
        }
        catch (Exception ex) { _status.Text = "导出失败：" + ex.Message; }
        finally { _saving = false; _handles.IsHitTestVisible = true; _save.IsEnabled = _ready && _tiles.Count > 0; }
    }
    internal async Task RenderToFileAsync(string path)
    {
        if (!_ready || _tiles.Count == 0) throw new InvalidOperationException("导出画布尚未就绪。");
        // 暂时移出 Viewbox，避免把预览阶段已经栅格化的小字号再次放大。
        // 同一画布仍连接主窗口视觉树，超出窗口的部分也可被 RenderTargetBitmap 捕获。
        _stage.Children.Remove(_canvas);
        _renderHost.Children.Add(_canvas);
        try
        {
            TaskCompletionSource laidOut = new();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { _canvas.UpdateLayout(); laidOut.TrySetResult(); });
            await laidOut.Task;
            RenderTargetBitmap bitmap = new();
            int width = (int)_width.Value, height = (int)_height.Value;
            double dpiScale = _canvas.XamlRoot.RasterizationScale;
            await bitmap.RenderAsync(_canvas, (int)Math.Ceiling(width / dpiScale), (int)Math.Ceiling(height / dpiScale));
            if (bitmap.PixelWidth < width || bitmap.PixelHeight < height)
                throw new InvalidOperationException($"当前设备返回 {bitmap.PixelWidth}×{bitmap.PixelHeight}，无法生成所选的 {width}×{height} 图片，请降低图片尺寸后重试。");
            byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
            using var memory = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            encoder.BitmapTransform.ScaledWidth = (uint)width;
            encoder.BitmapTransform.ScaledHeight = (uint)height;
            await encoder.FlushAsync(); memory.Seek(0);
            using Stream input = memory.AsStreamForRead();
            ProjectStore.AtomicWrite(path, target => input.CopyTo(target));
        }
        finally
        {
            _renderHost.Children.Remove(_canvas);
            _stage.Children.Insert(0, _canvas);
        }
    }

#if PANCAKE_UI_TESTS
    internal string VerificationStatus => _status.Text;
    internal void VerificationSetCanvas(int ratio, int titleAlignment)
    {
        _ratio.SelectedIndex = ratio;
        _alignment.SelectedIndex = titleAlignment;
    }
#endif

}
