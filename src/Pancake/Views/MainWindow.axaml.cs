using Avalonia;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Platforms.Abstraction;
using Pancake.Services;
using Pancake.ViewModels;

namespace Pancake.Views;

/// <summary>
/// 主窗口：顶部栏、看板区（时钟与作业区）、自由布局组件与底部控制窗。
/// 平台差异全部通过 PlatformServices 访问，窗口本身不引用任何系统 API。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    // 全屏退出提示：手势触发后短暂高亮「退出全屏」按钮，超时自动恢复。
    private readonly DispatcherTimer _fullScreenHintTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private readonly Dictionary<SubjectBoard, SubjectTileControl> _tiles = [];
    private readonly AutofillPopup _autofillPopup = new();
    private static readonly IBrush FullScreenHintBrush =
        new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private bool _isFullScreen;
    private bool _isEditing;
    private bool _isLoaded;
    private bool _isTileInteracting;
    private bool _showFullScreenExitHint;
    private IPointer? _gesturePointer;
    private Point _gestureStart;
    // 记录已绘制的网格尺寸，避免尺寸未变化时重复重建网格线。
    private double _renderedGridWidth;
    private double _renderedGridHeight;

    /// <summary>XAML 预览与手工构建使用的无参构造。</summary>
    public MainWindow() : this(new MainViewModel(), new AppLaunchOptions(true, "display"))
    {
    }

    public MainWindow(MainViewModel viewModel, AppLaunchOptions options)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        _clockTimer.Tick += (_, _) => UpdateClock();
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _viewModel.SaveToDisk();
        };

        Opened += (_, _) => OnWindowOpened(options);
        // 设置页可用宽度变化时重新决定磁贴预览挂在页内还是右侧固定区。
        SettingsContentScrollViewer.SizeChanged += (_, _) => UpdateTilePreviewHosting();
        // 窗口材质：支持的平台上启用 Mica／Acrylic，并在实际材质变化时重新决定半透明范围。
        PlatformServices.Backdrop.Apply(this, PlatformServices.Backdrop.IsSupported);
        // 全屏查看模式下在看板上拖动时提示退出方式；用隧道事件保证子控件不吞掉手势。
        AddHandler(PointerPressedEvent, RootShell_GlobalPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, RootShell_GlobalPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, RootShell_GlobalPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _fullScreenHintTimer.Tick += (_, _) =>
        {
            _fullScreenHintTimer.Stop();
            HideFullScreenExitHint();
        };
        // 最小化暂停检测依赖窗口状态变化。
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty && _isLoaded) StartNoiseMonitoring();
            // 实际窗口材质由平台稍后上报，变化时重新决定设置页要不要半透明。
            if (args.Property == TopLevel.ActualTransparencyLevelProperty) UpdateBackdropSurfaces();
        };
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            // 退出前停止采集与播放，避免音频线程继续回调已关闭的窗口。
            _noiseCapture.SamplesAvailable -= NoiseSamplesAvailable;
            _noiseCapture.CaptureFailed -= NoiseCaptureFailed;
            StopNoiseMonitoring();
            _noiseAlert.Dispose();
            // 毛玻璃背衬的定时刷新与快照一起收掉，避免窗口关闭后继续拍摄。
            StopToolbarBackdropTimer();
            _toolbarBackdropBitmap?.Dispose();
            _toolbarBackdropBitmap = null;
            _viewModel.SaveToDisk();
#if PANCAKE_UI_TESTS
            // 自检会改动控制窗设置并触发保存，退出前把验证前的项目与设置写回。
            RestoreVerificationLibrary();
#endif
        };
    }

    private BoardSettingsState Settings => _viewModel.Settings;

    private double GridSize => Settings.GridSize > 0 ? Settings.GridSize : 48;

    private bool IsGridSnappingEnabled => Settings.GridSnappingEnabled;







    private void OnWindowOpened(AppLaunchOptions options)
    {
        _isLoaded = true;
        ApplyTheme();
        BuildTiles();
        BuildPenToolbar();
        BuildProjectCommands();
        InitializeBoardZoom();
        InitializeIslands();
        // 候选浮层需要一个可视树挂点，打开时它是独立窗口。
        _autofillPopup.Attach(this.FindControl<Canvas>("PopupHost")!);
        StartWeather();
        // 采集与报警事件只订阅一次，窗口存活期间始终有效。
        _noiseCapture.SamplesAvailable += NoiseSamplesAvailable;
        _noiseCapture.CaptureFailed += NoiseCaptureFailed;
        StartNoiseMonitoring();
        StartAutoUpdateCheck();
        ApplyBackgrounds();
        ApplyDisplayLayout();
        // 控制窗与浮岛的停靠位置、缩放与背景都来自设置，启动时必须先应用一次。
        ApplyToolbarAppearance();
        UpdateBackdropSurfaces();
        // 实际材质由平台在窗口创建后上报，布局稳定后再核对一次，避免窗口已显示时还是纯色。
        Dispatcher.UIThread.Post(UpdateBackdropSurfaces, DispatcherPriority.Loaded);
        UpdateClock();
        UpdateBoardBounds();
        UpdateLayoutHandles();
        _clockTimer.Start();
        if (options.StartFullScreen) SetFullScreen(true);
        if (_viewModel.LoadError is { } loadError)
        {
            // 数据损坏时已经退回空项目，这里只提示一次，避免用户以为数据被静默丢弃。
            Dispatcher.UIThread.Post(() => _ = ShowMessageAsync("项目数据无法读取", $"{loadError}\n\n原文件已备份为 projects.json.corrupt-*，新建的项目不会覆盖它。", "知道了"));
        }

        if (options.View == "settings") ShowSettings();
#if PANCAKE_UI_TESTS
        if (Environment.GetCommandLineArgs().Any(argument => argument.StartsWith("--verify-", StringComparison.Ordinal)))
        {
            CaptureVerificationLibrary();
        }

        if (Environment.GetCommandLineArgs().Contains("--verify-settings-pages")) RunSettingsPageVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-export")) RunExportVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-zoom")) RunZoomVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-islands")) RunIslandVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-update")) RunUpdateVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-playlist")) RunPlaylistVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-video")) RunVideoVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-web")) RunWebWallpaperVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-richtext-island")) RunRichTextIslandVerification();
        if (Environment.GetCommandLineArgs().Contains("--verify-autolayout")) RunAutoLayoutVerification();
#endif
    }

