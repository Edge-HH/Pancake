using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Pancake.Controls;
using Pancake.Platforms.Abstraction;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 背景媒体的进阶编辑：最近使用的图像、播放队列与 Wallpaper Engine 导入。
/// 队列的选曲逻辑在核心层的 BackgroundPlaylist，界面只负责编辑队列内容。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>最近使用图像条：点击即复用，列表为空时整行隐藏。</summary>
    private Control CreateRecentImagesStrip(Action<string> select)
    {
        StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (string path in _viewModel.RecentImages)
        {
            Button thumbnail = new()
            {
                Width = 56,
                Height = 56,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(BoardTheme.LineColor.ToColor())
            };
            if (TryLoadThumbnail(path) is { } bitmap)
            {
                thumbnail.Content = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
            }
            else
            {
                thumbnail.Content = new FluentIcon { Symbol = nameof(FluentGlyphs.Image), FontSize = 16 };
            }

            ToolTip.SetTip(thumbnail, Path.GetFileName(path));
            thumbnail.Click += (_, _) => select(path);
            strip.Children.Add(thumbnail);
        }

        return new StackPanel
        {
            Spacing = 6,
            IsVisible = strip.Children.Count > 0,
            Children =
            {
                new TextBlock { Text = "最近使用的图像", FontSize = 12 },
                strip
            }
        };
    }

    /// <summary>缩略图按宽度解码，避免为列表里的每张图都展开成完整位图。</summary>
    private static Bitmap? TryLoadThumbnail(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 112);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 播放队列编辑器：开关、内容列表（上移／下移／移除）、顺序或随机、定时切换与间隔。
    /// 队列内容与单媒体互斥，关闭队列后回到单媒体背景。
    /// </summary>
    private Control CreatePlaylistEditor(BackgroundSettings style, Action apply, bool allowVideo)
    {
        ToggleSwitch enabled = CreateToggle(style.PlaylistEnabled);
        StackPanel list = new() { Spacing = 4 };
        void RebuildList()
        {
            list.Children.Clear();
            for (int index = 0; index < style.Playlist.Count; index++)
            {
                int captured = index;
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}. {Path.GetFileName(style.Playlist[index])}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 220
                });
                row.Children.Add(MoveButton("上移", () =>
                {
                    if (captured == 0) return;
                    (style.Playlist[captured - 1], style.Playlist[captured]) = (style.Playlist[captured], style.Playlist[captured - 1]);
                    RebuildList();
                    apply();
                    ScheduleSave();
                }, 1));
                row.Children.Add(MoveButton("下移", () =>
                {
                    if (captured >= style.Playlist.Count - 1) return;
                    (style.Playlist[captured + 1], style.Playlist[captured]) = (style.Playlist[captured], style.Playlist[captured + 1]);
                    RebuildList();
                    apply();
                    ScheduleSave();
                }, 2));
                row.Children.Add(MoveButton("移除", () =>
                {
                    style.Playlist.RemoveAt(captured);
                    RebuildList();
                    apply();
                    ScheduleSave();
                }, 3));
                list.Children.Add(row);
            }

            if (style.Playlist.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = allowVideo ? "队列为空，请添加图片或视频。" : "队列为空，请添加图片。",
                    IsEnabled = false
                });
            }
        }

        // 队列可以混放图片与视频：与旧版一致，支持视频的平台允许一次选择多个文件。
        string[] queueExtensions = allowVideo
            ? [.. MediaLibrary.ImageExtensions, .. MediaLibrary.VideoExtensions]
            : MediaLibrary.ImageExtensions;
        Button add = CreateActionButton(allowVideo ? "添加多个媒体" : "添加图片到队列", async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = allowVideo ? "添加背景媒体" : "添加背景图片",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(allowVideo ? "图片或视频" : "图片")
                    {
                        Patterns = queueExtensions.Select(extension => "*" + extension).ToArray()
                    }
                ]
            });
            foreach (IStorageFile file in files)
            {
                string? path = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(path)) continue;
                string stored = await _viewModel.ImportRecentImageAsync(path);
                if (!style.Playlist.Contains(stored, StringComparer.OrdinalIgnoreCase)) style.Playlist.Add(stored);
            }

            RebuildList();
            apply();
            ScheduleSave();
        });

        ComboBox order = CreateComboBox(
            ["按顺序循环", "随机播放"],
            style.Shuffle ? 1 : 0,
            index => { style.Shuffle = index == 1; apply(); ScheduleSave(); });

        ToggleSwitch switchOnTimer = CreateToggle(style.SwitchOnTimer);
        Slider interval = CreateSlider(1, 86400, 1, BackgroundPlaylist.NormalizeInterval(style.SwitchIntervalSeconds), snapToTick: true);
        interval.ValueChanged += (_, _) =>
        {
            style.SwitchIntervalSeconds = BackgroundPlaylist.NormalizeInterval(interval.Value);
            apply();
            ScheduleSave();
        };
        Control intervalRow = CreateRow("切换间隔", "每经过该秒数切换到下一项（1–86400 秒）。", interval);
        switchOnTimer.IsCheckedChanged += (_, _) =>
        {
            style.SwitchOnTimer = switchOnTimer.IsChecked == true;
            intervalRow.IsEnabled = style.SwitchOnTimer;
            apply();
            ScheduleSave();
        };
        intervalRow.IsEnabled = style.SwitchOnTimer;

        enabled.IsCheckedChanged += (_, _) =>
        {
            style.PlaylistEnabled = enabled.IsChecked == true;
            RebuildList();
            apply();
            ScheduleSave();
        };

        // 视频播放结束时切换需要播放器事件；图片没有结束事件，因此只对队列里的视频生效。
        ToggleSwitch switchOnEnded = CreateToggle(style.SwitchOnMediaEnded);
        switchOnEnded.IsCheckedChanged += (_, _) =>
        {
            style.SwitchOnMediaEnded = switchOnEnded.IsChecked == true;
            ScheduleSave();
        };
        Control endedRow = CreateRow("视频播放完成时切换",
            allowVideo
                ? "只对视频生效：队列播到视频结尾时切到下一项；关闭后视频循环播放。"
                : "当前平台没有视频播放能力，这一项始终不生效。",
            switchOnEnded);
        endedRow.IsEnabled = allowVideo;

        RebuildList();
        StackPanel panel = new() { Spacing = 10 };
        panel.Children.Add(enabled);
        panel.Children.Add(list);
        panel.Children.Add(add);
        panel.Children.Add(CreateRow("播放顺序", "随机播放在有多个可用媒体时不会连续重复同一项。", order));
        panel.Children.Add(CreateRow("按固定时间切换", "关闭后停留在当前媒体。", switchOnTimer));
        panel.Children.Add(intervalRow);
        panel.Children.Add(endedRow);
        panel.Children.Add(new TextBlock
        {
            Text = "两种切换都开启时，先触发的条件生效；都关闭时停留在当前媒体。图片与 GIF 没有播放结束事件，需要开启定时切换才会轮换。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
        });
        return panel;
    }

    private static Button MoveButton(string text, Action click, int column)
    {
        Button button = new() { Content = text, Padding = new Thickness(8, 4), MinWidth = 52 };
        button.Click += (_, _) => click();
        Grid.SetColumn(button, column);
        return button;
    }

    /// <summary>
    /// Wallpaper Engine 导入：只在平台支持且检测到安装时出现。
    /// 只导入视频壁纸；网页项目按旧版规则单独走网页壁纸入口。
    /// </summary>
    private Control? CreateWallpaperEngineButton(BackgroundSettings style, Action apply)
    {
        if (!PlatformServices.Features.WallpaperEngine) return null;
        WallpaperEngineInstallation? installation = WallpaperEngineLibrary.Detect(PlatformServices.WallpaperEnginePaths);
        if (installation is null)
        {
            // 平台支持但没检测到安装：给一句说明，用户才知道要自己选本地视频。
            return new TextBlock
            {
                Text = "未检测到 Wallpaper Engine，可直接选择本地视频作为背景。",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
            };
        }

        Button button = CreateActionButton("从 Wallpaper Engine 导入", async () =>
        {
            // 网页壁纸只在平台支持时列出；视频壁纸始终可导入。
            bool web = PlatformServices.Features.WebWallpaper;
            IReadOnlyList<WallpaperProject> projects = web
                ? WallpaperEngineLibrary.ReadProjects(installation)
                : WallpaperEngineLibrary.ReadVideos(installation);
            if (projects.Count == 0)
            {
                await ShowMessageAsync(
                    "没有可导入的壁纸",
                    web ? "在已安装的 Wallpaper Engine 里没有找到视频或网页壁纸项目。" : "在已安装的 Wallpaper Engine 里没有找到视频壁纸项目。",
                    "知道了");
                return;
            }

            ListBox list = new()
            {
                Height = 320,
                Width = 520,
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = projects,
                // 与旧版一致：每行给出预览图、标题与类型（视频壁纸／网页壁纸）。
                ItemTemplate = new FuncDataTemplate<WallpaperProject>((project, _) => CreateWallpaperRow(project), true)
            };
            StackPanel content = new() { Width = 520, Spacing = 8 };
            content.Children.Add(list);
            DialogResult result = await ShowChoiceAsync(
                "从 Wallpaper Engine 导入",
                "选择要导入的壁纸；导入后会复制到程序数据目录，播放不再依赖 Wallpaper Engine。",
                "导入",
                string.Empty,
                "取消",
                content);
            if (result != DialogResult.Primary) return;

            List<int> selected = [.. list.Selection.SelectedIndexes];
            if (style.PlaylistEnabled && selected.Count > 0)
            {
                // 队列模式下可以一次导入多项。
                foreach (int index in selected) await AddImportedWallpaperAsync(style, projects[index], queue: true);
            }
            else
            {
                int index = selected.Count > 0 ? selected[0] : 0;
                await AddImportedWallpaperAsync(style, projects[index], queue: false);
            }

            apply();
            ScheduleSave();
        });
        return button;
    }

    /// <summary>
    /// Wallpaper Engine 导入列表的一行：预览图（没有预览图时占位）、标题与类型标签。
    /// 预览图只从项目目录内读取，缩略图按宽度解码，避免为列表里每张图展开完整位图。
    /// </summary>
    private static Control CreateWallpaperRow(WallpaperProject project)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 4) };
        Border frame = new()
        {
            Width = 96,
            Height = 54,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor())
        };
        if (TryLoadThumbnail(project.PreviewPath) is { } preview)
        {
            frame.Child = new Image { Source = preview, Stretch = Stretch.UniformToFill };
        }
        else
        {
            frame.Child = new TextBlock
            {
                Text = "无预览图",
                FontSize = 11,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        row.Children.Add(frame);
        StackPanel text = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = project.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 380
        });
        text.Children.Add(new TextBlock
        {
            Text = project.TypeLabel,
            FontSize = 12,
            Foreground = new SolidColorBrush(BoardTheme.TextColor.ToColor()) { Opacity = 0.65 }
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private async Task AddImportedWallpaperAsync(BackgroundSettings style, WallpaperProject project, bool queue)
    {
        try
        {
            string stored = await _viewModel.ImportWallpaperAsync(project);
            if (queue)
            {
                if (!style.Playlist.Contains(stored, StringComparer.OrdinalIgnoreCase)) style.Playlist.Add(stored);
            }
            else
            {
                style.ImagePath = stored;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            await ShowMessageAsync("导入壁纸失败", ex.Message, "知道了");
        }
    }
}
