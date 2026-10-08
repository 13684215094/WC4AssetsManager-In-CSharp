using System.Diagnostics;
using System.Xml;
using SkiaSharp;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Rendering.Imaging;

/// <summary>
/// HD图集编辑器：管理HD国旗图集的读取、修改和保存
/// </summary>
public class HdAtlasEditor
{
    private readonly TacticalMapEditor _document = new();
    private string? _imageFilePath;
    private string? _xmlFilePath;
    private byte[]? _imageData;
    private readonly List<HdAtlasEntry> _entries = new();
    private readonly Dictionary<int, HdAtlasEntry> _entryById = new();

    public IReadOnlyList<HdAtlasEntry> Entries => _entries;
    public string? ImageFilePath => _imageFilePath;
    public string? XmlFilePath => _xmlFilePath;
    public byte[]? ImageData => _imageData;
    public int ImageWidth { get; private set; }
    public int ImageHeight { get; private set; }
    public bool IsLoaded { get; private set; }

    public event Action? DataChanged;

    /// <summary>
    /// 从文件加载HD图集
    /// </summary>
    public bool LoadFromFiles(string imageFilePath, string xmlFilePath)
    {
        IsLoaded = _document.LoadFromFiles(imageFilePath, xmlFilePath);
        RefreshEntries();
        return IsLoaded;
    }

    public bool LoadFromAssetManager(AssetManager manager)
    {
        IsLoaded = false;
        var xml = manager.Find("image/image_flags_hd.xml") ?? manager.Find("image_flags_hd.xml");
        var image = manager.Find("image/image_flags_hd.webp") ?? manager.Find("image_flags_hd.webp")
            ?? manager.Find("image/image_flags_hd.png") ?? manager.Find("image_flags_hd.png");
        if (xml == null || image == null) { _entries.Clear(); _entryById.Clear(); return false; }
        return LoadFromFiles(image.FullPath, xml.FullPath);
    }

    private void RefreshEntries()
    {
        _entries.Clear();
        _entryById.Clear();
        _imageFilePath = _document.ImageFilePath;
        _xmlFilePath = _document.XmlFilePath;
        _imageData = _document.ImageData;
        ImageWidth = _document.ImageWidth;
        ImageHeight = _document.ImageHeight;
        foreach (var obj in _document.Objects)
        {
            if (!obj.Name.StartsWith("flag_", StringComparison.Ordinal) || !obj.Name.EndsWith(".png", StringComparison.Ordinal)
                || !int.TryParse(obj.Name[5..^4], out int id)) continue;
            var entry = new HdAtlasEntry { Id = id, Name = obj.Name, X = obj.X, Y = obj.Y, Width = obj.Width, Height = obj.Height };
            _entries.Add(entry);
            _entryById.Add(id, entry);
        }
    }

    public HdAtlasEntry? GetEntry(int id)
    {
        return _entryById.TryGetValue(id, out var entry) ? entry : null;
    }

    public bool HasEntry(int id)
    {
        return _entryById.ContainsKey(id);
    }

    /// <summary>
    /// 添加或替换HD国旗条目
    /// </summary>
    public (HdAtlasEntry entry, bool isUpdate) AddOrReplaceEntry(int id, int width, int height)
    {
        string name = $"flag_{id}.png";
        bool isUpdate = false;

        if (_entryById.TryGetValue(id, out var existing))
        {
            _entries.Remove(existing);
            _entryById.Remove(id);
            isUpdate = true;
        }

        var entry = new HdAtlasEntry
        {
            Name = name,
            Id = id,
            Width = width,
            Height = height,
            X = 0,
            Y = 0
        };

        _entries.Add(entry);
        _entryById[id] = entry;
        DataChanged?.Invoke();

        return (entry, isUpdate);
    }

