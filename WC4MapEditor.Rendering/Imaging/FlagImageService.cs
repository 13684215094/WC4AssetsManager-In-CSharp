using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Diagnostics;
using System.Linq;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Rendering.Imaging;

public class FlagImageService : IFlagImageService, IDisposable
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly Dictionary<string, SKBitmap> _pages = new(StringComparer.OrdinalIgnoreCase);
    private long _revision = -1;
    public string? LastError { get; private set; }

    public SKBitmap? LoadFlagFromTacticalMap(string flagName) => LoadTile("tacticalmap", flagName);
    public SKBitmap? LoadFlagFromHdAtlas(int countryId) => LoadTile("image/image_flags_hd", $"flag_{countryId}.png");
    public SKBitmap? LoadBigFlag(int countryId) => LoadFlagFromTacticalMap($"flag_{countryId}.png");
    public SKBitmap? LoadSmallFlag(int countryId) => LoadFlagFromTacticalMap($"f_{countryId:D2}.png");

    private SKBitmap? LoadTile(string atlasName, string imageName)
    {
        try
        {
            EnsureCurrent();
            var parser = WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.Get(atlasName);
            if (!parser.IsLoaded) throw new InvalidDataException(parser.LastError ?? "图集加载失败。");
            var def = parser.GetImageDef(imageName) ?? throw new FileNotFoundException($"图集中没有 {imageName}：{atlasName}");
            if (!_pages.TryGetValue(atlasName, out var page))
            {
                page = ProjectImageDecoder.Decode(parser.SurfaceData!);
                _pages.Add(atlasName, page);
            }
            var result = BitmapPixels.Crop(page, def.X, def.Y, def.Width, def.Height);
            LastError = null;
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[FlagImageService] {LastError}");
            return null;
        }
    }

    public SKBitmap? LoadFlagFromFile(string path)
    {
        try { var bitmap = ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(path)); LastError = null; return bitmap; }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public List<string> GetAvailableTacticalMapFlags()
    {
        EnsureCurrent();
        return WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.Get("tacticalmap").ImageDefinitions
            .Where(def => def.Name.StartsWith("flag_") || def.Name.StartsWith("f_")).Select(def => def.Name).ToList();
    }

    public List<int> GetAvailableHdFlags()
    {
        EnsureCurrent();
        var ids = new List<int>();
        foreach (var def in WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.Get("image/image_flags_hd").ImageDefinitions)
            if (def.Name.StartsWith("flag_") && int.TryParse(Path.GetFileNameWithoutExtension(def.Name)[5..], out int id)) ids.Add(id);
        return ids;
    }

    private void EnsureCurrent()
    {
        long revision = AssetManager.Default.Revision;
        if (_revision == revision) return;
        Dispose();
        _revision = revision;
    }

    public bool SaveFlag(SKBitmap flag, string path, SKEncodedImageFormat format, int quality)
    {
        try
        {
            using var data = flag.Encode(format, quality);
            if (data == null) return false;
            WC4MapEditor.Core.Parsers.AtomicFile.Write(path, data.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 保存国旗失败: {ex.Message}");
            return false;
        }
    }

    public SKBitmap? ResizeFlag(SKBitmap flag, int width, int height)
    {
        try
        {
            var info = new SKImageInfo(width, height, flag.ColorType, flag.AlphaType);
            return flag.Resize(info, HighQuality);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 缩放国旗失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CropCircularFlag(SKBitmap flag, int x, int y, int size)
    {
        try
        {
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var path = new SKPath();
            path.AddCircle(size / 2f, size / 2f, size / 2f);
            canvas.ClipPath(path);

            canvas.DrawBitmap(flag, new SKRectI(x, y, x + size, y + size), new SKRect(0, 0, size, size));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagImageService] 裁剪圆形国旗失败: {ex.Message}");
            return null;
        }
    }

    public void RefreshCache()
    {
        Dispose();
        _revision = -1;
        WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.ClearCache();
        TacticalMapParser.ClearCache();
    }

    public void Dispose()
    {
        foreach (var page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
