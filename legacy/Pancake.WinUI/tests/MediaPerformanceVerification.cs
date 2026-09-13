using Microsoft.UI.Xaml.Controls;
using Pancake.Services;
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
                string video = Path.Combine(AppContext.BaseDirectory, "media-test.mp4");
                _settings.AutoUpdateEnabled = false;
                _settings.SharedBackgroundEnabled = true;
                _settings.SharedBackground = new() { ImagePath = video };
                ApplyExtendedSettings(); ShowSettings(); ShowSettingsPage("AppearanceBackground");
                await Task.Delay(900);
                var players = new HashSet<MediaPlayer>();
                double maximumPosition = 0;
                int samples = 0, resets = 0;
                MediaPlayer? previous = null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(3))
                {
                    // 走自然时钟刷新和设置预览路径，不改变解码模式，不向播放器注入帧。
                    var player = FindVisuals<MediaPlayerElement>(_appearancePreviews["Shared"]).FirstOrDefault()?.MediaPlayer;
                    if (player is not null)
                    {
                        if (previous is not null && !ReferenceEquals(previous, player)) resets++;
                        previous = player; players.Add(player);
                        maximumPosition = Math.Max(maximumPosition, player.PlaybackSession.Position.TotalSeconds);
                    }
                    samples++;
                    await Task.Delay(50);
                }
                evidence.Add($"MEASURE: natural preview over 3s: players={players.Count}, restarts={resets}, maxPosition={maximumPosition:F3}s, samples={samples}");
                if (players.Count != 1 || maximumPosition < 2) throw new Exception("动态预览在自然刷新期间重新创建播放器，播放进度不能连续前进。");
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
                    var player = FindVisuals<MediaPlayerElement>(_appearancePreviews["Shared"]).FirstOrDefault()?.MediaPlayer;
                    if (player is not null) players.Add(player);
                }
                evidence.Add($"MEASURE: 16 appearance updates: players={players.Count}, maxUiWork={durations.Max():F1}ms");
                if (players.Count != 1) throw new Exception("外观调整导致动态壁纸预览反复重启，出现可重复的间歇停顿。");
                evidence.Add("PASS: appearance adjustments preserve continuous preview playback");
                evidence.Add("MEDIA_PERFORMANCE_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("MEDIA_PERFORMANCE_VERIFICATION_FAILED\n" + ex); }
            finally { File.WriteAllLines(Path.Combine(output, "performance-result.txt"), evidence); Close(); }
        };
    }
}
