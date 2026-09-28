using QRCoder;

namespace Pancake.Services;

/// <summary>
/// 二维码 PNG 生成，验证器绑定弹窗扫码用。
/// 固定黑模块、白底并带静区，保证深浅色主题下都能被验证器应用识别。
/// </summary>
public static class QrCodeImage
{
    public static byte[] CreatePng(string payload, int pixelsPerModule = 6)
    {
        QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        PngByteQRCode code = new(data);
        return code.GetGraphic(pixelsPerModule, [0, 0, 0], [255, 255, 255], drawQuietZones: true);
    }
}
