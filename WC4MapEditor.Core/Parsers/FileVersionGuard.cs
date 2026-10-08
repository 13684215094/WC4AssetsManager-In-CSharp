using System.Security.Cryptography;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Parsers;

public sealed class FileVersionGuard
{
    private string _path = "";
    private byte[]? _fingerprint;

    public void Capture(string path)
    {
        _path = path;
        _fingerprint = Fingerprint();
    }

    private byte[]? Fingerprint() => ProjectFiles.Exists(_path)
        ? SHA256.HashData(ProjectFiles.ReadAllBytes(_path)) : null;

    public void Verify()
    {
        byte[]? current = Fingerprint();
        if ((_fingerprint == null) != (current == null) ||
            _fingerprint != null && !_fingerprint.AsSpan().SequenceEqual(current))
            throw new IOException($"资源已被其他编辑器或程序修改，请重新加载后再保存：{_path}");
    }

    public void AcceptSaved(byte[] bytes) => _fingerprint = SHA256.HashData(bytes);
}
