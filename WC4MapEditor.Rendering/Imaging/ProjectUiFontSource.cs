using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Imaging;

public sealed class ProjectUiFontSource : IDisposable
{
    private readonly Dictionary<string, SKTypeface> _fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = [];
    private long _revision = -1;
    public IReadOnlyCollection<string> MissingFonts => _missing;

    public SKTypeface Get(GameUiFontDefinition definition)
    {
        if (_revision != AssetManager.Default.Revision) { Dispose(); _revision = AssetManager.Default.Revision; }
        string key = definition.RelativePath ?? "";
        if (_fonts.TryGetValue(key, out var cached)) return cached;
        try
        {
            string path = Path.Combine(AssetManager.Default.AssetsRoot, key);
            if (ProjectFiles.Exists(path))
            {
                var face = SKTypeface.FromFile(ProjectFiles.ResolveRead(path));
                if (face != null) return _fonts[key] = face;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { }
        _missing.Add($"缺少或无法加载字体 {key}，使用系统回退字体。");
        var fallback = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
        return _fonts[key] = fallback;
    }

    public void Dispose()
    {
        foreach (var face in _fonts.Values.Distinct()) if (!ReferenceEquals(face, SKTypeface.Default)) face.Dispose();
        _fonts.Clear(); _missing.Clear(); _revision = -1;
    }
}
