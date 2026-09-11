using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>所有图片入口共用一行历史缩略图，视图卸载后不保留事件订阅。</summary>
public sealed class RecentImagesView : StackPanel
{
    private readonly MediaLibrary _library;
    private readonly Func<string, Task> _select;
    private readonly StackPanel _images = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    public RecentImagesView(MediaLibrary library, Func<string, Task> select)
    {
        _library = library;
        _select = select;
        Spacing = 8;
        Children.Add(new TextBlock { Text = "最近使用的图像" });
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
        foreach (string path in _library.RecentImages())
        {
            Button button = new() { Width = 88, Height = 88, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                Content = new Image { Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 176 }, Stretch = Stretch.UniformToFill } };
            AutomationProperties.SetName(button, "使用最近图片 " + Path.GetFileName(path));
            ToolTipService.SetToolTip(button, path);
            button.Click += async (_, _) => await _select(path);
            _images.Children.Add(button);
        }
        if (_images.Children.Count == 0) _images.Children.Add(new TextBlock { Text = "选择图片后会显示在这里", Opacity = .6 });
    }
}
