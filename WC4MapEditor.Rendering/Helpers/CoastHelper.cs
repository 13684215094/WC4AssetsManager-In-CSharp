using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Rendering.Helpers;

public sealed class CoastHelper : IDisposable
{
    private static readonly Lazy<CoastHelper> Shared = new(() => new CoastHelper());
    public static CoastHelper Instance => Shared.Value;

    private readonly Dictionary<string, SKImage> _coastImageCache = new();
    private readonly List<string> _coastImageAccessOrder = new();
    private const int MaxCoastImageCacheSize = 150;
    private readonly Dictionary<string, SKImage> _hexagonCoastCache = new();
    private readonly List<string> _hexagonCoastAccessOrder = new();
    private const int MaxHexagonCacheSize = 150;
    private SKImage? _coastAtlasImage;
    private SKImage? _grayLevelAtlasImage;
    private readonly Dictionary<string, SKRect> _coastAtlasSpriteRects = new();
    private readonly Dictionary<string, SKPoint> _coastAtlasSpriteOrigins = new();
    private readonly Dictionary<string, SKRect> _grayLevelAtlasSpriteRects = new();
    private readonly Dictionary<string, SKPoint> _grayLevelAtlasSpriteOrigins = new();
    private bool _coastAtlasLoaded;
    private bool _grayLevelAtlasLoaded;
    private long _revision = -1;

    public class CoastSpriteInfo
    {
        public string Name { get; set; } = "";
        public int RefX { get; set; }
        public int RefY { get; set; }
    }

    public bool IsAtlasLoaded { get { RefreshResources(); return _coastAtlasLoaded; } }
    public bool IsGrayLevelAtlasLoaded { get { RefreshResources(); return _grayLevelAtlasLoaded; } }
    public CoastHelper() => RefreshResources();

    private void RefreshResources()
    {
        long revision = AssetManager.Default.Revision;
        if (_revision == revision) return;
        ClearCache();
        _coastAtlasImage?.Dispose();
        _grayLevelAtlasImage?.Dispose();
        _coastAtlasImage = LoadAtlas("coast_hd", _coastAtlasSpriteRects, _coastAtlasSpriteOrigins);
        _grayLevelAtlasImage = LoadAtlas("coastmask_hd", _grayLevelAtlasSpriteRects, _grayLevelAtlasSpriteOrigins);
        _coastAtlasLoaded = _coastAtlasImage != null;
        _grayLevelAtlasLoaded = _grayLevelAtlasImage != null;
        _revision = revision;
    }

