using System.Buffers.Binary;
using System.IO.Compression;
using SkiaSharp;
using WC4MapEditor.Rendering.Imaging;

static class PngColorProfileTests
{
    public static void Run(Action<string, Action> test)
    {
        test("PNG color profiles preserve valid linear ICC and non-PNG data", () =>
        {
            using var linear = SKColorSpace.CreateSrgbLinear();
            using var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul, linear));
            bitmap.Erase(new SKColor(180, 80, 40, 128));
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            byte[] png = encoded.ToArray();
            Check(png.AsSpan().IndexOf("iCCP"u8) >= 0, "fixture lacks its linear ICC");
            Check(ReferenceEquals(png, PngColorProfile.NormalizeForDecode(png)), "valid ICC was removed");
            using var decoded = ProjectImageDecoder.Decode(png);
            Check(decoded.ColorSpace?.GammaIsLinear == true && decoded.GetPixel(0, 0).Alpha == 128,
                "valid color space or transparency changed");
            foreach (var format in new[] { SKEncodedImageFormat.Webp, SKEncodedImageFormat.Jpeg })
            {
                using var data = bitmap.Encode(format, 100);
                byte[] bytes = data.ToArray();
                Check(ReferenceEquals(bytes, PngColorProfile.NormalizeForDecode(bytes)), "non-PNG was rewritten");
                using var image = ProjectImageDecoder.Decode(bytes);
                Check(image.Width == 3 && image.Height == 2, "non-PNG decode changed");
            }
        });

        test("PNG color profiles preserve unknown ICC and malformed or oversized metadata", () =>
        {
            using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.Blue);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            byte[] png = encoded.ToArray();
            foreach (int size in new[] { 3144, 3145, 1024 * 1024 })
            {
                byte[] profile = new byte[size];
                new Random(719).NextBytes(profile);
                byte[] image = InsertProfile(png, profile);
                Check(ReferenceEquals(image, PngColorProfile.NormalizeForDecode(image)), "unknown profile was removed");
            }
            byte[] highlyCompressed = InsertProfile(png, new byte[1024 * 1024]);
            Check(ReferenceEquals(highlyCompressed, PngColorProfile.NormalizeForDecode(highlyCompressed)), "oversized decompressed ICC was rewritten");
            byte[] truncated = png[..^5];
            Check(ReferenceEquals(truncated, PngColorProfile.NormalizeForDecode(truncated)), "truncated PNG rewritten");
            byte[] invalid = (byte[])png.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(invalid.AsSpan(33), uint.MaxValue);
            Check(ReferenceEquals(invalid, PngColorProfile.NormalizeForDecode(invalid)), "overflowing chunk accepted");
            foreach (byte[] bytes in new byte[][] { [], [137], [0, 1, 2, 3] })
                Check(ReferenceEquals(bytes, PngColorProfile.NormalizeForDecode(bytes)), "invalid data rewritten");
        });
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        test("1.30.0 PNG color profiles normalize only known-bad ICC without changing pixels or saved originals", () =>
        {
            string assets = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0", "assets");
            int normalizedCount = 0;
            byte[]? known = null, clean = null;
            foreach (string path in Directory.EnumerateFiles(assets, "*.png", SearchOption.AllDirectories))
            {
                byte[] original = File.ReadAllBytes(path), snapshot = (byte[])original.Clone();
                byte[] normalized = PngColorProfile.NormalizeForDecode(original);
                Check(original.AsSpan().SequenceEqual(snapshot), "source bytes mutated");
                if (ReferenceEquals(original, normalized)) continue;
                known ??= original; clean ??= normalized;
                Check(normalized.AsSpan().IndexOf("iCCP"u8) < 0, "broken ICC remains");
                Check(ReferenceEquals(normalized, PngColorProfile.NormalizeForDecode(normalized)), "normalization is not idempotent");
                using var raw = SKBitmap.Decode(original);
                using var compatible = ProjectImageDecoder.Decode(original);
                Check(raw != null && raw.Width == compatible.Width && raw.Height == compatible.Height &&
                    raw.ColorType == compatible.ColorType && raw.AlphaType == compatible.AlphaType &&
                    SKColorSpace.Equal(raw.ColorSpace, compatible.ColorSpace) && raw.GetPixelSpan().SequenceEqual(compatible.GetPixelSpan()),
                    $"pixels, dimensions, alpha or color space differ: {Path.GetRelativePath(assets, path)}; " +
                    $"raw sRGB={raw?.ColorSpace?.IsSrgb}, normalized sRGB={compatible.ColorSpace?.IsSrgb}");
                Check(AtlasCompositeRenderer.Encode(compatible, path).AsSpan().SequenceEqual(original), "no-op save rewrites original metadata");
                normalizedCount++;
            }
            Check(normalizedCount == 37, $"expected 37 known profiles, found {normalizedCount}");
            if (known == null || clean == null) throw new Exception("missing corpus profile");
            int chunk = known.AsSpan().IndexOf("iCCP"u8) - 4;
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(known.AsSpan(chunk));
            byte[] badCrc = (byte[])known.Clone(); badCrc[chunk + length + 8] ^= 1;
            Check(ReferenceEquals(badCrc, PngColorProfile.NormalizeForDecode(badCrc)), "damaged ICC CRC silently repaired");
            byte[] truncated = known[..^6];
            Check(ReferenceEquals(truncated, PngColorProfile.NormalizeForDecode(truncated)), "truncated known-profile PNG rewritten");
            byte[] duplicate = new byte[known.Length + length + 12];
            known.AsSpan(0, chunk).CopyTo(duplicate);
            known.AsSpan(chunk, length + 12).CopyTo(duplicate.AsSpan(chunk));
            known.AsSpan(chunk).CopyTo(duplicate.AsSpan(chunk + length + 12));
            Check(ReferenceEquals(duplicate, PngColorProfile.NormalizeForDecode(duplicate)), "duplicate profile silently repaired");
            Console.WriteLine($"PNG profile corpus: {normalizedCount} normalized, identical decoded pixels; original bytes retained on no-op save.");
        });
    }

    private static byte[] InsertProfile(byte[] png, byte[] profile)
    {
        using var payload = new MemoryStream();
        payload.Write("profile\0\0"u8);
        using (var zlib = new ZLibStream(payload, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(profile);
        byte[] bytes = payload.ToArray();
        byte[] chunk = new byte[bytes.Length + 12];
        BinaryPrimitives.WriteInt32BigEndian(chunk, bytes.Length);
        "iCCP"u8.CopyTo(chunk.AsSpan(4)); bytes.CopyTo(chunk, 8);
        uint crc = uint.MaxValue;
        foreach (byte value in chunk.AsSpan(4, bytes.Length + 4))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        return png[..33].Concat(chunk).Concat(png[33..]).ToArray();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
