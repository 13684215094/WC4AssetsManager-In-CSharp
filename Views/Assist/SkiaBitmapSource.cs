using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal static class SkiaBitmapSource
{
    public static BitmapSource Create(SKBitmap bitmap)
    {
        using var converted = BitmapPixels.ToBgraPremultiplied(bitmap);
        var result = BitmapSource.Create(converted.Width, converted.Height, 96, 96,
            PixelFormats.Pbgra32, null, converted.GetPixels(),
            checked(converted.Height * converted.RowBytes), converted.RowBytes);
        result.Freeze();
        return result;
    }

    public static BitmapSource Load(string path)
    {
        using var bitmap = ProjectImageDecoder.Decode(ProjectFiles.ReadAllBytes(path));
        return Create(bitmap);
    }
}
