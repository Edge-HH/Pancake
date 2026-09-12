using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Models;
using Pancake.Services;
using Windows.Foundation;

namespace Pancake.Controls;

/// <summary>
/// 仅时钟模式的全屏笔迹层：整块屏幕都是书写范围，笔迹直接写进当前项目的时钟笔迹列表。
/// 屏幕坐标就是笔迹坐标，因此不做磁贴那样的缩放补偿；颜色、粗细与橡皮擦沿用全局画笔设置。
/// </summary>
public sealed class ClockInkLayer : Canvas
{
    private readonly Dictionary<Polyline, InkStrokeData> _renderedStrokes = [];
    private IList<InkStrokeData>? _strokes;
    private InkToolSettings _settings = new();
    private uint? _pointerId;
    private InkStrokeData? _activeStroke;
    private Polyline? _activeShape;
    private bool _erasing;

    /// <summary>笔迹内容被修改后触发，宿主据此安排保存。</summary>
    public event Action? StrokesChanged;

    public ClockInkLayer()
    {
        // 透明背景让整块区域都能接收指针，否则只有笔画本身可命中。
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        IsHitTestVisible = false;
        PointerPressed += Layer_PointerPressed;
        PointerMoved += Layer_PointerMoved;
        PointerReleased += Layer_PointerReleased;
        PointerCanceled += Layer_PointerReleased;
        PointerCaptureLost += Layer_PointerReleased;
    }

    /// <summary>当前笔迹总数，宿主和验证脚本用它确认内容是否写进项目。</summary>
    public int StrokeCount => _strokes?.Count ?? 0;

    /// <summary>绑定笔迹列表并重画；传入 null 表示当前没有可写内容（未选择项目）。</summary>
    public void Attach(IList<InkStrokeData>? strokes)
    {
        _strokes = strokes;
        RenderStoredStrokes();
    }

    /// <summary>切换书写状态；退出书写时结束当前笔段，避免残留未完成的笔迹。</summary>
    public void SetInkMode(bool drawing, InkToolSettings settings)
    {
        _settings = settings;
        IsHitTestVisible = drawing;
        if (drawing) return;
        _pointerId = null;
        _activeStroke = null;
        _activeShape = null;
        _erasing = false;
        ReleasePointerCaptures();
    }

    public void ClearStrokes()
    {
        if (_strokes is null || _strokes.Count == 0) return;
        _strokes.Clear();
        RenderStoredStrokes();
        StrokesChanged?.Invoke();
    }

    private void RenderStoredStrokes()
    {
        Children.Clear();
        _renderedStrokes.Clear();
        if (_strokes is null) return;
        foreach (InkStrokeData stroke in _strokes)
        {
            Polyline shape = InkStrokeVisual.Create(stroke);
            foreach (Point point in stroke.Points) shape.Points.Add(ScaleOut(point, stroke));
            Children.Add(shape);
            _renderedStrokes[shape] = stroke;
        }
    }

    private void Layer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsHitTestVisible || _pointerId is not null || _strokes is null) return;
        Point point = e.GetCurrentPoint(this).Position;
        _pointerId = e.Pointer.PointerId;
        if (_settings.Eraser)
        {
            _erasing = true;
            EraseStrokeAt(point);
            CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        BeginStroke(point);
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    /// <summary>落笔：创建笔迹数据与显示折线，笔迹立即进入当前项目的列表。</summary>
    private void BeginStroke(Point point)
    {
        if (_strokes is null) return;
        _activeStroke = new InkStrokeData { Color = _settings.Color, Thickness = _settings.Thickness };
        _activeStroke.Points.Add(point);
        // 点一下也应留下一个圆点：补一个极小位移让折线可以渲染出线帽。
        _activeStroke.Points.Add(new Point(point.X + 0.01, point.Y));
        _strokes.Add(_activeStroke);
        _activeShape = InkStrokeVisual.Create(_activeStroke);
        _activeShape.Points.Add(ScaleOut(point, _activeStroke));
        _activeShape.Points.Add(ScaleOut(new Point(point.X + 0.01, point.Y), _activeStroke));
        Children.Add(_activeShape);
        _renderedStrokes[_activeShape] = _activeStroke;
    }

    private void ExtendStroke(Point point)
    {
        if (_activeStroke is null || _activeShape is null) return;
        _activeStroke.Points.Add(point);
        _activeShape.Points.Add(ScaleOut(point, _activeStroke));
    }

    private void Layer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        Point point = e.GetCurrentPoint(this).Position;
        if (_erasing) EraseStrokeAt(point);
        else ExtendStroke(point);
        e.Handled = true;
    }

    private void Layer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointerId != e.Pointer.PointerId) return;
        _pointerId = null;
        ReleasePointerCapture(e.Pointer);
        EndStroke();
        e.Handled = true;
    }

    /// <summary>收笔：结束当前笔段并通知宿主保存。</summary>
    private void EndStroke()
    {
        bool contentChanged = _activeStroke is not null || _erasing;
        _activeStroke = null;
        _activeShape = null;
        _erasing = false;
        if (contentChanged) StrokesChanged?.Invoke();
    }

    private void EraseStrokeAt(Point point)
    {
        var hit = _renderedStrokes.LastOrDefault(pair => InkGeometry.HitTest(
            pair.Value.Points.Select(p => (p.X, p.Y)).ToList(), point.X, point.Y,
            Math.Max(8, pair.Value.Thickness * Math.Max(pair.Value.TipScaleX, pair.Value.TipScaleY))));
        if (hit.Key is null) return;
        _renderedStrokes.Remove(hit.Key);
        _strokes?.Remove(hit.Value);
        Children.Remove(hit.Key);
        StrokesChanged?.Invoke();
    }

    private static Point ScaleOut(Point point, InkStrokeData stroke) =>
        new(point.X / Math.Max(0.0001, stroke.TipScaleX), point.Y / Math.Max(0.0001, stroke.TipScaleY));

#if PANCAKE_UI_TESTS
    /// <summary>验证脚本用：不经过指针事件直接落笔，走的是和真实书写完全相同的落笔与结束逻辑。</summary>
    internal void VerificationDrawStroke(IReadOnlyList<Point> points)
    {
        if (points.Count == 0) return;
        BeginStroke(points[0]);
        for (int index = 1; index < points.Count; index++) ExtendStroke(points[index]);
        EndStroke();
    }
#endif
}
