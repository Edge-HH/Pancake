// 仅在 EnableUiVerification=true 时编译；在隔离的构建目录运行，不访问用户正常运行目录的数据。
// 覆盖"网格/点阵必须铺满当前可见区域"：缩放变小或使用无限作业板时，一开始不在屏幕范围的部分也要有网格。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pancake.Models;
using Pancake.Services;
using Windows.Foundation;
using Path = System.IO.Path;

namespace Pancake;

public sealed partial class MainWindow
{
    internal void ScheduleGridCoverageVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                // 先收集全部场景的证据再统一判失败：一次运行就能看到每个场景的可见区域与网格范围对比。
                List<string> failures = [];
                void Check(bool condition, string message)
                {
                    if (!condition) { failures.Add("FAIL: " + message); return; }
                    evidence.Add("PASS: " + message);
                }
                await NextLayoutAsync();
                // 没有项目时作业板区域是折叠的，网格断言会在零尺寸视口上假绿：先建一个真实项目。
                _library = new ProjectLibrary { Settings = _settings };
                ProjectStore.Create(_library, false).Name = "网格覆盖探针";
                ViewModel.ReplaceSubjects([]);
                SubjectBoard probe = ViewModel.AddSubject("数学");
                probe.X = 24; probe.Y = 24; probe.TileWidth = 420; probe.TileHeight = 320;
                probe.Entries.Add(new HomeworkEntry { Content = "网格覆盖验证用作业。" });
                ShowBoard();
                await NextLayoutAsync();
                await VerifyGridCoverageAsync(evidence, Check);
                evidence.AddRange(failures);
                if (failures.Count > 0) throw new Exception($"{failures.Count} grid coverage checks failed");
                evidence.Add("GRID_COVERAGE_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("GRID_COVERAGE_VERIFICATION_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "grid-coverage-result.txt"), evidence);
                Close();
            }
        };
    }

    private async Task VerifyGridCoverageAsync(List<string> evidence, Action<bool, string> check)
    {
        _settings.LayoutMode = "Split";
        _settings.InfiniteBoard = false;
        _settings.GridStyle = "Grid";
        _settings.GridSize = 64;
        _settings.ShowGridWhileEditing = true;
        ApplyExtendedSettings(); await NextLayoutAsync();

        // 固定作业板缩小：视口露出初始屏幕外的四周，网格也要铺到那里。
        SetBoardZoom(.5); await NextLayoutAsync();
        CheckGridCoversViewport("fixed board zoomed out to 50%", check);

        // 无限作业板缩小：同一要求。
        _settings.InfiniteBoard = true; ApplyExtendedSettings(); await NextLayoutAsync();
        SetBoardZoom(.35); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board zoomed out to 35%", check);

        // 无限作业板平移回 100% 后到初始屏幕外的区域，网格必须跟着走。
        SetBoardZoom(1); await NextLayoutAsync();
        BoardScroller.ChangeView(Math.Max(0, BoardSurface.Width / 2), Math.Max(0, BoardSurface.Height / 2), null, true);
        await NextLayoutAsync();
        CheckGridCoversViewport("infinite board panned past the startup screen", check);

        // 点阵样式同样铺满可见区域。
        _settings.GridStyle = "Dots"; ApplyExtendedSettings(); await NextLayoutAsync();
        CheckGridCoversViewport("dot grid on the panned infinite board", check);

        // 缩放≠1 时平移：判别 offset 坐标语义（网格窗口用 offset/ZoomFactor 换算，缩放后拖动正是用户的无限板用法）。
        _settings.GridStyle = "Grid"; ApplyExtendedSettings(); await NextLayoutAsync();
        SetBoardZoom(.5); await NextLayoutAsync();
        BoardScroller.ChangeView(900, 450, null, true); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board panned at 50% zoom", check);
        SetBoardZoom(1.8); await NextLayoutAsync();
        BoardScroller.ChangeView(400, 200, null, true); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board panned at 180% zoom", check);

        // 视口变大但板尺寸不变（无 ViewChanged 的路径）：全屏切换与分栏比例变化后网格必须覆盖新视口。
        SetBoardZoom(1); BoardScroller.ChangeView(0, 0, 1, true); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board back at 100% before viewport growth", check);
        SetFullScreen(true); await NextLayoutAsync(); await Task.Delay(400); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board after entering fullscreen", check);
        SetFullScreen(false); await NextLayoutAsync(); await Task.Delay(400); await NextLayoutAsync();
        double splitRatio = _settings.SplitRatio;
        // 减小时钟区占比，让作业板视口变大（视口变大而画板尺寸不变，正是缓存键缺窗口时漏画的路径）。
        _settings.SplitRatio = Math.Max(.05, splitRatio - .3); ApplyExtendedSettings(); await NextLayoutAsync();
        CheckGridCoversViewport("infinite board after split ratio growth", check);
        _settings.SplitRatio = splitRatio; ApplyExtendedSettings(); await NextLayoutAsync();
    }

    /// <summary>当前视口在 GridCanvas 内容坐标里的矩形；用可视变换取，避开 offset 坐标语义的歧义。</summary>
    private (double Left, double Top, double Right, double Bottom) VisibleContentRect()
    {
        GeneralTransform transform = BoardScroller.TransformToVisual(GridCanvas);
        Point topLeft = transform.TransformPoint(new Point(0, 0));
        Point bottomRight = transform.TransformPoint(new Point(BoardScroller.ActualWidth, BoardScroller.ActualHeight));
        return (topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>断言网格标记从四边都覆盖到可见区域边缘（允许一个网格间距的余量），否则大面积空白即用户症状。</summary>
    private void CheckGridCoversViewport(string label, Action<bool, string> check)
    {
        (double left, double top, double right, double bottom) = VisibleContentRect();
        // 视口没有实际尺寸时可见矩形会退化为一个点，下面的断言会恒真——先卡住这种情况。
        check(BoardScroller.ActualWidth > 100 && BoardScroller.ActualHeight > 100 && right - left > 100 && bottom - top > 100,
            $"{label}: viewport has real size (viewport {BoardScroller.ActualWidth:0.#}x{BoardScroller.ActualHeight:0.#}, visible {right - left:0.#}x{bottom - top:0.#})");
        double cell = GridSize;
        List<double> verticalLines = GridCanvas.Children.OfType<Line>().Where(line => line.X1 == line.X2).Select(line => line.X1).ToList();
        List<double> horizontalLines = GridCanvas.Children.OfType<Line>().Where(line => line.Y1 == line.Y2).Select(line => line.Y1).ToList();
        List<(double X, double Y)> dots = GridCanvas.Children.OfType<Ellipse>()
            .Select(dot => (Canvas.GetLeft(dot) + dot.Width / 2, Canvas.GetTop(dot) + dot.Height / 2)).ToList();
        bool usesDots = GridAppearance.EffectiveStyle(_settings.GridStyle, _isEditing, _settings.ShowGridWhileEditing) == "Dots";
        double minX = usesDots && dots.Count > 0 ? dots.Min(dot => dot.X) : verticalLines.Count > 0 ? verticalLines.Min() : double.NaN;
        double maxX = usesDots && dots.Count > 0 ? dots.Max(dot => dot.X) : verticalLines.Count > 0 ? verticalLines.Max() : double.NaN;
        double minY = usesDots && dots.Count > 0 ? dots.Min(dot => dot.Y) : horizontalLines.Count > 0 ? horizontalLines.Min() : double.NaN;
        double maxY = usesDots && dots.Count > 0 ? dots.Max(dot => dot.Y) : horizontalLines.Count > 0 ? horizontalLines.Max() : double.NaN;
        check(true, $"{label}: visible=({left:0.#},{top:0.#})-({right:0.#},{bottom:0.#}) gridX=({minX:0.#}..{maxX:0.#}) gridY=({minY:0.#}..{maxY:0.#}) marks={(usesDots ? dots.Count : verticalLines.Count + horizontalLines.Count)}");
        check(!double.IsNaN(maxX), label + ": grid marks exist");
        check(maxX >= right - cell, label + ": grid reaches the right visible edge");
        check(maxY >= bottom - cell, label + ": grid reaches the bottom visible edge");
        check(minX <= left + cell, label + ": grid reaches the left visible edge");
        check(minY <= top + cell, label + ": grid reaches the top visible edge");
    }
}