    private static SKImage? LoadAtlas(string name, Dictionary<string, SKRect> rectangles, Dictionary<string, SKPoint> origins)
    {
        rectangles.Clear();
        origins.Clear();
        var parser = HdAtlasParser.Get(name);
        if (parser.SurfaceData == null) return null;
        try
        {
            using var bitmap = ProjectImageDecoder.Decode(parser.SurfaceData);
            foreach (var definition in parser.ImageDefinitions)
            {
                if (definition.X < 0 || definition.Y < 0 || definition.Width <= 0 || definition.Height <= 0 ||
                    (long)definition.X + definition.Width > bitmap.Width || (long)definition.Y + definition.Height > bitmap.Height) continue;
                rectangles[definition.Name] = new SKRect(definition.X, definition.Y,
                    definition.X + definition.Width, definition.Y + definition.Height);
                origins[definition.Name] = new SKPoint(definition.RefX, definition.RefY);
            }
            return SKImage.FromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] {name}: {ex.Message}");
            return null;
        }
    }

    public string? GetCoastSpriteName(int doodad1Group, int doodad1Id)
    {
        RefreshResources();
        if (doodad1Group != 31 || doodad1Id is < 11 or > 73) return null;
        // Existing coast adjacency rules encode the 63 standard tiles as 11..73.
        string name = $"coast00{doodad1Id - 10}.png";
        return _coastAtlasSpriteRects.ContainsKey(name) ? name : null;
    }

    public SKImage? GetCoastImageByDoodad(int group, int id)
    {
        string? name = GetCoastSpriteName(group, id);
        return name == null ? null : ExtractTile("coast:" + name, _coastAtlasImage, _coastAtlasSpriteRects, name);
    }

    public SKImage? GetGrayLevelCoastImage(string name)
    {
        RefreshResources();
        return ExtractTile("mask:" + name, _grayLevelAtlasImage, _grayLevelAtlasSpriteRects, name);
    }

    private SKImage? ExtractTile(string key, SKImage? atlas, Dictionary<string, SKRect> rectangles, string name)
    {
        if (_coastImageCache.TryGetValue(key, out var cached)) return cached;
        if (atlas == null || !rectangles.TryGetValue(name, out var rectangle)) return null;
        var tile = atlas.Subset(SKRectI.Round(rectangle));
        if (tile == null) return null;
        while (_coastImageCache.Count >= MaxCoastImageCacheSize)
        {
            string oldest = _coastImageAccessOrder[0];
            _coastImageAccessOrder.RemoveAt(0);
            _coastImageCache.Remove(oldest, out var image);
            image?.Dispose();
        }
        _coastImageCache[key] = tile;
        _coastImageAccessOrder.Add(key);
        return tile;
    }

    public CoastSpriteInfo? GetGrayLevelCoastSpriteInfo(string name)
    {
        RefreshResources();
        return _grayLevelAtlasSpriteOrigins.TryGetValue(name, out var origin)
            ? new CoastSpriteInfo { Name = name, RefX = (int)origin.X, RefY = (int)origin.Y } : null;
    }

    public List<string> GetGrayLevelSpriteNames() { RefreshResources(); return _grayLevelAtlasSpriteRects.Keys.ToList(); }
    public SKImage? GetCoastAtlasImage() { RefreshResources(); return _coastAtlasImage; }
    public SKRect? GetCoastAtlasSpriteRect(string name) { RefreshResources(); return _coastAtlasSpriteRects.TryGetValue(name, out var rect) ? rect : null; }
    public SKPoint? GetCoastAtlasSpriteOrigin(string name) { RefreshResources(); return _coastAtlasSpriteOrigins.TryGetValue(name, out var origin) ? origin : null; }

    public SKImage? GetHexagonCoastImage(string spriteName)
    {
        RefreshResources();
        lock (_hexagonCoastCache)
        {
            if (_hexagonCoastCache.TryGetValue(spriteName, out var cached))
            {
                _hexagonCoastAccessOrder.Remove(spriteName);
                _hexagonCoastAccessOrder.Add(spriteName);
                return cached;
            }
        }
        return ExtractHexagonFromAtlas(spriteName);
    }

    private SKImage? ExtractHexagonFromAtlas(string spriteName)
    {
        if (!_coastAtlasLoaded || !_coastAtlasSpriteRects.ContainsKey(spriteName)) return null;

        try
        {
            var srcRect = _coastAtlasSpriteRects[spriteName];
            var width = (int)srcRect.Width;
            var height = (int)srcRect.Height;

            float hexSide = width / 2f;
            float hexHeight = hexSide * (float)Math.Sqrt(3);
            if (hexHeight > height)
            {
                hexHeight = height;
                hexSide = hexHeight / (float)Math.Sqrt(3);
            }

            float verticalMargin = (height - hexHeight) / 2;
            float centerX = width / 2f;
            float centerY = height / 2f;

            using var hexPath = new SKPath();
            hexPath.MoveTo(centerX - hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide / 2, verticalMargin);
            hexPath.LineTo(centerX + hexSide, centerY);
            hexPath.LineTo(centerX + hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide / 2, height - verticalMargin);
            hexPath.LineTo(centerX - hexSide, centerY);
            hexPath.Close();

            var imgInfo = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(imgInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.ClipPath(hexPath);
            canvas.DrawImage(_coastAtlasImage!, srcRect, new SKRect(0, 0, width, height));
            var hexImage = surface.Snapshot();

            if (hexImage != null)
            {
                lock (_hexagonCoastCache)
                {
                    _hexagonCoastCache[spriteName] = hexImage;
                    _hexagonCoastAccessOrder.Add(spriteName);
                    while (_hexagonCoastCache.Count > MaxHexagonCacheSize)
                    {
                        var oldestKey = _hexagonCoastAccessOrder[0];
                        _hexagonCoastAccessOrder.RemoveAt(0);
                        if (_hexagonCoastCache.Remove(oldestKey, out var oldest))
                            oldest?.Dispose();
                    }
                }
            }
            return hexImage;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CoastHelper] 裁剪六边形图块失败 [{spriteName}]: {ex.Message}");
            return null;
        }
    }

    public void PreGenerateAllHexagonCoasts(Action<int, string>? progressCallback)
    {
        RefreshResources();
        if (!_coastAtlasLoaded)
        {
            progressCallback?.Invoke(100, "图集未加载，跳过六边形裁剪");
            return;
        }

        var spriteNames = _coastAtlasSpriteRects.Keys.ToList();
        var totalCount = spriteNames.Count;
        Debug.WriteLine($"[CoastHelper] 开始预生成六边形图块，共 {totalCount} 个...");

        for (int i = 0; i < totalCount; i++)
        {
            var spriteName = spriteNames[i];
            try
            {
                var progress = (int)((i / (double)totalCount) * 100);
                progressCallback?.Invoke(progress, $"裁剪海岸线图块... ({i + 1}/{totalCount})");
                GetHexagonCoastImage(spriteName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CoastHelper] 预生成六边形图块失败 [{spriteName}]: {ex.Message}");
            }
        }

        progressCallback?.Invoke(100, $"海岸线图块裁剪完成 ({totalCount})");
    }

    public void ClearCache()
    {
        foreach (var image in _coastImageCache.Values) image.Dispose();
        _coastImageCache.Clear();
        _coastImageAccessOrder.Clear();
        foreach (var image in _hexagonCoastCache.Values) image.Dispose();
        _hexagonCoastCache.Clear();
        _hexagonCoastAccessOrder.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        _coastAtlasImage?.Dispose(); _coastAtlasImage = null;
        _grayLevelAtlasImage?.Dispose(); _grayLevelAtlasImage = null;
        _coastAtlasLoaded = _grayLevelAtlasLoaded = false;
        _revision = -1;
    }
}
