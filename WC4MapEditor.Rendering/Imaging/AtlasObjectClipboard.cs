using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public sealed class AtlasObjectClipboard : IDisposable
{
    private readonly TacticalMapObject _object;
    private readonly SKBitmap _pixels;

    public AtlasObjectClipboard(TacticalMapObject source, SKBitmap pixels)
    {
        _object = TacticalMapObject.Clone(source);
        _pixels = pixels.Copy() ?? throw new InvalidOperationException("无法复制图块像素。");
    }

    public TacticalMapObject Paste(TacticalMapEditor editor, IDictionary<string, SKBitmap> pixels)
    {
        string name = _object.Name.EndsWith("_copy", StringComparison.Ordinal) ? _object.Name : _object.Name + "_copy";
        var copy = _pixels.Copy() ?? throw new InvalidOperationException("无法粘贴图块像素。");
        try
        {
            var obj = editor.DuplicateObject(_object, name);
            pixels.Add(obj.Name, copy);
            return obj;
        }
        catch { copy.Dispose(); throw; }
    }

    public void Dispose() => _pixels.Dispose();
}
