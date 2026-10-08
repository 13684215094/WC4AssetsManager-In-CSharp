using System.Text;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Core.Assets;

public sealed class TextResourceDocument
{
    private readonly FileVersionGuard _version = new();
    private string _baseline;
    private readonly bool _bom;
    public string FilePath { get; }
    public string Text { get; set; }
    public bool HasChanges => Normalize(Text) != Normalize(_baseline);

    public TextResourceDocument(string path)
    {
        FilePath = ProjectFiles.FindProject(path)?.GetEditablePath(path) ?? Path.GetFullPath(path);
        byte[] bytes = ProjectFiles.ReadAllBytes(FilePath);
        _bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        Text = _baseline = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        _version.Capture(FilePath);
    }

    public void Save()
    {
        _version.Verify();
        if (!HasChanges) return;
        byte[] bytes = new UTF8Encoding(_bom).GetBytes(Text);
        if (_bom) bytes = [.. Encoding.UTF8.Preamble, .. bytes];
        string savedText = Text;
        ResourceSaveTracker.Register(bytes, _version.Verify, saved =>
        {
            _version.AcceptSaved(saved);
            _baseline = savedText;
        });
        AtomicFile.Write(FilePath, bytes);
    }

    public void Discard() => Text = _baseline;
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
