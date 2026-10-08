using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.IO;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Core.Parsers;
using LayoutPath = System.IO.Path;

namespace WC4MapEditor.Views.Assist;

/// <summary>布局中一个图片引用的解析结果：命中的图集页（或散图）与源矩形。</summary>
internal sealed class LayoutImageRec
{
    public SKBitmap? Page { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }

    public bool Ready => Page != null && W > 0 && H > 0;
    public SKRect SrcRect => new(X, Y, X + W, Y + H);
}

/// <summary>
/// 布局编辑器的图片资源提供器：扫描 assets 中所有图集 xml 建立「图片名 → 图块」索引，
/// 找不到时回退到散图文件（自动尝试 .png/.webp 与 image/ 前缀），行为对齐 HTML 的 RT/resolveImg。
/// </summary>
internal static class LayoutImageProvider
{
    private static Dictionary<string, (string Atlas, int X, int Y, int W, int H)>? _index;
    private static AtlasResourceIndex? _resources;
    private static readonly List<string> _atlasNames = new();
    private static readonly Dictionary<string, SKBitmap?> _pages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LayoutImageRec?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();
    private static long _revision = -1;

    // ---------- 额外资源（用户导入的文件夹 / 文件） ----------
    private static readonly List<string> _extraRoots = new();
    private static readonly Dictionary<string, (SKBitmap Page, int X, int Y, int W, int H)> _extraAtlas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SKBitmap> _extraLoose = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<SKBitmap> _extraPages = [];
    private static int _extraAtlasCount, _extraImageCount;

    public static IReadOnlyList<string> AtlasNames { get { EnsureIndex(); return _atlasNames; } }
    public static int ImageCount { get { EnsureIndex(); return _index!.Count; } }
    public static int ExtraAtlasCount => _extraAtlasCount;
    public static int ExtraImageCount => _extraImageCount;

    private static readonly string[] ImageExtensions = { ".webp", ".png", ".pkm", ".jpg", ".jpeg", ".bmp" };

    /// <summary>
    /// 导入一个资源文件夹：扫描顶层 .xml（图集 / 布局）、图片散图，
    /// 返回发现的布局 XML 列表 (name, text)。对齐 html 的 ingestFileList。
    /// </summary>
    public static List<(string Name, string Text)> IngestFolder(string folder)
    {
        var layouts = new List<(string, string)>();
        lock (_gate)
        {
            EnsureIndex();
            if (ProjectFile.DirectoryExists(folder) && !_extraRoots.Contains(folder))
                _extraRoots.Add(folder);

            foreach (var xml in SafeEnumerateFiles(folder, "*.xml"))
            {
                string text;
                try { text = ProjectFile.ReadAllText(xml); } catch { continue; }
                if (text.Contains("<Texture", StringComparison.OrdinalIgnoreCase) &&
                    (text.Contains("<Image", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)))
                {
                    RegisterExtraAtlas(xml, text, folder);
                }
                else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                {
                    var name = LayoutPath.GetFileName(xml) ?? "";
                    layouts.Add((name, text));
                }
            }

            foreach (var ext in ImageExtensions)
                foreach (var img in SafeEnumerateFiles(folder, "*" + ext))
                    RegisterExtraLoose(img);

            _cache.Clear();
        }
        return layouts;
    }

    /// <summary>导入一批文件（多选）：图集 xml / 布局 xml / 散图 / .fnt。</summary>
    public static List<(string Name, string Text)> IngestFiles(string[] paths)
    {
        var layouts = new List<(string, string)>();
        lock (_gate)
        {
            EnsureIndex();
            foreach (var path in paths)
            {
                if (!ProjectFile.Exists(path)) continue;
                var ext = LayoutPath.GetExtension(path).ToLowerInvariant();
                var dir = LayoutPath.GetDirectoryName(path) ?? "";
                if (!_extraRoots.Contains(dir)) _extraRoots.Add(dir);

                if (ext == ".xml")
                {
                    string text;
                    try { text = ProjectFile.ReadAllText(path); } catch { continue; }
                    if (text.Contains("<Texture", StringComparison.OrdinalIgnoreCase) &&
                        (text.Contains("<Image", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)))
                        RegisterExtraAtlas(path, text, dir);
                    else if (text.Contains("<Layouts", StringComparison.OrdinalIgnoreCase))
                        layouts.Add((LayoutPath.GetFileName(path) ?? "", text));
                }
                else if (ext is ".png" or ".webp" or ".pkm" or ".jpg" or ".jpeg" or ".bmp")
                {
                    RegisterExtraLoose(path);
                }
            }
            _cache.Clear();
        }
        return layouts;
    }

