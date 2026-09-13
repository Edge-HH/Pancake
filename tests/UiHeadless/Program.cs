using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Pancake.Views;

namespace UiHeadless;

/// <summary>
/// Avalonia Headless 界面自检的宿主：启动无显示设备的 Avalonia（关闭无头绘图、使用真实渲染栈，
/// 这样随包字体与文本排版与正式运行一致），把真实输入管线交给检查使用，然后打印结果。
/// 检查本体在界面库的 HeadlessUiChecks 里，需要 EnableUiVerification=true 才会编译进去。
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "pancake-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        try
        {
            AppBuilder builder = AppBuilder.Configure<Pancake.App>()
                // 无头平台默认用桩渲染与桩文本排版，随包字体因此加载不了；
                // 打开真实 Skia 渲染后，字体、文本换行与命中测试和正式运行一致。
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
            builder.SetupWithoutStarting();
            // 无头平台不提供渲染缩放开关，缩放检查用「同一 DIP 布局按不同像素密度渲染」的方式做，
            // 证据图（看板与设置页截图）写在这个目录里。
            string evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "headless-evidence");
            Directory.CreateDirectory(evidenceDirectory);
            IReadOnlyList<string> lines = HeadlessUiChecks.Run(new HeadlessInputDriver(), dataRoot, evidenceDirectory);
            foreach (string line in lines) Console.WriteLine(line);
            Console.WriteLine($"evidence={evidenceDirectory}");
            return lines.Count > 0 && lines[0].StartsWith("HEADLESS_OK", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HEADLESS_FAILED {ex.GetType().Name} {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(dataRoot, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响检查结果。
            }
        }
    }

    /// <summary>
    /// 用 Avalonia.Headless 的输入扩展驱动真实的指针事件管线：事件经过命中测试与路由，
    /// 界面层的处理函数是被真正调用的，因此这里的结论对正式运行有效。
    /// 无头平台没有帧循环，等待期间要持续驱动调度器，定时器与自动隐藏才会推进。
    /// </summary>
    private sealed class HeadlessInputDriver : IHeadlessInput
    {
        private TopLevel? _window;

        public void Attach(TopLevel window) => _window = window;

        public void Idle()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            // 命中测试用的是渲染阶段产生的场景，因此这里真的渲染一帧，
            // 否则新建出来的控件不会出现在命中测试的结果里。
            _window?.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
        }

        public void Drag(Point from, Point to)
        {
            Press(from);
            // 分几步移动：一次跳到位也符合“位移超过阈值”的判定，多步更接近真实滑动。
            const int steps = 4;
            for (int step = 1; step <= steps; step++)
            {
                _window!.MouseMove(new Point(
                    from.X + (to.X - from.X) * step / steps,
                    from.Y + (to.Y - from.Y) * step / steps));
                Idle();
            }

            Release(to);
        }

        public void Press(Point point)
        {
            _window!.MouseMove(point);
            _window!.MouseDown(point, MouseButton.Left);
            Idle();
        }

        public void Release(Point point)
        {
            _window!.MouseUp(point, MouseButton.Left);
            Idle();
        }

        public void Wait(TimeSpan duration)
        {
            // 无头平台的时间由渲染定时器推进：按 16ms 一帧把等待时间“走”完，
            // 比真实睡眠更确定，也不会让测试变慢。
            int ticks = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / 16));
            for (int index = 0; index < ticks; index++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }

            Dispatcher.UIThread.RunJobs();
        }

        public void TypeText(string text)
        {
            _window!.KeyTextInput(text);
            Idle();
        }

        public void PressKey(Key key)
        {
            // 新重载要求同时给出物理键与键符号；导航键不需要额外符号。
            _window!.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Idle();
        }
    }
}
