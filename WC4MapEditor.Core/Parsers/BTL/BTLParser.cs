using System.Collections.ObjectModel;
using System.Globalization;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

public static class BTLParser
{
    private delegate T Reader<T>(ReadOnlySpan<byte> data, int offset);
    private delegate void Writer<T>(T value, Span<byte> data);

    public static MapData LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var data = new byte[MapLimits.FileSize(stream.Length)];
        stream.ReadExactly(data);
        return LoadFromBytes(data, path);
    }

    public static MapData LoadFromBytes(byte[] data, string filePath)
    {
        var header = BTLHeader.Parse(data);
        var layout = new BtlLayout(header);
        if (layout.Length != data.Length)
            throw new InvalidDataException($"BTL length {data.Length}, expected {layout.Length}.");
        var map = new MapData { Header = header, FilePath = filePath, FileKind = MapFileKind.Battle };
        map.InitializeTerrain(header.MapLength, header.MapWidth);
        if (header.MapNumber == 0)
            map.LoadTerrainsFromBytes(data, layout["terrain"].Offset, layout.Area);
        map.LoadProvincesFromBytes(data, layout["provinces"].Offset, layout.Area);
        map.ProvincePadding = data.AsSpan(layout["provinces"].Offset + layout.Area * 2, (layout.Capacity - layout.Area) * 2).ToArray();
        map.BelongPadding = data.AsSpan(layout["belongs"].Offset + layout.Area, layout.Capacity - layout.Area).ToArray();
        map.Legions = Read<Legion>("Legions", Legion.FromBytes);
        map.Buildings = Read<Building>("Buildings", Building.FromBytes);
        map.Traps = Read<Trap>("Traps", Trap.FromBytes);
        map.Cases = Read<MapCase>("Cases", MapCase.FromBytes);
        map.Weathers = Read<Weather>("Weathers", Weather.FromBytes);
        map.Events = Read<MapEvent>("Events", MapEvent.FromBytes);
        map.AirForces = Read<AirForce>("AirForces", AirForce.FromBytes);
        map.Capitals = Read<Capital>("Capitals", Capital.FromBytes);
        map.StrategyConstructions = Read<StrategicConstruction>("StrategyConstructions", StrategicConstruction.FromBytes);
        map.AirSupports = Read<AirSupport>("AirSupports", AirSupport.FromBytes);
        map.BelongOffset = header.MapNumber != 0 && map.Legions.Count > 0 && map.Legions.Min(l => l.ActionId) > 0;
        for (int i = 0; i < layout.Area; i++)
        {
            byte value = data[layout["belongs"].Offset + i];
            if (map.BelongOffset) value = unchecked((byte)(value + 1));
            map.Belongs.Add(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        if (header.BtlVersion == 1) map.Armies = Read<Army>("armies", Army.FromBytes);
        else map.ArmiesV3 = Read<Army_3>("armies", Army_3.FromBytes);
        if (header.BtlVersion == 1) map.Reinforcements = Read<Reinforcement>("reinforcements", Reinforcement.FromBytes);
        else map.ReinforcementsV3 = Read<Reinforcement_3>("reinforcements", Reinforcement_3.FromBytes);
        map.UnitPlaces = Read<UnitPlacement>("placementA", UnitPlacement.FromBytes);
        foreach (var value in Read<UnitPlacement>("placementB", UnitPlacement.FromBytes)) map.UnitPlaces.Add(value);
        map.OpaqueRecords = data.AsSpan(layout["opaque"].Offset, layout["opaque"].Size).ToArray();
        map.ExtraData = data.AsSpan(layout["extra"].Offset, layout["extra"].Size).ToArray();
        return map;

        ObservableCollection<T> Read<T>(string name, Reader<T> reader)
        {
            var section = layout[name];
            var values = new List<T>(section.Count);
            for (int i = 0; i < section.Count; i++) values.Add(reader(data, section.Offset + i * section.Stride));
            return new(values);
        }
    }

    public static byte[] SaveToBytes(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        int area = MapLimits.Area(map.MapWidth, map.MapHeight);
        if (map.TerrainCount != area || map.Belongs.Count != area)
            throw new InvalidDataException("BTL dimensions and map planes must match.");
        var h = map.Header.DeepClone();
        h.MapLength = map.MapWidth;
        h.MapWidth = map.MapHeight;
        h.ArmyCount = map.Legions.Count;
        h.BuildingCount = map.Buildings.Count;
        if ((h.BtlVersion == 1 && (map.ArmiesV3.Count != 0 || map.ReinforcementsV3.Count != 0)) ||
            (h.BtlVersion >= 2 && (map.Armies.Count != 0 || map.Reinforcements.Count != 0)))
            throw new InvalidDataException("Unit collections do not match the BTL version; explicit conversion is required.");
        h.TroopCount = h.BtlVersion == 1 ? map.Armies.Count : map.ArmiesV3.Count;
        h.TrapCount = map.Traps.Count;
        h.PlanCount = map.Cases.Count;
        h.WeatherCount = map.Weathers.Count;
        h.EventCount = map.Events.Count;
        h.ReinforcementCount = h.BtlVersion == 1 ? map.Reinforcements.Count : map.ReinforcementsV3.Count;
        h.AirRaidCount = map.AirForces.Count;
        if ((long)h.PlacementA + h.PlacementB != map.UnitPlaces.Count)
            throw new InvalidDataException("Placement A/B counts must be updated explicitly; their boundary cannot be guessed.");
        h.ConqueredFlagPosition = map.Capitals.Count;
        if (map.OpaqueRecords.Length % 8 != 0) throw new InvalidDataException("Malformed opaque records.");
        h.Unknown5 = map.OpaqueRecords.Length / 8;
        h.StrategyCount = map.StrategyConstructions.Count;
        h.AirSupportCount = map.AirSupports.Count;
        if (h.BtlVersion == 3) h.Unknown4 = map.ExtraData.Length;
        else if (map.ExtraData.Length != 0) throw new InvalidDataException("This BTL version cannot store extra data.");
        var layout = new BtlLayout(h);
        int padding = layout.Capacity - area;
        if (map.ProvincePadding.Length != padding * 2 || map.BelongPadding.Length != padding)
            throw new InvalidDataException("Map-plane padding does not match the header capacity.");
        var data = new byte[layout.Length];
        h.ToBytes().CopyTo(data, 0);
        if (h.MapNumber == 0) map.TerrainsToBytes().CopyTo(data, layout["terrain"].Offset);
        map.ProvincesToBytes().CopyTo(data, layout["provinces"].Offset);
        map.ProvincePadding.CopyTo(data, layout["provinces"].Offset + area * 2);
        for (int i = 0; i < area; i++)
        {
            string text = map.Belongs[i];
            if (text.StartsWith("&H", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            byte value = byte.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (map.BelongOffset) value = unchecked((byte)(value - 1));
            data[layout["belongs"].Offset + i] = value;
        }
        map.BelongPadding.CopyTo(data, layout["belongs"].Offset + area);
        Write("Legions", map.Legions, (Legion v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Buildings", map.Buildings, (Building v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Traps", map.Traps, (Trap v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Cases", map.Cases, (MapCase v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Weathers", map.Weathers, (Weather v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Events", map.Events, (MapEvent v, Span<byte> d) => v.ToBytes(d, 0));
        Write("AirForces", map.AirForces, (AirForce v, Span<byte> d) => v.ToBytes(d, 0));
        Write("Capitals", map.Capitals, (Capital v, Span<byte> d) => v.ToBytes(d, 0));
        Write("StrategyConstructions", map.StrategyConstructions, (StrategicConstruction v, Span<byte> d) => v.ToBytes(d, 0));
        Write("AirSupports", map.AirSupports, (AirSupport v, Span<byte> d) => v.ToBytes(d, 0));
        if (h.BtlVersion == 1) Write("armies", map.Armies, (Army v, Span<byte> d) => v.ToBytes(d, 0));
        else Write("armies", map.ArmiesV3, (Army_3 v, Span<byte> d) => v.ToBytes(d, 0));
        if (h.BtlVersion == 1) Write("reinforcements", map.Reinforcements, (Reinforcement v, Span<byte> d) => v.ToBytes(d, 0));
        else Write("reinforcements", map.ReinforcementsV3, (Reinforcement_3 v, Span<byte> d) => v.ToBytes(d, 0));
        Write("placementA", map.UnitPlaces.Take(h.PlacementA).ToList(), (UnitPlacement v, Span<byte> d) => v.ToBytes(d, 0));
        Write("placementB", map.UnitPlaces.Skip(h.PlacementA).ToList(), (UnitPlacement v, Span<byte> d) => v.ToBytes(d, 0));
        map.OpaqueRecords.CopyTo(data, layout["opaque"].Offset);
        map.ExtraData.CopyTo(data, layout["extra"].Offset);
        return data;

        void Write<T>(string name, IList<T> values, Writer<T> writer)
        {
            var section = layout[name];
            for (int i = 0; i < values.Count; i++) writer(values[i], data.AsSpan(section.Offset + i * section.Stride, section.Stride));
        }
    }

    public static void SaveToFile(MapData map, string path)
    {
        var bytes = SaveToBytes(map);
        AtomicFile.Write(path, bytes);
        map.FilePath = path;
        map.Header = BTLHeader.Parse(bytes);
        map.IsModified = false;
    }

    public static MapData CreateNew(int width, int height, int numLegions = 2, int mapNumber = 0)
    {
        int area = MapLimits.Area(width, height);
        if (numLegions is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(numLegions));
        if (mapNumber is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(mapNumber));
        var map = new MapData { FileKind = MapFileKind.Battle };
        map.InitializeTerrain(width, height);
        map.Header.MapLength = width;
        map.Header.MapWidth = height;
        map.Header.MapNumber = mapNumber;
        map.Header.MapClipY = mapNumber == 0 ? 0 : 2;
        map.Header.SelectableTileCount = BtlLayout.Align(area);
        map.Legions.Clear();
        for (int i = 0; i < numLegions; i++) map.Legions.Add(Legion.CreateDefault(i + 1));
        map.Header.ArmyCount = numLegions;
        for (int i = 0; i < area; i++) { map.SetProvince(i, Province.Create(0xFFFF)); map.Belongs.Add("FF"); }
        map.ProvincePadding = new byte[(map.Header.SelectableTileCount - area) * 2];
        map.BelongPadding = new byte[map.Header.SelectableTileCount - area];
        map.IsModified = true;
        return map;
    }
}
