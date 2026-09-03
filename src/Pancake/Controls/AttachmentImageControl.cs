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
/// 在固定视窗中显示图片，并持久化非破坏性的缩放、移动和裁切参数。
/// </summary>
public sealed class AttachmentImageControl : Grid
{
    private const double MinimumScale = 1;
    private const double MaximumScale = 6;
    private const double MinimumViewportHeight = 96;
    private const double MaximumViewportHeight = 420;

    private readonly AttachmentItem _attachment;
    private readonly Action _deleteAttachment;
    private readonly Action _contentChanged;
    private readonly Action<bool> _interactionChanged;
    private readonly Grid _viewport;
    private readonly CompositeTransform _imageTransform = new();
    private readonly StackPanel _editingTools;
    private readonly Thumb _cropThumb;
    private bool _isEditing;
    private bool _isManipulating;
    private uint? _mouseDragPointerId;
    private Point _lastMousePosition;

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

        Height = Math.Clamp(attachment.ViewportHeight, MinimumViewportHeight, MaximumViewportHeight);
        Clip = new RectangleGeometry();
        SizeChanged += (_, args) =>
        {
            ((RectangleGeometry)Clip).Rect = new Rect(0, 0, args.NewSize.Width, args.NewSize.Height);
            ClampOffsets();
            ApplyTransform();
        };

