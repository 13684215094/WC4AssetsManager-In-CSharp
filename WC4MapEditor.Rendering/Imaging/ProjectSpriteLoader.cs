using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Rendering.Imaging;

// Returned crops are owned by the caller; decoded atlas pages belong to this loader.
public sealed class ProjectSpriteLoader : IDisposable
{
    private readonly Dictionary<string, SKBitmap> _pages = new(StringComparer.OrdinalIgnoreCase);
    private long _revision = -1;
    private static readonly string[] DefaultAtlases = ["battleres_hd", "tacticalmap", "image/image_flags_hd"];

    public SKBitmap? Load(string name, params string[] atlases)
    {
        long revision = AssetManager.Default.Revision;
        if (_revision != revision) { Dispose(); _revision = revision; }
        if (!AssetManager.Default.IsLoaded) return null;
        foreach (string atlas in atlases.Length == 0 ? DefaultAtlases : atlases)
        {
            var parser = HdAtlasParser.Get(atlas);
            var definition = parser.GetImageDef(name);
            if (definition == null || parser.SurfaceData == null) continue;
            if (!_pages.TryGetValue(atlas, out var page))
                _pages[atlas] = page = ProjectImageDecoder.Decode(parser.SurfaceData);
            return BitmapPixels.Crop(page, definition.X, definition.Y, definition.Width, definition.Height);
        }
        return null;
    }

    public SKImage? LoadImage(string name, params string[] atlases)
    {
        using var bitmap = Load(name, atlases);
        return bitmap == null ? null : SKImage.FromBitmap(bitmap);
    }

    public void Dispose()
    {
        foreach (var page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
