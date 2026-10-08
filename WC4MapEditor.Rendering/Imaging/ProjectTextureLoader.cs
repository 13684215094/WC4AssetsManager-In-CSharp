using SkiaSharp;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Rendering.Imaging;

public static class ProjectTextureLoader
{
    public static string? FindPath(string stem)
    {
        var manager = AssetManager.Default;
        if (!manager.IsLoaded) return null;
        foreach (string extension in new[] { ".webp", ".png", ".pkm" })
        {
            string path = Path.Combine(manager.AssetsRoot, stem + extension);
            if (ProjectFiles.Exists(path)) return path;
        }
        return null;
    }

    public static SKBitmap? Load(string stem)
    {
        string? path = FindPath(stem);
        return path == null ? null : ProjectImageDecoder.Decode(ProjectFiles.ReadAllBytes(path));
    }
}
