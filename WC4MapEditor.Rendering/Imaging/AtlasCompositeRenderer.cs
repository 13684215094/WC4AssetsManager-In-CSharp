using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public static class AtlasCompositeRenderer
{
    public static SKBitmap Compose(SKBitmap? source, IReadOnlyList<TacticalMapObject> objects,
        IReadOnlyDictionary<string, SKBitmap> pixels, SKRectI? clip = null)
    {
        if (source == null || source.Width <= 0 || source.Height <= 0)
            throw new InvalidOperationException("Load a decodable image before saving.");
        var area = clip ?? new SKRectI(0, 0, source.Width, source.Height);
        if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("The output area is empty.");
        var result = new SKBitmap(new SKImageInfo(area.Width, area.Height, SKColorType.Rgba8888, SKAlphaType.Premul, source.ColorSpace));
        try
        {
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            // A plain raster has no atlas objects; retain its original pixels.
            if (objects.Count == 0) canvas.DrawBitmap(source, -area.Left, -area.Top);
            else foreach (var obj in objects)
            {
                if (obj.Width <= 0 || obj.Height <= 0 || !pixels.TryGetValue(obj.Name, out var bitmap))
                    throw new InvalidOperationException($"Missing or invalid image pixels: {obj.Name}");
                canvas.DrawBitmap(bitmap, new SKRect(0, 0, bitmap.Width, bitmap.Height),
                    new SKRect(obj.X - area.Left, obj.Y - area.Top, obj.X - area.Left + obj.Width, obj.Y - area.Top + obj.Height));
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static byte[] Encode(SKBitmap bitmap, string path)
    {
        var format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SKEncodedImageFormat.Png,
            ".webp" => SKEncodedImageFormat.Webp,
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            _ => throw new ArgumentException("Save images as PNG, WebP or JPEG; other formats require Save As.")
        };
        using var data = bitmap.Encode(format, 100) ?? throw new InvalidOperationException("Image encoding failed.");
        return data.ToArray();
    }
}
