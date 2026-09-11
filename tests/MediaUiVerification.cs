// 仅在隔离的 UI 验证构建中编译。视频样本由 verify-background-media.ps1 生成。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Controls;
using Pancake.Services;
using Windows.Media.Playback;

namespace Pancake;

public sealed partial class MainWindow
{
    internal void ScheduleBackgroundMediaVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS: " + message); }
                void InvokeButton(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                async Task WaitUntil(Func<bool> predicate, string message)
                {
                    for (int attempt = 0; attempt < 80 && !predicate(); attempt++) await Task.Delay(100);
                    Check(predicate(), message);
                }
                _settings.AutoUpdateEnabled = false;
                ExportOverlay.Visibility = Visibility.Visible;
                Grid sample = new() { Width = 320, Height = 180, Background = new SolidColorBrush(Microsoft.UI.Colors.CornflowerBlue) };
                ExportOverlay.Children.Add(sample);
                await NextLayoutAsync();
                string first = Path.Combine(output, "first.png"), second = Path.Combine(output, "second.png");
                await SaveVisualAsync(sample, first, 320, 180);
                sample.Background = new SolidColorBrush(Microsoft.UI.Colors.Orange);
                await NextLayoutAsync(); await SaveVisualAsync(sample, second, 320, 180);
                ExportOverlay.Children.Clear();
                string imageA = await MediaLibraryStore.ImportAsync(first), imageB = await MediaLibraryStore.ImportAsync(second);
                string video = Path.Combine(AppContext.BaseDirectory, "media-test.mp4");
                Check(File.Exists(video), "generated H264 video fixture exists");
                BackgroundSettings settings = new() { PlaylistEnabled = true, Playlist = [video, imageA], SwitchOnTimer = false, SwitchOnMediaEnded = true };
                BackgroundVisual background = new() { Width = 320, Height = 180 };
                background.Apply(settings, BoardTheme.SurfaceBrush);
                ExportOverlay.Children.Add(background);
                await NextLayoutAsync();
                await WaitUntil(() => FindVisuals<MediaPlayerElement>(background).Any(v => v.MediaPlayer.PlaybackSession.Position > TimeSpan.FromMilliseconds(80)), "real video decodes and playback position advances");
                MediaPlayer player = FindVisuals<MediaPlayerElement>(background).Single().MediaPlayer;
                Check(player.IsMuted, "wallpaper video is muted");
                settings.Color = "#123456"; background.Apply(settings, BoardTheme.SurfaceBrush);
                Check(ReferenceEquals(player, FindVisuals<MediaPlayerElement>(background).Single().MediaPlayer), "applying appearance retains the playing video instance");
                await WaitUntil(() => FindVisuals<Image>(background).Any(), "video ended event advances to image when timer is disabled");
                settings.Playlist = [video]; settings.SwitchOnMediaEnded = false;
                background.Apply(settings, BoardTheme.SurfaceBrush);
                await NextLayoutAsync();
                player = FindVisuals<MediaPlayerElement>(background).Single().MediaPlayer;
                Check(player.IsLoopingEnabled, "disabling end switching enables video loop");
                settings.Playlist = [imageA, imageB]; settings.SwitchOnTimer = true; settings.SwitchIntervalSeconds = 1;
                background.Apply(settings, BoardTheme.SurfaceBrush);
                await WaitUntil(() => FindVisuals<Image>(background).Any(image => image.Source is BitmapImage bitmap && bitmap.UriSource.LocalPath == imageB), "fixed timer switches between images");
                settings.SwitchOnTimer = false; background.Apply(settings, BoardTheme.SurfaceBrush);
                var still = FindVisuals<Image>(background).Single();
                await Task.Delay(1200);
                Check(ReferenceEquals(still, FindVisuals<Image>(background).Single()), "both switches disabled retains current image");
                string broken = Path.Combine(output, "broken.mp4"); File.WriteAllText(broken, "not a movie");
                settings.Playlist = [broken, imageA];
                background.Apply(settings, BoardTheme.SurfaceBrush);
                await WaitUntil(() => FindVisuals<Image>(background).Any(), "undecodable video is skipped to a valid image");
                settings.Playlist = [broken]; background.Apply(settings, BoardTheme.SurfaceBrush);
                await WaitUntil(() => FindVisuals<TextBlock>(background).Any(text => text.Text.Contains("无法播放")), "all invalid media stop with visible error");
                settings.Playlist = [video]; background.Apply(settings, BoardTheme.SurfaceBrush);
                await NextLayoutAsync();
                var oldElement = FindVisuals<MediaPlayerElement>(background).Single();
                ExportOverlay.Children.Clear(); await NextLayoutAsync();
                Check(oldElement.MediaPlayer is null, "unloading the background detaches and disposes video playback");
                ExportOverlay.Visibility = Visibility.Collapsed;
                ShowSettings(); ShowSettingsPage("AppearanceTile"); await NextLayoutAsync();
                var editor = _settingsPages["AppearanceTile"].Content;
                RecentImagesView recent = FindVisuals<RecentImagesView>(editor).Single();
                Check(FindVisuals<Button>(recent).Count() == 2, "background picker shows shared recent image thumbnails");
                InvokeButton(FindVisuals<Button>(recent).Last());
                await WaitUntil(() => _settings.TileBackground.ImagePath == imageA, "clicking a recent thumbnail applies that image");
                ToggleSwitch playlist = FindVisuals<ToggleSwitch>(editor).Single(toggle => toggle.Header?.ToString() == "播放队列模式");
                playlist.IsOn = true;
                _settings.TileBackground.Playlist.Add(imageB); SettingChanged(); await NextLayoutAsync();
                InvokeButton(FindVisuals<Button>(editor).First(button => button.Content?.ToString() == "下移" && button.IsEnabled));
                Check(_settings.TileBackground.Playlist.SequenceEqual(new[] { imageB, imageA }), "queue down button changes stored order");
                InvokeButton(FindVisuals<Button>(editor).First(button => button.Content?.ToString() == "移除"));
                Check(_settings.TileBackground.Playlist.SequenceEqual(new[] { imageA }), "queue remove button removes selected item");
                recent.StartBringIntoView(); await NextLayoutAsync();
                await SaveVisualAsync(RootShell, Path.Combine(output, "media-settings.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                await SaveVisualAsync(recent, Path.Combine(output, "recent-images.png"), (int)recent.ActualWidth, (int)recent.ActualHeight);
                SaveStateNow();
                var installed = await Task.Run(WallpaperEngineLibrary.Detect);
                evidence.Add(installed is null ? "INFO: Wallpaper Engine is not installed; real-library import unverified." : $"INFO: Wallpaper Engine detected at {installed.Directory}; {WallpaperEngineLibrary.ReadVideos(installed).Count} video projects.");
                if (installed is not null)
                {
                    var actual = WallpaperEngineLibrary.ReadVideos(installed).Where(item => Path.GetExtension(item.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)).OrderBy(item => new FileInfo(item.Path).Length).FirstOrDefault();
                    if (actual is not null)
                    {
                        string imported = await MediaLibraryStore.ImportAsync(actual.Path);
                        Check(imported != actual.Path && new FileInfo(imported).Length == new FileInfo(actual.Path).Length, "real Wallpaper Engine video imported as an owned copy");
                        ExportOverlay.Visibility = Visibility.Visible; ExportOverlay.Children.Add(background);
                        settings.Playlist = [imported]; background.Apply(settings, BoardTheme.SurfaceBrush);
                        await NextLayoutAsync();
                        await WaitUntil(() => FindVisuals<MediaPlayerElement>(background).Any(v => v.MediaPlayer.PlaybackSession.Position > TimeSpan.FromMilliseconds(80)), "imported Wallpaper Engine video decodes in Pancake");
                        ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                evidence.Add("BACKGROUND_MEDIA_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("BACKGROUND_MEDIA_VERIFICATION_FAILED\n" + ex); }
            finally { File.WriteAllLines(Path.Combine(output, "media-result.txt"), evidence); Close(); }
        };
    }
}
