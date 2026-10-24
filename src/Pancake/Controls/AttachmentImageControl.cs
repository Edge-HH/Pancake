using Pancake.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Models;
using Windows.Foundation;
using Windows.Storage;

namespace Pancake.Controls;

/// <summary>
/// 图片框编辑控件：选中后显示八点边框和图标工具栏。
/// 普通模式拖中央移动、拖边角等比缩放；裁切模式复用同一套拖拽框调整取景区域。
/// 位置由所在的 <see cref="AttachmentArea"/> 参与布局，移动和缩放都限制在这条作业的图片区内。
/// </summary>
public sealed class AttachmentImageControl : Grid
{
    // 尺寸只保留防止除零的下限，缩放上限只受作业区宽度约束，调节范围交给用户。
    private const double MinimumWidth = 24;
    private const double MinimumHeight = 24;

    private readonly AttachmentItem _attachment;
    private readonly Action _deleteAttachment;
    private readonly Action _contentChanged;
    private readonly Action<bool> _interactionChanged;
    private readonly Grid _imageFrame;
    private readonly Grid _overlay;
    private readonly Image _image;
    private readonly Border _selectionBorder;
    private readonly StackPanel _toolbar;
    private readonly CompositeTransform _imageTransform = new();
    private readonly List<Border> _resizeHandles = [];
    private bool _isEditing;
    private bool _isSelected;
    private bool _isCropping;
    private bool _isInteracting;
    private uint? _pointerId;
    private Point _lastPointerPosition;
    private ResizeHandle? _activeResizeHandle;
    // 拖动和缩放都从按下时的状态加总位移推算，避免增量累加在夹紧边界处漂移。
    private Point _dragStartPointer;
    private Rect _dragStartFrame;
    private double _imageAspect;
    private readonly TaskCompletionSource<bool> _imageReady = new();
    public Task<bool> ImageReady => _imageReady.Task;

    public AttachmentImageControl(AttachmentItem attachment, Action deleteAttachment, Action contentChanged, Action<bool> interactionChanged)
    {
        _attachment = attachment;
        _deleteAttachment = deleteAttachment;
        _contentChanged = contentChanged;
        _interactionChanged = interactionChanged;

        double initialWidth = Math.Max(MinimumWidth, attachment.FrameWidth);
        double initialHeight = attachment.AspectRatio > 0 ? initialWidth / attachment.AspectRatio : 240;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _imageFrame = new Grid
        {
            Width = initialWidth,
            Height = initialHeight,
            Background = BoardTheme.SurfaceBrush,
            Clip = new RectangleGeometry(),
            ManipulationMode = ManipulationModes.None
        };
        _overlay = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _overlay.Children.Add(_imageFrame);
        Children.Add(_overlay);
        _imageFrame.SizeChanged += (_, e) => ((RectangleGeometry)_imageFrame.Clip).Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _imageTransform
        };
        _imageFrame.Children.Add(_image);

