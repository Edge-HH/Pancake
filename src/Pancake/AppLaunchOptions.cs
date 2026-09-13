namespace Pancake;

/// <summary>启动参数；与旧版保持兼容：默认全屏，可用 --windowed 与 --view= 切换。</summary>
public sealed record AppLaunchOptions(bool StartFullScreen, string View)
{
    public static AppLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        bool windowed = arguments.Any(argument => argument.Contains("--windowed", StringComparison.OrdinalIgnoreCase));
        string view = arguments.Any(argument => argument.Contains("--view=editor", StringComparison.OrdinalIgnoreCase)) ? "editor"
            : arguments.Any(argument => argument.Contains("--view=settings", StringComparison.OrdinalIgnoreCase)) ? "settings"
            : "display";
        return new AppLaunchOptions(!windowed, view);
    }
}
