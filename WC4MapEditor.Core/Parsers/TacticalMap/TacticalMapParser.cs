using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Diagnostics;
using System.Xml;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Core.Parsers;

public class TacticalMapParser : IDisposable
{
    private byte[]? _tacticalMapSurfaceData;
    private List<TacticalMapImageDef> _imageDefinitions = new();

    private static List<TacticalMapImageDef>? _cachedDefinitions;
    private static byte[]? _cachedSurfaceData;
    private static bool _isParsed;
    private static long _cacheRevision = -1;
    private long _instanceRevision = -1;
    private static long _cacheGeneration;
    private long _instanceGeneration = -1;
    private static readonly object _parseLock = new();

    public IReadOnlyList<TacticalMapImageDef> ImageDefinitions { get { EnsureLoadedFromAssets(); return _imageDefinitions; } }
    public byte[]? SurfaceData { get { EnsureLoadedFromAssets(); return _tacticalMapSurfaceData; } }
    public static bool IsParsed => _isParsed;

    public TacticalMapParser()
    {
    }

    public void EnsureLoadedFromAssets()
    {
        lock (_parseLock)
        {
            EnsureCurrentRevision();
            if (_isParsed)
            {
                if ((_instanceRevision != _cacheRevision || _instanceGeneration != _cacheGeneration) && _cachedDefinitions != null)
                {
                    _imageDefinitions = new List<TacticalMapImageDef>(_cachedDefinitions);
                }
                _tacticalMapSurfaceData = _cachedSurfaceData;
                _instanceRevision = _cacheRevision;
                _instanceGeneration = _cacheGeneration;
                return;
            }

            _imageDefinitions.Clear();
            _tacticalMapSurfaceData = null;
            ParseTacticalMapFromAssets();

            _isParsed = true;
            _cachedDefinitions = new List<TacticalMapImageDef>(_imageDefinitions);
            _cachedSurfaceData = _tacticalMapSurfaceData;
            _instanceRevision = _cacheRevision;
            _instanceGeneration = _cacheGeneration;
        }
    }

    private static void EnsureCurrentRevision()
    {
        long revision = AssetManager.Default.Revision;
        if (_cacheRevision == revision) return;
        _isParsed = false;
        _cachedDefinitions = null;
        _cachedSurfaceData = null;
        _cacheRevision = revision;
    }

    private void ParseTacticalMapFromAssets()
    {
        try
        {
            var am = AssetManager.Default;
            if (!am.IsLoaded)
            {
                Debug.WriteLine("[TacticalMapParser] AssetManager 未加载，跳过从 assets 加载战术地图");
                return;
            }

            var atlas = HdAtlasParser.Get("tacticalmap");
            if (!atlas.IsLoaded) { Debug.WriteLine(atlas.LastError); return; }
            _imageDefinitions = atlas.ImageDefinitions.Select(def => new TacticalMapImageDef
            {
                Name = def.Name, X = def.X, Y = def.Y, Width = def.Width, Height = def.Height,
                RefX = def.RefX, RefY = def.RefY
            }).ToList();
            _tacticalMapSurfaceData = atlas.SurfaceData;

            Debug.WriteLine($"[TacticalMapParser] 成功解析 {_imageDefinitions.Count} 个图片定义 (from AssetManager)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 从 AssetManager 解析战术地图失败: {ex.Message}");
        }
    }

