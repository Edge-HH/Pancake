namespace Pancake.Models;

/// <summary>编辑会话共享的工具状态；笔迹本身始终存储在各自的科目中。</summary>
public sealed class InkToolSettings
{
    public Windows.UI.Color Color { get; set; } = Windows.UI.Color.FromArgb(255, 247, 247, 249);
    public double Thickness { get; set; } = 5;
    public bool Eraser { get; set; }
}
