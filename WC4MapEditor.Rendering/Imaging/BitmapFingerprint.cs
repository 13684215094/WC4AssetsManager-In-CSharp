using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;

namespace WC4MapEditor.Rendering.Imaging;

public static class BitmapFingerprint
{
    public static string Create(SKBitmap? bitmap, string options = "") => bitmap == null ? options :
        $"{bitmap.Width}:{bitmap.Height}:{Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(bitmap.Pixels.AsSpan())))}:{options}";
}
