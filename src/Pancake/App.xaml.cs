using Microsoft.UI.Xaml;

namespace Pancake;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => WriteCrashLog(new Exception(args.Message, args.Exception));
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception);
    }

    private static void WriteCrashLog(Exception? exception)
    {
        try
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "crash.log"), $"[{DateTimeOffset.Now:O}] {exception}\n");
        }
        catch
        {
            // 崩溃记录不能覆盖原始异常。
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string commandLine = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        // 发布流水线使用真实窗口及资源加载路径；隔离复制目录由验证脚本负责。
        if (Environment.GetCommandLineArgs().Contains("--smoke-test"))
        {
            _window = new MainWindow(false, "verification");
            ((FrameworkElement)_window.Content).Loaded += (_, _) => _window.DispatcherQueue.TryEnqueue(() =>
            {
                ((FrameworkElement)_window.Content).UpdateLayout();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-ok.txt"), "WINDOW_LOADED");
                _window.Close();
            });
            _window.Activate();
            return;
        }
        bool startFullScreen = !commandLine.Contains("--windowed", StringComparison.OrdinalIgnoreCase);
        string initialView = commandLine.Contains("--view=editor", StringComparison.OrdinalIgnoreCase)
            ? "editor"
            : commandLine.Contains("--view=settings", StringComparison.OrdinalIgnoreCase)
                ? "settings"
                : commandLine.Contains("--view=ink", StringComparison.OrdinalIgnoreCase)
                    ? "ink"
                    : "display";
#if PANCAKE_UI_TESTS
        if (commandLine.Contains("--verify-ui", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleUiVerification();
            _window.Activate();
            return;
        }
#endif
        _window = new MainWindow(startFullScreen, initialView);
        _window.Activate();
    }
}
