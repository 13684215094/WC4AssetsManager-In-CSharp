using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Rendering.Imaging;

public sealed record GameUiSprite(SKBitmap Page, SKRect Source, string Origin);

// This provider reads only the active project and its overrides, never layout-editor imports.
public sealed class ProjectUiImageSource : IDisposable
{
    private AtlasResourceIndex? _index;
    private long _revision = -1;
    private readonly Dictionary<string, SKBitmap> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _order = new();
    private readonly HashSet<string> _misses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _issues = new();
    public IReadOnlyCollection<string> Issues => _issues;
    public void BeginFrame() { EnsureCurrent(); _issues.Clear(); }

    public GameUiSprite? Resolve(string reference, bool diagnose = true)
    {
        EnsureCurrent();
        if (!AssetManager.Default.IsLoaded) return null;
        string normalized = reference.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException("图片引用必须是项目内的相对路径。");
        string? file = null;
        foreach (string candidate in new[] { normalized, "image/" + normalized })
        {
            string stem = Path.ChangeExtension(candidate, null);
            file = ProjectTextureLoader.FindPath(stem);
            if (file != null) break;
        }
        if (file != null)
        {
            var page = Page("file:" + file, () => ProjectFiles.ReadAllBytes(file), diagnose);
            return page == null ? null : new(page, SKRect.Create(page.Width, page.Height),
                Path.GetRelativePath(AssetManager.Default.AssetsRoot, file).Replace('\\', '/'));
        }
        _index ??= new AtlasResourceIndex(AssetManager.Default);
        var candidates = _index.ResolveCandidates(normalized);
        var tile = candidates.FirstOrDefault();
        if (tile == null) { if (diagnose) _issues.Add($"缺少图片：{reference}"); return null; }
        if (candidates.Select(item => (item.Definition.Width, item.Definition.Height)).Distinct().Count() > 1 && diagnose)
            _issues.Add($"图片 {reference} 在多个图集中尺寸不同，使用 {tile.Atlas}；可用“图集路径/图片名”限定来源。");
        var definition = tile.Definition;
        var bitmap = Page("atlas:" + tile.Atlas, () => HdAtlasParser.Get(tile.Atlas).SurfaceData ??
            throw new InvalidDataException($"图集 {tile.Atlas} 缺少纹理页。"), diagnose);
        if (bitmap == null) return null;
        if (definition.X < 0 || definition.Y < 0 || definition.Width <= 0 || definition.Height <= 0 ||
            (long)definition.X + definition.Width > bitmap.Width || (long)definition.Y + definition.Height > bitmap.Height)
        { if (diagnose) _issues.Add($"图块 {reference} 超出 {tile.Atlas} 纹理页。"); return null; }
        return new(bitmap, SKRect.Create(definition.X, definition.Y, definition.Width, definition.Height), tile.Atlas);
    }

    private SKBitmap? Page(string key, Func<byte[]> read, bool diagnose)
    {
        if (_pages.TryGetValue(key, out var cached)) { _order.Remove(key); _order.AddFirst(key); return cached; }
        if (_misses.Contains(key)) { if (diagnose) _issues.Add($"图片无法解码：{key}"); return null; }
        try
        {
            var bitmap = ProjectImageDecoder.Decode(read());
            _pages[key] = bitmap; _order.AddFirst(key);
            if (_pages.Count > 64 && _order.Last is { } last)
            { _pages[last.Value].Dispose(); _pages.Remove(last.Value); _order.RemoveLast(); }
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { _misses.Add(key); if (diagnose) _issues.Add($"图片无法解码：{key}：{ex.Message}"); return null; }
    }

    private void EnsureCurrent()
    {
        if (_revision == AssetManager.Default.Revision) return;
        Dispose(); _revision = AssetManager.Default.Revision;
    }

    public void Dispose()
    {
        foreach (var page in _pages.Values) page.Dispose();
        _pages.Clear(); _order.Clear(); _misses.Clear(); _issues.Clear(); _index = null; _revision = -1;
    }
}
