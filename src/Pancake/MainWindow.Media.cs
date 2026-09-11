using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Controls;
using Pancake.Services;
using Windows.Storage.Pickers;

namespace Pancake;

public sealed partial class MainWindow
{
    private MediaLibrary? _mediaLibrary;
    private MediaLibrary MediaLibraryStore => _mediaLibrary ??= new(_dataStore.DataDirectory);

    private FileOpenPicker MediaPicker(bool videos)
    {
        FileOpenPicker picker = new();
        foreach (string extension in MediaLibrary.ImageExtensions.Concat(videos ? MediaLibrary.VideoExtensions : [])) picker.FileTypeFilter.Add(extension);
        InitializePicker(picker);
        return picker;
    }

    private StackPanel CreateBackgroundMediaEditor(BackgroundSettings style)
    {
        style.Playlist ??= [];
        StackPanel panel = SettingsStack(), rows = SettingsStack();
        bool busy = false;
        Button pick = new() { Content = "选择图片或视频" };
        panel.Children.Add(pick);
        async Task Import(IEnumerable<string> paths, IReadOnlyDictionary<string, WallpaperProject>? projects = null)
        {
            if (busy) return;
            busy = true;
            panel.IsHitTestVisible = false;
            try
            {
                foreach (string path in paths)
                {
                    string owned = projects is null ? await MediaLibraryStore.ImportAsync(path) : await MediaLibraryStore.ImportWallpaperAsync(projects[path]);
                    if (style.PlaylistEnabled)
                    {
                        if (!style.Playlist.Contains(owned, StringComparer.OrdinalIgnoreCase)) style.Playlist.Add(owned);
                    }
                    else style.ImagePath = owned;
                }
            }
            catch (Exception ex) { await ShowMessageAsync("无法设置背景", ex.Message, "知道了"); }
            finally { busy = false; panel.IsHitTestVisible = true; SettingChanged(); }
        }
        pick.Click += async (_, _) =>
        {
            try
            {
                var file = await MediaPicker(true).PickSingleFileAsync();
                if (file is not null) await Import([file.Path]);
            }
            catch (Exception ex) { await ShowMessageAsync("无法选择媒体", ex.Message, "知道了"); }
        };
        panel.Children.Add(new RecentImagesView(MediaLibraryStore, path => Import([path])));
        panel.Children.Add(Toggle("播放队列模式", style.PlaylistEnabled, value =>
        {
            style.PlaylistEnabled = value;
            if (value && style.Playlist.Count == 0 && File.Exists(style.ImagePath)) style.Playlist.Add(style.ImagePath);
        }));
        StackPanel queue = SettingsStack();
        Button add = new() { Content = "添加多个媒体" };
        add.Click += async (_, _) =>
        {
            try { var files = await MediaPicker(true).PickMultipleFilesAsync(); await Import(files.Select(file => file.Path)); }
            catch (Exception ex) { await ShowMessageAsync("无法选择媒体", ex.Message, "知道了"); }
        };
        queue.Children.Add(add);
        queue.Children.Add(rows);
        queue.Children.Add(Choice("播放顺序", ["按列表顺序", "随机播放"], ["Ordered", "Shuffle"], style.Shuffle ? "Shuffle" : "Ordered", value => style.Shuffle = value == "Shuffle"));
        queue.Children.Add(Toggle("按固定时间切换", style.SwitchOnTimer, value => style.SwitchOnTimer = value));
        NumberBox interval = new() { Header = "切换间隔（秒）", Minimum = 1, Maximum = 86400,
            Value = BackgroundPlaylist.NormalizeInterval(style.SwitchIntervalSeconds), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        interval.ValueChanged += (_, args) =>
        {
            if (!double.IsFinite(args.NewValue)) return;
            style.SwitchIntervalSeconds = BackgroundPlaylist.NormalizeInterval(args.NewValue); SettingChanged();
        };
        queue.Children.Add(interval);
        queue.Children.Add(Toggle("视频播放完成时切换", style.SwitchOnMediaEnded, value => style.SwitchOnMediaEnded = value));
        queue.Children.Add(Note("两种切换都开启时，先触发的条件生效。关闭播放完成切换后视频循环播放；图片、GIF 和网页壁纸通过定时切换。两个开关都关闭时停留在当前媒体。"));
        panel.Children.Add(queue);
        string signature = "";
        _refreshSettingAvailability.Add(() =>
        {
            queue.Visibility = style.PlaylistEnabled ? Visibility.Visible : Visibility.Collapsed;
            interval.IsEnabled = style.SwitchOnTimer;
            string current = string.Join("\n", style.Playlist);
            if (current == signature && rows.Children.Count > 0) return;
            signature = current;
            rows.Children.Clear();
            if (style.Playlist.Count == 0) rows.Children.Add(Note("队列为空，请添加图片或视频。"));
            for (int index = 0; index < style.Playlist.Count; index++)
            {
                int position = index;
                string path = style.Playlist[index];
                Grid row = new() { ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"{index + 1}. {MediaLibrary.DisplayName(path)}" + (File.Exists(path) ? "" : "（文件缺失）"),
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                StackPanel commands = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
                void Command(string label, bool enabled, Action action)
                {
                    Button button = new() { Content = label, IsEnabled = enabled };
                    button.Click += (_, _) => { action(); SettingChanged(); };
                    commands.Children.Add(button);
                }
                Command("上移", index > 0, () => (style.Playlist[position - 1], style.Playlist[position]) = (style.Playlist[position], style.Playlist[position - 1]));
                Command("下移", index < style.Playlist.Count - 1, () => (style.Playlist[position + 1], style.Playlist[position]) = (style.Playlist[position], style.Playlist[position + 1]));
                Command("移除", true, () => style.Playlist.RemoveAt(position));
                Grid.SetColumn(commands, 1); row.Children.Add(commands); rows.Children.Add(row);
            }
        });
        Button wallpaper = new() { Content = "从 Wallpaper Engine 导入", Visibility = Visibility.Collapsed };
        TextBlock detection = Note("正在检测 Wallpaper Engine…");
        panel.Children.Add(wallpaper); panel.Children.Add(detection);
        bool detected = false;
        WallpaperEngineInstallation? installation = null;
        panel.Loaded += async (_, _) =>
        {
            if (detected) return;
            detected = true;
            try
            {
                installation = await Task.Run(WallpaperEngineLibrary.Detect);
                wallpaper.Visibility = installation is null ? Visibility.Collapsed : Visibility.Visible;
                detection.Text = installation is null ? "未检测到 Wallpaper Engine，可直接选择本地视频。" : "已检测到 Wallpaper Engine，可导入视频和网页壁纸；场景和应用壁纸暂不支持。";
            }
            catch { detection.Text = "无法读取 Wallpaper Engine 安装信息，可直接选择本地视频。"; }
        };
        wallpaper.Click += async (_, _) =>
        {
            if (installation is null) return;
            wallpaper.IsEnabled = false;
            try
            {
                var projects = await Task.Run(() => WallpaperEngineLibrary.ReadProjects(installation));
                if (projects.Count == 0) { await ShowMessageAsync("没有可导入的壁纸", "请先在 Wallpaper Engine 下载视频或网页壁纸。场景和应用类型暂不支持。", "知道了"); return; }
                GridView list = CreateWallpaperGallery(projects, style.PlaylistEnabled);
                ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = "导入 Wallpaper Engine 壁纸", Content = list, PrimaryButtonText = "导入", CloseButtonText = "取消", IsPrimaryButtonEnabled = false };
                list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItems.Count > 0;
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var selected = list.SelectedItems.Cast<WallpaperProject>().ToDictionary(project => project.Path, StringComparer.OrdinalIgnoreCase);
                    await Import(selected.Keys, selected);
                }
            }
            catch (Exception ex) { await ShowMessageAsync("导入失败", ex.Message, "知道了"); }
            finally { wallpaper.IsEnabled = true; }
        };
        panel.Children.Add(Note("视频背景默认静音，支持 MP4 等 Windows 可解码的视频；动态 GIF 可作为图片背景使用。"));
        return panel;
    }