        _viewport = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 20)),
            ManipulationMode = ManipulationModes.None
        };
        Children.Add(_viewport);

        Image image = new()
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _imageTransform
        };
        _viewport.Children.Add(image);
        Loaded += async (_, _) => await LoadImageAsync(image);

        Border outline = new()
        {
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 82, 82, 91)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false
        };
        Children.Add(outline);

        _editingTools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6)
        };
        _editingTools.Children.Add(CreateButton("\uE777", "复位图片", (_, _) => ResetImage()));
        _editingTools.Children.Add(CreateButton("\uE74D", "删除图片", (_, _) => _deleteAttachment()));
        Children.Add(_editingTools);

        _cropThumb = new Thumb
        {
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0))
        };
        _cropThumb.DragStarted += (_, _) => BeginInteraction();
        _cropThumb.DragDelta += (_, args) =>
        {
            Height = Math.Clamp(Height + args.VerticalChange, MinimumViewportHeight, MaximumViewportHeight);
            _attachment.ViewportHeight = Height;
            ClampOffsets();
            ApplyTransform();
        };
        _cropThumb.DragCompleted += (_, _) => EndInteraction();
        Children.Add(_cropThumb);

        _viewport.ManipulationStarted += (_, _) => BeginInteraction();
        _viewport.ManipulationDelta += Viewport_ManipulationDelta;
        _viewport.ManipulationCompleted += (_, _) => EndInteraction();
        _viewport.PointerWheelChanged += Viewport_PointerWheelChanged;
        _viewport.PointerPressed += Viewport_PointerPressed;
        _viewport.PointerMoved += Viewport_PointerMoved;
        _viewport.PointerReleased += Viewport_PointerReleased;
        _viewport.PointerCanceled += Viewport_PointerReleased;
        _viewport.PointerCaptureLost += Viewport_PointerCaptureLost;
        ToolTipService.SetToolTip(_viewport, "拖动移动图片，滚轮或双指缩放，拖动底边调整裁切范围");
        ApplyTransform();
    }

    public void SetEditing(bool editing)
    {
        _isEditing = editing;
        _editingTools.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _cropThumb.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _viewport.ManipulationMode = editing
            ? ManipulationModes.TranslateX | ManipulationModes.TranslateY | ManipulationModes.Scale
            : ManipulationModes.None;
    }

    private async Task LoadImageAsync(Image image)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(_attachment.Path);
            using var stream = await file.OpenReadAsync();
            BitmapImage bitmap = new();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch
        {
            _viewport.Children.Add(new TextBlock
            {
                Text = $"无法读取图片：{_attachment.Name}",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 114, 114))
            });
        }
    }

    private void Viewport_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (!_isEditing) return;
        ApplyZoom(e.Delta.Scale, e.Position);
        _attachment.OffsetX += e.Delta.Translation.X;
        _attachment.OffsetY += e.Delta.Translation.Y;
        ClampOffsets();
        ApplyTransform();
        e.Handled = true;
    }

    private void Viewport_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_isEditing) return;
        var point = e.GetCurrentPoint(_viewport);
        ApplyZoom(point.Properties.MouseWheelDelta > 0 ? 1.1 : 1 / 1.1, point.Position);
        ClampOffsets();
        ApplyTransform();
        _contentChanged();
        e.Handled = true;
    }

    private void Viewport_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isEditing || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        var point = e.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed) return;

        _mouseDragPointerId = e.Pointer.PointerId;
        _lastMousePosition = point.Position;
        _viewport.CapturePointer(e.Pointer);
        BeginInteraction();
        e.Handled = true;
    }

    private void Viewport_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_mouseDragPointerId != e.Pointer.PointerId) return;
        Point position = e.GetCurrentPoint(_viewport).Position;
        _attachment.OffsetX += position.X - _lastMousePosition.X;
        _attachment.OffsetY += position.Y - _lastMousePosition.Y;
        _lastMousePosition = position;
        ClampOffsets();
        ApplyTransform();
        e.Handled = true;
    }

    private void Viewport_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_mouseDragPointerId != e.Pointer.PointerId) return;
        _viewport.ReleasePointerCapture(e.Pointer);
        _mouseDragPointerId = null;
        EndInteraction();
        e.Handled = true;
    }

    private void Viewport_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_mouseDragPointerId != e.Pointer.PointerId) return;
        _mouseDragPointerId = null;
        EndInteraction();
    }

    private void ApplyZoom(double factor, Point anchor)
    {
        double oldScale = _attachment.Scale;
        double newScale = Math.Clamp(oldScale * factor, MinimumScale, MaximumScale);
        if (Math.Abs(newScale - oldScale) < 0.001) return;

        // 保持鼠标或双指中心下的画面位置不变，缩放时不会突然跳到中央。
        double ratio = newScale / oldScale;
        double centerX = ActualWidth / 2;
        double centerY = ActualHeight / 2;
        _attachment.OffsetX = anchor.X - centerX - (anchor.X - centerX - _attachment.OffsetX) * ratio;
        _attachment.OffsetY = anchor.Y - centerY - (anchor.Y - centerY - _attachment.OffsetY) * ratio;
        _attachment.Scale = newScale;
    }

    private void ClampOffsets()
    {
        // 图片始终覆盖裁切视窗，避免移动后露出空白区域。
        double maximumX = Math.Max(0, ActualWidth * (_attachment.Scale - 1) / 2);
        double maximumY = Math.Max(0, ActualHeight * (_attachment.Scale - 1) / 2);
        _attachment.OffsetX = Math.Clamp(_attachment.OffsetX, -maximumX, maximumX);
        _attachment.OffsetY = Math.Clamp(_attachment.OffsetY, -maximumY, maximumY);
    }

    private void ApplyTransform()
    {
        _imageTransform.ScaleX = _attachment.Scale;
        _imageTransform.ScaleY = _attachment.Scale;
        _imageTransform.TranslateX = _attachment.OffsetX;
        _imageTransform.TranslateY = _attachment.OffsetY;
    }

    private void ResetImage()
    {
        _attachment.Scale = 1;
        _attachment.OffsetX = 0;
        _attachment.OffsetY = 0;
        ApplyTransform();
        _contentChanged();
    }

    private void BeginInteraction()
    {
        if (_isManipulating) return;
        _isManipulating = true;
        _interactionChanged(true);
    }

    private void EndInteraction()
    {
        if (!_isManipulating) return;
        _isManipulating = false;
        _interactionChanged(false);
        _contentChanged();
    }

    private static Button CreateButton(string glyph, string tooltip, RoutedEventHandler click)
    {
        Button button = new()
        {
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 24, 24, 27)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 14 }
        };
        button.Click += click;
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }
}
