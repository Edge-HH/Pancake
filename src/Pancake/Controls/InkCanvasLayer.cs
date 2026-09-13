using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Pancake.Models;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 手写层：磁贴笔迹与仅时钟模式的全屏笔迹共用同一套书写、擦除与重画逻辑。
/// 坐标直接使用控件内的 DIP，缩放磁贴不会缩放已有笔迹；越界部分由父级裁剪。
/// </summary>
public sealed class InkCanvasLayer : Canvas
{
    private readonly Dictionary<Polyline, InkStrokeData> _rendered = [];
    private IList<InkStrokeData>? _strokes;
    private InkToolSettings _settings = new();
    private IPointer? _pointer;
    private InkStrokeData? _activeStroke;
    private Polyline? _activeShape;
    private bool _erasing;

    public InkCanvasLayer()
    {
        // 透明背景让整块区域都能接收指针，否则只有笔画本身可命中。
        Background = new SolidColorBrush(BoardColor.Transparent.ToColor());
        IsHitTestVisible = false;
        PointerPressed += Layer_PointerPressed;
        PointerMoved += Layer_PointerMoved;
        PointerReleased += Layer_PointerReleased;
        PointerCaptureLost += Layer_PointerCaptureLost;
    }

    /// <summary>笔迹内容被修改后触发，宿主据此安排保存。</summary>
    public event Action? StrokesChanged;

    /// <summary>当前笔迹数量，供宿主与验证脚本确认内容已写入项目。</summary>
    public int StrokeCount => _strokes?.Count ?? 0;

    /// <summary>绑定笔迹列表并重画；传入 null 表示当前没有可写内容。</summary>
    public void Attach(IList<InkStrokeData>? strokes)
    {
        _strokes = strokes;
        Render();
    }

    /// <summary>切换书写状态；退出书写时结束当前笔段，避免残留未完成的笔迹。</summary>
    public void SetInkMode(bool drawing, InkToolSettings settings)
    {
        _settings = settings;
        IsHitTestVisible = drawing;
        if (drawing) return;
        _pointer = null;
        _activeStroke = null;
        _activeShape = null;
        _erasing = false;
    }

    public void ClearStrokes()
    {
        if (_strokes is null || _strokes.Count == 0) return;
        _strokes.Clear();
        Render();
        StrokesChanged?.Invoke();
    }

    /// <summary>撤销最后一笔；返回是否真的移除了内容。</summary>
    public bool UndoLastStroke()
    {
        if (_strokes is null || _strokes.Count == 0) return false;
        _strokes.RemoveAt(_strokes.Count - 1);
        Render();
        StrokesChanged?.Invoke();
        return true;
    }

    /// <summary>主题切换或笔迹颜色变化后重画一次。</summary>
    public void RefreshTheme() => Render();

    private void Render()
    {
        Children.Clear();
        _rendered.Clear();
        if (_strokes is null) return;
        foreach (InkStrokeData stroke in _strokes)
        {
            Polyline shape = InkStrokeVisual.Create(stroke);
            Children.Add(shape);
            _rendered[shape] = stroke;
        }
    }

    private void Layer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsHitTestVisible || _pointer is not null || _strokes is null) return;
        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !IsTouchOrPen(point)) return;
        Point position = point.Position;
        if (!IsInBounds(position)) return;

        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        if (_settings.Eraser)
        {
            _erasing = true;
            EraseStrokeAt(position);
            e.Handled = true;
            return;
        }

        _activeStroke = new InkStrokeData { Color = _settings.Color, Thickness = _settings.Thickness };
        _activeStroke.Points.Add(new BoardPoint(position.X, position.Y));
        // 单点无法画出线条，额外补一个极近的点，让轻点也能落下一个圆点。
        _activeStroke.Points.Add(new BoardPoint(position.X + 0.01, position.Y));
        _strokes.Add(_activeStroke);
        // 折线的点由创建时统一填充，这里只需把新点追加到后面。
        _activeShape = InkStrokeVisual.Create(_activeStroke);
        Children.Add(_activeShape);
        _rendered[_activeShape] = _activeStroke;
        e.Handled = true;
    }

    private void Layer_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        Point position = e.GetCurrentPoint(this).Position;
        if (!IsInBounds(position))
        {
            // 越界即结束当前笔段，避免重新进入时画出跨越空隙的连接线。
            _activeStroke = null;
            _activeShape = null;
            return;
        }

        if (_erasing)
        {
            EraseStrokeAt(position);
            e.Handled = true;
            return;
        }

        if (_activeStroke is null || _activeShape is null) return;
        _activeStroke.Points.Add(new BoardPoint(position.X, position.Y));
        _activeShape.Points.Add(position);
        e.Handled = true;
    }

    private void Layer_PointerReleased(object? sender, PointerEventArgs e)
    {
        if (_pointer is null || !ReferenceEquals(e.Pointer, _pointer)) return;
        e.Pointer.Capture(null);
        _pointer = null;
        bool changed = _activeStroke is not null || _erasing;
        _activeStroke = null;
        _activeShape = null;
        _erasing = false;
        if (changed) StrokesChanged?.Invoke();
        e.Handled = true;
    }

    private void Layer_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_pointer is null) return;
        _pointer = null;
        bool changed = _activeStroke is not null || _erasing;
        _activeStroke = null;
        _activeShape = null;
        _erasing = false;
        if (changed) StrokesChanged?.Invoke();
    }

    private static bool IsTouchOrPen(PointerPoint point) =>
        point.Pointer.Type is PointerType.Touch or PointerType.Pen;

    private bool IsInBounds(Point point) =>
        point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height;

    /// <summary>
    /// 橡皮擦按命中点半径删除整条笔迹：半径跟随笔迹粗细放大，
    /// 触屏与触控笔都能可靠命中，不必精确点在细线上。
    /// 命中判定用核心层的手写几何（点到线段的距离），与旧版一致；
    /// 一次采样会删掉所有被扫到的笔迹，连续拖动因此很跟手。
    /// </summary>
    private void EraseStrokeAt(Point point)
    {
        if (_strokes is null) return;
        List<Polyline> hits = [];
        foreach ((Polyline shape, InkStrokeData stroke) in _rendered)
        {
            // 与旧版一致：半径至少 8 像素，并按笔迹粗细（含笔尖缩放）放大。
            double radius = Math.Max(8, stroke.Thickness * Math.Max(stroke.TipScaleX, stroke.TipScaleY));
            List<(double X, double Y)> points = [.. stroke.Points.Select(candidate => (candidate.X, candidate.Y))];
            if (points.Count == 0) continue;
            if (InkGeometry.HitTest(points, point.X, point.Y, radius)) hits.Add(shape);
        }

        if (hits.Count == 0) return;
        foreach (Polyline shape in hits)
        {
            _strokes.Remove(_rendered[shape]);
            Children.Remove(shape);
            _rendered.Remove(shape);
        }

        StrokesChanged?.Invoke();
    }
}