    private static GridView CreateWallpaperGallery(IReadOnlyList<WallpaperProject> projects, bool multiple) => new()
    {
        ItemsSource = projects, SelectionMode = multiple ? ListViewSelectionMode.Multiple : ListViewSelectionMode.Single,
        MaxHeight = 440, MinWidth = 420, IsItemClickEnabled = false,
        ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Width="180" Spacing="6" Margin="4">
                    <Border Height="104" CornerRadius="6" Background="{ThemeResource ControlFillColorSecondaryBrush}">
                        <Grid>
                            <TextBlock Text="无预览图" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.6"/>
                            <Image Stretch="UniformToFill">
                                <Image.Source><BitmapImage UriSource="{Binding PreviewUri}" DecodePixelWidth="360" AutoPlay="False"/></Image.Source>
                            </Image>
                        </Grid>
                    </Border>
                    <TextBlock Text="{Binding Title}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding Title}"/>
                    <TextBlock Text="{Binding TypeLabel}" Opacity="0.65" FontSize="12"/>
                </StackPanel>
            </DataTemplate>
            """)
    };

    private async Task<string?> SelectAttachmentImageAsync()
    {
        string? selected = null;
        StackPanel panel = SettingsStack();
        ContentDialog dialog = new() { XamlRoot = RootShell.XamlRoot, Title = "添加图片", Content = panel, CloseButtonText = "取消" };
        Button browse = new() { Content = "浏览图片" };
        bool browseRequested = false;
        browse.Click += (_, _) => { browseRequested = true; dialog.Hide(); };
        panel.Children.Add(browse);
        panel.Children.Add(new RecentImagesView(MediaLibraryStore, path => { selected = path; dialog.Hide(); return Task.CompletedTask; }));
        await dialog.ShowAsync();
        if (browseRequested) selected = (await MediaPicker(false).PickSingleFileAsync())?.Path;
        return selected;
    }
}
