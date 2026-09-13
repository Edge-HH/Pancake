using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Pancake.Controls;

/// <summary>
/// 控制窗与浮岛的毛玻璃背衬：按控件自身在背景坐标系里的位置，
/// 把背景快照平移到正确位置画进来，并裁剪成圆角。
/// 快照由宿主定时刷新，模糊本身交给控件外观上的 <see cref="BlurEffect"/>。
/// </summary>
public sealed class BackdropBlurLayer : Control
{
    public static readonly StyledProperty<Bitmap?> SnapshotProperty =
        AvaloniaProperty.Register<BackdropBlurLayer, Bitmap?>(nameof(Snapshot));

    /// <summary>快照左上角相对本控件的偏移：控件在背景坐标系里的位置。</summary>
    public static readonly StyledProperty<Point> SourceOffsetProperty =
        AvaloniaProperty.Register<BackdropBlurLayer, Point>(nameof(SourceOffset));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<BackdropBlurLayer, CornerRadius>(nameof(CornerRadius), new CornerRadius(0));

    static BackdropBlurLayer()
    {
        AffectsRender<BackdropBlurLayer>(SnapshotProperty, SourceOffsetProperty, CornerRadiusProperty);
    }

    public Bitmap? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public Point SourceOffset
    {
        get => GetValue(SourceOffsetProperty);
        set => SetValue(SourceOffsetProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Snapshot is not { } snapshot || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Rect destination = new(
            -SourceOffset.X,
            -SourceOffset.Y,
            snapshot.Size.Width,
            snapshot.Size.Height);
        double radius = Math.Max(CornerRadius.TopLeft, CornerRadius.BottomRight);
        Size bounds = Bounds.Size;
        using (context.PushGeometryClip(new RectangleGeometry(new Rect(bounds), radius, radius)))
        {
            context.DrawImage(snapshot, new Rect(snapshot.Size), destination);
        }
    }
}