        _selectionBorder = new Border
        {
            Name = "SelectionBorder",
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250)),
            BorderThickness = new Thickness(2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _overlay.Children.Add(_selectionBorder);

        AddResizeHandle(HorizontalAlignment.Left, VerticalAlignment.Top, ResizeHandle.TopLeft);
        AddResizeHandle(HorizontalAlignment.Center, VerticalAlignment.Top, ResizeHandle.Top);
        AddResizeHandle(HorizontalAlignment.Right, VerticalAlignment.Top, ResizeHandle.TopRight);
        AddResizeHandle(HorizontalAlignment.Left, VerticalAlignment.Center, ResizeHandle.Left);
        AddResizeHandle(HorizontalAlignment.Right, VerticalAlignment.Center, ResizeHandle.Right);
        AddResizeHandle(HorizontalAlignment.Left, VerticalAlignment.Bottom, ResizeHandle.BottomLeft);
        AddResizeHandle(HorizontalAlignment.Center, VerticalAlignment.Bottom, ResizeHandle.Bottom);
        AddResizeHandle(HorizontalAlignment.Right, VerticalAlignment.Bottom, ResizeHandle.BottomRight);

        _toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 2),
            Visibility = Visibility.Collapsed
        };
        _toolbar.Children.Add(CreateIconButton(FluentGlyphs.Crop, "裁切", (_, _) => ToggleCropMode()));
        _toolbar.Children.Add(CreateIconButton(FluentGlyphs.Rotate, "逆时针旋转", (_, _) => RotateImage(-90), true));
        _toolbar.Children.Add(CreateIconButton(FluentGlyphs.Rotate, "顺时针旋转", (_, _) => RotateImage(90)));
        _toolbar.Children.Add(CreateIconButton(FluentGlyphs.Reset, "复位", (_, _) => ResetImage()));
        _toolbar.Children.Add(CreateIconButton(FluentGlyphs.Delete, "删除", (_, _) => _deleteAttachment()));
        Grid.SetRow(_toolbar, 1);
        Children.Add(_toolbar);

        _imageFrame.PointerPressed += ImageFrame_PointerPressed;
        _imageFrame.PointerMoved += ImageFrame_PointerMoved;
        _imageFrame.PointerReleased += ImageFrame_PointerReleased;
        _imageFrame.PointerCanceled += ImageFrame_PointerReleased;
        _imageFrame.PointerCaptureLost += ImageFrame_PointerCaptureLost;
        ApplyTransforms();
        Loaded += async (_, _) => await LoadImageAsync();
    }

    public double FrameWidth => _imageFrame.Width;
    public double FrameHeight => _imageFrame.Height;
    public double PositionX => _attachment.PositionX;
    public double PositionY => _attachment.PositionY;

    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _imageFrame.IsHitTestVisible = editing;
        if (!editing) { _isSelected = false; _isCropping = false; }
        UpdateSelectionVisuals();
    }

    public void Deselect()
    {
        if (!_isSelected || _isInteracting) return;
        _isSelected = false;
        _isCropping = false;
        UpdateSelectionVisuals();
    }

    private async Task LoadImageAsync()
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(_attachment.Path);
            using var stream = await file.OpenReadAsync();
            BitmapImage bitmap = new();
            await bitmap.SetSourceAsync(stream);
            _image.Source = bitmap;
            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                _imageAspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
                if (_attachment.AspectRatio <= 0)
                {
                    _attachment.AspectRatio = _imageAspect;
                    _imageFrame.Height = _imageFrame.Width / _attachment.AspectRatio;
                    Area?.InvalidateMeasure();
                    _contentChanged();
                }
            }
            _imageReady.TrySetResult(true);
        }
        catch
        {
            _imageReady.TrySetResult(false);
            _imageFrame.Children.Insert(1, new TextBlock
            {
                Text = $"无法读取图片：{_attachment.Name}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 114, 114))
            });
        }
    }

    private void AddResizeHandle(HorizontalAlignment horizontal, VerticalAlignment vertical, ResizeHandle handleType)
    {
        Border handle = new()
        {
            Width = handleType is ResizeHandle.Left or ResizeHandle.Right ? 16 : 20,
            Height = handleType is ResizeHandle.Top or ResizeHandle.Bottom ? 16 : 20,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 250, 250)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235)),
            BorderThickness = new Thickness(2),
            Tag = handleType,
            Visibility = Visibility.Collapsed
        };
        handle.PointerPressed += ResizeStarted;
        handle.PointerMoved += ResizeDelta;
        handle.PointerReleased += ResizeCompleted;
        handle.PointerCanceled += ResizeCompleted;
        handle.PointerCaptureLost += ResizeCompleted;
        _resizeHandles.Add(handle);
        _overlay.Children.Add(handle);
    }

    private void ResizeStarted(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSelected) return;
        var point = e.GetCurrentPoint(PointerReference());
        if (!point.Properties.IsLeftButtonPressed) return;
        Border handle = (Border)sender;
        _activeResizeHandle = (ResizeHandle)handle.Tag;
        _pointerId = e.Pointer.PointerId;
        _dragStartPointer = point.Position;
        _dragStartFrame = CurrentFrameRect();
        handle.CapturePointer(e.Pointer);
        BeginInteraction();
        e.Handled = true;
    }

    private void ResizeCompleted(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _activeResizeHandle is null) return;
        ((Border)sender).ReleasePointerCapture(e.Pointer);
        _pointerId = null;
        _activeResizeHandle = null;
        EndInteraction();
        e.Handled = true;
    }

    // 按住的边跟手、对边固定；宽度不超出作业区左右边界，顶边不越过图片区上沿，向下不设限（下面的作业会让开）。
    private void ResizeDelta(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _activeResizeHandle is not ResizeHandle handle) return;
        e.Handled = true;
        Point position = e.GetCurrentPoint(PointerReference()).Position;
        double dx = position.X - _dragStartPointer.X;
        double dy = position.Y - _dragStartPointer.Y;
        Rect start = _dragStartFrame;
        bool left = handle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft;
        bool right = handle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight;
        bool top = handle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight;
        bool bottom = handle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;
        double areaWidth = Area?.AvailableWidth ?? double.PositiveInfinity;
        double maxWidth = Math.Max(MinimumWidth, left ? start.X + start.Width : areaWidth - start.X);
        double maxHeight = Math.Max(MinimumHeight, top ? start.Y + start.Height + SlotTop() : double.PositiveInfinity);
        double newWidth = start.Width;
        double newHeight = start.Height;

        if (_isCropping)
        {
            if (left) newWidth = start.Width - dx;
            if (right) newWidth = start.Width + dx;
            if (top) newHeight = start.Height - dy;
            if (bottom) newHeight = start.Height + dy;
            newWidth = Math.Clamp(newWidth, MinimumWidth, maxWidth);
            newHeight = Math.Clamp(newHeight, MinimumHeight, maxHeight);
        }
        else
        {
            double aspect = GetAspectRatio();
            double widthFromX = start.Width + (left ? -dx : dx);
            double widthFromY = (start.Height + (top ? -dy : dy)) * aspect;
            if ((left || right) && (top || bottom))
                newWidth = Math.Abs(widthFromX - start.Width) >= Math.Abs(widthFromY - start.Width) ? widthFromX : widthFromY;
            else
                newWidth = left || right ? widthFromX : widthFromY;
            newWidth = Math.Max(MinUniformWidth(aspect), Math.Min(newWidth, Math.Min(maxWidth, maxHeight * aspect)));
            newHeight = newWidth / aspect;
        }

        double oldFrameWidth = _imageFrame.Width;
        double oldFrameHeight = _imageFrame.Height;
        (double oldContentWidth, double oldContentHeight) = _isCropping ? GetVisualContentSize() : (0, 0);
        _imageFrame.Width = newWidth;
        _imageFrame.Height = newHeight;
        _attachment.FrameWidth = newWidth;
        _attachment.ViewportHeight = newHeight;
        _attachment.PositionX = left ? start.X + start.Width - newWidth : start.X;
        _attachment.PositionY = top ? start.Y + start.Height - newHeight : start.Y;
        if (_isCropping)
        {
            // 取景框比例随裁切结果更新并持久化，避免退出裁切或重启后按原图比例弹回。
            _attachment.AspectRatio = newWidth / newHeight;
            // 补偿取景框尺寸变化造成的画面滑动，让静止一侧的画面保持不动。
            (double contentWidth, double contentHeight) = GetVisualContentSize();
            if (left)
                _attachment.OffsetX += (newWidth - oldFrameWidth) / 2 + (oldContentWidth - contentWidth) / 2;
            else if (right)
                _attachment.OffsetX += (oldFrameWidth - newWidth) / 2 + (contentWidth - oldContentWidth) / 2;
            if (top)
                _attachment.OffsetY += (newHeight - oldFrameHeight) / 2 + (oldContentHeight - contentHeight) / 2;
            else if (bottom)
                _attachment.OffsetY += (oldFrameHeight - newHeight) / 2 + (contentHeight - oldContentHeight) / 2;
            ClampCropOffsets();
        }
        ApplyTransforms();
        Area?.InvalidateMeasure();
    }

    private AttachmentArea? Area => Parent as AttachmentArea;

    // 控件整体随拖拽平移，取自身坐标系会把位移抵消掉；换算到图片区坐标才是指针的真实移动。
    private UIElement PointerReference() => Parent as UIElement ?? this;

    private double SlotTop() => Area?.SlotTop(this) ?? 0;

    // 保存的偏移可能在磁贴变窄后超出区域，以当前实际显示的位置作为拖动起点，避免一开始拖不动。
    private Rect CurrentFrameRect() => new(
        AttachmentArea.ClampX(_attachment.PositionX, _imageFrame.Width, Area?.AvailableWidth ?? double.PositiveInfinity),
        Math.Max(-SlotTop(), _attachment.PositionY),
        _imageFrame.Width,
        _imageFrame.Height);

    private void ImageFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isEditing) return;
        var point = e.GetCurrentPoint(PointerReference());
        if (!point.Properties.IsLeftButtonPressed) return;
        _isSelected = true;
        UpdateSelectionVisuals();
        Area?.SelectOnly(this);
        // 上一次松开若落在窗口外没送达，按下新的一次时直接接管，不让残留状态卡住拖动。
        _activeResizeHandle = null;
        _pointerId = e.Pointer.PointerId;
        _lastPointerPosition = point.Position;
        _dragStartPointer = point.Position;
        _dragStartFrame = CurrentFrameRect();
        _imageFrame.CapturePointer(e.Pointer);
        BeginInteraction();
        e.Handled = true;
    }

    private void ImageFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _activeResizeHandle is not null) return;
        Point position = e.GetCurrentPoint(PointerReference()).Position;
        if (_isCropping)
        {
            _attachment.OffsetX += position.X - _lastPointerPosition.X;
            _attachment.OffsetY += position.Y - _lastPointerPosition.Y;
            ClampCropOffsets();
        }
        else
        {
            // 左右限制在作业区内，向上不越过图片区上沿，向下自由移动并把下面的作业顶开。
            double areaWidth = Area?.AvailableWidth ?? double.PositiveInfinity;
            _attachment.PositionX = AttachmentArea.ClampX(_dragStartFrame.X + position.X - _dragStartPointer.X, _imageFrame.Width, areaWidth);
            _attachment.PositionY = Math.Max(-SlotTop(), _dragStartFrame.Y + position.Y - _dragStartPointer.Y);
            Area?.InvalidateMeasure();
        }
        ApplyTransforms();
        _lastPointerPosition = position;
        e.Handled = true;
    }

    private void ImageFrame_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _activeResizeHandle is not null) return;
        _imageFrame.ReleasePointerCapture(e.Pointer);
        _pointerId = null;
        EndInteraction();
        e.Handled = true;
    }

    private void ImageFrame_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId || _activeResizeHandle is not null) return;
        _pointerId = null;
        EndInteraction();
    }

    private void ToggleCropMode() { _isCropping = !_isCropping; UpdateSelectionVisuals(); }

    private void RotateImage(double degrees)
    {
        _attachment.Rotation = (_attachment.Rotation + degrees + 360) % 360;
        double ratio = GetAspectRatio();
        _attachment.AspectRatio = 1 / ratio;
        _imageFrame.Height = _imageFrame.Width / GetAspectRatio();
        _attachment.ViewportHeight = _imageFrame.Height;
        ClampCropOffsets();
        ApplyTransforms();
        Area?.InvalidateMeasure();
        _contentChanged();
    }

    private void ResetImage()
    {
        _attachment.Scale = 1;
        _attachment.OffsetX = 0;
        _attachment.OffsetY = 0;
        _attachment.PositionX = 0;
        _attachment.PositionY = 0;
        ApplyTransforms();
        Area?.InvalidateMeasure();
        _contentChanged();
    }

    private void UpdateSelectionVisuals()
    {
        bool visible = _isSelected && _isEditing;
        _selectionBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _toolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (Border handle in _resizeHandles)
            handle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _selectionBorder.BorderBrush = new SolidColorBrush(_isCropping
            ? Windows.UI.Color.FromArgb(255, 250, 204, 21)
            : Windows.UI.Color.FromArgb(255, 96, 165, 250));
    }

    private double GetAspectRatio() => _attachment.AspectRatio > 0 ? _attachment.AspectRatio : 4d / 3d;

    // 等比缩放下取宽高的共同下限，避免一边贴近最小值时另一边缩到不可见。
    private static double MinUniformWidth(double aspect) => Math.Max(MinimumWidth, MinimumHeight * aspect);

    private void ClampCropOffsets()
    {
        // 可移动范围 = 画面按 UniformToFill 填充（含旋转）后超出取景框的部分，保证画面始终覆盖取景框。
        (double contentWidth, double contentHeight) = GetVisualContentSize();
        double maxX = Math.Max(0, (contentWidth - _imageFrame.Width) / 2);
        double maxY = Math.Max(0, (contentHeight - _imageFrame.Height) / 2);
        _attachment.OffsetX = Math.Clamp(_attachment.OffsetX, -maxX, maxX);
        _attachment.OffsetY = Math.Clamp(_attachment.OffsetY, -maxY, maxY);
    }

    private (double Width, double Height) GetVisualContentSize()
    {
        double frameWidth = _imageFrame.Width;
        double frameHeight = _imageFrame.Height;
        if (frameWidth <= 0 || frameHeight <= 0) return (0, 0);
        // 图片未加载成功时按无溢出处理，此时裁切拖动中央不产生位移。
        double imageAspect = _imageAspect > 0 ? _imageAspect : frameWidth / frameHeight;
        bool widthOverflow = imageAspect >= frameWidth / frameHeight;
        double fillWidth = widthOverflow ? frameHeight * imageAspect : frameWidth;
        double fillHeight = widthOverflow ? frameHeight : frameWidth / imageAspect;
        bool rotated = _attachment.Rotation % 180 is not 0;
        return rotated
            ? (fillHeight * _attachment.Scale, fillWidth * _attachment.Scale)
            : (fillWidth * _attachment.Scale, fillHeight * _attachment.Scale);
    }

    private void ApplyTransforms()
    {
        _imageTransform.ScaleX = _attachment.Scale;
        _imageTransform.ScaleY = _attachment.Scale;
        _imageTransform.TranslateX = _attachment.OffsetX;
        _imageTransform.TranslateY = _attachment.OffsetY;
        _imageTransform.Rotation = _attachment.Rotation;
    }

    private void BeginInteraction() { if (!_isInteracting) { _isInteracting = true; _interactionChanged(true); } }
    private void EndInteraction() { if (_isInteracting) { _isInteracting = false; _interactionChanged(false); _contentChanged(); } }

    private static Button CreateIconButton(string glyph, string tooltip, RoutedEventHandler click, bool mirrored = false)
    {
        FontIcon icon = new() { Glyph = glyph, FontSize = 16, RenderTransformOrigin = new Point(0.5, 0.5) };
        if (mirrored) icon.RenderTransform = new ScaleTransform { ScaleX = -1, ScaleY = 1 };
        Button button = new Button
        {
            Width = 36,
            Height = 34,
            MinWidth = 36,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = BoardTheme.SurfaceBrush,
            BorderThickness = new Thickness(0),
            Foreground = BoardTheme.TextBrush,
            Content = icon
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += click;
        return button;
    }
    private enum ResizeHandle { TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }
}
