using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Pancake.Services;
using Pancake.ViewModels;
using Pancake.Views;

namespace Pancake;

/// <summary>
/// 三端共用的 Avalonia 应用。平台差异全部通过 PlatformServices 注入，
/// 这里只负责装配主题、字体回退与主窗口。
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow(AppLaunchOptions.Parse(Environment.GetCommandLineArgs()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>按启动参数创建主窗口；桌面入口与后续的验证入口共用同一处逻辑。</summary>
    public static MainWindow CreateMainWindow(AppLaunchOptions options)
    {
        // 字体必须在主窗口创建前注册，否则首帧会退回系统默认字体并导致排版跳动。
        FontService.RegisterBundledFonts();

        MainViewModel viewModel = new();
        // 项目数据优先读取新数据目录，并在首次启动时迁移旧的 pancake.json。
        viewModel.LoadFromDisk(AppPathService.ResolveDataRoot());
        return new MainWindow(viewModel, options);
    }
}
