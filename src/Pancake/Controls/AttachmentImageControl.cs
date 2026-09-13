using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 图片框：选中后显示八点边框和图标工具栏。
/// 普通模式拖中央移动、拖边角等比缩放；裁切模式复用同一套拖拽框调整取景区域。
/// </summary>
public sealed class AttachmentImageControl : Grid
{
    private const double MinimumWidth = 96;
    private const double MinimumHeight = 72;
    private const double MaximumWidth = 640;
    private const double MaximumHeight = 520;
    // 工具栏占用的高度；控件总高 = 取景框高 + 该值。
    private const double ToolbarHeight = 40;

    private enum ResizeHandle
    {
        TopLeft,
        Top,
        TopRight,
        Left,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }

    private readonly AttachmentItem _attachment;
    private readonly Action _deleteAttachment;
    private readonly Action _contentChanged;
    private readonly Action<bool> _interactionChanged;
    private readonly Grid _imageFrame;
    private readonly Grid _overlay;
    private readonly Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
        RenderTransformOrigin = RelativePoint.Center
    };
    private readonly Border _selectionBorder;
    private readonly StackPanel _toolbar;
    private readonly TranslateTransform _frameTransform = new();
    private readonly List<(Thumb Thumb, ResizeHandle Handle)> _resizeHandles = [];
    private bool _isEditing;
    private bool _isSelected;
    private bool _isCropping;
    private bool _isInteracting;
    private IPointer? _pointer;
    private Point _lastPointerPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartX;
    private double _resizeStartY;
    private ResizeHandle _activeHandle;
    private double _imageAspect;

    public AttachmentImageControl(
        AttachmentItem attachment,
        Action deleteAttachment,
        Action contentChanged,
        Action<bool> interactionChanged)
    {
        _attachment = attachment;
        _deleteAttachment = deleteAttachment;
        _contentChanged = contentChanged;
        _interactionChanged = interactionChanged;

        Width = Math.Clamp(attachment.FrameWidth <= 0 ? 360 : attachment.FrameWidth, MinimumWidth, MaximumWidth);
        double initialHeight = attachment.AspectRatio > 0 ? Width / attachment.AspectRatio : 240;
        Height = initialHeight + ToolbarHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderTransform = _frameTransform;
        RowDefinitions = new RowDefinitions("Auto,Auto");

        _imageFrame = new Grid
        {
            Width = Width,
            Height = initialHeight,
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor()),
            ClipToBounds = true
        };
        _imageFrame.Children.Add(_image);
        _overlay = new Grid();
        _overlay.Children.Add(_imageFrame);
        Children.Add(_overlay);

        _selectionBorder = new Border
        {
            BorderBrush = new SolidColorBrush(BoardColor.FromRgb(96, 165, 250).ToColor()),
            BorderThickness = new Thickness(2),
            IsVisible = false,
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
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 2),
            IsVisible = false
        };
        _toolbar.Children.Add(CreateIconButton(nameof(FluentGlyphs.Crop), "裁切", (_, _) => ToggleCropMode()));
        _toolbar.Children.Add(CreateIconButton(nameof(FluentGlyphs.Rotate), "逆时针旋转", (_, _) => RotateImage(-90), mirrored: true));
        _toolbar.Children.Add(CreateIconButton(nameof(FluentGlyphs.Rotate), "顺时针旋转", (_, _) => RotateImage(90)));
        _toolbar.Children.Add(CreateIconButton(nameof(FluentGlyphs.Reset), "复位", (_, _) => ResetImage()));
        _toolbar.Children.Add(CreateIconButton(nameof(FluentGlyphs.Delete), "删除", (_, _) => _deleteAttachment()));
        Grid.SetRow(_toolbar, 1);
        Children.Add(_toolbar);

        _imageFrame.PointerPressed += ImageFrame_PointerPressed;
        _imageFrame.PointerMoved += ImageFrame_PointerMoved;
        _imageFrame.PointerReleased += ImageFrame_PointerReleased;
        _imageFrame.PointerCaptureLost += ImageFrame_PointerCaptureLost;
        ApplyTransforms();
        if (!string.IsNullOrWhiteSpace(attachment.Path)) _ = LoadImageAsync(attachment.Path);
    }

    /// <summary>编辑态下允许选中与拖动；查看态只显示画面。</summary>
    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _imageFrame.IsHitTestVisible = editing;
        if (!editing)
        {
            _isSelected = false;
            _isCropping = false;
        }

        UpdateSelectionVisuals();
    }

    /// <summary>验证脚本用：当前是否处于编辑态、是否已选中（手柄与工具条的可见性来源）。</summary>
    internal (bool Editing, bool Selected) SelectionStateForVerification => (_isEditing, _isSelected);

    /// <summary>验证脚本用：是否处于裁切模式（裁切时拖动改的是取景偏移而不是图片框位置）。</summary>
    internal bool IsCroppingForVerification => _isCropping;

    private async Task LoadImageAsync(string path)
    {
        try
        {
            Bitmap bitmap = await Task.Run(() => new Bitmap(path));
            _image.Source = bitmap;
            if (bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0)
            {
                _imageAspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
                if (_attachment.AspectRatio <= 0)
                {
                    _attachment.AspectRatio = _imageAspect;
                    _imageFrame.Width = Width;
                    _imageFrame.Height = Width / _attachment.AspectRatio;
                    Height = _imageFrame.Height + ToolbarHeight;
                    _contentChanged();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _imageFrame.Children.Add(new TextBlock
            {
                Text = $"无法读取图片：{_attachment.Name}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(BoardColor.FromRgb(244, 114, 114).ToColor())
            });
        }
    }

    private void AddResizeHandle(HorizontalAlignment horizontal, VerticalAlignment vertical, ResizeHandle handleType)
    {
        // 与磁贴把手一样：裸 Thumb 拿不到主题模板就不渲染也收不到指针，必须套最小模板。
        Thumb thumb = ThumbVisuals.Apply(new Thumb
        {
            Width = handleType is ResizeHandle.Left or ResizeHandle.Right ? 16 : 20,
            Height = handleType is ResizeHandle.Top or ResizeHandle.Bottom ? 16 : 20,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Background = new SolidColorBrush(BoardColor.FromRgb(250, 250, 250).ToColor()),
            BorderBrush = new SolidColorBrush(BoardColor.FromRgb(37, 99, 235).ToColor()),
            BorderThickness = new Thickness(2),
            Tag = handleType,
            IsVisible = false
        });
        thumb.DragStarted += ResizeStarted;
        thumb.DragDelta += ResizeDelta;
        thumb.DragCompleted += (_, _) => EndInteraction();
        _resizeHandles.Add((thumb, handleType));
        _overlay.Children.Add(thumb);
    }

    private void ResizeStarted(object? sender, VectorEventArgs e)
    {
        if (!_isSelected || sender is not Thumb thumb) return;
        _activeHandle = (ResizeHandle)thumb.Tag!;
        _resizeStartWidth = _imageFrame.Width;
        _resizeStartHeight = _imageFrame.Height;
        _resizeStartX = _attachment.PositionX;
        _resizeStartY = _attachment.PositionY;
        BeginInteraction();
    }

    /// <summary>
    /// 缩放取景框。普通模式四角等比、四边按比例联动；裁切模式允许自由改变取景框比例，
    /// 并补偿画面位移，让没有拖动的一侧保持不动。
    /// </summary>
    private void ResizeDelta(object? sender, VectorEventArgs e)
    {
        if (!_isSelected) return;
        double dx = e.Vector.X;
        double dy = e.Vector.Y;
        bool corner = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.TopRight or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;
        double newWidth = _resizeStartWidth;
        double newHeight = _resizeStartHeight;
        double oldFrameWidth = _imageFrame.Width;
        double oldFrameHeight = _imageFrame.Height;
        (double oldContentWidth, double oldContentHeight) = _isCropping ? GetVisualContentSize() : (0, 0);

        if (_isCropping)
        {
            if (_activeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
                newWidth = Math.Clamp(_resizeStartWidth - dx, MinimumWidth, MaximumWidth);
            if (_activeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
                newWidth = Math.Clamp(_resizeStartWidth + dx, MinimumWidth, MaximumWidth);
            if (_activeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
                newHeight = Math.Clamp(_resizeStartHeight - dy, MinimumHeight, MaximumHeight);
            if (_activeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
                newHeight = Math.Clamp(_resizeStartHeight + dy, MinimumHeight, MaximumHeight);
        }
        else if (corner)
        {
            double signedWidthDelta = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.BottomLeft ? -dx : dx;
            double signedHeightDelta = _activeHandle is ResizeHandle.TopLeft or ResizeHandle.TopRight ? -dy : dy;
            double aspect = GetAspectRatio();
            double widthFromX = _resizeStartWidth + signedWidthDelta;
            double widthFromY = _resizeStartWidth + signedHeightDelta * aspect;
            newWidth = Math.Clamp(
                Math.Abs(widthFromX - _resizeStartWidth) > Math.Abs(widthFromY - _resizeStartWidth) ? widthFromX : widthFromY,
                MinimumWidth,
                MaximumWidth);
            newHeight = newWidth / aspect;
        }
        else if (_activeHandle is ResizeHandle.Left or ResizeHandle.Right)
        {
            double signedWidthDelta = _activeHandle == ResizeHandle.Left ? -dx : dx;
            newWidth = Math.Clamp(_resizeStartWidth + signedWidthDelta, MinimumWidth, MaximumWidth);
            newHeight = newWidth / GetAspectRatio();
        }
        else
        {
            double signedHeightDelta = _activeHandle == ResizeHandle.Top ? -dy : dy;
            newHeight = Math.Clamp(_resizeStartHeight + signedHeightDelta, MinimumHeight, MaximumHeight);
            newWidth = newHeight * GetAspectRatio();
        }

        Width = newWidth;
        Height = newHeight + ToolbarHeight;
        _imageFrame.Width = newWidth;
        _imageFrame.Height = newHeight;
        _attachment.FrameWidth = newWidth;
        _attachment.ViewportHeight = newHeight;
        _attachment.PositionX = _activeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft
            ? _resizeStartX + _resizeStartWidth - newWidth
            : _resizeStartX;
        _attachment.PositionY = _activeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight
            ? _resizeStartY + _resizeStartHeight - newHeight
            : _resizeStartY;
        if (_isCropping)
        {
            // 取景框比例随裁切结果更新并持久化，避免退出裁切或重启后按原图比例弹回。
            _attachment.AspectRatio = newWidth / newHeight;
            (double contentWidth, double contentHeight) = GetVisualContentSize();
            if (_activeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
                _attachment.OffsetX += (newWidth - oldFrameWidth) / 2 + (oldContentWidth - contentWidth) / 2;
            else if (_activeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
                _attachment.OffsetX += (oldFrameWidth - newWidth) / 2 + (contentWidth - oldContentWidth) / 2;
            if (_activeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
                _attachment.OffsetY += (newHeight - oldFrameHeight) / 2 + (oldContentHeight - contentHeight) / 2;
            else if (_activeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
                _attachment.OffsetY += (oldFrameHeight - newHeight) / 2 + (contentHeight - oldContentHeight) / 2;
            ClampCropOffsets();
        }

        ApplyTransforms();
    }

    private void ImageFrame_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEditing) return;
        PointerPoint point = e.GetCurrentPoint(_imageFrame);
        if (!point.Properties.IsLeftButtonPressed) return;
        _isSelected = true;
        UpdateSelectionVisuals();
        _pointer = e.Pointer;
        _lastPointerPosition = point.Position;
        e.Pointer.Capture(_imageFrame);
        BeginInteraction();
        e.Handled = true;
    }

    private void ImageFrame_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        Point position = e.GetCurrentPoint(_imageFrame).Position;
        MoveBy(position.X - _lastPointerPosition.X, position.Y - _lastPointerPosition.Y);
        _lastPointerPosition = position;
        e.Handled = true;
    }

    private void ImageFrame_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        e.Pointer.Capture(null);
        _pointer = null;
        EndInteraction();
        e.Handled = true;
    }

    /// <summary>指针被其它控件抢走时也要结束交互并结算内容，避免卡在按住状态。</summary>
    private void ImageFrame_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pointer is null) return;
        _pointer = null;
        EndInteraction();
    }

    /// <summary>裁切模式移动画面取景，普通模式移动整个图片框。</summary>
    private void MoveBy(double x, double y)
    {
        if (_isCropping)
        {
            _attachment.OffsetX += x;
            _attachment.OffsetY += y;
            ClampCropOffsets();
        }
        else
        {
            _attachment.PositionX += x;
            _attachment.PositionY += y;
        }

        ApplyTransforms();
    }

    private void ToggleCropMode()
    {
        _isCropping = !_isCropping;
        UpdateSelectionVisuals();
    }

    /// <summary>旋转 90 度时同步交换取景框宽高与比例，保证画面始终铺满。</summary>
    private void RotateImage(double degrees)
    {
        _attachment.Rotation = (_attachment.Rotation + degrees + 360) % 360;
        double ratio = GetAspectRatio();
        _attachment.AspectRatio = 1 / ratio;
        _imageFrame.Width = Width;
        _imageFrame.Height = Width / GetAspectRatio();
        Height = _imageFrame.Height + ToolbarHeight;
        _attachment.ViewportHeight = _imageFrame.Height;
        ClampCropOffsets();
        ApplyTransforms();
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
        _contentChanged();
    }

    private void UpdateSelectionVisuals()
    {
        bool visible = _isSelected && _isEditing;
        _selectionBorder.IsVisible = visible;
        _toolbar.IsVisible = visible;
        foreach ((Thumb thumb, _) in _resizeHandles) thumb.IsVisible = visible;
        _selectionBorder.BorderBrush = new SolidColorBrush(
            _isCropping ? BoardColor.FromRgb(250, 204, 21).ToColor() : BoardColor.FromRgb(96, 165, 250).ToColor());
    }

    private double GetAspectRatio() => _attachment.AspectRatio > 0 ? _attachment.AspectRatio : 4d / 3d;

    /// <summary>画面按 UniformToFill 填充（含旋转）后超出取景框的部分即可移动范围。</summary>
    private void ClampCropOffsets()
    {
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
        // 图片没加载成功时按无溢出处理，此时裁切拖动不会产生位移。
        double imageAspect = _imageAspect > 0 ? _imageAspect : frameWidth / frameHeight;
        bool widthOverflow = imageAspect >= frameWidth / frameHeight;
        double fillWidth = widthOverflow ? frameHeight * imageAspect : frameWidth;
        double fillHeight = widthOverflow ? frameHeight : frameWidth / imageAspect;
        bool rotated = _attachment.Rotation % 180 is not 0;
        double scale = _attachment.Scale <= 0 ? 1 : _attachment.Scale;
        return rotated
            ? (fillHeight * scale, fillWidth * scale)
            : (fillWidth * scale, fillHeight * scale);
    }

    private void ApplyTransforms()
    {
        _frameTransform.X = _attachment.PositionX;
        _frameTransform.Y = _attachment.PositionY;
        _image.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_attachment.Scale <= 0 ? 1 : _attachment.Scale, _attachment.Scale <= 0 ? 1 : _attachment.Scale),
                new RotateTransform(_attachment.Rotation),
                new TranslateTransform(_attachment.OffsetX, _attachment.OffsetY)
            }
        };
    }

    private void BeginInteraction()
    {
        if (_isInteracting) return;
        _isInteracting = true;
        _interactionChanged(true);
    }

    private void EndInteraction()
    {
        if (!_isInteracting) return;
        _isInteracting = false;
        _interactionChanged(false);
        _contentChanged();
    }

    /// <summary>图片工具栏按钮；symbol 使用 FluentGlyphs 的语义名。</summary>
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
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor()),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()),
            Content = icon
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += click;
        return button;
    }
}
