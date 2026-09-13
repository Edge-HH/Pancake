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
            UpdateBoardBounds();
            RenderGrid(BoardCanvas.Width, BoardCanvas.Height);
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
