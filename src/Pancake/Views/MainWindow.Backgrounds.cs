using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Pancake.Controls;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 区域背景与磁贴外观：把设置里的背景配置应用到跨区、时钟区、作业板与磁贴四层，
/// 并提供图片选择、透明度与毛玻璃的编辑入口。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>把设置应用到四个背景层；跨区背景只在分屏模式下生效。</summary>
    private void ApplyBackgrounds()
    {
        bool shared = Settings.SharedBackgroundEnabled && Settings.LayoutMode == "Split";
        SharedBackgroundVisual.IsVisible = shared;
        if (shared) SharedBackgroundVisual.Apply(Settings.SharedBackground);
        ClockBackgroundVisual.Apply(Settings.ClockBackground);
        BoardBackgroundVisual.Apply(Settings.BoardBackground);
        foreach (SubjectTileControl tile in _tiles.Values) tile.ApplyBackground(Settings.TileBackground);
    }

    /// <summary>磁贴外观设置页：标题大小与磁贴背景。</summary>
    private void BuildTileAppearanceSettings()
    {
        Slider titleSize = CreateSlider(14, 72, 1, Settings.TileTitleSize, snapToTick: true);
        titleSize.ValueChanged += (_, _) =>
        {
            Settings.TileTitleSize = Math.Round(titleSize.Value);
            foreach (SubjectTileControl tile in _tiles.Values) tile.ApplyTitleSize(Settings.TileTitleSize);
            ScheduleSave();
        };

        SettingsContent.Children.Add(CreateCard(
            "标题",
            CreateRow("标题大小", "磁贴标题的字号（14–72）。", titleSize)));
        SettingsContent.Children.Add(CreateBackgroundCard(
            "磁贴背景", Settings.TileBackground, ApplyBackgrounds, allowVideo: VideoPlayerHost.IsSupported, previewKind: "Tile"));
    }

    /// <summary>背景板设置页：跨区背景与两个区域背景。</summary>
    private void BuildBackgroundSettings()
    {
        ToggleSwitch sharedToggle = CreateToggle(Settings.SharedBackgroundEnabled);
        sharedToggle.IsCheckedChanged += (_, _) =>
        {
            Settings.SharedBackgroundEnabled = sharedToggle.IsChecked == true;
            ApplyBackgrounds();
            ScheduleSave();
        };
        SettingsContent.Children.Add(CreateCard(
            "跨区背景",
            CreateRow("启用跨区背景", "仅在分屏模式生效：两区共用一张连续底图，区域底图仍可分别设置毛玻璃与模糊。", sharedToggle)));

        bool video = VideoPlayerHost.IsSupported;
        SettingsContent.Children.Add(CreateBackgroundCard(
            "跨区底图", Settings.SharedBackground, ApplyBackgrounds, allowVideo: video, previewKind: "Shared"));
        SettingsContent.Children.Add(CreateBackgroundCard(
            "时钟区域背景", Settings.ClockBackground, ApplyBackgrounds, allowVideo: video, previewKind: "Clock"));
        SettingsContent.Children.Add(CreateBackgroundCard(
            "作业板区域背景", Settings.BoardBackground, ApplyBackgrounds, allowVideo: video, previewKind: "Board"));
    }

    /// <summary>
    /// 背景编辑卡片：颜色层（含透明度与清除）、图片、显示模式与毛玻璃。
    /// 跨区底图沿用同一套控件，保证三处背景的编辑体验一致。
    /// </summary>
    private Control CreateBackgroundCard(
        string title,
        BackgroundSettings style,
        Action apply,
        bool allowVideo,
        string? previewKind = null)
    {
        Control? preview = previewKind is null ? null : CreateAppearancePreview(previewKind);
        ToggleSwitch glass = CreateToggle(style.Glass);
        glass.IsCheckedChanged += (_, _) => { style.Glass = glass.IsChecked == true; apply(); ScheduleSave(); };

        Slider blur = CreateSlider(0, 100, 1, style.Blur, snapToTick: true);
        blur.ValueChanged += (_, _) => { style.Blur = Math.Round(blur.Value); apply(); ScheduleSave(); };

        ComboBox mode = CreateComboBox(
            ["缩放（铺满裁剪）", "拉伸（填满）", "适应（完整显示）"],
            style.ImageMode switch { "Stretch" => 1, "Fit" => 2, _ => 0 },
            index =>
            {
                style.ImageMode = index switch { 1 => "Stretch", 2 => "Fit", _ => "Zoom" };
                apply();
                ScheduleSave();
            });

        TextBlock path = new()
        {
            Text = string.IsNullOrWhiteSpace(style.ImagePath) ? "未选择" : Path.GetFileName(style.ImagePath),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            VerticalAlignment = VerticalAlignment.Center
        };
        Button choose = CreateActionButton(allowVideo ? "选择图片或视频" : "选择图片", async () =>
        {
            string? picked = await PickMediaAsync(allowVideo);
            if (picked is null) return;
            string stored = await _viewModel.ImportRecentImageAsync(picked);
            style.ImagePath = stored;
            path.Text = Path.GetFileName(stored);
            apply();
            ScheduleSave();
        });
        Button clear = CreateActionButton("清除背景媒体", () =>
        {
            style.ImagePath = string.Empty;
            path.Text = "未选择";
            apply();
            ScheduleSave();
        });
        StackPanel mediaRow = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        mediaRow.Children.Add(choose);
        mediaRow.Children.Add(clear);
        mediaRow.Children.Add(path);

        // 最近使用的图像：与图片附件、导出页共用同一份历史。
        Control recent = CreateRecentImagesStrip(picked =>
        {
            style.ImagePath = picked;
            path.Text = Path.GetFileName(picked);
            apply();
            ScheduleSave();
        });

        StackPanel card = new() { Spacing = 18 };
        card.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        // 预览放在卡片标题下方：改颜色、图片、毛玻璃时立刻能看到效果。
        if (preview is not null) card.Children.Add(preview);
        card.Children.Add(CreateRow("背景颜色", "颜色层与透明度只影响底色；清除颜色后仍保留毛玻璃。", CreateSurfaceColorEditor(
                () => style.Color,
                value => { style.Color = value; style.ColorCleared = false; apply(); ScheduleSave(); },
                () => style.ColorOpacity,
                value => { style.ColorOpacity = value; apply(); ScheduleSave(); },
                () => style.ColorCleared,
                cleared => { style.ColorCleared = cleared; apply(); ScheduleSave(); })));
        card.Children.Add(CreateRow("背景媒体",
            allowVideo
                ? "支持图片与视频，选择后会复制到程序数据目录；视频默认静音、循环播放，动态 GIF 按图片背景显示。"
                : "支持图片，选择后会复制到程序数据目录；动态 GIF 按图片背景显示。",
            mediaRow));
        if (recent.IsVisible) card.Children.Add(recent);
        if (CreateWallpaperEngineButton(style, apply) is { } wallpaper) card.Children.Add(wallpaper);
        card.Children.Add(CreateRow("显示模式", "缩放为保持比例铺满并裁剪，拉伸为填满区域，适应为保持比例完整显示。", mode));
        card.Children.Add(CreateRow("毛玻璃", "只作用于背景媒体，颜色层保持清晰。", glass));
        card.Children.Add(CreateRow("模糊程度", "毛玻璃的模糊半径（0–100）。", blur));
        card.Children.Add(CreatePlaylistEditor(style, apply, allowVideo));
        return new Border
        {
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor()),
            BorderBrush = new SolidColorBrush(BoardTheme.LineColor.ToColor()),
            BorderThickness = new Thickness(1),
            Child = card
        };
    }

    /// <summary>选择背景媒体文件；返回本机路径，取消时返回 null。</summary>
    private async Task<string?> PickMediaAsync(bool allowVideo)
    {
        string[] extensions = allowVideo
            ? [.. MediaLibrary.ImageExtensions, .. MediaLibrary.VideoExtensions]
            : MediaLibrary.ImageExtensions;
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = allowVideo ? "选择图片或视频" : "选择图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(allowVideo ? "图片或视频" : "图片")
                {
                    Patterns = extensions.Select(extension => "*" + extension).ToArray()
                }
            ]
        });
        return files.Select(file => file.TryGetLocalPath()).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }
}
