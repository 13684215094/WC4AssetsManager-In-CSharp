using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public static class BitmapPixels
{
    // WPF Pbgra32 requires both this channel order and premultiplied alpha.
    public static SKBitmap ToBgraPremultiplied(SKBitmap source)
    {
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static SKBitmap Crop(SKBitmap source, int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            (long)x + width > source.Width || (long)y + height > source.Height)
            throw new InvalidDataException($"图块范围超出图片：({x}, {y}, {width}, {height}) / {source.Width} × {source.Height}");
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, -x, -y);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
