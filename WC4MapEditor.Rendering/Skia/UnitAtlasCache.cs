using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 从当前项目 image/image_legion_icon_hd 提取兵种图标，部队和援军共享使用。
/// 按资源 Revision 重建；共享图片和切片表归缓存所有。
/// </summary>
internal static class UnitAtlasCache
{
    /// <summary>图集中每个单位的格子边长，三个渲染器原本都使用 64。</summary>
    internal const int TileSize = 64;

    private static readonly object _lock = new();
    private static bool _initialized;
    private static long _revision = -1;

    private static SKImage? _atlas;
    private static Dictionary<int, SKRect> _tileMap = new();

    /// <summary>
    /// 取共享单位图集。返回的 <paramref name="atlas"/> 归缓存所有，调用方不得 Dispose；
    /// <paramref name="tileMap"/> 为只读用途，调用方不得修改。
    /// </summary>
    internal static bool TryGet(
        [NotNullWhen(true)] out SKImage? atlas,
        [NotNullWhen(true)] out Dictionary<int, SKRect>? tileMap)
    {
        EnsureInitialized();

        atlas = _atlas;
        tileMap = _atlas != null ? _tileMap : null;
        return _atlas != null;
    }

    /// <summary>丢弃已有图集并允许下次调用重建（用于资源目录变更后的热重载）。</summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            _atlas?.Dispose();
            _atlas = null;
            _tileMap = new Dictionary<int, SKRect>();
            _initialized = false;
        }
    }

    private static void EnsureInitialized()
    {
        lock (_lock)
        {
            long revision = AssetManager.Default.Revision;
            if (_revision != revision) { Invalidate(); _revision = revision; }
            if (_initialized) return;

            try
            {
                const string atlasName = "image/image_legion_icon_hd";
                var availableUnits = HdAtlasParser.Get(atlasName).ImageDefinitions
                    .Where(def => def.Name.StartsWith("legion_icon_", StringComparison.Ordinal))
                    .Select(def => int.TryParse(Path.GetFileNameWithoutExtension(def.Name)[12..], out int id) ? id : -1)
                    .Where(id => id >= 0).Distinct().Order().ToList();
                using var loader = new ProjectSpriteLoader();
                if (availableUnits.Count == 0) return;

                var sw = Stopwatch.StartNew();

                int atlasTiles = CalculateAtlasSize(availableUnits.Count);
                int atlasWidth = atlasTiles * TileSize;
                int atlasHeight = atlasTiles * TileSize;

                using var surface = SKSurface.Create(new SKImageInfo(atlasWidth, atlasHeight));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var map = new Dictionary<int, SKRect>(availableUnits.Count);
                int tileIndex = 0;

                foreach (int unitType in availableUnits)
                {
                    if (tileIndex >= atlasTiles * atlasTiles) break;

                    using var unitImage = loader.LoadImage($"legion_icon_{unitType}.png", atlasName);
                    if (unitImage == null) continue;

                    int atlasX = (tileIndex % atlasTiles) * TileSize;
                    int atlasY = (tileIndex / atlasTiles) * TileSize;

                    var srcRect = new SKRect(0, 0, unitImage.Width, unitImage.Height);
                    var dstRect = new SKRect(atlasX, atlasY, atlasX + TileSize, atlasY + TileSize);
                    canvas.DrawImage(unitImage, srcRect, dstRect);

                    map[unitType] = new SKRect(atlasX, atlasY, atlasX + TileSize, atlasY + TileSize);
                    tileIndex++;
                }

                _atlas = surface.Snapshot();
                _tileMap = map;
                _initialized = true;

                Debug.WriteLine($"[UnitAtlasCache] 单位图集构建完成: {atlasWidth}x{atlasHeight}, {map.Count} 个类型, 耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                // 保持 _initialized = false，下次调用可重试，行为与原先各渲染器内部实现一致。
                Debug.WriteLine($"[UnitAtlasCache] 构建单位图集失败: {ex.Message}");
            }
        }
    }

    private static int CalculateAtlasSize(int unitCount)
    {
        int tiles = 1;
        while (tiles * tiles < unitCount) tiles *= 2;
        return tiles;
    }

}
