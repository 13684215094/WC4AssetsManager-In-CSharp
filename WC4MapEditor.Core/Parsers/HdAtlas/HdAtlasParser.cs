using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Diagnostics;
using System.Xml;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Parsers.HdAtlas;

/// <summary>图集中单个图块的定义（&lt;Image name x y w h refx refy /&gt;）。</summary>
public class HdAtlasImageDef
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefX { get; set; }
    public int RefY { get; set; }

    public override string ToString() => $"{Name} ({X}, {Y}, {Width}x{Height})";
}

/// <summary>
/// 通用图集解析器：支持 assets 内相对路径、image/ 目录与 XML 相对纹理页，
/// 对外提供图块定义、大图字节及资源缺失原因。
/// 与 <see cref="TacticalMapParser"/> 同一套解析约定，但以图集名参数化，
/// 因此可同时服务于 buildings_hd / image_flags_hd / image_countrytech_hd 等任意图集。
/// 实例按图集名做静态缓存，避免重复读盘。
/// </summary>
public class HdAtlasParser
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, HdAtlasParser> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _cacheRevision = -1;

    /// <summary>按图集名（不带扩展名）获取解析器，首次访问时加载。</summary>
    public static HdAtlasParser Get(string baseName)
    {
        lock (_lock)
        {
            long revision = AssetManager.Default.Revision;
            if (_cacheRevision != revision)
            {
                _cache.Clear();
                _cacheRevision = revision;
            }
            if (_cache.TryGetValue(baseName, out var cached)) return cached;
            var parser = new HdAtlasParser(baseName);
            parser.Load();
            _cache[baseName] = parser;
            return parser;
        }
    }

    public static void ClearCache()
    {
        lock (_lock) { _cache.Clear(); }
    }

    private static readonly string[] ImageExtensions = { ".webp", ".png", ".pkm", ".jpg", ".jpeg", ".bmp" };

    private readonly string _baseName;
    private readonly List<HdAtlasImageDef> _definitions = new();
    private byte[]? _surfaceData;

    public string BaseName => _baseName;
    public IReadOnlyList<HdAtlasImageDef> ImageDefinitions => _definitions;
    public byte[]? SurfaceData => _surfaceData;
    public string? XmlPath { get; private set; }
    public string? ImagePath { get; private set; }
    public string? LastError { get; private set; }
    public bool IsLoaded => LastError == null && _surfaceData is { Length: > 0 };

    private HdAtlasParser(string baseName) => _baseName = baseName;

    private void Load()
    {
        try
        {
            var manager = AssetManager.Default;
            var root = manager.GetActiveAssetsRoot();
            string name = _baseName.Replace('\\', '/');
            if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            XmlPath = ResolveWithin(root, name + ".xml");
            // Bare UI atlas names conventionally live in image/. Explicit paths stay exact.
            if (!ProjectFile.Exists(XmlPath) && !name.Contains('/'))
                XmlPath = ResolveWithin(root, "image/" + name + ".xml");
            if (!ProjectFile.Exists(XmlPath)) throw new FileNotFoundException($"项目缺少图集：{name}.xml");
            var document = ResourceXml.ParseFragments(ProjectFile.ReadAllText(XmlPath));
            ParseXml(document);
            var textures = document.SelectNodes("//Texture")!;
            if (textures.Count > 1) throw new InvalidDataException("暂不支持含多张纹理页的图集。");
            string texture = (textures.Count == 0 ? null : textures[0]?.Attributes?["name"]?.Value)
                ?? (Path.GetFileNameWithoutExtension(XmlPath) + ".png");
            if (string.IsNullOrWhiteSpace(texture)) throw new InvalidDataException("图集 Texture.name 为空。");
            texture = texture.Replace('\\', '/');
            string directory = Path.GetDirectoryName(XmlPath)!;
            ImagePath = FindImage(root, directory, texture);
            if (ImagePath == null) throw new FileNotFoundException($"图集缺少对应图片：{texture}（支持 WebP / PNG / PKM）");
            _surfaceData = ProjectFile.ReadAllBytes(ImagePath);

            Debug.WriteLine($"[HdAtlasParser] {_baseName}: {_definitions.Count} 个图块, 图片 {_surfaceData?.Length ?? 0} bytes");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _surfaceData = null;
            Debug.WriteLine($"[HdAtlasParser] 加载 {_baseName} 失败: {ex.Message}");
        }
    }

    private static string ResolveWithin(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("图集资源必须使用项目内的相对路径。");
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!GameProjectWorkspace.IsWithin(path, root)) throw new InvalidDataException("图集资源路径超出 assets。");
        return path;
    }

    private static string? FindImage(string root, string directory, string texture)
    {
        if (Path.IsPathRooted(texture) || texture.Contains(':'))
            throw new InvalidDataException("纹理必须使用项目内的相对路径。");
        var directories = texture.Contains('/') && !texture.StartsWith("../", StringComparison.Ordinal)
            ? new[] { directory, root }.Distinct() : new[] { directory };
        foreach (string baseDirectory in directories)
        {
            string stem = Path.ChangeExtension(texture, null);
            foreach (string extension in ImageExtensions)
            {
                string relative = Path.GetRelativePath(root, baseDirectory).Replace('\\', '/') + "/" + stem + extension;
                string path = ResolveWithin(root, relative);
                if (ProjectFile.Exists(path)) return path;
            }
        }
        return null;
    }

    private void ParseXml(XmlDocument document)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definitions = new List<HdAtlasImageDef>();
        foreach (XmlElement node in document.SelectNodes("//Images/Image | //SubTexture")!)
        {
            var def = new HdAtlasImageDef
            {
                Name = node.GetAttribute("name"), X = Read("x"), Y = Read("y"),
                Width = Read(node.HasAttribute("w") ? "w" : "width"),
                Height = Read(node.HasAttribute("h") ? "h" : "height"),
                RefX = Read("refx", true), RefY = Read("refy", true)
            };
            if (string.IsNullOrWhiteSpace(def.Name) || def.X < 0 || def.Y < 0 ||
                def.Width <= 0 || def.Height <= 0 || !names.Add(def.Name))
                throw new InvalidDataException($"图集存在无效或重复图块：{def.Name}");
            definitions.Add(def);

            int Read(string attribute, bool optional = false) => int.TryParse(node.GetAttribute(attribute), out int value)
                ? value : optional && !node.HasAttribute(attribute) ? 0
                : throw new InvalidDataException($"图集属性无效：{attribute}");
        }
        _definitions.AddRange(definitions);
    }

    public HdAtlasImageDef? GetImageDef(string name)
    {
        foreach (var def in _definitions)
            if (string.Equals(def.Name, name, StringComparison.OrdinalIgnoreCase)) return def;
        return null;
    }

    public bool HasImage(string name) => GetImageDef(name) != null;
}
