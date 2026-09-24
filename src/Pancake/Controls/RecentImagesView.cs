using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>图片入口共用一行图片历史；磁贴与背景板的最近媒体按使用面分开显示，互不串扰。</summary>
public sealed class RecentImagesView : StackPanel
{
    private readonly MediaLibrary _library;
    private readonly Func<string, Task> _select;
    private readonly MediaScope _scope;
    private readonly StackPanel _images = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    public RecentImagesView(MediaLibrary library, Func<string, Task> select, MediaScope scope = MediaScope.Images)
    {
        _library = library;
        _select = select;
        _scope = scope;
        Spacing = 8;
        Children.Add(new TextBlock { Text = scope == MediaScope.Images ? "最近使用的图像" : "最近媒体" });
        Children.Add(new ScrollViewer { Content = _images, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled });
        Loaded += (_, _) => { _library.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => _library.Changed -= Refresh;
        Refresh();
    }
    private void Refresh()
    {
        _images.Children.Clear();
        foreach (string path in _library.RecentMedia(_scope))
        {
            bool image = MediaLibrary.IsImage(path);
            // 历史列表只展示静态封面/类型标签，不能为每个视频或网页启动播放器。
            object preview = image
                ? new Image { Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 176, AutoPlay = false }, Stretch = Stretch.UniformToFill }
                : new TextBlock { Text = (MediaLibrary.IsVideo(path) ? "视频" : "网页壁纸") + "\n" + MediaLibrary.DisplayName(path),
                    TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6) };
            Button button = new() { Width = 88, Height = 88, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                Content = preview };
            AutomationProperties.SetName(button, "使用最近媒体 " + MediaLibrary.DisplayName(path));
            ToolTipService.SetToolTip(button, path);
            button.Click += async (_, _) => await _select(path);
            _images.Children.Add(button);
        }
        if (_images.Children.Count == 0) _images.Children.Add(new TextBlock
        {
            Text = _scope == MediaScope.Images ? "选择图片后会显示在这里" : "导入图片、视频或动态壁纸后会显示在这里",
            Opacity = .6
        });
    }
}
