using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Rendering.Helpers;

public class TerrainHelper
{
    private const int MaxSkImageCacheSize = 30;
    private SKImage? _defaultImage;
    private Dictionary<int, MapTerrainEntry> _projectTerrains;
    private long _revision = -1;
    private readonly Dictionary<string, SKImage?> _atlasSurfaces = new();
    private readonly Dictionary<string, SKImage> _atlasTiles = new();
    private readonly List<string> _atlasTileOrder = new();
    private static readonly string[] TerrainAtlases = { "terrain_hd", "plant_hd", "buildings_hd", "terrain", "plant", "buildings" };

    public TerrainHelper(string textureFolderName)
    {
        // The parameter remains for existing callers; game textures come from the active project.
        ConfigManager.Instance.Initialize();
        _projectTerrains = ConfigManager.Instance.GetMapTerrainEntries().GroupBy(entry => entry.Terrain)
            .ToDictionary(group => group.Key, group => group.Last());
        _revision = AssetManager.Default.Revision;
    }

    public SKImage? GetTerrainSkImage(int terrainId, int decorationType = 0)
    {
        RefreshProjectResources();
        return GetProjectTerrainImage(terrainId, decorationType) ?? GetDefaultSkImage();
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
                    using var bitmap = WC4MapEditor.Rendering.Imaging.ProjectImageDecoder.Decode(parser.SurfaceData);
                    surface = SKImage.FromBitmap(bitmap);
                    _atlasSurfaces[atlasName] = surface;
                }
                if (surface == null || def.X < 0 || def.Y < 0 ||
                    (long)def.X + def.Width > surface.Width || (long)def.Y + def.Height > surface.Height) continue;
                var tile = surface.Subset(new SKRectI(def.X, def.Y, def.X + def.Width, def.Y + def.Height));
                if (tile == null) continue;
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

    public string? GetTerrainTypeName(int terrainId)
    {
        RefreshProjectResources();
        return _projectTerrains.TryGetValue(terrainId, out var terrain) ? terrain.Name : null;
    }

    public int GetTerrainVariantCount(int terrainId)
    {
        RefreshProjectResources();
        return _projectTerrains.TryGetValue(terrainId, out var terrain) ? Math.Max(1, terrain.TileCount) : 1;
    }

    private void RefreshProjectResources()
    {
        if (_revision == AssetManager.Default.Revision) return;
        foreach (var tile in _atlasTiles.Values) tile.Dispose();
        foreach (var surface in _atlasSurfaces.Values) surface?.Dispose();
        _atlasTiles.Clear();
        _atlasTileOrder.Clear();
        _atlasSurfaces.Clear();
        _projectTerrains = ConfigManager.Instance.GetMapTerrainEntries().GroupBy(entry => entry.Terrain)
            .ToDictionary(group => group.Key, group => group.Last());
        _revision = AssetManager.Default.Revision;
    }

    public Dictionary<int, string> GetAllTerrainTypes()
    {
        RefreshProjectResources();
        return _projectTerrains.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
    }

    public void ClearAllCaches()
    {
        foreach (var image in _atlasTiles.Values) image.Dispose();
        _atlasTiles.Clear();
        _atlasTileOrder.Clear();
        foreach (var image in _atlasSurfaces.Values) image?.Dispose();
        _atlasSurfaces.Clear();
        _defaultImage?.Dispose();
        _defaultImage = null;
    }

    private SKImage? GetDefaultSkImage()
    {
        if (_defaultImage != null) return _defaultImage;
        try
        {
            using var bitmap = new SKBitmap(64, 64);
            bitmap.Erase(new SKColor(128, 128, 128));
            return _defaultImage = SKImage.FromBitmap(bitmap);
        }
        catch { return null; }
    }
}