    private void ParseXmlConfigFromText(string xmlContent)
    {
        try
        {
            XmlDocument xmlDoc = ResourceXml.ParseFragments(xmlContent);

            XmlNodeList? imageNodes = xmlDoc.SelectNodes("//Images/Image");
            if (imageNodes == null) return;

            foreach (XmlNode imageNode in imageNodes)
            {
                if (imageNode.Attributes == null) continue;

                var def = new TacticalMapImageDef();

                var nameAttr = imageNode.Attributes["name"];
                if (nameAttr != null) def.Name = nameAttr.Value;

                var xAttr = imageNode.Attributes["x"];
                if (xAttr != null)
                {
                    int.TryParse(xAttr.Value, out int x);
                    def.X = x;
                }

                var yAttr = imageNode.Attributes["y"];
                if (yAttr != null)
                {
                    int.TryParse(yAttr.Value, out int y);
                    def.Y = y;
                }

                var wAttr = imageNode.Attributes["w"];
                if (wAttr != null)
                {
                    int.TryParse(wAttr.Value, out int w);
                    def.Width = w;
                }

                var hAttr = imageNode.Attributes["h"];
                if (hAttr != null)
                {
                    int.TryParse(hAttr.Value, out int h);
                    def.Height = h;
                }

                var refxAttr = imageNode.Attributes["refx"];
                if (refxAttr != null)
                {
                    int.TryParse(refxAttr.Value, out int refx);
                    def.RefX = refx;
                }

                var refyAttr = imageNode.Attributes["refy"];
                if (refyAttr != null)
                {
                    int.TryParse(refyAttr.Value, out int refy);
                    def.RefY = refy;
                }

                if (!string.IsNullOrEmpty(def.Name) && def.Width > 0 && def.Height > 0)
                {
                    _imageDefinitions.Add(def);
                }
            }

            Debug.WriteLine($"[TacticalMapParser] 加载了 {_imageDefinitions.Count} 个图片定义");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 解析 XML 配置失败: {ex.Message}");
        }
    }

    private void LoadImage(string imagePath)
    {
        try
        {
            _tacticalMapSurfaceData = ProjectFile.ReadAllBytes(imagePath);
            Debug.WriteLine($"[TacticalMapParser] 加载战术地图: {_tacticalMapSurfaceData.Length} bytes");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 加载图片失败: {ex.Message}");
        }
    }

    private void LoadImageFromBytes(byte[] data)
    {
        try
        {
            _tacticalMapSurfaceData = data;
            Debug.WriteLine($"[TacticalMapParser] 加载战术地图: {_tacticalMapSurfaceData.Length} bytes (from AssetManager)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TacticalMapParser] 加载图片失败: {ex.Message}");
        }
    }

    public TacticalMapImageDef? GetImageDef(string name)
    {
        EnsureLoadedFromAssets();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name == name)
                return def;
        }
        return null;
    }

    public bool HasImage(string name)
    {
        EnsureLoadedFromAssets();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name == name)
                return true;
        }
        return false;
    }

    public List<string> GetAllImageNames()
    {
        EnsureLoadedFromAssets();
        var results = new List<string>();
        foreach (var def in _imageDefinitions)
        {
            results.Add(def.Name);
        }
        return results;
    }

    public List<string> SearchImages(string keyword)
    {
        EnsureLoadedFromAssets();
        var results = new List<string>();
        string lowerKeyword = keyword.ToLower();

        foreach (var def in _imageDefinitions)
        {
            if (def.Name.ToLower().Contains(lowerKeyword))
                results.Add(def.Name);
        }

        return results;
    }

    public List<TacticalMapImageDef> GetGeneralHeadImages()
    {
        EnsureLoadedFromAssets();
        var results = new List<TacticalMapImageDef>();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name.StartsWith("head_"))
                results.Add(def);
        }
        return results;
    }

    public List<TacticalMapImageDef> GetCountryFlagImages()
    {
        EnsureLoadedFromAssets();
        var results = new List<TacticalMapImageDef>();
        foreach (var def in _imageDefinitions)
        {
            if (def.Name.StartsWith("flag_") || (def.Name.EndsWith(".png") && !def.Name.StartsWith("head_")))
                results.Add(def);
        }
        return results;
    }

    public static void ClearCache()
    {
        lock (_parseLock)
        {
            _isParsed = false;
            _cacheGeneration++;
            _cachedDefinitions = null;
            _cachedSurfaceData = null;
        }
    }

    public void Dispose()
    {
        _imageDefinitions.Clear();
        _tacticalMapSurfaceData = null;
        _instanceRevision = -1;
    }
}
