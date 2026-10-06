using System.Buffers.Binary;
using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.World;

public static class WorldParser
{
    private static readonly byte[] ExpectedHeader = { 0x59, 0x53, 0x41, 0x45, 4, 0, 0, 0 };

    public static MapData LoadFromFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        MapLimits.FileSize(stream.Length);
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        return LoadFromBytes(data, filePath);
    }

    public static MapData LoadFromBytes(byte[] data, string filePath)
    {
        if (data.Length < 16 || !data.AsSpan(0, 8).SequenceEqual(ExpectedHeader))
            throw new InvalidDataException("Expected a YSAE/v4 world header.");
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        int area = MapLimits.Area(width, height);
        int expected = MapLimits.FileSize(16L + area * 18L);
        if (data.Length != expected)
            throw new InvalidDataException($"World length {data.Length}, expected {expected} including both planes.");
        var map = new MapData { FilePath = filePath, FileKind = MapFileKind.World };
        map.Legions.Clear();
        map.InitializeTerrain(width, height);
        map.Header.MapLength = width;
        map.Header.MapWidth = height;
        map.LoadTerrainsFromBytes(data, 16, area);
        map.LoadProvincesFromBytes(data, 16L + area * 16L, area);
        return map;
    }

    public static MapData CreateNew(int width, int height)
    {
        var map = new MapData { FileKind = MapFileKind.World };
        map.Legions.Clear();
        map.InitializeTerrain(width, height);
        map.Header.MapLength = width;
        map.Header.MapWidth = height;
        for (int i = 0; i < map.TerrainCount; i++)
            map.SetTerrain(i, new TerrainData());
        return map;
    }

    public static byte[] SaveToBytes(MapData mapData)
    {
        ArgumentNullException.ThrowIfNull(mapData);
        int area = MapLimits.Area(mapData.MapWidth, mapData.MapHeight);
        if (mapData.TerrainCount != area)
            throw new InvalidDataException("World dimensions do not match its planes.");
        var data = new byte[MapLimits.FileSize(16L + area * 18L)];
        ExpectedHeader.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), mapData.MapWidth);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), mapData.MapHeight);
        mapData.TerrainsToBytes().CopyTo(data, 16);
        mapData.ProvincesToBytes().CopyTo(data, 16 + area * 16);
        return data;
    }

    public static void SaveToFile(MapData mapData, string filePath)
    {
        var bytes = SaveToBytes(mapData);
        AtomicFile.Write(filePath, bytes);
        mapData.FilePath = filePath;
        mapData.IsModified = false;
    }

    public static string CreateHdBinFile(MapData mapData, string baseFilePath)
    {
        if (mapData == null)
            throw new ArgumentNullException(nameof(mapData));

        if (string.IsNullOrEmpty(baseFilePath))
            return string.Empty;

        try
        {
            string? fileDir = System.IO.Path.GetDirectoryName(baseFilePath);
            if (fileDir == null)
                return string.Empty;
            string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(baseFilePath);
            string hdFilePath = System.IO.Path.Combine(fileDir, $"{fileNameWithoutExt}_map_hd.bin");

            int mapWidth = mapData.MapWidth;
            int mapHeight = mapData.MapHeight;

            int value1 = mapWidth * 108;
            int value2 = (int)(mapHeight * 62.5 * 2.0683076);

            int count1 = (value1 / 125) + 1;
            int count2 = (value2 / 125) + 1;
            int repeatCount = count1 * count2;

            using (var fs = new FileStream(hdFilePath, FileMode.Create, FileAccess.Write))
            {
                byte[] value1Bytes = BitConverter.GetBytes(value1);
                fs.Write(value1Bytes, 0, value1Bytes.Length);

                byte[] value2Bytes = BitConverter.GetBytes(value2);
                fs.Write(value2Bytes, 0, value2Bytes.Length);

                byte[] pattern = BitConverter.GetBytes(0xFFFFFFFE);
                for (int i = 0; i < repeatCount; i++)
                    fs.Write(pattern, 0, pattern.Length);
            }

            Debug.WriteLine($"[WorldParser] HD文件创建成功：{hdFilePath}");
            return hdFilePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WorldParser] 创建HD文件失败: {ex.Message}");
            return string.Empty;
        }
    }

    private static double GetProcessMemoryMB()
    {
        return Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
    }
}
