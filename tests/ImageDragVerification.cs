// 仅在 EnableUiVerification=true 时编译；在隔离的构建目录运行，不访问用户正常运行目录的数据。
// 用真实鼠标输入验证作业图片：拖动和八点缩放跟手、限制在所属作业的图片区内，且下面一条作业自动让开。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;
using Windows.Foundation;
using Path = System.IO.Path;

namespace Pancake;

public sealed partial class MainWindow
{
    internal void ScheduleImageDragVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                List<string> failures = [];
                void Check(bool condition, string message)
                {
                    if (!condition) { failures.Add("FAIL: " + message); return; }
                    evidence.Add("PASS: " + message);
                }
                await NextLayoutAsync();
                await VerifyImageDragAsync(output, evidence, Check);
                evidence.AddRange(failures);
                if (failures.Count > 0) throw new Exception($"{failures.Count} image drag checks failed");
                evidence.Add("IMAGE_DRAG_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("IMAGE_DRAG_VERIFICATION_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "image-drag-result.txt"), evidence);
                Close();
            }
        };
    }

    private async Task VerifyImageDragAsync(string output, List<string> evidence, Action<bool, string> check)
    {
        _settings.AutoUpdateEnabled = false;
        _library = new ProjectLibrary { Settings = _settings };
        ProjectDocument project = ProjectStore.Create(_library, false);
        project.Name = "图片拖动探针";
        string imagePath = Path.Combine(output, "drag-source.png");
        Grid imageSource = new() { Width = 200, Height = 100, Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue) };
        imageSource.Children.Add(new TextBlock { Text = "拖我", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        ExportOverlay.Visibility = Visibility.Visible; ExportOverlay.Children.Add(imageSource);
        await NextLayoutAsync(); await SaveVisualAsync(imageSource, imagePath, 200, 100);
        ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed;

        ViewModel.ReplaceSubjects([]);
        SubjectBoard subject = ViewModel.AddSubject("数学");
        subject.X = 360; subject.Y = 200; subject.TileWidth = 560; subject.TileHeight = 640;
        HomeworkEntry first = new() { Content = "第一条作业" };
        AttachmentItem attachment = new() { Name = "drag-source.png", Kind = "图片", Path = _projectStore.CopyAttachment(project.Id, imagePath), FrameWidth = 200, AspectRatio = 2 };
        first.Attachments.Add(attachment);
        subject.Entries.Add(first);
        subject.Entries.Add(new HomeworkEntry { Content = "第二条作业" });
        ShowBoard();
        SetBoardZoom(1);
        BuildTiles(); EnterEditing(); await NextLayoutAsync();

        SubjectTileControl tile = BoardCanvas.Children.OfType<SubjectTileControl>().Single();
        // 每一步都重新查找控件，避免作业行重建后仍拿着脱离视觉树的旧引用。
        AttachmentImageControl Image() => FindVisuals<AttachmentImageControl>(tile).Single();
        AttachmentArea Area() => (AttachmentArea)Image().Parent;
        Grid Frame() => FindVisuals<Grid>(Image()).First(grid => grid.Clip is not null);
        Grid SecondRow() => FindVisuals<RichEditBox>(tile).Select(editor => (Grid)editor.Parent).Last();
        // 隔离目录下解码可能受环境影响，只记录结果；拖动与缩放不依赖图片内容。
        evidence.Add($"Image decoded: {await Image().ImageReady}");

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // 默认窗口可能盖不住磁贴所在的屏幕位置，注入的点击会落到别的窗口：先铺开测试窗口。
        Windows.Graphics.SizeInt32 size = VerificationWindowSize();
        _appWindow?.MoveAndResize(new Windows.Graphics.RectInt32(0, 0, size.Width, size.Height));
        await NextLayoutAsync();
        // 本机可能还开着另一个 Pancake 窗口，置顶测试窗口，注入的鼠标才会落在被测控件上。
        SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        ForceForeground(hwnd);
        await Task.Delay(300);
        double scale = RootShell.XamlRoot.RasterizationScale;
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(Area()).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        double RowTop() => SecondRow().TransformToVisual(tile).TransformPoint(new Point(0, 0)).Y;
        Point Screen(FrameworkElement element, double x, double y)
        {
            Point client = element.TransformToVisual(null).TransformPoint(new Point(x, y));
            POINT origin = new(); ClientToScreen(hwnd, ref origin);
            return new Point(origin.X + client.X * scale, origin.Y + client.Y * scale);
        }
        // 分多步移动，模拟真实拖动轨迹；每步都让 UI 线程处理输入与布局。
        async Task Drag(Point from, double dx, double dy)
        {
            MoveMouse(from); await Task.Delay(60);
            SendMouse(MouseLeftDown); await Task.Delay(60);
            for (int step = 1; step <= 12; step++)
            {
                MoveMouse(new Point(from.X + dx * scale * step / 12, from.Y + dy * scale * step / 12));
                await Task.Delay(25);
            }
            await Task.Delay(60);
            SendMouse(MouseLeftUp);
            await NextLayoutAsync();
        }
        static bool Near(double a, double b, double tolerance = 2.5) => Math.Abs(a - b) <= tolerance;

        await SaveVisualAsync(tile, Path.Combine(output, "image-drag-initial.png"), (int)tile.ActualWidth, (int)tile.ActualHeight);
        // 先点选，让工具栏出现后再记录下一条作业的位置，排除工具栏本身占位的影响。
        await Drag(Screen(Frame(), 100, 50), 0, 0);
        Rect start = Bounds(Frame());
        double rowStart = RowTop();
        evidence.Add($"Initial frame {start}, second row top {rowStart:0.#}");
        check(FindVisuals<Border>(Image()).Any(border => border.Name == "SelectionBorder" && border.Visibility == Visibility.Visible), "clicking the image selects it");

        await Drag(Screen(Frame(), 100, 50), 80, 60);
        Rect moved = Bounds(Frame());
        evidence.Add($"After move frame {moved}, second row top {RowTop():0.#}");
        check(Near(moved.X - start.X, 80) && Near(moved.Y - start.Y, 60), "image follows the pointer while dragging");
        check(Near(RowTop() - rowStart, 60), "dragging the image down pushes the next homework entry down by the same amount");
        await SaveVisualAsync(tile, Path.Combine(output, "image-drag-moved.png"), (int)tile.ActualWidth, (int)tile.ActualHeight);

        await Drag(Screen(Frame(), 100, 50), 600, 0);
        Rect pushedRight = Bounds(Frame());
        check(Near(pushedRight.Right, Area().ActualWidth, 1), "dragging far right stops at the entry's right edge");
        await Drag(Screen(Frame(), 100, 50), -400, -150);
        Rect pushedTopLeft = Bounds(Frame());
        check(Near(pushedTopLeft.X, 0, 1) && Near(pushedTopLeft.Y, 0, 1), "dragging far up-left stops at the entry's image area");
        check(Near(RowTop(), rowStart), "next entry returns once the image moves back up");

        // 右下角等比放大：左上角固定，高度增加量按比例传给下一条作业。
        Rect beforeGrow = Bounds(Frame());
        await Drag(Screen(Frame(), Frame().ActualWidth - 4, Frame().ActualHeight - 4), 60, 0);
        Rect grown = Bounds(Frame());
        evidence.Add($"After bottom-right resize {grown}");
        check(Near(grown.Width - beforeGrow.Width, 60) && Near(grown.Height, grown.Width / 2, 1), "bottom-right handle scales uniformly and follows the pointer");
        check(Near(grown.X, beforeGrow.X, 1) && Near(grown.Y, beforeGrow.Y, 1), "bottom-right resize keeps the top-left corner fixed");
        check(Near(RowTop() - rowStart, grown.Height - beforeGrow.Height), "growing the image pushes the next entry down");

        // 先往下移，再拖左上角缩小：右下角固定。
        await Drag(Screen(Frame(), Frame().ActualWidth / 2, Frame().ActualHeight / 2), 120, 80);
        Rect beforeShrink = Bounds(Frame());
        await Drag(Screen(Frame(), 4, 4), 40, 20);
        Rect shrunk = Bounds(Frame());
        evidence.Add($"After top-left resize {shrunk}");
        check(Near(shrunk.Right, beforeShrink.Right, 1) && Near(shrunk.Bottom, beforeShrink.Bottom, 1), "top-left resize keeps the bottom-right corner fixed");
        check(Near(beforeShrink.Width - shrunk.Width, 40) && Near(shrunk.Height, shrunk.Width / 2, 1), "top-left handle shrinks uniformly and follows the pointer");

        await Drag(Screen(Frame(), Frame().ActualWidth - 4, Frame().ActualHeight / 2), 600, 0);
        Rect widest = Bounds(Frame());
        check(Near(widest.Right, Area().ActualWidth, 1) && Near(widest.X, shrunk.X, 1), "right handle cannot stretch past the entry's right edge");
        check(Near(attachment.FrameWidth, widest.Width, 1) && Near(attachment.PositionX, widest.X, 1), "frame size and position are stored on the attachment");
        await SaveVisualAsync(tile, Path.Combine(output, "image-drag-final.png"), (int)tile.ActualWidth, (int)tile.ActualHeight);
    }

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint window, ref POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    // 鼠标版 INPUT：MOUSEINPUT 在 x64 下从偏移 8 开始，dwFlags 位于偏移 20。
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 40)]
    private struct MOUSE_INPUT
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public uint Type;
        [System.Runtime.InteropServices.FieldOffset(8)] public int X;
        [System.Runtime.InteropServices.FieldOffset(12)] public int Y;
        [System.Runtime.InteropServices.FieldOffset(20)] public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendInput")]
    private static extern uint SendMouseInput(uint count, MOUSE_INPUT[] inputs, int size);

    private static void SendMouse(uint flags) => SendMouseInput(1, [new MOUSE_INPUT { Flags = flags }], 40);

    // 以虚拟桌面归一化坐标做绝对移动，WinUI 才会收到真实的指针更新。
    private static void MoveMouse(Point screen)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        SendMouseInput(1, [new MOUSE_INPUT
        {
            X = (int)Math.Round((screen.X - left) * 65535 / Math.Max(1, width - 1)),
            Y = (int)Math.Round((screen.Y - top) * 65535 / Math.Max(1, height - 1)),
            Flags = 0x0001 | 0x4000 | 0x8000
        }], 40);
    }
}