#if PANCAKE_UI_TESTS
    /// <summary>
    /// 验证构建专用：进入编辑、聚焦某个作业编辑器，确认富文本悬浮岛出现、
    /// 工具齐全，并且贴在控制窗的左侧（横向布局下与画笔栏同槽位）。
    /// </summary>
    private void RunRichTextIslandVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            try
            {
                EnterEditing();
                if (_viewModel.Subjects.Count == 0)
                {
                    SubjectBoard subject = _viewModel.AddSubject("验证科目");
                    AddTile(subject);
                    subject.Entries.Add(new HomeworkEntry { Content = "格式验证" });
                }

                await Task.Delay(400);
                SubjectTileControl tile = _tiles.Values.First();
                RichTextEditor? editor = tile.FirstEditorForVerification;
                if (editor is null) throw new InvalidOperationException("磁贴里没有可编辑的作业条目。");

                OnEntryEditorFocused(tile, editor);
                await Task.Delay(300);
                ApplyIslandPlacement();
                await Task.Delay(200);

                bool visible = IsRichTextIslandVisible;
                int tools = RichTextIslandItems.Children.Count;
                // 与旧版一致：第一个工具是字体选择，第二个是加粗。
                // 无字模式下按钮内容会被包装成“图标 + 名称”，因此两种结构都要认。
                bool fontFirst = RichTextIslandItems.Children.Count > 0 &&
                                 IslandButtonSymbol(RichTextIslandItems.Children[0]) == nameof(FluentGlyphs.TextFont);
                bool boldSecond = RichTextIslandItems.Children.Count > 1 &&
                                  IslandButtonSymbol(RichTextIslandItems.Children[1]) == nameof(FluentGlyphs.Bold);
                Rect toolbar = FloatingToolbar.Bounds;
                bool leftOfToolbar = RichTextIsland.Bounds.Right <= toolbar.Left + 1;
                lines.Add($"visible={visible} tools={tools} fontFirst={fontFirst} boldSecond={boldSecond} island={RichTextIsland.Bounds.Left:0} toolbar={toolbar.Left:0} leftOfToolbar={leftOfToolbar}");
                if (!visible) throw new InvalidOperationException("聚焦编辑器后富文本悬浮岛没有显示。");
                if (tools != 6) throw new InvalidOperationException($"格式工具数量不是 6，而是 {tools}。");
                if (!fontFirst) throw new InvalidOperationException("悬浮岛第一个工具不是字体选择。");
                if (!boldSecond) throw new InvalidOperationException("悬浮岛第二个工具不是加粗。");
                if (!leftOfToolbar) throw new InvalidOperationException("横向布局下悬浮岛没有贴在控制窗左侧。");

                // 开启画笔后格式化工具应让位给画笔栏。
                this.FindControl<ToggleButton>("GlobalPenButton")!.IsChecked = true;
                ApplyInkMode();
                await Task.Delay(200);
                bool hiddenForPen = !IsRichTextIslandVisible;
                lines.Add($"penModeHidesIsland={hiddenForPen}");
                if (!hiddenForPen) throw new InvalidOperationException("开启画笔后富文本悬浮岛仍然显示。");

                lines.Insert(0, "RICHTEXT_ISLAND_OK");
            }
            catch (Exception ex)
            {
                lines.Insert(0, $"RICHTEXT_ISLAND_FAILED {ex.GetType().Name} {ex.Message}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "richtext-island-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：在临时目录里造一个最小的网页壁纸项目，交给平台宿主加载，
    /// 确认 WebView2 真的把页面拉起来了。平台不支持时直接报错，避免"跳过即通过"。
    /// </summary>
    private void RunWebWallpaperVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pancake-web-" + Guid.NewGuid().ToString("N"));
            try
            {
                lines.Add($"hostSupported={PlatformServices.WebWallpaper.IsSupported}");
                if (!PlatformServices.WebWallpaper.IsSupported) throw new InvalidOperationException("当前平台没有网页壁纸宿主。");

                // 项目必须位于 web-wallpapers 目录下，WebWallpaperPackage.Load 才会接受它。
                string project = System.IO.Path.Combine(root, "web-wallpapers", "sample");
                Directory.CreateDirectory(project);
                File.WriteAllText(System.IO.Path.Combine(project, "project.json"),
                    """{"type":"web","title":"验证壁纸","file":"index.html","general":{"properties":{"color":"#ff8800"}}}""");
                File.WriteAllText(System.IO.Path.Combine(project, "index.html"),
                    "<!doctype html><html><head><meta charset=\"utf-8\"><title>pancake-web-check</title></head>" +
                    "<body style=\"margin:0;background:#123456\"><h1 id=\"marker\">Pancake web wallpaper</h1></body></html>");

                WebWallpaperPackage package = WebWallpaperPackage.Load(System.IO.Path.Combine(project, "index.html"));
                Control? control = PlatformServices.WebWallpaper.TryCreate(package.Directory, package.EntryPath);
                if (control is null) throw new InvalidOperationException("宿主没有创建承载控件。");
                IslandCanvas.Children.Add(control);
                await Task.Delay(300);

                bool ready = false;
                for (int attempt = 0; attempt < 40 && !ready; attempt++)
                {
                    await Task.Delay(250);
                    ready = PlatformServices.WebWallpaper.IsReady(control);
                }

                lines.Add($"packageDir={System.IO.Path.GetFileName(package.Directory)} entry={package.EntryPath} ready={ready}");
                if (!ready) throw new InvalidOperationException("网页壁纸在 10 秒内没有完成加载。");
                IslandCanvas.Children.Remove(control);
                (control as IDisposable)?.Dispose();
                lines.Insert(0, "WEB_OK");
            }
            catch (Exception ex)
            {
                lines.Insert(0, $"WEB_FAILED {ex.GetType().Name} {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                }
                catch (IOException)
                {
                    // 临时目录清理失败不影响验证结论。
                }
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "web-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：播放一段测试视频，确认 LibVLC 原生库可用、解码真正在推进。
    /// 视频路径由 PANCAKE_VIDEO_PATH 提供（由调用方用 ffmpeg 生成）。
    /// </summary>
    private void RunVideoVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            try
            {
                string path = Environment.GetEnvironmentVariable("PANCAKE_VIDEO_PATH") ?? string.Empty;
                lines.Add($"libvlcSupported={VideoPlayerHost.IsSupported}");
                if (!VideoPlayerHost.IsSupported) throw new InvalidOperationException("LibVLC 原生库不可用。");
                if (!File.Exists(path)) throw new InvalidOperationException($"测试视频不存在：{path}");

                Controls.BackgroundVisual visual = new();
                IslandCanvas.Children.Add(visual);
                visual.Apply(new BackgroundSettings { ImagePath = path, ImageMode = "Zoom" });
                await Task.Delay(1200);
                bool playing = visual.IsVideoPlayingForVerification;
                long first = visual.VideoPositionForVerification;
                lines.Add($"playing={playing} position={first}ms");

                // 再等一会儿确认播放位置在推进（不只是打开了文件）。
                await Task.Delay(1500);
                long second = visual.VideoPositionForVerification;
                lines.Add($"positionAfter1.5s={second}ms advanced={second > first}");
                if (!playing) throw new InvalidOperationException("视频没有进入播放状态。");
                if (second <= first) throw new InvalidOperationException("视频播放位置没有推进。");

                IslandCanvas.Children.Remove(visual);
                lines.Insert(0, "VIDEO_OK");
            }
            catch (Exception ex)
            {
                lines.Insert(0, $"VIDEO_FAILED {ex.GetType().Name} {ex.Message}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "video-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>验证构建专用：生成一张纯色小图，供播放队列验证使用。</summary>
    private static void WriteSamplePng(string path, BoardColor color)
    {
        Border swatch = new()
        {
            Width = 16,
            Height = 16,
            Background = new SolidColorBrush(color.ToColor())
        };
        swatch.Measure(new Size(16, 16));
        swatch.Arrange(new Rect(0, 0, 16, 16));
        Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(new PixelSize(16, 16), new Vector(96, 96));
        bitmap.Render(swatch);
        bitmap.Save(path);
    }

    /// <summary>
    /// 验证构建专用：用三张临时图片建立背景播放队列，确认首次加载、定时切换与坏文件跳过都生效。
    /// </summary>
    private void RunPlaylistVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pancake-playlist-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                List<string> paths = [];
                for (int index = 0; index < 3; index++)
                {
                    string target = System.IO.Path.Combine(folder, $"bg{index}.png");
                    // 界面资源是嵌入资源、不在输出目录，因此直接生成三张不同颜色的临时图片。
                    WriteSamplePng(target, index switch
                    {
                        0 => BoardColor.FromRgb(200, 60, 60),
                        1 => BoardColor.FromRgb(60, 200, 90),
                        _ => BoardColor.FromRgb(60, 120, 220)
                    });
                    paths.Add(target);
                }

                Controls.BackgroundVisual visual = new();
                IslandCanvas.Children.Add(visual);
                BackgroundSettings settings = new()
                {
                    PlaylistEnabled = true,
                    Playlist = paths,
                    SwitchOnTimer = true,
                    SwitchIntervalSeconds = 1,
                    Shuffle = false
                };
                visual.Apply(settings);
                await Task.Delay(400);
                string first = visual.CurrentMediaPathForVerification;
                bool timerRunning = visual.IsTimerRunningForVerification;
                lines.Add($"first={System.IO.Path.GetFileName(first)} timer={timerRunning}");
                if (!first.EndsWith("bg0.png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("首次未加载队列第一项。");
                if (!timerRunning) throw new InvalidOperationException("定时切换未启动。");

                // 每 200 毫秒采样一次当前媒体，得到真实的切换序列。
                List<string> sequence = [System.IO.Path.GetFileName(first)];
                bool deleted = false;
                for (int tick = 0; tick < 20; tick++)
                {
                    await Task.Delay(200);
                    if (!deleted && tick >= 5)
                    {
                        // 运行到中途删掉 bg2，验证坏文件会被跳过。
                        File.Delete(paths[2]);
                        deleted = true;
                    }

                    string current = System.IO.Path.GetFileName(visual.CurrentMediaPathForVerification);
                    if (!File.Exists(visual.CurrentMediaPathForVerification)) throw new InvalidOperationException("切换到了已被删除的文件。");
                    if (sequence[^1] != current) sequence.Add(current);
                }

                lines.Add("sequence=" + string.Join(" -> ", sequence));
                if (sequence.Count < 3) throw new InvalidOperationException("定时切换没有产生多次轮播。");
                if (sequence.Contains("bg2.png")) throw new InvalidOperationException("删除后的文件仍被播放。");

                IslandCanvas.Children.Remove(visual);
                lines.Insert(0, "PLAYLIST_OK");
            }
            catch (Exception ex)
            {
                lines.Insert(0, $"PLAYLIST_FAILED {ex.GetType().Name} {ex.Message}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    // 临时目录清理失败不影响验证结论。
                }
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "playlist-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：真实调用一次发布检查；设置 PANCAKE_UPDATE_VERIFY_DOWNLOAD=1 时
    /// 继续下载更新包并在临时目录里走完校验与解压（不启动安装程序、不覆盖任何文件）。
    /// </summary>
    private void RunUpdateVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            try
            {
                ReleaseUpdateService service = new();
                ReleaseSnapshot? latest = await service.GetLatestAsync("GitHub");
                Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
                lines.Add(latest is null
                    ? "latest=none"
                    : $"latest={latest.Tag} version={latest.Version} hasAsset={latest.Update is not null} newer={ReleaseUpdateService.IsNewer(latest.Version, current)}");

                if (latest?.Update is not null && Environment.GetEnvironmentVariable("PANCAKE_UPDATE_VERIFY_DOWNLOAD") == "1")
                {
                    string dataRoot = AppPathService.ResolveDataRoot();
                    lines.Add($"downloading {latest.Update.AssetName}…");
                    string package = await service.DownloadAsync(latest.Update, dataRoot);
                    long size = new FileInfo(package).Length;
                    lines.Add($"downloaded bytes={size}");
                    if (System.IO.Path.GetExtension(package).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                        System.IO.Path.GetExtension(package).Equals(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        string normalized = await SplitUpdatePackage.NormalizeAsync(package);
                        // 在临时目录里演练解压与覆盖配置生成，绝不触碰当前程序目录。
                        string sandbox = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pancake-update-sandbox-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(sandbox);
                        string configuration = PortableUpdateService.Prepare(normalized, sandbox, System.IO.Path.Combine(sandbox, "staging"), Environment.ProcessId);
                        bool configOk = File.Exists(configuration);
                        // 配置与解压产物都在 staging 下：统计文件数确认更新包确实被解开，而不是只下载成功。
                        string staging = System.IO.Path.Combine(sandbox, "staging");
                        int unpacked = Directory.Exists(staging) ? Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length : 0;
                        lines.Add($"prepare configExists={configOk} unpackedFiles={unpacked}");
                        if (!configOk) throw new InvalidOperationException("便携更新未生成覆盖配置。");
                        if (unpacked == 0) throw new InvalidOperationException("便携更新包没有被解压。");
                    }
                }

                lines.Insert(0, "UPDATE_OK");
            }
            catch (Exception ex)
            {
                // GitHub 对匿名请求按 IP 限流（403 + rate limit）。这是外部配额问题，
                // 不是程序缺陷，也不是「已经是最新版本」——因此单独标记为跳过，
                // 其它任何失败仍然按失败上报。
                bool rateLimited = ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
                lines.Insert(0, rateLimited
                    ? $"UPDATE_SKIPPED_RATE_LIMIT {ex.GetType().Name} {ex.Message}"
                    : $"UPDATE_FAILED {ex.GetType().Name} {ex.Message}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "update-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>验证开始前的 projects.json 原文；为空表示验证前还没有数据文件。</summary>
    private string? _verificationLibraryBackup;
    private bool _verificationLibraryCaptured;

    /// <summary>验证入口启动时先记下项目与设置的原文，退出时原样写回。</summary>
    private void CaptureVerificationLibrary()
    {
        _verificationLibraryCaptured = true;
        string path = System.IO.Path.Combine(AppPathService.ResolveDataRoot(), "projects.json");
        _verificationLibraryBackup = File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>把验证前的数据文件写回，避免自检改动用户的控制窗设置。</summary>
    private void RestoreVerificationLibrary()
    {
        if (!_verificationLibraryCaptured) return;
        try
        {
            string path = System.IO.Path.Combine(AppPathService.ResolveDataRoot(), "projects.json");
            if (_verificationLibraryBackup is null)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            File.WriteAllText(path, _verificationLibraryBackup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 恢复失败只影响验证环境，不影响正式运行，因此不再向上抛出。
        }
    }

    /// <summary>取浮岛按钮的图标名：按钮内容可能是图标本身，也可能是“图标 + 名称”。</summary>
    private static string? IslandButtonSymbol(Control control) => control switch
    {
        Button { Content: FluentIcon icon } => icon.Symbol,
        Button { Content: StackPanel { Children.Count: 2 } stack } when stack.Children[0] is FluentIcon icon => icon.Symbol,
        _ => null
    };

    /// <summary>
    /// 验证构建专用：检查自动排列是否真的按内容收紧磁贴、关闭后是否保持原尺寸，
    /// 以及窄看板下是否收窄磁贴让文字换行而不是溢出。结果写入 autolayout-check.txt。
    /// </summary>
    private void RunAutoLayoutVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            try
            {
                // 固定示例，不读取项目作业：宽窄不同的内容用于观察收紧与换行。
                Settings.LayoutMode = "Board";
                Settings.InfiniteBoard = false;
                Settings.AutoLayoutGap = 0;
                Settings.GridSnappingEnabled = false;
                Settings.AutoLayoutAlign = false;
                Settings.AutoLayoutResize = true;
                // 布局模式决定作业板视口尺寸，改完要先应用再测量。
                ApplyDisplayLayout();
                ResetArrangeSamples();
                BuildTiles();
                await Task.Delay(400);

                double viewportWidth = Math.Max(BoardViewport.Bounds.Width, 600);
                double viewportHeight = Math.Max(BoardViewport.Bounds.Height, 600);
                ArrangeTiles();
                await Task.Delay(200);
                List<(double X, double Y, double Width, double Height)> fitted =
                    _viewModel.Subjects.Select(subject => (subject.X, subject.Y, subject.TileWidth, subject.TileHeight)).ToList();
                bool tightened = fitted.All(rect => rect.Width < ArrangeSampleWidth);
                bool insideBoard = fitted.All(rect =>
                    rect.X >= -1 && rect.Y >= -1 && rect.X + rect.Width <= viewportWidth + 1 && rect.Y + rect.Height <= viewportHeight + 1);
                bool noOverlap = true;
                for (int a = 0; a < fitted.Count; a++)
                for (int b = a + 1; b < fitted.Count; b++)
                {
                    Rect first = new(fitted[a].X, fitted[a].Y, fitted[a].Width, fitted[a].Height);
                    Rect second = new(fitted[b].X, fitted[b].Y, fitted[b].Width, fitted[b].Height);
                    if (first.Intersects(second)) noOverlap = false;
                }

                lines.Add($"content viewport={viewportWidth:0}x{viewportHeight:0} tightened={tightened} insideBoard={insideBoard} noOverlap={noOverlap}");
                lines.Add("widths=" + string.Join(",", fitted.Select(rect => rect.Width.ToString("0"))));
                if (!tightened) throw new InvalidOperationException("自动调整磁贴大小没有按内容收紧磁贴。");
                if (!insideBoard) throw new InvalidOperationException("自动排列后有磁贴越出作业板。");
                if (!noOverlap) throw new InvalidOperationException("自动排列后磁贴互相重叠。");

                // 关闭自动调整：位置重新排布，尺寸保持用户设置的值。
                Settings.AutoLayoutResize = false;
                foreach (SubjectBoard subject in _viewModel.Subjects)
                {
                    subject.TileWidth = 320;
                    subject.TileHeight = 220;
                }

                BuildTiles();
                await Task.Delay(300);
                ArrangeTiles();
                await Task.Delay(200);
                bool sizesKept = _viewModel.Subjects.All(subject =>
                    Math.Abs(subject.TileWidth - 320) < 0.5 && Math.Abs(subject.TileHeight - 220) < 0.5);
                lines.Add("kept=" + string.Join(",", _viewModel.Subjects.Select(subject => subject.TileWidth.ToString("0"))));
                if (!sizesKept) throw new InvalidOperationException("关闭自动调整磁贴大小后尺寸被改动了。");

                // 窄看板：收窄磁贴让文字换行，高度随之增加。
                Settings.AutoLayoutResize = true;
                ResetArrangeSamples();
                BuildTiles();
                await Task.Delay(300);
                ArrangeTiles();
                await Task.Delay(200);
                List<(double Width, double Height)> wide =
                    _viewModel.Subjects.Select(subject => (subject.TileWidth, subject.TileHeight)).ToList();
                Width = 760;
                Height = 560;
                await Task.Delay(600);
                ResetArrangeSamples();
                BuildTiles();
                await Task.Delay(300);
                ArrangeTiles();
                await Task.Delay(200);
                List<(double Width, double Height)> narrow =
                    _viewModel.Subjects.Select(subject => (subject.TileWidth, subject.TileHeight)).ToList();
                bool wrapped = false, narrower = true;
                for (int index = 0; index < narrow.Count; index++)
                {
                    if (narrow[index].Height > wide[index].Height + 1) wrapped = true;
                    if (narrow[index].Width > wide[index].Width + 1) narrower = false;
                }
                lines.Add("wide=" + string.Join(",", wide.Select(size => $"{size.Width:0}x{size.Height:0}")));
                lines.Add("narrow=" + string.Join(",", narrow.Select(size => $"{size.Width:0}x{size.Height:0}")));
                if (!narrower) throw new InvalidOperationException("窄看板下磁贴没有收窄。");
                if (!wrapped) throw new InvalidOperationException("收窄后长文本没有换行（高度没有增长）。");

                lines.Insert(0, "AUTOLAYOUT_OK");
            }
            catch (Exception ex)
            {
                string trace = ex.StackTrace ?? string.Empty;
                lines.Insert(0, $"AUTOLAYOUT_FAILED {ex.GetType().Name} {ex.Message}\n{trace[..Math.Min(trace.Length, 2000)]}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "autolayout-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>自动排列验证用的示例磁贴：尺寸故意给得比内容宽，便于观察收紧效果。</summary>
    private const double ArrangeSampleWidth = 900;

    /// <summary>把验证用的四个示例科目重新装回项目，保证每次测量起点一致。</summary>
    private void ResetArrangeSamples() => _viewModel.ReplaceSubjects(
    [
        CreateArrangeSample("语文", ["背诵《赤壁赋》第二段"]),
        CreateArrangeSample("数学", ["完成 P30 练习题", "复习二次函数公式", "订正昨天的错题。"]),
        CreateArrangeSample("英语", ["朗读课文三遍并默写 Unit 5 单词，整理今天课堂上的语法笔记。"]),
        CreateArrangeSample("物理", ["整理浮力实验报告", "预习下一节内容。"])
    ]);

    private static SubjectBoard CreateArrangeSample(string name, string[] lines)
    {
        SubjectBoard subject = new()
        {
            Name = name,
            TileWidth = ArrangeSampleWidth,
            TileHeight = 620,
            AccentHex = "#818CF8",
            IsAccentExplicit = true
        };
        foreach (string line in lines) subject.Entries.Add(new HomeworkEntry { Content = line });
        return subject;
    }

    /// <summary>
    /// 验证构建专用：检查浮岛相对控制窗的摆放方向与外观跟随，以及自动隐藏与恢复是否真的改变了可见状态。
    /// </summary>
    private void RunIslandVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> lines = [];
            try
            {
                // 启动路径必须先把保存的停靠位置应用到界面上，否则重启后控制窗会回到默认位置。
                bool startupApplied =
                    FloatingToolbar.HorizontalAlignment == ExpectedToolbarHorizontal() &&
                    FloatingToolbar.VerticalAlignment == ExpectedToolbarVertical();
                lines.Add($"startup position={Settings.ToolbarPosition} applied={startupApplied}");
                if (!startupApplied) throw new InvalidOperationException("启动时没有应用保存的控制窗停靠位置。");

                // 横置控制窗：画笔栏应在左、缩放岛应在右。
                Settings.ToolbarPosition = "BottomCenter";
                ApplyToolbarAppearance();
                PenToolbar.IsVisible = true;
                ZoomIsland.IsVisible = true;
                await Task.Delay(300);
                ApplyIslandPlacement();
                await Task.Delay(200);
                Rect toolbar = FloatingToolbar.Bounds;
                bool leftOk = PenToolbar.Bounds.Right <= toolbar.Left + 1;
                bool rightOk = ZoomIsland.Bounds.Left >= toolbar.Right - 1;
                lines.Add($"horizontal toolbar={toolbar.Left:0},{toolbar.Top:0} pen={PenToolbar.Bounds.Left:0} zoom={ZoomIsland.Bounds.Left:0} leftOk={leftOk} rightOk={rightOk}");
                if (!leftOk || !rightOk) throw new InvalidOperationException("横置控制窗时浮岛未贴在两侧。");

                // 竖置控制窗：画笔栏应在上、缩放岛应在下。
                Settings.ToolbarPosition = "CenterRight";
                ApplyToolbarAppearance();
                await Task.Delay(300);
                ApplyIslandPlacement();
                await Task.Delay(200);
                toolbar = FloatingToolbar.Bounds;
                bool aboveOk = PenToolbar.Bounds.Bottom <= toolbar.Top + 1;
                bool belowOk = ZoomIsland.Bounds.Top >= toolbar.Bottom - 1;
                lines.Add($"vertical toolbar={toolbar.Left:0},{toolbar.Top:0} penBottom={PenToolbar.Bounds.Bottom:0} zoomTop={ZoomIsland.Bounds.Top:0} aboveOk={aboveOk} belowOk={belowOk}");
                if (!aboveOk || !belowOk) throw new InvalidOperationException("竖置控制窗时浮岛未改为上下摆放。");

                // 外观跟随：圆角与颜色层与控制窗一致，且竖版控制窗下按钮改成竖排。
                Settings.ToolbarRadius = 22;
                Settings.ToolbarIconOnly = true;
                ApplyToolbarAppearance();
                await Task.Delay(200);
                bool radiusOk = PenToolbar.CornerRadius.TopLeft == FloatingToolbar.CornerRadius.TopLeft &&
                                RichTextIsland.CornerRadius.TopLeft == FloatingToolbar.CornerRadius.TopLeft;
                bool backgroundOk = ReferenceEquals(PenToolbarTint.Background, ToolbarTint.Background);
                bool verticalItems = PenToolbarItems.Orientation == Orientation.Vertical &&
                                     ZoomIslandItems.Orientation == Orientation.Vertical;
                lines.Add($"appearance radius={FloatingToolbar.CornerRadius.TopLeft:0} radiusOk={radiusOk} backgroundOk={backgroundOk} verticalItems={verticalItems}");
                if (!radiusOk || !backgroundOk) throw new InvalidOperationException("浮岛外观没有跟随控制窗。");
                if (!verticalItems) throw new InvalidOperationException("竖版控制窗下浮岛按钮没有改成竖排。");

                // 竖版控制窗：两块浮岛与控制窗等宽（横置时等高），截图里看起来是同一套面板。
                Settings.ToolbarScale = 1.25;
                ApplyToolbarAppearance();
                await Task.Delay(300);
                ApplyIslandPlacement();
                await Task.Delay(200);
                // 浮岛按缩放后的实际占位对齐，因此基准取缩放后的控制窗截面尺寸。
                double expectedCross = ToolbarRenderedBounds().Width / 1.25;
                bool widthMatched = Math.Abs(ZoomIsland.Width - expectedCross) < 1.5 &&
                                    Math.Abs(RichTextIsland.Width - expectedCross) < 1.5;
                bool heightAuto = double.IsNaN(ZoomIsland.Height) && double.IsNaN(RichTextIsland.Height);
                lines.Add($"crossSize vertical expected={expectedCross:0.#} zoom={ZoomIsland.Width:0.#} rich={RichTextIsland.Width:0.#} ok={widthMatched && heightAuto}");
                if (!widthMatched) throw new InvalidOperationException("竖版控制窗下浮岛没有与控制窗等宽。");
                if (!heightAuto) throw new InvalidOperationException("竖版控制窗下浮岛高度不应被固定。");

                Settings.ToolbarPosition = "BottomCenter";
                Settings.ToolbarScale = 1;
                ApplyToolbarAppearance();
                await Task.Delay(300);
                double expectedHeight = ToolbarRenderedBounds().Height;
                bool heightMatched = Math.Abs(ZoomIsland.Height - expectedHeight) < 1.5 &&
                                     Math.Abs(RichTextIsland.Height - expectedHeight) < 1.5;
                bool widthAuto = double.IsNaN(ZoomIsland.Width) && double.IsNaN(RichTextIsland.Width);
                lines.Add($"crossSize horizontal expected={expectedHeight:0.#} zoom={ZoomIsland.Height:0.#} rich={RichTextIsland.Height:0.#} ok={heightMatched && widthAuto}");
                if (!heightMatched) throw new InvalidOperationException("横置控制窗下浮岛没有与控制窗等高。");
                if (!widthAuto) throw new InvalidOperationException("横置控制窗下浮岛宽度不应被固定。");
                Settings.ToolbarPosition = "CenterRight";
                ApplyToolbarAppearance();
                await Task.Delay(200);

                // 无字模式：按钮只留图标；关闭后名称显示在图标下方。
                bool iconOnlyOk = PenToolbarItems.Children[0] is Button { Content: StackPanel { Children.Count: 2 } iconOnlyStack } &&
                                  iconOnlyStack.Children[1] is TextBlock { IsVisible: false };
                Settings.ToolbarIconOnly = false;
                ApplyToolbarAppearance();
                bool labelOk = PenToolbarItems.Children[0] is Button { Content: StackPanel { Children.Count: 2 } labeledStack } &&
                               labeledStack.Children[1] is TextBlock { IsVisible: true } label &&
                               label.Text is { Length: > 0 };
                lines.Add($"labels iconOnlyOk={iconOnlyOk} labelOk={labelOk}");
                if (!iconOnlyOk || !labelOk) throw new InvalidOperationException("浮岛按钮的无字模式没有生效。");

                // 毛玻璃：开启后控制窗与浮岛都有背衬快照，关闭后背衬被撤掉。
                Settings.ToolbarGlass = true;
                Settings.ToolbarBlur = 40;
                ApplyToolbarAppearance();
                await Task.Delay(400);
                bool glassOk = ToolbarBackdrop.Snapshot is not null && PenToolbarBackdrop.Snapshot is not null &&
                               ToolbarBackdrop.Effect is BlurEffect;
                Settings.ToolbarGlass = false;
                ApplyToolbarAppearance();
                bool glassCleared = ToolbarBackdrop.Snapshot is null && PenToolbarBackdrop.Snapshot is null &&
                                    ToolbarBackdrop.Effect is null;
                lines.Add($"glass snapshotOk={glassOk} clearedOk={glassCleared}");
                if (!glassOk || !glassCleared) throw new InvalidOperationException("控制窗毛玻璃没有正确启用或撤下。");

                // 自动隐藏：1 秒无操作后淡出，任何操作立即恢复。
                Settings.ToolbarAutoHide = true;
                Settings.ToolbarAutoHideSeconds = 1;
                Settings.ToolbarHideAnimation = "Fade";
                RegisterActivity();

                // 打开的弹出层算作“正在操作”：菜单或取色器开着时不能把控制窗藏起来。
                Flyout probe = new() { Content = new TextBlock { Text = "验证弹层" } };
                probe.ShowAt(FloatingToolbar);
                await Task.Delay(1600);
                bool popupDetected = HasOpenPopup();
                bool keptByPopup = !_toolbarHidden && FloatingToolbar.Opacity == 1;
                int dismissLayers = this.GetVisualDescendants().OfType<LightDismissOverlayLayer>().Count();
                lines.Add($"popupGuard detected={popupDetected} visible={keptByPopup} dismissLayers={dismissLayers}");
                probe.Hide();
                if (!popupDetected) throw new InvalidOperationException("没有检测到打开的弹出层。");
                if (!keptByPopup) throw new InvalidOperationException("弹出层打开时控制窗被自动隐藏了。");

                RegisterActivity();
                await Task.Delay(1600);
                bool hidden = _toolbarHidden && FloatingToolbar.Opacity == 0;
                lines.Add($"autoHide hidden={hidden} opacity={FloatingToolbar.Opacity:0.00}");
                if (!hidden) throw new InvalidOperationException("空闲超过设定时长后控制窗没有隐藏。");
                RegisterActivity();
                bool restored = !_toolbarHidden && FloatingToolbar.Opacity == 1;
                lines.Add($"restore visible={restored} opacity={FloatingToolbar.Opacity:0.00}");
                if (!restored) throw new InvalidOperationException("操作后控制窗没有恢复显示。");

                Settings.ToolbarAutoHide = false;

                // 退出提示的自动收起由调度器定时器驱动：真实窗口里定时器会跑，这里验证它真的会收起。
                SetFullScreen(true);
                ShowFullScreenHintForVerification();
                await Task.Delay(200);
                bool hintShown = FullScreenHintVisibleForVerification;
                await Task.Delay(2600);
                bool hintCleared = !FullScreenHintVisibleForVerification;
                SetFullScreen(false);
                lines.Add($"fullScreenHintTimer shown={hintShown} cleared={hintCleared}");
                if (!hintShown) throw new InvalidOperationException("退出提示没有展开。");
                if (!hintCleared) throw new InvalidOperationException("退出提示没有自动收起。");

                lines.Insert(0, "ISLANDS_OK");
            }
            catch (Exception ex)
            {
                string trace = ex.StackTrace ?? string.Empty;
                lines.Insert(0, $"ISLANDS_FAILED {ex.GetType().Name} {ex.Message}\n{trace[..Math.Min(trace.Length, 2000)]}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "islands-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：把作业板缩放到若干档位，检查滚动范围随比例同步变化，
    /// 并把每档的记录写入 zoom-check.txt。
    /// </summary>
    private void RunZoomVerification()
    {
        Dispatcher.UIThread.Post(() =>
        {
            List<string> lines = [];
            try
            {
                foreach (double zoom in new[] { 0.2, 1, 2.5, 4 })
                {
                    SetBoardZoom(zoom);
                    UpdateBoardBounds();
                    double expected = BoardCanvas.Width * _boardZoom;
                    bool matches = Math.Abs(BoardViewport.Width - expected) < 1;
                    // 放大后滚动范围必须大于可视区域，否则用户看不到超出视口的部分。
                    bool scrollable = zoom <= 1 || BoardViewport.Width > BoardScroller.Viewport.Width + 1;
                    lines.Add($"{zoom * 100:0}% extent={BoardViewport.Width:0} expected={expected:0} match={matches} scrollable={scrollable}");
                    if (!matches) throw new InvalidOperationException($"缩放 {zoom} 后滚动范围未同步。");
                    if (!scrollable) throw new InvalidOperationException($"缩放 {zoom} 后内容超出视口却无法滚动。");
                }

                SetBoardZoom(1);
                // 双指捏合：构造真实手势事件走同一条处理链，确认缩放按手势比例变化。
                Settings.LayoutMode = "Split";
                BoardWorkspace.IsVisible = true;
                ApplyDisplayLayout();
                double before = BoardZoom;
                BoardScroller.RaiseEvent(new PinchEventArgs(1.5, new Point(120, 120)));
                double after = BoardZoom;
                lines.Add($"pinch {before:0.00} -> {after:0.00} ratio={after / before:0.00}");
                if (Math.Abs(after - before * 1.5) > 0.05) throw new InvalidOperationException("双指捏合没有按手势比例缩放作业板。");
                SetBoardZoom(1);
                lines.Insert(0, "ZOOM_OK");
            }
            catch (Exception ex)
            {
                lines.Insert(0, $"ZOOM_FAILED {ex.GetType().Name} {ex.Message}");
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "zoom-check.txt"), string.Join("\n", lines));
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：用当前项目（没有项目时用临时项目）渲染一次 800×450 的 PNG，
    /// 并把实际像素尺寸与文件大小写入 export-check.txt，确认导出链路真的产出图片。
    /// </summary>
    private void RunExportVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            string result;
            try
            {
                ProjectDocument document = _viewModel.SnapshotCurrentProject() ?? new ProjectDocument { Name = "导出验证" };
                if (document.Subjects.Count == 0)
                {
                    document.Subjects.Add(new SubjectState
                    {
                        Name = "验证科目",
                        Width = 430,
                        Height = 320,
                        InkCoordinateVersion = 1,
                        AccentHex = "#65D46E",
                        IsAccentExplicit = true,
                        Entries = [new HomeworkState { Content = "导出验证内容" }]
                    });
                }

                Grid overlay = this.FindControl<Grid>("ExportOverlay")!;
                Pancake.Controls.ExportView view = new(document, Settings, this, () => { });
                overlay.Children.Add(view);
                overlay.IsVisible = true;
                view.SetCanvasForVerification(800, 450);
                // 标题字体：确认候选列表包含随包字体，并且切换到系统字体后仍能正常排版导出。
                IReadOnlyList<string> fonts = view.TitleFontsForVerification;
                if (fonts.Count == 0) throw new InvalidOperationException("标题字体候选列表为空。");
                if (fonts[0] != FontService.FamilyName) throw new InvalidOperationException("标题字体候选首位不是随包字体。");
                // 让新加入可视树的控件先完成一次布局，再渲染。
                await Task.Delay(250);
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "export-check.png");
                view.RenderToFile(path);
                if (fonts.Count > 1)
                {
                    // 换成第一个系统字体再导出一次，确认自定义字体不会让渲染失败。
                    view.ApplyTitleFontForVerification(1);
                    await Task.Delay(150);
                    view.RenderToFile(path);
                }

                // 导出色系：预设主题色按色系换算，看板数据保持不变，切换后仍能重新导出。
                IReadOnlyList<string> before = view.ExportAccentsForVerification;
                string boardAccentBefore = _viewModel.Subjects.FirstOrDefault()?.AccentHex ?? string.Empty;
                view.ApplyPaletteForVerification(macaron: true);
                await Task.Delay(200);
                IReadOnlyList<string> after = view.ExportAccentsForVerification;
                view.RenderToFile(path);
                bool paletteChanged = before.Count == after.Count && !before.SequenceEqual(after);
                bool boardUntouched = string.Equals(
                    boardAccentBefore, _viewModel.Subjects.FirstOrDefault()?.AccentHex ?? string.Empty, StringComparison.Ordinal);
                if (!paletteChanged) throw new InvalidOperationException("切换导出色系后主题色没有变化。");
                if (!boardUntouched) throw new InvalidOperationException("导出色系改动污染了看板数据。");
                view.ApplyPaletteForVerification(macaron: false);
                await Task.Delay(150);
                view.RenderToFile(path);

                using Avalonia.Media.Imaging.Bitmap bitmap = new(path);
                FileInfo info = new(path);
                result = $"EXPORT_OK {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height} tiles={view.TileCount} bytes={info.Length} titleFonts={fonts.Count} paletteRoundTrip={paletteChanged && boardUntouched}";
            }
            catch (Exception ex)
            {
                result = $"EXPORT_FAILED {ex.GetType().Name} {ex.Message}";
            }

            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "export-check.txt"), result);
            Close();
        });
    }

    /// <summary>
    /// 验证构建专用：依次构建设置页的每一个分类，确认控件能真实创建。
    /// 结果写入程序目录的 settings-pages.txt，供自动化脚本判定。
    /// </summary>
    private void RunSettingsPageVerification()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            List<string> failures = [];
            List<string> previews = [];
            foreach (string tag in SettingsSections.Select(section => section.Tag))
            {
                try
                {
                    ShowSettings();
                    ShowSettingsSection(tag);
                    // 预览要等一次布局提交后再渲染，否则拿到的是还没排版的空场景。
                    await Task.Delay(120);
                    VerifyPreviewsForSection(tag);
                    if (tag == "AppearanceTile") previews.Add(await VerifyTilePreviewHostingAsync());
                    previews.AddRange(RenderPreviewsForVerification(tag));
                }
                catch (Exception ex)
                {
                    failures.Add($"{tag}: {ex.GetType().Name} {ex.Message}");
                }
            }

            string result = failures.Count == 0
                ? $"ALL_SECTIONS_OK previews={string.Join(",", previews)} " + BackdropSummaryForVerification()
                : string.Join("\n", failures);
            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "settings-pages.txt"), result);
            Close();
        });
    }

    /// <summary>
    /// 把当前分类的预览渲染成 PNG 留作证据，并返回“种类=元素数”的摘要。
    /// 元素数为 0 说明预览是空的，这时由调用方按失败处理。
    /// </summary>
    private List<string> RenderPreviewsForVerification(string tag)
    {
        string[] kinds = tag switch
        {
            "Layout" => ["Layout"],
            "AppearanceTile" => ["Tile"],
            "AppearanceGrid" => ["Grid"],
            "AppearanceToolbar" => ["Toolbar"],
            "AppearanceBackground" => ["Shared", "Clock", "Board"],
            _ => []
        };
        List<string> summary = [];
        foreach (string kind in kinds)
        {
            if (!_appearancePreviews.TryGetValue(kind, out Grid? scene)) continue;
            int descendants = scene.GetLogicalDescendants().Count();
            if (descendants == 0) throw new InvalidOperationException($"{kind} 预览里没有任何元素。");
            // 磁贴预览在宽窗口下会铺满右侧固定区，这里按场景自身的排版尺寸渲染，
            // 缩略图里看到的比例和用户看到的一致。
            Size sceneSize = scene.Bounds.Size;
            if (sceneSize.Width < 1 || sceneSize.Height < 1)
            {
                sceneSize = new Size(PreviewSceneWidth, PreviewSceneHeight);
            }

            RenderTargetBitmap bitmap = new(
                new PixelSize(Math.Max(1, (int)sceneSize.Width), Math.Max(1, (int)sceneSize.Height)),
                new Vector(96, 96));
            bitmap.Render(scene);
            string path = System.IO.Path.Combine(AppContext.BaseDirectory, $"preview-{kind}.png");
            bitmap.Save(path);
            // 文件大小可以区分“真的画出内容”和“只有纯色底”；
            // 例如 640×300 的纯色 PNG 只有几百字节。
            long size = new FileInfo(path).Length;
            if (size < 1024) throw new InvalidOperationException($"{kind} 预览渲染出来几乎是空白（{size} 字节）。");
            summary.Add($"{kind}:{descendants}/{size}");
        }

        return summary;
    }

    /// <summary>
    /// 验证磁贴预览的固定行为：宽窗口时搬到设置页右侧固定区并铺满，
    /// 窄窗口时回到「标题大小」上方的页内位置，两次都共用同一个场景。
    /// </summary>
    private async Task<string> VerifyTilePreviewHostingAsync()
    {
        if (!_appearancePreviews.TryGetValue("Tile", out Grid? scene) || _tilePreviewInlineFrame is null)
        {
            throw new InvalidOperationException("磁贴预览还没有创建。");
        }

        double originalWidth = Width;
        try
        {
            Width = 1680;
            await Task.Delay(500);
            UpdateTilePreviewHosting();
            bool sticky = SettingsPreviewHost.IsVisible && ReferenceEquals(scene.Parent, SettingsPreviewHost) &&
                          !_tilePreviewInlineFrame.IsVisible;
            if (!sticky) throw new InvalidOperationException("宽窗口下磁贴预览没有固定到设置页右侧。");

            Width = 1000;
            await Task.Delay(500);
            UpdateTilePreviewHosting();
            bool inline = !SettingsPreviewHost.IsVisible && _tilePreviewInlineFrame.IsVisible &&
                          ReferenceEquals(scene.Parent, _tilePreviewInlineViewbox);
            if (!inline) throw new InvalidOperationException(
                $"窄窗口下磁贴预览没有回到页内位置（host={SettingsPreviewHost.IsVisible} " +
                $"inline={_tilePreviewInlineFrame.IsVisible} parent={scene.Parent?.GetType().Name ?? "null"} " +
                $"root={SettingsRoot.Bounds.Width:0} content={SettingsContentScrollViewer.Bounds.Width:0}）。");
            return "TileHosting:sticky+inline";
        }
        finally
        {
            Width = originalWidth;
            await Task.Delay(400);
            UpdateTilePreviewHosting();
        }
    }

    /// <summary>
    /// 验证设置页预览：每个带预览的页面都要真的画出内容，
    /// 布局预览的角标要跟着网格大小变，控制窗预览要跟着停靠位置变。
    /// </summary>
    private void VerifyPreviewsForSection(string tag)
    {
        string[] expected = tag switch
        {
            "Layout" => ["Layout"],
            "AppearanceTile" => ["Tile"],
            "AppearanceGrid" => ["Grid"],
            "AppearanceToolbar" => ["Toolbar"],
            "AppearanceBackground" => ["Shared", "Clock", "Board"],
            _ => []
        };
        foreach (string kind in expected)
        {
            if (!_appearancePreviews.TryGetValue(kind, out Grid? scene) || scene.Children.Count == 0)
            {
                throw new InvalidOperationException($"{tag} 的 {kind} 预览没有内容。");
            }
        }

        if (tag == "Layout")
        {
            Grid scene = _appearancePreviews["Layout"];
            string before = PreviewChipText(scene);
            double original = Settings.GridSize;
            try
            {
                // 改网格大小应当立刻反映到预览角标上，说明刷新链路真的接通了。
                Settings.GridSize = original >= 120 ? 48 : 120;
                RefreshAppearancePreviews();
                string after = PreviewChipText(scene);
                if (after == before || !after.Contains($"{Settings.GridSize:0.#}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"布局预览没有跟随网格大小刷新（before={before} after={after}）。");
                }
            }
            finally
            {
                Settings.GridSize = original;
                RefreshAppearancePreviews();
            }
        }

        if (tag == "AppearanceToolbar")
        {
            Grid scene = _appearancePreviews["Toolbar"];
            string position = Settings.ToolbarPosition;
            try
            {
                // 停靠位置变化必须重建预览：控制窗在预览里的位置要跟着走。
                Settings.ToolbarPosition = position == "TopLeft" ? "BottomRight" : "TopLeft";
                RefreshAppearancePreviews();
                Control? toolbar = FirstPreviewToolbar(scene);
                if (toolbar is null) throw new InvalidOperationException("控制窗预览里没有控制窗。");
                double left = Canvas.GetLeft(toolbar);
                double top = Canvas.GetTop(toolbar);
                bool matches = Settings.ToolbarPosition == "TopLeft"
                    ? left <= Settings.ToolbarHorizontalInset + 1 && top <= Settings.ToolbarVerticalInset + 1
                    : left > Settings.ToolbarHorizontalInset + 1 || top > Settings.ToolbarVerticalInset + 1;
                if (!matches)
                {
                    throw new InvalidOperationException(
                        $"控制窗预览没有跟随停靠位置刷新（position={Settings.ToolbarPosition} left={left:0} top={top:0}）。");
                }
            }
            finally
            {
                Settings.ToolbarPosition = position;
                RefreshAppearancePreviews();
            }
        }
    }

    /// <summary>取布局预览里的角标文字：网格大小 + 自动排列间隔。</summary>
    /// <summary>窗口材质状态：平台是否支持、实际生效的材质，以及设置页是否切成了半透明。</summary>
    private string BackdropSummaryForVerification()
    {
        // 先按当前状态重新判定一次，报告里反映的就是界面此刻的真实底色。
        UpdateBackdropSurfaces();
        IBrush? theme = ResolveThemeBackgroundBrush();
        bool translucent = SettingsRoot.Background is SolidColorBrush { Opacity: < 1 };
        string themeHex = theme is ISolidColorBrush solid ? solid.Color.ToString() : "null";
        // 有材质就必须透出材质，没有材质就必须是纯色：两者不一致说明底色判定有问题。
        bool material = PlatformServices.Backdrop.IsSupported && ActualTransparencyLevel != WindowTransparencyLevel.None;
        if (translucent != material) throw new InvalidOperationException(
            $"窗口材质与设置页底色不一致（material={material} translucent={translucent} level={ActualTransparencyLevel}）。");
        if (theme is null) throw new InvalidOperationException("取不到主题底色画刷。");
        return $"backdrop={PlatformServices.Backdrop.IsSupported}/{ActualTransparencyLevel}/translucent={translucent}/theme={themeHex}";
    }

    private static string PreviewChipText(Grid scene) => string.Join("|",
        scene.GetLogicalDescendants().OfType<TextBlock>()
            .Select(text => text.Text ?? string.Empty)
            .Where(text => text.Contains("网格", StringComparison.Ordinal) ||
                           text.Contains("自动排列", StringComparison.Ordinal)));

    /// <summary>取控制窗预览里的迷你控制窗边框。</summary>
    private static Control? FirstPreviewToolbar(Grid scene) =>
        scene.GetLogicalDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Child is StackPanel { Children.Count: > 0 } buttons &&
                                      buttons.Children[0] is Button);
#endif

    private void UpdateClock()
    {
        DateTime now = DateTime.Now;
        this.FindControl<TextBlock>("MainTimeText")!.Text = now.ToString("HH:mm");
        this.FindControl<TextBlock>("SecondsText")!.Text = now.ToString("ss");
        this.FindControl<TextBlock>("ClockDateText")!.Text =
            $"{now:yyyy 年 M 月 d 日} {GetChineseWeekday(now.DayOfWeek)}";
        this.FindControl<TextBlock>("TopDateText")!.Text = now.ToString("yyyy-MM-dd");
        this.FindControl<TextBlock>("ProjectNameText")!.Text = _viewModel.ProjectName;
    }

    private static string GetChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    /// <summary>
    /// 窗口材质与设置页底色。Windows 上启用 Mica（不支持时系统回退 Acrylic／Blur），
    /// 此时窗口底色透明、设置页半透明，材质因此透出来；其它平台或系统没有可用材质时一律使用纯色背景。
    /// 看板区域始终由自己的背景层覆盖，观感与材质无关。
    /// </summary>
    private void UpdateBackdropSurfaces()
    {
        IBrush? theme = ResolveThemeBackgroundBrush();
        // 实际材质由平台决定：系统没有可用材质时（None）继续使用纯色背景。
        bool material = PlatformServices.Backdrop.IsSupported &&
                        ActualTransparencyLevel != WindowTransparencyLevel.None;
        if (material && theme is ISolidColorBrush solid)
        {
            Background = Brushes.Transparent;
            // 保留主题色只降低不透明度：文字对比度基本不变，同时能看出系统材质。
            SettingsRoot.Background = new SolidColorBrush(solid.Color) { Opacity = 0.86 };
            return;
        }

        Background = theme;
        SettingsRoot.Background = theme;
    }

    /// <summary>
    /// 按当前实际主题取看板底色画刷。主题字典挂在应用资源上，必须显式带上主题变体查询，
    /// 否则拿不到 ThemeDictionaries 里的颜色；取不到时返回 null，由调用方保持纯色。
    /// </summary>
    private IBrush? ResolveThemeBackgroundBrush()
    {
        if (Application.Current?.TryGetResource("BoardBackgroundBrush", ActualThemeVariant, out object? value) == true &&
            value is IBrush brush)
        {
            return brush;
        }

        return Application.Current?.TryGetResource("BoardBackgroundBrush", null, out object? fallback) == true
            ? fallback as IBrush
            : null;
    }

    /// <summary>主题与色系：深色、浅色、跟随系统，并同步核心层主题状态。</summary>
    private void ApplyTheme()
    {
        bool light = Settings.Theme switch
        {
            "Light" => true,
            "Default" => SystemTheme.IsSystemLightTheme(),
            _ => false
        };
        BoardTheme.IsLight = light;
        RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        ColorPalette.IsMacaron = Settings.Palette == "Macaron";
        // 主题切换后按新主题重新取底色，材质生效时继续保持半透明。
        UpdateBackdropSurfaces();
    }

    private void BuildTiles()
    {
        BoardCanvas.Children.Clear();
        _tiles.Clear();
        foreach (SubjectBoard subject in _viewModel.Subjects) AddTile(subject);
        UpdateBoardBounds();
    }

    private void AddTile(SubjectBoard subject)
    {
        SubjectTileControl tile = new(
            subject,
            DeleteSubject,
            LayoutChanged,
            LayoutCommitted,
            TileInteractionChanged,
            () => ScheduleSave(),
            AddAttachmentAsync,
            _viewModel.Autofill,
            _autofillPopup);
        _tiles[subject] = tile;
        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        tile.SetEditing(_isEditing);
        tile.EntryEditorFocused += OnEntryEditorFocused;
        tile.ApplyBackground(Settings.TileBackground);
        tile.ApplyTitleSize(Settings.TileTitleSize);
        BoardCanvas.Children.Add(tile);
    }

    private SubjectTileControl? FindTile(SubjectBoard subject) =>
        _tiles.TryGetValue(subject, out SubjectTileControl? tile) ? tile : null;

    /// <summary>
    /// 拖动或缩放磁贴时同步模型坐标：查看模式下关闭网格吸附会显示对齐辅助线，
    /// 吸附结果直接写回模型，保证松手前后看到的位置一致。
    /// </summary>
    private void LayoutChanged(SubjectBoard subject)
    {
        ClampSubjectToViewport(subject);
        if (FindTile(subject) is not { } tile) return;
        if (!IsGridSnappingEnabled && tile.IsMoving)
        {
            SnapResult snap = BoardLayout.Snap(
                new LayoutRect(subject.X, subject.Y, subject.TileWidth, subject.TileHeight),
                _viewModel.Subjects
                    .Where(other => !ReferenceEquals(other, subject))
                    .Select(other => new LayoutRect(other.X, other.Y, other.TileWidth, other.TileHeight)),
                BoardCanvas.Width,
                BoardCanvas.Height);
            subject.X = snap.X;
            subject.Y = snap.Y;
            AlignmentGuides.Draw(AlignmentCanvas, snap, BoardCanvas.Width, BoardCanvas.Height);
        }

        Canvas.SetLeft(tile, subject.X);
        Canvas.SetTop(tile, subject.Y);
        tile.ApplyModelLayout();
    }

    private void LayoutCommitted(SubjectBoard subject)
    {
        if (IsGridSnappingEnabled)
        {
            double width = Math.Floor(BoardCanvas.Width / GridSize) * GridSize;
            double height = Math.Floor(BoardCanvas.Height / GridSize) * GridSize;
            double minWidth = Math.Ceiling(280 / GridSize) * GridSize;
            if (width >= minWidth && height >= SubjectTileControl.MinimumTileHeight)
            {
                subject.TileWidth = Math.Clamp(SnapToGrid(subject.TileWidth), minWidth, width);
                subject.TileHeight = Math.Clamp(SnapToGrid(subject.TileHeight), SubjectTileControl.MinimumTileHeight, height);
                subject.X = Math.Clamp(SnapToGrid(subject.X), 0, width - subject.TileWidth);
                subject.Y = Math.Clamp(SnapToGrid(subject.Y), 0, height - subject.TileHeight);
            }
        }

        LayoutChanged(subject);
        AlignmentCanvas.Children.Clear();
        UpdateBoardBounds();
        ScheduleSave();
    }

    /// <summary>把磁贴限制在作业板范围内，避免拖出可视区域后找不回来。</summary>
    private void ClampSubjectToViewport(SubjectBoard subject)
    {
        double width = Math.Max(BoardCanvas.Width, 1);
        double height = Math.Max(BoardCanvas.Height, 1);
        subject.X = Math.Clamp(subject.X, 0, Math.Max(0, width - subject.TileWidth));
        subject.Y = Math.Clamp(subject.Y, 0, Math.Max(0, height - subject.TileHeight));
    }

    /// <summary>磁贴正在被拖动或缩放；期间暂停会打断手势的界面更新。</summary>
    private void TileInteractionChanged(bool interacting) => _isTileInteracting = interacting;

    private async void DeleteSubject(SubjectBoard subject)
    {
        bool confirmed = await ShowConfirmAsync(
            "删除这个科目",
            $"确定删除“{subject.Name}”以及其中的文字、图片和笔迹吗？",
            "删除",
            "取消");
        if (!confirmed) return;
        _viewModel.Subjects.Remove(subject);
        BuildTiles();
        UpdateSubjectCount();
        ScheduleSave();
    }

    /// <summary>
    /// 选择图片并加入作业：先登记到最近使用列表，再复制进项目资源目录，
    /// 这样项目文件和导出都不依赖原图位置。
    /// </summary>
    private async Task AddAttachmentAsync(HomeworkEntry entry)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = MediaLibrary.ImageExtensions.Select(extension => "*" + extension).ToArray()
                }
            ]
        });
        string[] paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        if (paths.Length == 0) return;

        try
        {
            foreach (string path in paths)
            {
                string recent = await _viewModel.ImportRecentImageAsync(path);
                _viewModel.AddAttachment(entry, recent);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            await ShowMessageAsync("添加图片失败", ex.Message, "知道了");
            return;
        }

        // 附件控件需要按新数据重建，重建后仍保持当前编辑态。
        BuildTiles();
        UpdateBoardBounds();
        UpdateSubjectCount();
        ScheduleSave();
    }

    private void BoardViewport_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded) return;
        UpdateBoardBounds();
        ApplyDockedClockLayout();
    }

    private void RootShell_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded) return;
        ApplyDisplayLayout();
    }

    /// <summary>按磁贴占位计算画板尺寸；无限作业板在此基础上继续向外扩展。</summary>
    private void UpdateBoardBounds()
    {
        // 基准尺寸取可视区域本身（布局坐标不含缩放），再乘以缩放得到滚动范围：
        // 放大后内容超出视口才能滚动；若把视口除以缩放，放大后范围恰好等于视口，就永远滚不动。
        double width = Math.Max(BoardScroller.Viewport.Width, 1);
        double height = Math.Max(BoardScroller.Viewport.Height, 1);
        foreach (SubjectBoard subject in _viewModel.Subjects)
        {
            width = Math.Max(width, subject.X + subject.TileWidth + GridSize);
            height = Math.Max(height, subject.Y + subject.TileHeight + GridSize);
        }

        BoardCanvas.Width = width;
        BoardCanvas.Height = height;
        GridCanvas.Width = width;
        GridCanvas.Height = height;
        BoardSurface.Width = width;
        BoardSurface.Height = height;
        // 缩放后的滚动范围：内容按比例放大，滚动条据此计算可滚动区域。
        BoardViewport.Width = width * _boardZoom;
        BoardViewport.Height = height * _boardZoom;
        BoardViewport.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        BoardViewport.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        RenderGrid(width, height);
    }

    /// <summary>
    /// 绘制网格或点阵。样式取自设置，编辑时按“编辑时显示常规网格”强制显示网格线。
    /// </summary>
    private void RenderGrid(double width, double height)
    {
        if (Math.Abs(width - _renderedGridWidth) < 0.5 && Math.Abs(height - _renderedGridHeight) < 0.5) return;
        _renderedGridWidth = width;
        _renderedGridHeight = height;
        GridCanvas.Children.Clear();

        string style = GridAppearance.EffectiveStyle(Settings.GridStyle, _isEditing, Settings.ShowGridWhileEditing);
        if (style == "None") return;
        // 绘制逻辑与设置页预览共用 GridRenderer，只有一处需要维护。
        GridRenderer.Draw(
            GridCanvas,
            style,
            GridSize,
            width,
            height,
            GridAppearance.ParseColor(Settings.GridColor, GridRenderer.DefaultLineColor),
            Settings.GridLineThickness,
            GridAppearance.ParseColor(Settings.GridDotColor, GridRenderer.DefaultDotColor),
            Settings.GridDotDiameter);
    }

    private void RootShell_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        // 与旧版一致：Esc 先结束编辑，未编辑时退出全屏。
        if (_isEditing) FinishEditing();
        else if (SettingsRoot.IsVisible) ShowBoard();
        else if (_isFullScreen) SetFullScreen(false);
        e.Handled = true;
    }

    private void SetFullScreen(bool fullScreen)
    {
        _isFullScreen = fullScreen;
        PlatformServices.WindowPlatform.SetFullScreen(this, fullScreen);
        this.FindControl<FluentIcon>("FullScreenIcon")!.Symbol =
            fullScreen ? nameof(FluentGlyphs.ExitFullScreen) : nameof(FluentGlyphs.FullScreen);
        AutomationProperties.SetName(this.FindControl<Button>("FullScreenButton")!,
            fullScreen ? "退出全屏" : "进入全屏");
        HideFullScreenExitHint();
        // 按钮名称跟随状态，无字模式下提示显示的就是这句「退出全屏」。
        ApplyToolbarAppearance();
    }

    /// <summary>手势起点：只在全屏查看模式下记录，编辑时拖动磁贴不算提示手势。</summary>
    private void RootShell_GlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isEditing || !_isFullScreen) return;
        // 子控件可能接管指针并不再向上冒泡，按下时总是覆盖上一次的状态。
        _gesturePointer = e.Pointer;
        _gestureStart = e.GetPosition(this);
    }

    private void RootShell_GlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_gesturePointer is null || e.Pointer != _gesturePointer) return;
        Point current = e.GetPosition(this);
        double distance = Math.Sqrt(
            Math.Pow(current.X - _gestureStart.X, 2) + Math.Pow(current.Y - _gestureStart.Y, 2));
        // 8 像素以内视为点按，避免点击按钮也弹出提示。
        if (distance < 8) return;
        ShowFullScreenExitHint();
        _gesturePointer = null;
    }

    private void RootShell_GlobalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer == _gesturePointer) _gesturePointer = null;
    }

    /// <summary>显示退出全屏提示：高亮按钮并让「退出全屏」文字临时可见。</summary>
    private void ShowFullScreenExitHint()
    {
        if (_isEditing || !_isFullScreen) return;
        _showFullScreenExitHint = true;
        ApplyToolbarAppearance();
        _fullScreenHintTimer.Stop();
        _fullScreenHintTimer.Start();
    }

    private void HideFullScreenExitHint()
    {
        _fullScreenHintTimer.Stop();
        if (!_showFullScreenExitHint) return;
        _showFullScreenExitHint = false;
        ApplyToolbarAppearance();
    }

    private void FullScreenButton_Click(object? sender, RoutedEventArgs e) => SetFullScreen(!_isFullScreen);

    private void EditBoardButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isEditing) FinishEditing();
        else EnterEditing();
    }

    private void EnterEditing()
    {
        _viewModel.BeginEditing();
        _isEditing = true;
        ApplyEditingState();
    }

    private void FinishEditing()
    {
        _viewModel.PublishEditing();
        _isEditing = false;
        ApplyEditingState();
        SaveNow();
    }

    private void DiscardEditButton_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.DiscardEditing();
        _isEditing = false;
        ApplyEditingState();
        BuildTiles();
        SaveNow();
    }

    /// <summary>编辑态切换时统一刷新磁贴、网格与工具栏按钮的可用状态。</summary>
    private void ApplyEditingState()
    {
        foreach (SubjectTileControl tile in _tiles.Values) tile.SetEditing(_isEditing);
        _renderedGridWidth = 0;
        UpdateBoardBounds();
        ApplyToolbarAppearance();
        UpdateLayoutHandles();
        ApplyInkMode();
        UpdateZoomIslandVisibility();
        // 编辑态结束或进入画笔时收起富文本悬浮岛。
        if (!_isEditing || this.FindControl<ToggleButton>("GlobalPenButton")?.IsChecked == true) HideRichTextIsland();
    }

    private void AddSubjectButton_Click(object? sender, RoutedEventArgs e)
    {
        SubjectBoard subject = _viewModel.AddSubject("新科目");
        AddTile(subject);
        UpdateBoardBounds();
        UpdateSubjectCount();
        ScheduleSave();
    }

    private void AutoArrangeButton_Click(object? sender, RoutedEventArgs e)
    {
        ArrangeTiles();
        ScheduleSave();
    }

    /// <summary>
    /// 把全部磁贴按当前自动布局设置重新排列并吸附到网格：先横排满一行再换行，
    /// 必要时按列宽收窄磁贴让文字换行，仍然放不下才等比缩小；
    /// 打开“自动调整磁贴大小”时才会按内容收紧，关闭时保持用户设置的尺寸。
    /// </summary>
    private void ArrangeTiles()
    {
        if (_viewModel.Subjects.Count == 0) return;
        double width = Math.Max(BoardViewport.Bounds.Width, 600);
        double height = Math.Max(BoardViewport.Bounds.Height, 600);
        double gap = Math.Max(0, Settings.AutoLayoutGap);
        double grid = IsGridSnappingEnabled && Settings.AutoLayoutAlign ? GridSize : 0;
        bool aligned = Settings.AutoLayoutResize && Settings.AutoLayoutAlign;

        // 按内容收紧需要真实控件测量；控件缺失（例如看板还没构建）时保持原布局不动。
        List<SubjectTileControl> tiles = [];
        foreach (SubjectBoard subject in _viewModel.Subjects)
        {
            if (FindTile(subject) is not { } tile) return;
            tiles.Add(tile);
        }

        List<(double Width, double Height)> AlignAndSnap(IReadOnlyList<(double Width, double Height)> values)
        {
            IReadOnlyList<(double Width, double Height)> fitted = aligned ? BoardLayout.AlignSizes(values) : values;
            return grid > 0
                ? fitted.Select(size => (Math.Ceiling(size.Width / grid) * grid, Math.Ceiling(size.Height / grid) * grid)).ToList()
                : [.. fitted];
        }

        List<LayoutRect>? TryArrange(IReadOnlyList<(double Width, double Height)> values, double rowWidth, double rowHeight)
        {
            try
            {
                return BoardLayout.Arrange(values, rowWidth, rowHeight, gap, Settings.AutoLayoutAlign, grid);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        List<LayoutRect>? arranged = null;
        List<(double Width, double Height)> content = [];
        if (Settings.AutoLayoutResize)
        {
            // 从最多列开始试：每减少一列就放宽每列可用宽度，磁贴随之变宽、文字少换行。
            int maxColumns = Math.Min(tiles.Count,
                Math.Max(1, (int)Math.Floor((width + gap) / (SubjectTileControl.MinimumTileWidth + gap))));
            for (int columns = maxColumns; columns >= 1 && arranged is null; columns--)
            {
                double cap = columns <= 1 ? width : (width - gap * (columns - 1)) / columns;
                content = tiles.Select(tile => tile.MeasureContentSize(cap)).ToList();
                List<(double Width, double Height)> sizes = AlignAndSnap(content);
                double rowWidth = Settings.InfiniteBoard ? Math.Max(width, sizes.Max(size => size.Width)) : width;
                double rowHeight = Settings.InfiniteBoard
                    ? Math.Max(height, sizes.Sum(size => size.Height + gap + grid))
                    : height;
                arranged = TryArrange(sizes, rowWidth, rowHeight);
            }

            if (arranged is null)
            {
                // 内容尺寸仍放不下时等比缩小，保留旧版“总能排好”的行为；磁贴内文字可以滚动，不会因此丢失。
                for (double scale = .9; scale >= .4 - 0.0001 && arranged is null; scale -= .1)
                {
                    List<(double Width, double Height)> scaled = content
                        .Select(size => (Math.Max(SubjectTileControl.MinimumTileWidth, size.Width * scale),
                                         Math.Max(SubjectTileControl.MinimumTileHeight, size.Height * scale)))
                        .ToList();
                    arranged = TryArrange(scaled, width, height);
                }
            }
        }
        else
        {
            arranged = TryArrange(
                _viewModel.Subjects.Select(subject => (subject.TileWidth, subject.TileHeight)).ToList(), width, height);
        }

        if (arranged is null)
        {
            // 空间不足时保持原布局，并把原因告诉用户，而不是把磁贴挤成不可读的大小。
            _ = ShowMessageAsync(
                "无法自动排列",
                "当前作业板空间不足，无法在不低于最小磁贴尺寸的情况下自动排列。开启无限作业板后可以继续向外扩展。",
                "知道了");
            return;
        }

        for (int index = 0; index < _viewModel.Subjects.Count && index < arranged.Count; index++)
        {
            SubjectBoard subject = _viewModel.Subjects[index];
            LayoutRect rect = arranged[index];
            subject.X = rect.X;
            subject.Y = rect.Y;
            subject.TileWidth = rect.Width;
            subject.TileHeight = rect.Height;
            if (FindTile(subject) is { } tile)
            {
                tile.ApplyModelLayout();
                Canvas.SetLeft(tile, subject.X);
                Canvas.SetTop(tile, subject.Y);
            }
        }

        UpdateBoardBounds();
    }

    private void GridSnapToggleButton_Changed(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle)
        {
            Settings.GridSnappingEnabled = toggle.IsChecked == true;
            UpdateGridSnapHint();
            ScheduleSave();
        }
    }

    /// <summary>画笔按钮状态变化：切换书写模式并显示画笔栏。</summary>
    private void GlobalPen_Changed(object? sender, RoutedEventArgs e)
    {
        ApplyInkMode();
        ScheduleSave();
    }

    private void BackToBoardButton_Click(object? sender, RoutedEventArgs e) => ShowBoard();

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SettingsRoot.IsVisible) ShowBoard();
        else ShowSettings();
    }

    private void ShowBoard()
    {
        SettingsRoot.IsVisible = false;
        DisplayRoot.IsVisible = true;
        FloatingToolbar.IsVisible = true;
        // 设置页期间看板被隐藏、毛玻璃背衬已撤下；回到看板时重新按当前设置启用并贴合浮岛。
        ApplyToolbarAppearance();
        ApplyPendingBoardLayout();
        UpdateProjectCommands();
    }

    /// <summary>设置改动后统一走这里：刷新看板外观并延迟写盘。</summary>
    internal void SettingChanged()
    {
        ApplyTheme();
        ApplyDisplayLayout();
        ApplyToolbarAppearance();
        ApplyBackgrounds();
        RefreshAppearancePreviews();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        // 设置页里的滑条、色块等改动直接走这里：顺手刷新当前页的实时预览，
        // 预览只更新已有控件，拖动时不会重建场景。
        if (SettingsRoot.IsVisible) RefreshAppearancePreviews();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        _viewModel.SaveToDisk();
    }
}
