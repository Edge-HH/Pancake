using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pancake.Services;

/// <summary>
/// 窗口启动闸门：“允许多实例”关闭后，同一安装目录只允许同时打开一个软件窗口；
/// 再次启动按“已打开时再次启动的行为”唤醒已有窗口（移至前台 / 全屏 / 不执行任何操作）后退出。
/// 互斥体和唤醒事件按数据目录命名，不同便携副本互不干扰。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    public const string ForegroundAction = "Foreground";
    public const string FullScreenAction = "FullScreen";
    public const string NoneAction = "None";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _foregroundSignal;
    private readonly EventWaitHandle _fullScreenSignal;
    private readonly EventWaitHandle _quitSignal;
    private readonly bool _owned;
    private readonly Thread? _listener;
    private bool _disposed;

    /// <summary>true 表示本次启动认领了闸门（第一个窗口）；false 表示同目录已有窗口在运行。</summary>
    public bool IsFirstInstance => _owned;

    /// <summary>已有窗口收到再次启动时触发，参数是行为代码（Foreground / FullScreen）；在监听线程上触发。</summary>
    public event Action<string>? SecondLaunchRequested;

    private SingleInstanceService(Mutex mutex, EventWaitHandle foregroundSignal, EventWaitHandle fullScreenSignal, EventWaitHandle quitSignal, bool owned)
    {
        _mutex = mutex;
        _foregroundSignal = foregroundSignal;
        _fullScreenSignal = fullScreenSignal;
        _quitSignal = quitSignal;
        _owned = owned;
        if (!_owned) return;
        // 只有闸门的认领者监听唤醒信号；监听线程随进程退出，Dispose 负责正常收尾。
        _listener = new Thread(Listen) { IsBackground = true, Name = "Pancake.SingleInstance" };
        _listener.Start();
    }

    /// <summary>
    /// 尝试认领同一安装目录的窗口闸门。先建唤醒事件再认领互斥体：
    /// 后启动的进程抢不到互斥体时，唤醒事件必然已经存在。
    /// </summary>
    public static SingleInstanceService Start()
    {
        string suffix = InstanceSuffix();
        EventWaitHandle foregroundSignal = new(false, EventResetMode.AutoReset, ForegroundSignalPrefix + suffix);
        EventWaitHandle fullScreenSignal = new(false, EventResetMode.AutoReset, FullScreenSignalPrefix + suffix);
        EventWaitHandle quitSignal = new(false, EventResetMode.ManualReset, QuitSignalPrefix + suffix);
        Mutex mutex = new(true, MutexPrefix + suffix, out bool createdNew);
        return new SingleInstanceService(mutex, foregroundSignal, fullScreenSignal, quitSignal, createdNew);
    }

    /// <summary>把这次启动转成一次唤醒，交给已有窗口执行；“不执行任何操作”不发任何信号。</summary>
    public void ForwardToRunningInstance(string action)
    {
        if (action == FullScreenAction) _fullScreenSignal.Set();
        else if (action == ForegroundAction) _foregroundSignal.Set();
    }

    /// <summary>
    /// 把已有窗口移至前台。前台锁定会拒绝后台进程的置顶请求，
    /// 先挂到当前前台线程再激活，否则唤醒后的窗口可能仍留在后面。
    /// </summary>
    public static void BringToForeground(nint windowHandle)
    {
        if (windowHandle == nint.Zero) return;
        uint current = GetCurrentThreadId();
        nint foreground = GetForegroundWindow();
        uint foregroundThread = foreground == nint.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        if (foregroundThread != 0 && foregroundThread != current) AttachThreadInput(current, foregroundThread, true);
        // SW_RESTORE 让最小化的窗口先回到正常尺寸，置顶请求才真正可见。
        ShowWindow(windowHandle, IsIconic(windowHandle) ? 9 : 9);
        SetForegroundWindow(windowHandle);
        if (foregroundThread != 0 && foregroundThread != current) AttachThreadInput(current, foregroundThread, false);
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attach1);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// 启动闸门需要的窗口设置，从数据目录的项目清单读取（新结构 projects.json，旧结构 pancake.json）。
    /// 文件缺失或损坏时按“关闭允许多实例 + 移至前台”处理，保持只开一个窗口的默认行为。
    /// </summary>
    public static (bool AllowMultipleInstances, string SecondLaunchAction) ReadLaunchSettings()
    {
        foreach (string name in new[] { "projects.json", "pancake.json" })
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "data", name);
                if (!File.Exists(path)) continue;
                SettingsFile? file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path));
                if (file?.Settings is null) continue;
                return (file.Settings.AllowMultipleInstances, BoardSettingsState.NormalizeSecondLaunchAction(file.Settings.SecondLaunchAction));
            }
            catch
            {
                // 单个文件读不出来不能挡住启动闸门，继续尝试旧结构或回落默认值。
            }
        }
        return (false, ForegroundAction);
    }

    private void Listen()
    {
        WaitHandle[] signals = [_foregroundSignal, _fullScreenSignal, _quitSignal];
        while (WaitHandle.WaitAny(signals) is int index && index != 2)
            SecondLaunchRequested?.Invoke(index == 0 ? ForegroundAction : FullScreenAction);
    }

    public void Dispose()
    {
        // 还原备份后重启会先手动释放闸门再随窗口关闭释放一次；重复释放不能抛出。
        if (_disposed) return;
        _disposed = true;
        // 只有认领者能让监听线程收工；共享退出信号被误触发会关掉已有窗口的监听。
        if (_owned)
        {
            _quitSignal.Set();
            _listener?.Join(TimeSpan.FromSeconds(2));
        }
        _mutex.Dispose();
        _foregroundSignal.Dispose();
        _fullScreenSignal.Dispose();
        _quitSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>项目清单只取窗口设置两个字段；其余内容（项目、磁贴）与启动闸门无关。</summary>
    private sealed class SettingsFile
    {
        public BoardSettingsState? Settings { get; set; }
    }

    private const string MutexPrefix = "Local\\Pancake.SingleInstance.";
    private const string ForegroundSignalPrefix = "Local\\Pancake.SecondLaunch.Foreground.";
    private const string FullScreenSignalPrefix = "Local\\Pancake.SecondLaunch.FullScreen.";
    private const string QuitSignalPrefix = "Local\\Pancake.SingleInstance.Quit.";

    private static string InstanceSuffix()
    {
        // 闸门按数据目录区分：路径大小写不参与命名，避免同一副本因大小写差异被当成两份安装。
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data")).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)), 0, 8);
    }
}
