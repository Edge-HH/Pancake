using Microsoft.UI.Xaml;

namespace PancakeBoard;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
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
