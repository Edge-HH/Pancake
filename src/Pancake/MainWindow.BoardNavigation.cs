using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Pancake;

public sealed partial class MainWindow
{
    private double _infiniteWidth, _infiniteHeight;
    private bool _updatingBoardBounds;
    private uint? _panPointer;
    private Point _panStart;
    private double _panX, _panY;

    private void InitializeBoardNavigation()
    {
        BoardScroller.ViewChanged += (_, _) =>
        {
            if (_updatingBoardBounds) return;
            // 绘制窗口已进入 UpdateBoardBounds 的缓存键，视图变化后这里只需走重算路径。
            UpdateBoardBounds();
        };
        BoardScroller.SizeChanged += (_, _) => UpdateBoardBounds();
        BoardScroller.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (!_settings.InfiniteBoard || !args.GetCurrentPoint(BoardScroller).Properties.IsMiddleButtonPressed) return;
            _panPointer = args.Pointer.PointerId; _panStart = args.GetCurrentPoint(BoardScroller).Position;
            _panX = BoardScroller.HorizontalOffset; _panY = BoardScroller.VerticalOffset;
            BoardScroller.CapturePointer(args.Pointer); args.Handled = true;
        }), true);
        BoardScroller.PointerMoved += (_, args) =>
        {
            if (_panPointer != args.Pointer.PointerId) return;
            Point point = args.GetCurrentPoint(BoardScroller).Position;
            BoardScroller.ChangeView(Math.Max(0, _panX + _panStart.X - point.X), Math.Max(0, _panY + _panStart.Y - point.Y), null, true);
            args.Handled = true;
        };
        void EndPan(object sender, PointerRoutedEventArgs args)
        {
            if (_panPointer != args.Pointer.PointerId) return;
            _panPointer = null; BoardScroller.ReleasePointerCaptures();
        }
        BoardScroller.PointerReleased += EndPan;
        BoardScroller.PointerCanceled += EndPan;
        BoardScroller.PointerCaptureLost += EndPan;
    }
}
