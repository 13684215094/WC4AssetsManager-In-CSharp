using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace WC4MapEditor.Rendering.Imaging;

public static class PngColorProfile
{
    private const int BrokenProfileLength = 3144;
    // HP-Microsoft sRGB v2 perceptual, rejected by libpng's known-bad profile list.
    private static readonly byte[] BrokenProfileHash = Convert.FromHexString(
        "2B3AA1645779A9E634744FAF9B01E9102B0C9B88FD6DECED7934DF86B949AF7E");

    /// <summary>Replaces only the known-bad ICC with standard sRGB in a decode copy; never changes the input.</summary>
    public static byte[] NormalizeForDecode(byte[] data)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 33 || !data.AsSpan().StartsWith(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)) != 13 || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return data;
        int profileOffset = -1, profileSize = 0;
        bool imageDataSeen = false, srgbSeen = false;
        for (int offset = 8; offset <= data.Length - 12;)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            if (length > (uint)(data.Length - offset - 12)) return data;
            int size = (int)length + 12;
            var type = data.AsSpan(offset + 4, 4);
            if (type.SequenceEqual("sRGB"u8)) srgbSeen = true;
            if (type.SequenceEqual("iCCP"u8))
            {
                // Duplicate or late profiles are malformed; leave their diagnosis to the decoder.
                if (profileOffset >= 0 || imageDataSeen || !IsBrokenProfile(data.AsSpan(offset + 8, (int)length))) return data;
                uint expected = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + size - 4));
                if (Crc32(data.AsSpan(offset + 4, size - 8)) != expected) return data;
                profileOffset = offset;
                profileSize = size;
            }
            if (type.SequenceEqual("IDAT"u8)) imageDataSeen = true;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !imageDataSeen || profileOffset < 0 || srgbSeen) return data;
                var normalized = new byte[data.Length - profileSize + 13];
                data.AsSpan(0, profileOffset).CopyTo(normalized);
                var srgb = normalized.AsSpan(profileOffset, 13);
                BinaryPrimitives.WriteUInt32BigEndian(srgb, 1);
                "sRGB"u8.CopyTo(srgb[4..]);
                // Perceptual intent (0), matching the known profile. Keep sRGB ahead of IDAT.
                BinaryPrimitives.WriteUInt32BigEndian(srgb[9..], Crc32(srgb.Slice(4, 5)));
                data.AsSpan(profileOffset + profileSize).CopyTo(normalized.AsSpan(profileOffset + 13));
                return normalized;
            }
            offset += size;
        }
        return data;
    }

    private static bool IsBrokenProfile(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length > 64 * 1024) return false;
        int nameLength = chunk.IndexOf((byte)0);
        if (nameLength is < 1 or > 79 || chunk.Length <= nameLength + 2 || chunk[nameLength + 1] != 0) return false;
        // Bound decompression even for untrusted PNGs. Normal profiles pass through unchanged.
        byte[] profile = new byte[BrokenProfileLength + 1];
        try
        {
            using var input = new MemoryStream(chunk[(nameLength + 2)..].ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            int read = zlib.ReadAtLeast(profile, profile.Length, throwOnEndOfStream: false);
            return read == BrokenProfileLength && SHA256.HashData(profile.AsSpan(0, read)).AsSpan().SequenceEqual(BrokenProfileHash);
        }
        catch (InvalidDataException) { return false; }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        return ~crc;
    }
}
