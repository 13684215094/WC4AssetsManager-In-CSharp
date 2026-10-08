using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Rendering.Helpers;

public class TerrainHelper
{
    private readonly Dictionary<string, SKImage> _skImageCache = new();
    private readonly List<string> _skImageAccessOrder = new();
    private const int MaxSkImageCacheSize = 30;

    private readonly TextureDiskCache _diskCache = TextureDiskCache.Instance;
    private readonly bool _useDiskCache = true;

    private Dictionary<string, string> _terrainTypes = new();
    private Dictionary<string, string> _terrainMapping = new();
    private Dictionary<string, int> _terrainImageCounts = new();
    private Dictionary<string, string> _terrainImages = new();

    private readonly string _textureFolderPath;
    private readonly Dictionary<int, MapTerrainEntry> _projectTerrains;
    private readonly Dictionary<string, SKImage?> _atlasSurfaces = new();
    private readonly Dictionary<string, SKImage> _atlasTiles = new();
    private readonly List<string> _atlasTileOrder = new();
    private static readonly string[] TerrainAtlases = { "terrain_hd", "plant_hd", "buildings_hd", "terrain", "plant", "buildings" };

    public TerrainHelper(string textureFolderName)
    {
        ConfigManager.Instance.Initialize();
        _textureFolderPath = ConfigManager.Instance.GetTexturePath(textureFolderName);
        _projectTerrains = ConfigManager.Instance.GetMapTerrainEntries().GroupBy(entry => entry.Terrain)
            .ToDictionary(group => group.Key, group => group.Last());
        Debug.WriteLine($"[TerrainHelper] 纹理文件夹路径: {_textureFolderPath}");
        LoadTerrainConfig();
    }

