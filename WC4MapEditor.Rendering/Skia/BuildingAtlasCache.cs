using System.Diagnostics;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 当前项目的建筑、设施和城市等级图集缓存，按资源 Revision 重建。
/// 共享图片归缓存所有，调用方不得释放或修改。
/// </summary>
internal static class BuildingAtlasCache
{
    /// <summary>图集中每个纹理的格子边长，与原 BuildingRender.ATLAS_TILE_SIZE 一致。</summary>
    internal const int AtlasTileSize = 128;

    /// <summary>三张图集及其映射表的容器。</summary>
    internal sealed class AtlasSet
    {
        internal SKImage? BuildingAtlas { get; set; }
        internal SKImage? FacilityAtlas { get; set; }
        internal SKImage? CityLevelAtlas { get; set; }

        internal Dictionary<string, SKRect> BuildingAtlasMap { get; } = new();
        internal Dictionary<string, SKRect> FacilityAtlasMap { get; } = new();
        internal Dictionary<string, SKRect> CityLevelAtlasMap { get; } = new();

        internal Dictionary<string, SKSize> BuildingOriginalSize { get; } = new();
        internal Dictionary<string, SKSize> FacilityOriginalSize { get; } = new();
        internal Dictionary<string, SKSize> CityLevelOriginalSize { get; } = new();
    }

    private static readonly object _lock = new();
    private static AtlasSet? _set;
    private static long _revision = -1;

    /// <summary>取共享图集。返回对象归缓存所有，调用方不得 Dispose 或修改。</summary>
    internal static AtlasSet Get()
    {
        lock (_lock)
        {
            long revision = AssetManager.Default.Revision;
            if (_revision != revision) { Invalidate(); _revision = revision; }
            if (_set != null) return _set;

            var sw = Stopwatch.StartNew();
            var set = new AtlasSet();

            BuildBuildingAtlas(set);
            BuildFacilityAtlas(set);
            BuildCityLevelAtlas(set);

            _set = set;
            Debug.WriteLine($"[BuildingAtlasCache] 图集构建完成: 建筑 {set.BuildingAtlasMap.Count} 个, 设施 {set.FacilityAtlasMap.Count} 个, 城市等级 {set.CityLevelAtlasMap.Count} 个, 耗时 {sw.ElapsedMilliseconds}ms");
            return set;
        }
    }

    /// <summary>丢弃已有图集并允许下次重建（用于资源目录变更后的热重载）。</summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            _set?.BuildingAtlas?.Dispose();
            _set?.FacilityAtlas?.Dispose();
            _set?.CityLevelAtlas?.Dispose();
            _set = null;
        }
    }

    private static void BuildBuildingAtlas(AtlasSet set)
    {
        using var loader = new ProjectSpriteLoader();
        var images = new Dictionary<string, SKBitmap>();
        void Add(string key, string name)
        {
            var image = loader.Load(name, "buildings_hd", "buildings");
            if (image != null) images[key] = image;
        }
        try
        {
            for (int type = 1; type <= 31; type++)
            {
                Add($"{type}_0_0", $"building_{type}.png");
                for (int appearance = 1; appearance <= 9; appearance++)
                    Add($"{type}_{appearance}_0", $"building_{type}_{appearance}.png");
            }
            foreach (var definition in Core.Parsers.HdAtlas.HdAtlasParser.Get("buildings_hd").ImageDefinitions)
                if (definition.Name.StartsWith("capital_", StringComparison.Ordinal) &&
                    int.TryParse(Path.GetFileNameWithoutExtension(definition.Name)[8..], out int id))
                    Add($"0_0_{id}", definition.Name);
            set.BuildingAtlas = ComposeAtlas(images, set.BuildingAtlasMap, set.BuildingOriginalSize);
        }
        finally { foreach (var image in images.Values) image.Dispose(); }
    }

    private static void BuildFacilityAtlas(AtlasSet set)
    {
        using var loader = new ProjectSpriteLoader();
        var images = new Dictionary<string, SKBitmap>();
        try
        {
            foreach (string type in new[] { "factory", "lab", "depot", "airport", "launch", "nuclear" })
                if (loader.Load($"facility_{type}.png", "battleres_hd") is { } image) images[type] = image;
            set.FacilityAtlas = ComposeAtlas(images, set.FacilityAtlasMap, set.FacilityOriginalSize);
        }
        finally { foreach (var image in images.Values) image.Dispose(); }
    }

    private static void BuildCityLevelAtlas(AtlasSet set)
    {
        using var loader = new ProjectSpriteLoader();
        var images = new Dictionary<string, SKBitmap>();
        try
        {
            for (int level = 1; level <= 5; level++)
                if (loader.Load($"city_lv{level}.png", "battleres_hd") is { } image) images[$"city_lv{level}"] = image;
            set.CityLevelAtlas = ComposeAtlas(images, set.CityLevelAtlasMap, set.CityLevelOriginalSize);
        }
        finally { foreach (var image in images.Values) image.Dispose(); }
    }

    /// <summary>
    /// 把若干图片按行优先顺序拼进一张方形图集，同时记录每张图的格子矩形与原始尺寸。
    /// 三个图集原本各自复制了一份相同的拼装逻辑，这里合并。
    /// </summary>
    private static SKImage? ComposeAtlas(
        Dictionary<string, SKBitmap> images,
        Dictionary<string, SKRect> tileMap,
        Dictionary<string, SKSize> originalSizes)
    {
        if (images.Count == 0) return null;
        int atlasTiles = CalculateAtlasSize(images.Count);
        int atlasWidth = atlasTiles * AtlasTileSize;
        int atlasHeight = atlasTiles * AtlasTileSize;

        using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        int tileIndex = 0;
        foreach (var kvp in images)
        {
            if (tileIndex >= atlasTiles * atlasTiles) break;

            string cacheKey = kvp.Key;
            SKBitmap bitmap = kvp.Value;

            try
            {
                originalSizes[cacheKey] = new SKSize(bitmap.Width, bitmap.Height);

                int atlasX = (tileIndex % atlasTiles) * AtlasTileSize;
                int atlasY = (tileIndex / atlasTiles) * AtlasTileSize;

                var srcRect = new SKRect(0, 0, bitmap.Width, bitmap.Height);
                var dstRect = new SKRect(atlasX, atlasY, atlasX + AtlasTileSize, atlasY + AtlasTileSize);
                canvas.DrawBitmap(bitmap, srcRect, dstRect);

                tileMap[cacheKey] = new SKRect(atlasX, atlasY, atlasX + AtlasTileSize, atlasY + AtlasTileSize);
                tileIndex++;
            }
            catch { }
        }

        return surface.Snapshot();
    }

    private static int CalculateAtlasSize(int totalCount)
    {
        int size = 4;
        while (size * size < totalCount)
            size *= 2;
        return Math.Min(size, 32);
    }
}