    private static void RegisterExtraAtlas(string xmlPath, string text, string imageDir)
    {
        SKBitmap? page = null;
        try
        {
            var document = ResourceXml.ParseFragments(text);
            string baseName = LayoutPath.GetFileNameWithoutExtension(xmlPath);
            string texture = (document.SelectSingleNode("//Texture") as XmlElement)?.GetAttribute("name") ?? baseName + ".png";
            foreach (string extension in ImageExtensions)
            {
                string path = LayoutPath.Combine(imageDir, LayoutPath.ChangeExtension(texture.Replace('\\', '/'), extension));
                if (!ProjectFile.Exists(path)) continue;
                page = ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(path));
                break;
            }
            if (page == null) return;
            var entries = new Dictionary<string, (SKBitmap, int, int, int, int)>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement node in document.SelectNodes("//Images/Image | //SubTexture")!)
            {
                string name = node.GetAttribute("name");
                int Read(string key) => int.TryParse(node.GetAttribute(key), out int value) ? value : 0;
                int x = Read("x"), y = Read("y");
                int w = Read(node.HasAttribute("w") ? "w" : "width"), h = Read(node.HasAttribute("h") ? "h" : "height");
                if (name.Length == 0 || x < 0 || y < 0 || w <= 0 || h <= 0 ||
                    (long)x + w > page.Width || (long)y + h > page.Height)
                    throw new InvalidDataException($"额外图集存在无效图块：{name}");
                entries[name] = (page, x, y, w, h);
            }
            if (entries.Count == 0) return;
            foreach (var entry in entries) _extraAtlas[entry.Key] = entry.Value;
            _extraAtlasCount = _extraAtlas.Count;
            _extraPages.Add(page);
            page = null;
            if (!_atlasNames.Contains(baseName)) _atlasNames.Add(baseName);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LayoutImageProvider] {ex.Message}"); }
        finally { page?.Dispose(); }
    }

    private static void RegisterExtraLoose(string path)
    {
        try
        {
            var bmp = ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(path));
            if (bmp == null) return;
            var name = LayoutPath.GetFileNameWithoutExtension(path) ?? "";
            if (_extraLoose.TryGetValue(name, out var previous)) previous.Dispose();
            _extraLoose[name] = bmp;
            _extraImageCount = _extraLoose.Count;
        }
        catch { }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try { return ProjectFile.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
        catch { return Array.Empty<string>(); }
    }

    private static Dictionary<string, (string Atlas, int X, int Y, int W, int H)> EnsureIndex()
    {
        lock (_gate)
        {
            if (_revision != AssetManager.Default.Revision)
            {
                ClearCache();
                _revision = AssetManager.Default.Revision;
            }
            if (_index != null) return _index;

            var index = new Dictionary<string, (string, int, int, int, int)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var manager = AssetManager.Default;

                var resources = _resources = new AtlasResourceIndex(manager);
                _atlasNames.AddRange(resources.Atlases);
                foreach (var (name, tile) in resources.Tiles)
                {
                    var def = tile.Definition;
                    index[name] = (tile.Atlas, def.X, def.Y, def.Width, def.Height);
                }
            }
            catch { /* 索引失败时退化为空 */ }

            _index = index;
            return _index;
        }
    }

    /// <summary>解析图片引用；找不到返回 null。</summary>
    public static LayoutImageRec? Resolve(string? imageRef)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;
        var index = EnsureIndex();
        if (_cache.TryGetValue(imageRef, out var cached)) return cached;

        LayoutImageRec? result = null;

        var s = imageRef.Replace('\\', '/');
        var noPrefix = s.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? s[6..] : s;
        var baseName = noPrefix.Split('/')[^1];
        var stem = StripExt(baseName);

        // 优先查额外导入的资源
        if (_extraAtlas.TryGetValue(baseName, out var ea) || _extraAtlas.TryGetValue(stem, out ea))
        {
            result = new LayoutImageRec { Page = ea.Page, X = ea.X, Y = ea.Y, W = ea.W, H = ea.H };
            goto done;
        }
        if (_extraLoose.TryGetValue(stem, out var el))
        {
            result = new LayoutImageRec { Page = el, X = 0, Y = 0, W = el.Width, H = el.Height };
            goto done;
        }

        if (_resources?.Resolve(s) is { } tile)
        {
            var page = GetAtlasPage(tile.Atlas);
            var hit = tile.Definition;
            result = new LayoutImageRec { Page = page, X = hit.X, Y = hit.Y, W = hit.Width, H = hit.Height };
        }
        else
        {
            // 散图回退
            foreach (var ext in ImageExtensions)
            {
                foreach (var cand in new[] { System.IO.Path.ChangeExtension(s, ext), System.IO.Path.ChangeExtension(noPrefix, ext),
                    baseName, stem + ext, "image/" + stem + ext })
                {
                    var file = FindFile(cand);
                    if (file == null) continue;
                    var bmp = DecodeFile(file);
                    if (bmp == null) continue;
                    result = new LayoutImageRec { Page = bmp, X = 0, Y = 0, W = bmp.Width, H = bmp.Height };
                    goto done;
                }
            }
        }

    done:
        _cache[imageRef] = result;
        return result;
    }

    private static string StripExt(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string? FindFile(string relativePath)
    {
        try
        {
            var manager = AssetManager.Default;
            var entry = manager.Find(relativePath);
            if (entry != null) return entry.FullPath;

            var root = manager.AssetsRoot;
            if (!string.IsNullOrEmpty(root))
            {
                var p = System.IO.Path.Combine(root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (ProjectFile.Exists(p)) return p;
            }

            // 额外导入的资源目录
            var rel = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
            foreach (var extra in _extraRoots)
            {
                var p = System.IO.Path.Combine(extra, rel);
                if (ProjectFile.Exists(p)) return p;
                var fn = System.IO.Path.GetFileName(relativePath);
                p = System.IO.Path.Combine(extra, fn);
                if (ProjectFile.Exists(p)) return p;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static SKBitmap? GetAtlasPage(string atlasBase)
    {
        if (_pages.TryGetValue(atlasBase, out var cached)) return cached;

        SKBitmap? page = null;
        try
        {
            var parser = HdAtlasParser.Get(atlasBase);
            var data = parser.SurfaceData;
            if (data != null && data.Length > 0)
            {
                page = ProjectImageDecoder.Decode(data);
            }
        }
        catch { page = null; }

        _pages[atlasBase] = page;
        return page;
    }

    private static SKBitmap? DecodeFile(string path)
    {
        if (_pages.TryGetValue("file:" + path, out var cached)) return cached;
        SKBitmap? bmp = null;
        try
        {
            bmp = ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(path));
        }
        catch { bmp = null; }
        _pages["file:" + path] = bmp;
        return bmp;
    }

    /// <summary>清空缓存（资源切换后调用）。clearExtra=false 时保留额外导入的资源。</summary>
    public static void ClearCache(bool clearExtra = false)
    {
        lock (_gate)
        {
            _index = null;
            _resources = null;
            _atlasNames.Clear();
            foreach (var p in _pages.Values) p?.Dispose();
            _pages.Clear();
            _cache.Clear();
            if (clearExtra)
            {
                foreach (var p in _extraLoose.Values) p.Dispose();
                foreach (var p in _extraPages) p.Dispose();
                _extraPages.Clear();
                _extraLoose.Clear();
                _extraAtlas.Clear();
                _extraRoots.Clear();
                _extraAtlasCount = 0;
                _extraImageCount = 0;
            }
        }
    }
}
