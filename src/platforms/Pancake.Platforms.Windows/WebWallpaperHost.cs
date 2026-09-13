using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Windows;

/// <summary>
/// Windows 网页壁纸宿主：用系统 WebView2 渲染本地网页项目。
/// 页面通过虚拟主机名访问项目目录，禁止跨源、导航、下载、弹窗与权限请求，
/// 也不注入任何原生对象。
/// </summary>
public sealed class WindowsWebWallpaperHost : IWebWallpaperHost
{
    public bool IsSupported => true;

    public Control? TryCreate(string packageDirectory, string entryRelativePath) =>
        new WebView2WallpaperControl(packageDirectory, entryRelativePath);

    public bool IsReady(Control host) => host is WebView2WallpaperControl { IsReady: true };
}

/// <summary>
/// 把 WebView2 挂到 Avalonia 的原生控件宿主上。
/// Avalonia 要求由控件自己创建承载用的子窗口，WebView2 再把这个子窗口当作渲染面。
/// </summary>
internal sealed class WebView2WallpaperControl : NativeControlHost, IDisposable
{
    private const string WindowClassName = "PancakeWebWallpaperHost";
    private static readonly object ClassGate = new();
    private static bool _classRegistered;
    private static WndProcDelegate? _wndProc;

    private readonly string _packageDirectory;
    private readonly string _entryRelativePath;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private nint _hwnd;
    private bool _started;
    private bool _disposed;

    /// <summary>页面是否已成功加载；宿主据此决定是否降级回图片背景。</summary>
    public bool IsReady { get; private set; }

    public WebView2WallpaperControl(string packageDirectory, string entryRelativePath)
    {
        _packageDirectory = packageDirectory;
        _entryRelativePath = entryRelativePath;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>网页壁纸加载失败时触发，宿主据此降级回图片背景。</summary>
    public event Action? Failed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        EnsureWindowClass();
        // 子窗口先建出来交给 WebView2，控制器就绪后再调整到控件大小。
        _hwnd = CreateWindowEx(0, WindowClassName, string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height),
            parent.Handle, 0, 0, 0);
        if (_hwnd == 0) throw new InvalidOperationException("无法创建网页壁纸宿主窗口。");
        _ = InitializeAsync();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Dispose();
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyBounds();
    }

    /// <summary>WebView2 用原始像素设置渲染区域，因此要乘上窗口的缩放比例。</summary>
    private void ApplyBounds()
    {
        if (_controller is null) return;
        double scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1;
        _controller.Bounds = new System.Drawing.Rectangle(
            0,
            0,
            (int)Math.Round(Bounds.Width * scaling),
            (int)Math.Round(Bounds.Height * scaling));
    }

    private async Task InitializeAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        try
        {
            string cache = Path.Combine(AppContext.BaseDirectory, "data", "webview-cache");
            Directory.CreateDirectory(cache);
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, cache);
            if (_disposed || _hwnd == 0) return;
            _controller = await environment.CreateCoreWebView2ControllerAsync(_hwnd);
            if (_disposed) return;
            _core = _controller.CoreWebView2;
            _controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
            _controller.IsVisible = true;
            ApplyBounds();

            CoreWebView2Settings settings = _core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            _core.IsMuted = true;
            _core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            _core.NewWindowRequested += (_, args) => args.Handled = true;
            _core.DownloadStarting += (_, args) => args.Cancel = true;
            _core.ProcessFailed += (_, _) => Failed?.Invoke();

            // 只允许访问本地虚拟主机，其它导航一律取消。
            string host = "w-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_packageDirectory)))[..16].ToLowerInvariant() + ".pancake.invalid";
            _core.SetVirtualHostNameToFolderMapping(host, _packageDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            _core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" || uri.Host != host)
                {
                    args.Cancel = true;
                }
            };
            _core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) IsReady = true;
                else Failed?.Invoke();
            };
            await _core.AddScriptToExecuteOnDocumentCreatedAsync(CompatibilityScript);
            string relative = string.Join("/", _entryRelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString));
            _core.Navigate($"https://{host}/{relative}");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException or DllNotFoundException)
        {
            Failed?.Invoke();
        }
    }

    /// <summary>只注册一次窗口类；WndProc 交给系统默认处理。</summary>
    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered) return;
            _wndProc = static (hwnd, message, wParam, lParam) => DefWindowProc(hwnd, message, wParam, lParam);
            WndClassEx windowClass = new()
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = WindowClassName
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                throw new InvalidOperationException("无法注册网页壁纸窗口类。");
            }

            _classRegistered = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _core = null;
        _controller?.Close();
        _controller = null;
    }

    /// <summary>
    /// 与旧版一致的兼容脚本：补上 Wallpaper Engine 的宿主回调占位，
    /// 并提供暂停/继续接口，不向页面暴露任何原生对象。
    /// </summary>
    private const string CompatibilityScript = """
        (() => {
          window.wallpaperRegisterAudioListener = callback => callback(new Array(128).fill(0));
          for (const name of ['wallpaperRegisterMediaPropertiesListener', 'wallpaperRegisterMediaPlaybackListener',
            'wallpaperRegisterMediaTimelineListener', 'wallpaperRegisterMediaThumbnailListener']) window[name] = () => {};
          window.__pancakeSetPaused = paused => {
            window.wallpaperPropertyListener?.setPaused?.(paused);
            if (paused) {
              document.querySelectorAll('video,audio').forEach(media => media.pause());
            }
          };
        })();
        """;

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    private delegate nint WndProcDelegate(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
