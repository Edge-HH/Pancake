using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace Pancake.Controls;

internal static class RichTextContentMetrics
{
    internal static double Width(RichEditBox editor)
    {
        var range = editor.Document.GetRange(0, int.MaxValue);
        if (range.EndPosition <= 1) return 0;
        range.EndPosition--;
        // RichEditBox 的 DesiredSize 会填满容器；使用文本引擎的实际字形范围。
        // ClientCoordinates 排除窗口位置，AllowOffClient 包含滚动视口外的文本。
        range.GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var bounds, out _);
        return Math.Ceiling(Math.Max(0, bounds.Width)) + editor.Padding.Left + editor.Padding.Right + 4;
    }
}
