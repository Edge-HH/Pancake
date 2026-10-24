using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Pancake.Controls;

/// <summary>
/// 一条作业下方的图片区：图片默认依次纵向排列，PositionX/PositionY 是相对各自默认位置的偏移。
/// 偏移参与真实布局而非渲染变换，所以图片下移或放大时区域随之变高，下面的作业自动让开。
/// </summary>
public sealed class AttachmentArea : Panel
{
    public const double ImageSpacing = 8;

    // 图片在区内的默认纵向位置只按前面图片的取景框高度累计，工具栏显隐不会让后面的图片跳动。
    internal double SlotTop(AttachmentImageControl image)
    {
        double top = 0;
        foreach (UIElement child in Children)
        {
            if (ReferenceEquals(child, image)) return top;
            if (child is AttachmentImageControl other) top += other.FrameHeight + ImageSpacing;
        }
        return top;
    }

    // 同一条作业里只保留一个选中的图片，避免多套边框和工具栏叠在一起。
    internal void SelectOnly(AttachmentImageControl image)
    {
        foreach (AttachmentImageControl other in Children.OfType<AttachmentImageControl>())
            if (!ReferenceEquals(other, image)) other.Deselect();
    }

    internal double AvailableWidth => ActualWidth > 0 ? ActualWidth : double.PositiveInfinity;

    internal static double ClampX(double x, double frameWidth, double areaWidth) =>
        double.IsInfinity(areaWidth) ? Math.Max(0, x) : Math.Clamp(x, 0, Math.Max(0, areaWidth - frameWidth));

    protected override Size MeasureOverride(Size availableSize)
    {
        double slot = 0, right = 0, bottom = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (child is not AttachmentImageControl image) continue;
            (double x, double y) = Place(image, slot, availableSize.Width);
            right = Math.Max(right, x + image.DesiredSize.Width);
            bottom = Math.Max(bottom, y + image.DesiredSize.Height);
            slot += image.FrameHeight + ImageSpacing;
        }
        double width = double.IsInfinity(availableSize.Width) ? right : Math.Min(right, availableSize.Width);
        return new Size(width, bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double slot = 0;
        foreach (UIElement child in Children)
        {
            if (child is not AttachmentImageControl image)
            {
                child.Arrange(new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
                continue;
            }
            (double x, double y) = Place(image, slot, finalSize.Width);
            image.Arrange(new Rect(x, y, image.DesiredSize.Width, image.DesiredSize.Height));
            slot += image.FrameHeight + ImageSpacing;
        }
        return finalSize;
    }

    // 布局时同样夹紧：磁贴变窄后图片仍贴在区内，不改写保存的偏移，磁贴变宽后可恢复原位。
    private static (double X, double Y) Place(AttachmentImageControl image, double slotTop, double areaWidth) =>
        (ClampX(image.PositionX, image.FrameWidth, areaWidth), Math.Max(0, slotTop + image.PositionY));
}
