using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Controls;
using Windows.Media.Playback;

namespace Pancake;

public sealed partial class MainWindow
{
    internal void ScheduleMediaPerformanceVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                string video = Environment.GetEnvironmentVariable("PANCAKE_TEST_VIDEO") ?? Path.Combine(AppContext.BaseDirectory, "media-test.mp4");
                string baselineVideo = Environment.GetEnvironmentVariable("PANCAKE_TEST_VIDEO_BASE") ?? Path.Combine(AppContext.BaseDirectory, "media-test-base.mp4");
                File.WriteAllText(Path.Combine(output, "performance-progress.txt"), "Applying video: " + video);
                _settings.AutoUpdateEnabled = false;
                _settings.SharedBackgroundEnabled = true;
                _settings.SharedBackground = new() { ImagePath = video };
                ApplyExtendedSettings(); ShowSettings(); ShowSettingsPage("AppearanceBackground");
                await Task.Delay(900);
                var players = new HashSet<MediaPlayer>();
                double maximumPosition = 0;
                int samples = 0, resets = 0;
                double maximumDelay = 0;
                MediaPlayer? previous = null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(3))
                {
                    // 走自然时钟刷新和设置预览路径，不改变解码模式，不向播放器注入帧。
                    var player = FindVisuals<MediaPlayerElement>(_appearancePreviews["Background"]).FirstOrDefault()?.MediaPlayer;
                    if (player is not null)
                    {
                        if (previous is not null && !ReferenceEquals(previous, player)) resets++;
                        previous = player; players.Add(player);
                        maximumPosition = Math.Max(maximumPosition, player.PlaybackSession.Position.TotalSeconds);
                    }
                    samples++;
                    var response = System.Diagnostics.Stopwatch.StartNew();
                    await Task.Delay(50);
                    maximumDelay = Math.Max(maximumDelay, response.Elapsed.TotalMilliseconds);
                }
                evidence.Add($"MEASURE: natural preview over 3s: players={players.Count}, restarts={resets}, maxPosition={maximumPosition:F3}s, samples={samples}, maxUiDelay={maximumDelay:F1}ms");
                if (maximumDelay > 1500) throw new Exception("视频播放期间界面调度停顿超过 1500ms。");
                if (players.Count != 1 || resets > 0) throw new Exception("动态预览在自然刷新期间重新创建播放器，播放进度不能连续前进。");
                evidence.Add("PASS: preview playback remains continuous through natural clock refreshes");
                players.Clear();
                var durations = new List<double>();
                for (int step = 0; step < 16; step++)
                {
                    var update = System.Diagnostics.Stopwatch.StartNew();
                    _settings.SharedBackground.Color = step % 2 == 0 ? "#202020" : "#303030";
                    SettingChanged();
                    durations.Add(update.Elapsed.TotalMilliseconds);
                    await Task.Delay(60);
                    var player = FindVisuals<MediaPlayerElement>(_appearancePreviews["Background"]).FirstOrDefault()?.MediaPlayer;
                    if (player is not null) players.Add(player);
                }
                evidence.Add($"MEASURE: 16 appearance updates: players={players.Count}, maxUiWork={durations.Max():F1}ms");
                if (players.Count != 1) throw new Exception("外观调整导致动态壁纸预览反复重启，出现可重复的间歇停顿。");
                evidence.Add("PASS: appearance adjustments preserve continuous preview playback");

                // 隐藏管线测量：巡视各外观页后回到背景页。被折叠的页面预览和被设置页盖住的
                // 主显示区若仍在逐帧拷贝，高清壁纸下每条管线都按视频原生分辨率占用 UI 线程，
                // 页面越开越多、设置页就越卡，直至无法操作。
                foreach (string page in new[] { "AppearanceTile", "AppearanceToolbar", "Layout" })
                {
                    ShowSettingsPage(page);
                    await Task.Delay(700);
                }
                ShowSettingsPage("AppearanceBackground");
                await Task.Delay(900);
                var every = FindVisuals<VideoFrameImage>(RootShell).ToList();
                var hidden = every.Where(frame => !EffectivelyVisible(frame)).ToList();
                var shown = every.Where(frame => EffectivelyVisible(frame)).ToList();
                evidence.Add($"MEASURE: frame pipelines after page tour: total={every.Count}, visible={shown.Count}, hidden={hidden.Count}");
                int hiddenBefore = hidden.Sum(frame => frame.PresentedFrames);
                int shownBefore = shown.Sum(frame => frame.PresentedFrames);
                await Task.Delay(2000);
                int hiddenGained = hidden.Sum(frame => frame.PresentedFrames) - hiddenBefore;
                int shownGained = shown.Sum(frame => frame.PresentedFrames) - shownBefore;
                evidence.Add($"MEASURE: frames gained over 2s: visible=+{shownGained}, hidden=+{hiddenGained}");
                // 隐藏管线的解码器也必须停下：拷贝跳过但继续解码仍在白耗 GPU 和电池。
                var hiddenPlayers = FindVisuals<MediaPlayerElement>(RootShell)
                    .Where(element => !EffectivelyVisible(element))
                    .Select(element => element.MediaPlayer).Where(player => player is not null).ToList();
                var busyHidden = hiddenPlayers
                    .Where(player => player!.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering)
                    .ToList();
                evidence.Add($"MEASURE: hidden players: total={hiddenPlayers.Count}, still-decoding={busyHidden.Count}");

                // 差分测量：同一会话先换低清基线素材、再换高清素材，比较设置页预览每帧的
                // UI 线程开销。帧拷贝若按视频原生分辨率执行，高清素材会成倍放大开销。
                FrameCost light = await MeasurePreviewFrameCost(baselineVideo, evidence, "baseline");
                FrameCost heavy = await MeasurePreviewFrameCost(video, evidence, "high-quality");
                double ratio = heavy.RatioAgainst(light);
                evidence.Add($"MEASURE: differential: baseline={light.WorkPerFrame:F2}ms/frame ({light.Frames} frames, ui={light.UiFraction:P0}), " +
                    $"high-quality={heavy.WorkPerFrame:F2}ms/frame ({heavy.Frames} frames, ui={heavy.UiFraction:P0}), ratio={ratio:F2}x");

                // 拷贝面检查：帧拷贝按显示尺寸封顶才能与壁纸质量解耦。预览只有几百像素宽时
                // 按视频原生分辨率拷贝（4K 每帧 800 万像素）正是"高质量壁纸拖垮设置页"的直接原因。
                foreach (VideoFrameImage frame in FindVisuals<VideoFrameImage>(_appearancePreviews["Background"]))
                {
                    double scale = frame.XamlRoot?.RasterizationScale ?? 1;
                    double boxPixels = frame.ActualSize.X * scale * frame.ActualSize.Y * scale;
                    long surfacePixels = (long)frame.SurfaceWidth * frame.SurfaceHeight;
                    evidence.Add($"MEASURE: copy surface: {frame.SurfaceWidth}x{frame.SurfaceHeight} ({surfacePixels}px) for box {boxPixels:F0}px");
                    if (boxPixels > 1000 && surfacePixels > boxPixels * 4 + 65536)
                        throw new Exception($"帧拷贝面 {frame.SurfaceWidth}x{frame.SurfaceHeight} 超过显示需求（{boxPixels:F0} 像素）的 4 倍，" +
                            "视频原生分辨率被整帧拷贝，高质量壁纸会拖垮设置页。");
                }
                evidence.Add("PASS: frame copy surfaces are capped to display size");

                // 全部测量完成后统一断言，任一失败都能在证据里看到另一项的数值。
                if (shownGained < 10)
                    throw new Exception("可见预览两秒内出帧不足，播放未在推进，测量无效。");
                if (hiddenGained > 2)
                    throw new Exception($"隐藏的动态壁纸管线在 2 秒内继续拷贝了 {hiddenGained} 帧，高清壁纸下设置页会被这些管线拖垮。");
                if (busyHidden.Count > 0)
                    throw new Exception($"隐藏管线的播放器仍在解码（{busyHidden.Count} 个），持续消耗 GPU 与电池。");
                evidence.Add("PASS: hidden wallpaper pipelines stop frame work while not visible");
                if (light.Frames < 20 || heavy.Frames < 20)
                    throw new Exception("差分测量期间播放帧数不足，无法比较每帧开销。");
                if (ratio > 2.5)
                    throw new Exception($"高质量动态壁纸使 UI 线程每帧开销增长 {ratio:F1} 倍（超过 2.5 倍），帧拷贝按视频原生分辨率执行会拖垮设置页。");
                evidence.Add("PASS: high-quality wallpaper does not multiply per-frame UI thread cost");

                // 恢复检查：回到看板后壁纸必须重新播放，防止"隐藏暂停"变成"永久冻结"；
                // 同时设置页预览此时已隐藏，仍必须保持安静。
                ShowBoard();
                await Task.Delay(1000);
                var onBoard = FindVisuals<VideoFrameImage>(RootShell).ToList();
                var boardShown = onBoard.Where(EffectivelyVisible).ToList();
                var boardHidden = onBoard.Where(frame => !EffectivelyVisible(frame)).ToList();
                int boardShownBefore = boardShown.Sum(frame => frame.PresentedFrames);
                int boardHiddenBefore = boardHidden.Sum(frame => frame.PresentedFrames);
                await Task.Delay(1000);
                int boardShownGained = boardShown.Sum(frame => frame.PresentedFrames) - boardShownBefore;
                int boardHiddenGained = boardHidden.Sum(frame => frame.PresentedFrames) - boardHiddenBefore;
                evidence.Add($"MEASURE: frames gained over 1s back on board: visible=+{boardShownGained}, hidden=+{boardHiddenGained}");
                if (boardShownGained < 5)
                    throw new Exception("回到看板后壁纸没有恢复播放，隐藏暂停变成了永久冻结。");
                if (boardHiddenGained > 2)
                    throw new Exception("回到看板后设置页的隐藏管线仍在拷贝帧。");
                evidence.Add("PASS: wallpaper resumes on the board and hidden previews stay paused");
                evidence.Add("MEDIA_PERFORMANCE_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("MEDIA_PERFORMANCE_VERIFICATION_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "performance-result.txt"), evidence);
                // 直接退出进程：媒体管线析构偶发挂起会拖过验证脚本的超时上限，进程内容已完成取证。
                Environment.Exit(0);
            }
        };
    }

    // WinUI 3 没有有效可见性属性；沿父链检查 Visibility 才能区分"折叠管线"与"显示中的管线"。
    private static bool EffectivelyVisible(UIElement element)
    {
        for (DependencyObject? current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private readonly record struct FrameCost(int Frames, double WorkPerFrame, double UiFraction)
    {
        public double RatioAgainst(FrameCost baseline) =>
            baseline.Frames == 0 || baseline.WorkPerFrame < .01 || Frames == 0 ? 1
            : WorkPerFrame / baseline.WorkPerFrame;
    }

    /// <summary>换上指定素材后预热，再统计设置页预览所有帧服务实例的每帧 UI 线程开销。</summary>
    private async Task<FrameCost> MeasurePreviewFrameCost(string video, List<string> evidence, string label)
    {
        _settings.SharedBackground.ImagePath = video;
        SettingChanged();
        await Task.Delay(900);
        var frames = FindVisuals<VideoFrameImage>(_appearancePreviews["Background"]).ToList();
        long ticksBefore = frames.Sum(frame => frame.FrameWorkTicks);
        int presentedBefore = frames.Sum(frame => frame.PresentedFrames);
        await Task.Delay(2000);
        long ticksAfter = frames.Sum(frame => frame.FrameWorkTicks);
        int presentedAfter = frames.Sum(frame => frame.PresentedFrames);
        int presented = presentedAfter - presentedBefore;
        double workTicks = ticksAfter - ticksBefore;
        double workPerFrame = presented == 0 ? 0 : workTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / presented;
        double uiFraction = workTicks / System.Diagnostics.Stopwatch.Frequency / 2.0;
        evidence.Add($"MEASURE: frame cost {label}: instances={frames.Count}, frames={presented}, workPerFrame={workPerFrame:F2}ms, uiFraction={uiFraction:P0}");
        return new FrameCost(presented, workPerFrame, uiFraction);
    }
}
