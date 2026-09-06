using Pancake.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
/// </summary>
public sealed class AttachmentImageControl : Grid
{
    private const double MinimumWidth = 96;
    private const double MinimumHeight = 72;
    private const double MaximumWidth = 640;
    private const double MaximumHeight = 520;

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
    private readonly TranslateTransform _frameTransform = new();
    private readonly List<(Thumb Thumb, ResizeHandle Handle)> _resizeHandles = [];
    private bool _isEditing;
    private bool _isSelected;
    private bool _isCropping;
    private bool _isInteracting;
    private uint? _pointerId;
    private Point _lastPointerPosition;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartX;
    private double _resizeStartY;
    private ResizeHandle _activeResizeHandle;
    private double _imageAspect;

    public AttachmentImageControl(AttachmentItem attachment, Action deleteAttachment, Action contentChanged, Action<bool> interactionChanged)
    {
        _attachment = attachment;
        _deleteAttachment = deleteAttachment;
        _contentChanged = contentChanged;
        _interactionChanged = interactionChanged;

        Width = Math.Clamp(attachment.FrameWidth, MinimumWidth, MaximumWidth);
        double initialHeight = attachment.AspectRatio > 0 ? Width / attachment.AspectRatio : 240;
        Height = initialHeight + 40;
        HorizontalAlignment = HorizontalAlignment.Left;
        RenderTransform = _frameTransform;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _imageFrame = new Grid
        {
            Width = Width,
            Height = initialHeight,
            Background = BoardTheme.SurfaceBrush,
            Clip = new RectangleGeometry(),
            ManipulationMode = ManipulationModes.None
        };
        _overlay = new Grid();
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
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 2),
            Visibility = Visibility.Collapsed
        };
        _toolbar.Children.Add(CreateIconButton("\uE7A8", "裁切", (_, _) => ToggleCropMode()));
        _toolbar.Children.Add(CreateIconButton("\uE7AD", "逆时针旋转", (_, _) => RotateImage(-90), true));
        _toolbar.Children.Add(CreateIconButton("\uE7AD", "顺时针旋转", (_, _) => RotateImage(90)));
        _toolbar.Children.Add(CreateIconButton("\uE777", "复位", (_, _) => ResetImage()));
        _toolbar.Children.Add(CreateIconButton("\uE74D", "删除", (_, _) => _deleteAttachment()));
        Grid.SetRow(_toolbar, 1);
        Children.Add(_toolbar);

        _imageFrame.PointerPressed += ImageFrame_PointerPressed;
        _imageFrame.PointerMoved += ImageFrame_PointerMoved;
        _imageFrame.PointerReleased += ImageFrame_PointerReleased;
        _imageFrame.PointerCanceled += ImageFrame_PointerReleased;
        _imageFrame.PointerCaptureLost += ImageFrame_PointerCaptureLost;
        _imageFrame.ManipulationStarted += (_, _) => BeginInteraction();
        _imageFrame.ManipulationDelta += ImageFrame_ManipulationDelta;
        _imageFrame.ManipulationCompleted += (_, _) => EndInteraction();
        Loaded += async (_, _) => await LoadImageAsync();
    }

    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _imageFrame.IsHitTestVisible = editing;
        _imageFrame.ManipulationMode = editing ? ManipulationModes.TranslateX | ManipulationModes.TranslateY : ManipulationModes.None;
        if (!editing) { _isSelected = false; _isCropping = false; }
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
                    _imageFrame.Width = Width;
                    _imageFrame.Height = Width / _attachment.AspectRatio;
                    Height = _imageFrame.Height + 40;
                    _contentChanged();
                }
            }
        }
        catch
        {
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
        Thumb thumb = new()
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
        thumb.DragStarted += ResizeStarted;
        thumb.DragDelta += ResizeDelta;
        thumb.DragCompleted += (_, _) => EndInteraction();
        _resizeHandles.Add((thumb, handleType));
        _overlay.Children.Add(thumb);
    }

    private void ResizeStarted(object sender, DragStartedEventArgs e)
    {
        if (!_isSelected) return;
        _activeResizeHandle = (ResizeHandle)((Thumb)sender).Tag;
        _resizeStartWidth = _imageFrame.Width;
        _resizeStartHeight = _imageFrame.Height;
        _resizeStartX = _attachment.PositionX;
        _resizeStartY = _attachment.PositionY;
        BeginInteraction();
    }

    private void ResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isSelected) return;
        double dx = e.HorizontalChange;
        double dy = e.VerticalChange;
        bool corner = _activeResizeHandle is ResizeHandle.TopLeft or ResizeHandle.TopRight or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;
        double newWidth = _resizeStartWidth;
        double newHeight = _resizeStartHeight;
        double oldFrameWidth = _imageFrame.Width;
        double oldFrameHeight = _imageFrame.Height;
        (double oldContentWidth, double oldContentHeight) = _isCropping ? GetVisualContentSize() : (0, 0);

        if (_isCropping)
        {
            if (_activeResizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft) newWidth = Math.Clamp(_resizeStartWidth - dx, MinimumWidth, MaximumWidth);
            if (_activeResizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight) newWidth = Math.Clamp(_resizeStartWidth + dx, MinimumWidth, MaximumWidth);
            if (_activeResizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight) newHeight = Math.Clamp(_resizeStartHeight - dy, MinimumHeight, MaximumHeight);
            if (_activeResizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight) newHeight = Math.Clamp(_resizeStartHeight + dy, MinimumHeight, MaximumHeight);
        }
        else if (corner)
        {
            double signedWidthDelta = _activeResizeHandle is ResizeHandle.TopLeft or ResizeHandle.BottomLeft ? -dx : dx;
            double signedHeightDelta = _activeResizeHandle is ResizeHandle.TopLeft or ResizeHandle.TopRight ? -dy : dy;
            double aspect = GetAspectRatio();
            double widthFromX = _resizeStartWidth + signedWidthDelta;
            double widthFromY = _resizeStartWidth + signedHeightDelta * aspect;
            newWidth = Math.Clamp(Math.Abs(widthFromX - _resizeStartWidth) > Math.Abs(widthFromY - _resizeStartWidth) ? widthFromX : widthFromY, MinimumWidth, MaximumWidth);
            newHeight = newWidth / aspect;
        }
        else if (_activeResizeHandle is ResizeHandle.Left or ResizeHandle.Right)
        {
            double signedWidthDelta = _activeResizeHandle == ResizeHandle.Left ? -dx : dx;
            newWidth = Math.Clamp(_resizeStartWidth + signedWidthDelta, MinimumWidth, MaximumWidth);
            newHeight = newWidth / GetAspectRatio();
        }
        else
        {
            double signedHeightDelta = _activeResizeHandle == ResizeHandle.Top ? -dy : dy;
            newHeight = Math.Clamp(_resizeStartHeight + signedHeightDelta, MinimumHeight, MaximumHeight);
            newWidth = newHeight * GetAspectRatio();
        }

        Width = newWidth;
        Height = newHeight + 40;
        _imageFrame.Width = newWidth;
        _imageFrame.Height = newHeight;
        _attachment.FrameWidth = newWidth;
        _attachment.ViewportHeight = newHeight;
        if (_activeResizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
            _attachment.PositionX = _resizeStartX + _resizeStartWidth - newWidth;
        else _attachment.PositionX = _resizeStartX;
        if (_activeResizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
            _attachment.PositionY = _resizeStartY + _resizeStartHeight - newHeight;
        else _attachment.PositionY = _resizeStartY;
        if (_isCropping)
        {
            // 取景框比例随裁切结果更新并持久化，避免退出裁切或重启后按原图比例弹回。
            _attachment.AspectRatio = newWidth / newHeight;
            // 补偿取景框尺寸变化造成的画面滑动，让静止一侧的画面保持不动。
            (double contentWidth, double contentHeight) = GetVisualContentSize();
            if (_activeResizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
                _attachment.OffsetX += (newWidth - oldFrameWidth) / 2 + (oldContentWidth - contentWidth) / 2;
            else if (_activeResizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
                _attachment.OffsetX += (oldFrameWidth - newWidth) / 2 + (contentWidth - oldContentWidth) / 2;
            if (_activeResizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
                _attachment.OffsetY += (newHeight - oldFrameHeight) / 2 + (oldContentHeight - contentHeight) / 2;
            else if (_activeResizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
                _attachment.OffsetY += (oldFrameHeight - newHeight) / 2 + (contentHeight - oldContentHeight) / 2;
            ClampCropOffsets();
        }
        ApplyTransforms();
    }

    private void ImageFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isEditing || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        var point = e.GetCurrentPoint(_imageFrame);
        if (!point.Properties.IsLeftButtonPressed) return;
        _isSelected = true;
        UpdateSelectionVisuals();
        _pointerId = e.Pointer.PointerId;
        _lastPointerPosition = point.Position;
        _imageFrame.CapturePointer(e.Pointer);
        BeginInteraction();
        e.Handled = true;
    }

    private void ImageFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        Point position = e.GetCurrentPoint(_imageFrame).Position;
        MoveBy(position.X - _lastPointerPosition.X, position.Y - _lastPointerPosition.Y);
        _lastPointerPosition = position;
        e.Handled = true;
    }

    private void ImageFrame_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        _imageFrame.ReleasePointerCapture(e.Pointer);
        _pointerId = null;
        EndInteraction();
        e.Handled = true;
    }

    private void ImageFrame_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        _pointerId = null;
        EndInteraction();
    }

    private void ImageFrame_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_isEditing) return;
        _isSelected = true;
        UpdateSelectionVisuals();
        MoveBy(e.Delta.Translation.X, e.Delta.Translation.Y);
        e.Handled = true;
    }

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

    private void ToggleCropMode() { _isCropping = !_isCropping; UpdateSelectionVisuals(); }

    private void RotateImage(double degrees)
    {
        _attachment.Rotation = (_attachment.Rotation + degrees + 360) % 360;
        double ratio = GetAspectRatio();
        _attachment.AspectRatio = 1 / ratio;
        _imageFrame.Width = Width;
        _imageFrame.Height = Width / GetAspectRatio();
        Height = _imageFrame.Height + 40;
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
        _selectionBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _toolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _resizeHandles)
            item.Thumb.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _selectionBorder.BorderBrush = new SolidColorBrush(_isCropping
            ? Windows.UI.Color.FromArgb(255, 250, 204, 21)
            : Windows.UI.Color.FromArgb(255, 96, 165, 250));
    }

    private double GetAspectRatio() => _attachment.AspectRatio > 0 ? _attachment.AspectRatio : 4d / 3d;

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
        _frameTransform.X = _attachment.PositionX;
        _frameTransform.Y = _attachment.PositionY;
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