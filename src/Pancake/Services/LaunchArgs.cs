namespace Pancake.Services;

/// <summary>
/// 命令行启动参数的纯解析：开关一律精确匹配，--view= 与 --project= 按“前缀+值”取值。
/// 之前的整行子串匹配会把 --windowedX、--view=editorial 误当成有效参数。
/// </summary>
public sealed class LaunchArgs
{
    public const string ViewDisplay = "display";
    public const string ViewEditor = "editor";
    public const string ViewInk = "ink";
    public const string ViewSettings = "settings";

    /// <summary>true 为 --windowed 普通窗口，false 为 --fullscreen 全屏；null 表示未指定，按默认全屏。</summary>
    public bool? Windowed { get; init; }

    public string View { get; init; } = ViewDisplay;

    /// <summary>--project 的取值：项目名称或 .pch 文件路径；未给出时为 null。</summary>
    public string? Project { get; init; }

    /// <summary>二次启动转发时只带显式给出的参数，避免把默认值当成用户的指定。</summary>
    public bool HasViewOverride => View != ViewDisplay;

    public static LaunchArgs Parse(IEnumerable<string> arguments)
    {
        bool? windowed = null;
        string view = ViewDisplay;
        string? project = null;
        // --project 与值分开写（--project 路径）时先记下标记，把下一个参数当值。
        bool pendingProject = false;
        foreach (string raw in arguments)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string argument = raw.Trim();
            if (pendingProject)
            {
                project = argument;
                pendingProject = false;
                continue;
            }
            if (Is(argument, "--windowed")) { windowed = true; continue; }
            if (Is(argument, "--fullscreen")) { windowed = false; continue; }
            if (TakesValue(argument, "--view=", out string value))
            {
                view = value.Trim().ToLowerInvariant() switch
                {
                    ViewEditor => ViewEditor,
                    ViewInk => ViewInk,
                    ViewSettings => ViewSettings,
                    _ => ViewDisplay
                };
                continue;
            }
            if (TakesValue(argument, "--project=", out value))
            {
                if (value.Trim().Length > 0) project = value.Trim();
                else pendingProject = true; // --project= 空值时取下一个参数，路径含空格没加引号也能用。
                continue;
            }
            if (Is(argument, "--project")) pendingProject = true;
        }
        return new LaunchArgs { Windowed = windowed, View = view, Project = project };
    }

    private static bool Is(string argument, string flag) => argument.Equals(flag, StringComparison.OrdinalIgnoreCase);

    private static bool TakesValue(string argument, string prefix, out string value)
    {
        if (argument.Length > prefix.Length && argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = argument[prefix.Length..];
            return true;
        }
        value = "";
        return false;
    }
}
