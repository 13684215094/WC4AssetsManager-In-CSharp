using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public static class ProjectImageDecoder
{
    public static SKBitmap Decode(byte[] data)
    {
        if (data.AsSpan().StartsWith("PKM "u8)) return PkmDecoder.Decode(data);
        return SKBitmap.Decode(data) ?? throw new InvalidDataException("无法解码图片，请检查文件是否损坏或编码是否受支持。");
    }
}
