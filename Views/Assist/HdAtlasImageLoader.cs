using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal static class HdAtlasImageLoader
{
    private sealed class AtlasCache(SKBitmap surface)
    {
        public SKBitmap Surface { get; } = surface;
        public Dictionary<string, BitmapSource> Tiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, AtlasCache> Caches = new(StringComparer.OrdinalIgnoreCase);
    private static long _revision = -1;

    public static BitmapSource? Load(string atlasBase, string imageName)
    {
        try { return LoadRequired(atlasBase, imageName); }
        catch (Exception ex) { Debug.WriteLine($"[HdAtlasImageLoader] {ex.Message}"); return null; }
    }

    public static BitmapSource LoadRequired(string atlasBase, string imageName)
    {
        lock (Gate)
        {
            if (_revision != AssetManager.Default.Revision)
            {
                ClearCache();
                _revision = AssetManager.Default.Revision;
            }
            var parser = HdAtlasParser.Get(atlasBase);
            if (!parser.IsLoaded) throw new InvalidDataException(parser.LastError ?? $"图集加载失败：{atlasBase}");
            var def = parser.GetImageDef(imageName) ?? parser.GetImageDef(imageName + ".png") ?? parser.GetImageDef(imageName + ".webp")
                ?? throw new FileNotFoundException($"当前项目图集中没有 {imageName}：{atlasBase}");
            if (!Caches.TryGetValue(atlasBase, out var cache))
            {
                cache = new AtlasCache(ProjectImageDecoder.Decode(parser.SurfaceData!));
                Caches.Add(atlasBase, cache);
            }
            if (cache.Tiles.TryGetValue(def.Name, out var tile)) return tile;
            using var subset = BitmapPixels.Crop(cache.Surface, def.X, def.Y, def.Width, def.Height);
            tile = SkiaBitmapSource.Create(subset);
            cache.Tiles.Add(def.Name, tile);
            return tile;
        }
    }

    public static void ClearCache()
    {
        lock (Gate)
        {
            foreach (var cache in Caches.Values) cache.Surface.Dispose();
            Caches.Clear();
            _revision = -1;
        }
    }
}