    private void LoadTerrainConfig()
    {
        var configPath = Path.Combine(_textureFolderPath, "manager.json");
        if (!File.Exists(configPath))
        {
            Debug.WriteLine($"[TerrainHelper] 配置文件不存在: {configPath}");
            return;
        }

        try
        {
            var jsonContent = File.ReadAllText(configPath);
            using var config = JsonDocument.Parse(jsonContent);

            if (config.RootElement.TryGetProperty("terrain_types", out var typesEl))
            {
                _terrainTypes = new Dictionary<string, string>();
                foreach (var prop in typesEl.EnumerateObject())
                    _terrainTypes[prop.Name] = prop.Value.GetString() ?? "";
                Debug.WriteLine($"[TerrainHelper] 加载了 {_terrainTypes.Count} 个地形类型");
            }

            if (config.RootElement.TryGetProperty("terrain_mapping", out var mappingEl))
            {
                _terrainMapping = new Dictionary<string, string>();
                foreach (var prop in mappingEl.EnumerateObject())
                    _terrainMapping[prop.Name] = prop.Value.GetString() ?? "";
            }

            if (config.RootElement.TryGetProperty("terrain_image_counts", out var countsEl))
            {
                _terrainImageCounts = new Dictionary<string, int>();
                foreach (var prop in countsEl.EnumerateObject())
                    _terrainImageCounts[prop.Name] = prop.Value.GetInt32();
            }

            if (config.RootElement.TryGetProperty("terrain_images", out var imagesEl))
            {
                _terrainImages = new Dictionary<string, string>();
                foreach (var prop in imagesEl.EnumerateObject())
                    _terrainImages[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerrainHelper] 加载配置失败: {ex.Message}");
        }
    }

    public SKImage? GetTerrainSkImage(int terrainId, int decorationType = 0)
    {
        var projectImage = GetProjectTerrainImage(terrainId, decorationType);
        if (projectImage != null) return projectImage;
        var terrainTypeName = GetTerrainTypeName(terrainId);
        if (string.IsNullOrEmpty(terrainTypeName))
            return GetDefaultSkImage();

        var imageKey = GetTerrainImageKey(terrainTypeName, decorationType);

        if (_skImageCache.TryGetValue(imageKey, out var cached))
        {
            _skImageAccessOrder.Remove(imageKey);
            _skImageAccessOrder.Add(imageKey);
            return cached;
        }

        if (_useDiskCache)
        {
            var diskImage = _diskCache.GetTexture(imageKey);
            if (diskImage != null)
            {
                AddToSkImageCache(imageKey, diskImage);
                return diskImage;
            }
        }

        if (!_terrainImages.TryGetValue(imageKey, out var imageFileName) || string.IsNullOrEmpty(imageFileName))
            return GetDefaultSkImage();

        var imagePath = Path.Combine(_textureFolderPath, imageFileName);
        if (!File.Exists(imagePath))
            return GetDefaultSkImage();

        SKImage? skImage = null;
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap != null)
                skImage = SKImage.FromBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerrainHelper] 加载纹理失败: {imagePath} - {ex.Message}");
            return GetDefaultSkImage();
        }

        if (skImage != null)
        {
            if (_useDiskCache)
                _diskCache.SaveTexture(imageKey, skImage);
            AddToSkImageCache(imageKey, skImage);
        }

        return skImage;
    }

    private SKImage? GetProjectTerrainImage(int terrainId, int decorationType)
    {
        if (!_projectTerrains.TryGetValue(terrainId, out var terrain) || terrain.TileCount == 0 ||
            !terrain.TileImages.TryGetValue(decorationType % terrain.TileCount, out string? name)) return null;
        if (_atlasTiles.TryGetValue(name, out var cached))
        {
            _atlasTileOrder.Remove(name);
            _atlasTileOrder.Add(name);
            return cached;
        }

        foreach (string atlasName in TerrainAtlases)
        {
            var parser = HdAtlasParser.Get(atlasName);
            var def = parser.GetImageDef(name);
            if (def == null || parser.SurfaceData == null) continue;
            try
            {
                if (!_atlasSurfaces.TryGetValue(atlasName, out var surface))
                {
                    surface = SKImage.FromEncodedData(parser.SurfaceData);
                    _atlasSurfaces[atlasName] = surface;
                }
                if (surface == null || def.X < 0 || def.Y < 0 ||
                    (long)def.X + def.Width > surface.Width || (long)def.Y + def.Height > surface.Height) continue;
                var tile = surface.Subset(new SKRectI(def.X, def.Y, def.X + def.Width, def.Y + def.Height));
                if (tile == null) continue;
                // These images belong to this helper, independent of the bundled texture disk cache.
                if (_atlasTiles.Count >= MaxSkImageCacheSize)
                {
                    string oldest = _atlasTileOrder[0];
                    _atlasTileOrder.RemoveAt(0);
                    _atlasTiles.Remove(oldest, out var expired);
                    expired?.Dispose();
                }
                _atlasTiles[name] = tile;
                _atlasTileOrder.Add(name);
                return tile;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TerrainHelper] 加载项目地形失败: {atlasName}/{name} - {ex.Message}");
                _atlasSurfaces[atlasName] = null;
            }
        }
        return null;
    }

    private void AddToSkImageCache(string imageKey, SKImage skImage)
    {
        if (_skImageCache.Count >= MaxSkImageCacheSize)
        {
            var oldestKey = _skImageAccessOrder[0];
            _skImageAccessOrder.RemoveAt(0);
            _skImageCache.Remove(oldestKey, out _);
        }

        _skImageCache[imageKey] = skImage;
        _skImageAccessOrder.Add(imageKey);
    }

    public string? GetTerrainTypeName(int terrainId)
    {
        var terrainIdStr = terrainId.ToString();
        return _terrainTypes.TryGetValue(terrainIdStr, out var name) ? name : null;
    }

    public int GetTerrainVariantCount(int terrainId)
    {
        if (_projectTerrains.TryGetValue(terrainId, out var terrain) && terrain.TileCount > 0)
            return terrain.TileCount;
        var terrainTypeName = GetTerrainTypeName(terrainId);
        if (string.IsNullOrEmpty(terrainTypeName)) return 16;

        if (_terrainMapping.TryGetValue(terrainTypeName, out var mappedKey))
        {
            if (_terrainImageCounts.TryGetValue(mappedKey, out var count))
                return count;
        }

        return 16;
    }

    private string GetTerrainImageKey(string terrainTypeName, int decorationType)
    {
        if (_terrainMapping.TryGetValue(terrainTypeName, out var mappedKey))
        {
            if (_terrainImageCounts.TryGetValue(mappedKey, out var imageCount) && imageCount > 1)
            {
                var variantIndex = (decorationType % imageCount) + 1;
                return $"{mappedKey}_{variantIndex}";
            }
            return mappedKey;
        }
        return terrainTypeName;
    }

    public Dictionary<int, string> GetAllTerrainTypes()
    {
        var result = new Dictionary<int, string>();
        foreach (var kvp in _terrainTypes)
        {
            if (int.TryParse(kvp.Key, out int id))
                result[id] = kvp.Value;
        }
        return result;
    }

    public void ClearAllCaches()
    {
        var count = _skImageCache.Count;
        _skImageCache.Clear();
        _skImageAccessOrder.Clear();
        foreach (var image in _atlasTiles.Values) image.Dispose();
        _atlasTiles.Clear();
        _atlasTileOrder.Clear();
        foreach (var image in _atlasSurfaces.Values) image?.Dispose();
        _atlasSurfaces.Clear();
        Debug.WriteLine($"[TerrainHelper] 内存缓存已清空，释放了 {count} 个纹理引用");
    }

    private static SKImage GetDefaultSkImage()
    {
        try
        {
            using var bitmap = new SKBitmap(64, 64);
            bitmap.Erase(new SKColor(128, 128, 128));
            return SKImage.FromBitmap(bitmap);
        }
        catch { return null!; }
    }
}
