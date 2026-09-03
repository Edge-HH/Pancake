using Microsoft.UI.Xaml;

namespace Pancake;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => WriteCrashLog(args.Exception);
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
        bool startFullScreen = !commandLine.Contains("--windowed", StringComparison.OrdinalIgnoreCase);
        string initialView = commandLine.Contains("--view=editor", StringComparison.OrdinalIgnoreCase)
            ? "editor"
            : commandLine.Contains("--view=settings", StringComparison.OrdinalIgnoreCase)
                ? "settings"
                : commandLine.Contains("--view=ink", StringComparison.OrdinalIgnoreCase)
                    ? "ink"
                    : "display";
        _window = new MainWindow(startFullScreen, initialView);
        _window.Activate();
    }
}