    /// <summary>
    /// 整理HD图集布局（避免重叠）
    /// </summary>
    public void ArrangeEntries(int padding = 2)
    {
        if (_entries.Count == 0) return;

        // 按面积降序排序
        var sorted = _entries.OrderByDescending(e => e.Width * e.Height).ToList();

        int currentX = 0;
        int currentY = 0;
        int rowHeight = 0;
        int maxWidth = ImageWidth > 0 ? ImageWidth : 2048;

        foreach (var entry in sorted)
        {
            if (currentX + entry.Width > maxWidth && currentX > 0)
            {
                currentX = 0;
                currentY += rowHeight + padding;
                rowHeight = 0;
            }

            entry.X = currentX;
            entry.Y = currentY;
            currentX += entry.Width + padding;
            rowHeight = Math.Max(rowHeight, entry.Height);
        }

        // 更新图集尺寸
        ImageWidth = Math.Max(ImageWidth, _entries.Max(e => e.X + e.Width) + padding);
        ImageHeight = Math.Max(ImageHeight, _entries.Max(e => e.Y + e.Height) + padding);

        DataChanged?.Invoke();
    }

    /// <summary>
    /// 渲染HD图集表面
    /// </summary>
    public SKBitmap? RenderAtlasSurface()
    {
        if (_entries.Count == 0) return null;

        int width = ImageWidth > 0 ? ImageWidth : _entries.Max(e => e.X + e.Width) + 4;
        int height = ImageHeight > 0 ? ImageHeight : _entries.Max(e => e.Y + e.Height) + 4;

        try
        {
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var surface = new SKBitmap(info);
            using var canvas = new SKCanvas(surface);
            canvas.Clear(SKColors.Transparent);

            // 如果有原始图集数据，先绘制原始图集
            if (_imageData != null && _imageData.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(_imageData);
                    using var originalAtlas = SKBitmap.Decode(stream);
                    if (originalAtlas != null)
                    {
                        canvas.DrawBitmap(originalAtlas, 0, 0);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HdAtlasEditor] 绘制原始图集失败: {ex.Message}");
                }
            }

            return surface;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 渲染图集表面失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 将图片绘制到HD图集表面的指定位置
    /// </summary>
    public bool DrawImageToAtlas(SKBitmap atlasSurface, SKBitmap image, int x, int y)
    {
        try
        {
            using var canvas = new SKCanvas(atlasSurface);
            canvas.DrawBitmap(image, x, y);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 绘制图片到图集失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存HD图集表面到文件
    /// </summary>
    public bool SaveAtlasSurface(SKBitmap atlasSurface, string? outputPath = null)
    {
        try
        {
            string path = outputPath ?? _imageFilePath ?? throw new InvalidOperationException("无图片文件路径");
            if (!IsLoaded) return false;
            WC4MapEditor.Core.Parsers.AtomicFile.Write(path, AtlasCompositeRenderer.Encode(atlasSurface, path));
            Debug.WriteLine($"[HdAtlasEditor] 图集表面已保存: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HdAtlasEditor] 保存图集表面失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 保存HD图集XML配置
    /// </summary>
    public bool SaveXml(string? outputPath = null)
    {
        foreach (var entry in _entries)
        {
            var obj = _document.GetObject(entry.Name);
            if (obj == null) _document.AddObject(new TacticalMapObject { Name = entry.Name, X = entry.X, Y = entry.Y, Width = entry.Width, Height = entry.Height });
            else { obj.X = entry.X; obj.Y = entry.Y; obj.Width = entry.Width; obj.Height = entry.Height; }
        }
        return _document.SaveXml(outputPath);
    }

    /// <summary>
    /// 添加图片到HD图集，自动整理布局，重建整张图集并保存
    /// </summary>
    public bool AddImageAndSave(int id, SKBitmap image, string? imageOutputPath = null, string? xmlOutputPath = null)
    {
        bool saved = _document.AddImageAndArrange($"flag_{id}.png", image, imageOutputPath, xmlOutputPath);
        RefreshEntries();
        if (saved) DataChanged?.Invoke();
        return saved;
    }

}

public class HdAtlasEntry
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public override string ToString()
    {
        return $"{Name} ({X}, {Y}, {Width}x{Height})";
    }
}
