using System.Buffers.Binary;
using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public static class PkmDecoder
{
    private static readonly int[][] Modifiers =
    [
        [2, 8, -2, -8], [5, 17, -5, -17], [9, 29, -9, -29], [13, 42, -13, -42],
        [18, 60, -18, -60], [24, 80, -24, -80], [33, 106, -33, -106], [47, 183, -47, -183]
    ];

    public static SKBitmap Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..6].SequenceEqual("PKM 10"u8))
            throw new InvalidDataException("仅支持 PKM 1.0 / ETC1 RGB 图片。");
        int format = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        int encodedWidth = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int encodedHeight = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(data[14..]);
        if (format != 0 || width <= 0 || height <= 0 || (long)width * height > 16_777_216 ||
            encodedWidth != (width + 3) / 4 * 4 || encodedHeight != (height + 3) / 4 * 4 ||
            data.Length != 16L + (long)encodedWidth * encodedHeight / 2)
            throw new InvalidDataException("PKM 格式、尺寸或 ETC1 数据长度无效。");

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        try
        {
            var pixels = bitmap.GetPixelSpan();
            int offset = 16;
            for (int by = 0; by < encodedHeight; by += 4)
            for (int bx = 0; bx < encodedWidth; bx += 4, offset += 8)
            {
                uint high = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                uint low = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
                bool differential = (high & 2) != 0, flip = (high & 1) != 0;
                int r1, g1, b1, r2, g2, b2;
                if (differential)
                {
                    r1 = (int)(high >> 27 & 31); g1 = (int)(high >> 19 & 31); b1 = (int)(high >> 11 & 31);
                    r2 = r1 + Signed3(high >> 24); g2 = g1 + Signed3(high >> 16); b2 = b1 + Signed3(high >> 8);
                    if (r2 is < 0 or > 31 || g2 is < 0 or > 31 || b2 is < 0 or > 31)
                        throw new InvalidDataException("ETC1 差分颜色超出范围。");
                    r1 = Expand5(r1); g1 = Expand5(g1); b1 = Expand5(b1);
                    r2 = Expand5(r2); g2 = Expand5(g2); b2 = Expand5(b2);
                }
                else
                {
                    r1 = (int)(high >> 28 & 15) * 17; r2 = (int)(high >> 24 & 15) * 17;
                    g1 = (int)(high >> 20 & 15) * 17; g2 = (int)(high >> 16 & 15) * 17;
                    b1 = (int)(high >> 12 & 15) * 17; b2 = (int)(high >> 8 & 15) * 17;
                }
                int table1 = (int)(high >> 5 & 7), table2 = (int)(high >> 2 & 7);
                for (int y = 0; y < 4 && by + y < height; y++)
                for (int x = 0; x < 4 && bx + x < width; x++)
                {
                    // ETC1 selectors are column-major, with sign bits in the upper half.
                    int bit = x * 4 + y;
                    int selector = (int)((low >> bit & 1) | (low >> (bit + 16) & 1) << 1);
                    bool first = flip ? y < 2 : x < 2;
                    int delta = Modifiers[first ? table1 : table2][selector];
                    int pixel = (by + y) * bitmap.RowBytes + (bx + x) * 4;
                    pixels[pixel] = Clamp((first ? b1 : b2) + delta);
                    pixels[pixel + 1] = Clamp((first ? g1 : g2) + delta);
                    pixels[pixel + 2] = Clamp((first ? r1 : r2) + delta);
                    pixels[pixel + 3] = 255;
                }
            }
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static int Signed3(uint value)
    {
        int v = (int)(value & 7);
        return v >= 4 ? v - 8 : v;
    }
    private static int Expand5(int value) => value << 3 | value >> 2;
    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
