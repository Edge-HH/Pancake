using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace Pancake.Controls;

/// <summary>导出不使用桌面合成毛玻璃：先渲染背景像素，再生成同坐标系的模糊层。</summary>
internal static class ExportBackdrop
{
    internal static async Task<(WriteableBitmap Image, WriteableBitmap Blurred)> RenderAsync(
        string path, Color color, int width, int height, double blur, string mode)
    {
        CanvasDevice device = CanvasDevice.GetSharedDevice();
        using CanvasRenderTarget source = new(device, width, height, 96);
        using (var drawing = source.CreateDrawingSession())
        {
            drawing.Clear(color);
            if (!string.IsNullOrWhiteSpace(path))
            {
                using CanvasBitmap image = await CanvasBitmap.LoadAsync(device, path);
                double scale = mode == "Fit" ? Math.Min(width / image.Size.Width, height / image.Size.Height)
                    : Math.Max(width / image.Size.Width, height / image.Size.Height);
                double w = mode == "Stretch" ? width : image.Size.Width * scale;
                double h = mode == "Stretch" ? height : image.Size.Height * scale;
                drawing.DrawImage(image, new Rect((width - w) / 2, (height - h) / 2, w, h), new Rect(0, 0, image.Size.Width, image.Size.Height));
            }
        }
        var plain = ToBitmap(source, width, height);
        if (blur <= 0) return (plain, plain);
        using GaussianBlurEffect effect = new() { Source = source, BlurAmount = (float)Math.Clamp(blur, 0, 100), BorderMode = EffectBorderMode.Hard };
        using CanvasRenderTarget blurred = new(device, width, height, 96);
        using (var drawing = blurred.CreateDrawingSession()) { drawing.Clear(Microsoft.UI.Colors.Transparent); drawing.DrawImage(effect); }
        return (plain, ToBitmap(blurred, width, height));
    }

    private static WriteableBitmap ToBitmap(CanvasRenderTarget image, int width, int height)
    {
        WriteableBitmap bitmap = new(width, height);
        using Stream buffer = bitmap.PixelBuffer.AsStream();
        byte[] pixels = image.GetPixelBytes();
        buffer.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }
}
