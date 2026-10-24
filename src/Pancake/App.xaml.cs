using Microsoft.UI.Xaml;
using Pancake.Services;

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
        LaunchArgs launchArgs = LaunchArgs.Parse(Environment.GetCommandLineArgs().Skip(1));
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
        // --fullscreen 与默认行为一致，显式写出是为了让参数表完整；--windowed 才是切换窗口模式。
        bool startFullScreen = launchArgs.Windowed is not true;
#if PANCAKE_UI_TESTS
        // LaunchArgs 只解析常规启动开关；验证分支仍按子串匹配 --verify-*，这里保留原始命令行供其使用。
        string commandLine = string.Join(' ', Environment.GetCommandLineArgs().Skip(1));
        if (commandLine.Contains("--verify-media-performance", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleMediaPerformanceVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-background-media", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleBackgroundMediaVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-fullscreen-hint", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleFullScreenHintVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-font-audit", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleFontAuditVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-grid-coverage", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleGridCoverageVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-image-drag", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleImageDragVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-ui", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleUiVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-autofill-keys", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleAutofillKeyVerification();
            _window.Activate();
            return;
        }
        if (commandLine.Contains("--verify-autofill-ime", StringComparison.OrdinalIgnoreCase))
        {
            _window = new MainWindow(false, "verification");
            _window.ScheduleAutofillImeDiagnostic();
            _window.Activate();
            return;
        }
#endif
        // 允许多实例关闭时拦下重复启动：转成对已有窗口的一次唤醒后直接退出。
        // 冒烟与验证模式已在上面提前返回，各自跑隔离副本，不参与互斥。
        (bool allowMultipleInstances, string secondLaunchAction) = SingleInstanceService.ReadLaunchSettings();
        SingleInstanceService? instanceGate = null;
        if (!allowMultipleInstances)
        {
            instanceGate = SingleInstanceService.Start();
            if (!instanceGate.IsFirstInstance)
            {
                instanceGate.ForwardToRunningInstance(secondLaunchAction, launchArgs);
                instanceGate.Dispose();
                return;
            }
        }
        _window = new MainWindow(startFullScreen, launchArgs.View, launchArgs.Project);
        if (instanceGate is not null) _window.AttachSingleInstanceGate(instanceGate);
        _window.Activate();
    }
}
