using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

public sealed record BtlSection(string Name, int Offset, int Count, int Stride)
{
    public int Size => checked(Count * Stride);
    public int End => checked(Offset + Size);
}

public sealed class BtlLayout
{
    public List<BtlSection> Sections { get; } = [];
    public int Area { get; }
    public int Capacity { get; }
    public int Length => Sections[^1].End;
    public BtlSection this[string name] => Sections.Single(s => s.Name == name);

    public static int Align(int area) => checked((area + 7) & ~7);

    public BtlLayout(BTLHeader header)
    {
        if (header.BtlVersion is < 1 or > 3 || header.MapNumber is < 0 or > 255)
            throw new InvalidDataException("Unsupported BTL version or map resource ID.");
        if (header.MapClipX < 0 || header.MapClipY < 0)
            throw new InvalidDataException("Negative map origin.");
        Area = MapLimits.Area(header.MapLength, header.MapWidth);
        Capacity = header.SelectableTileCount == 0 ? Area : header.SelectableTileCount;
        if (Capacity < Area || Capacity > Align(Area))
            throw new InvalidDataException("Invalid BTL map-plane capacity.");
        Add("header", 1, 128);
        Add("Legions", header.ArmyCount, 300);
        Add("terrain", header.MapNumber == 0 ? Area : 0, 16);
        Add("provinces", Capacity, 2);
        Add("belongs", Capacity, 1);
        Add("Buildings", header.BuildingCount, 32);
        Add("armies", header.TroopCount, header.BtlVersion == 1 ? 48 : 64);
        Add("Traps", header.TrapCount, 12);
        Add("Cases", header.PlanCount, 16);
        Add("Weathers", header.WeatherCount, 16);
        Add("Events", header.EventCount, 44);
        Add("reinforcements", header.ReinforcementCount, header.BtlVersion == 1 ? 80 : 104);
        Add("AirForces", header.AirRaidCount, 20);
        Add("placementA", header.PlacementA, 8);
        Add("placementB", header.PlacementB, 8);
        Add("Capitals", header.ConqueredFlagPosition, 4);
        Add("opaque", header.Unknown5, 8);
        Add("StrategyConstructions", header.StrategyCount, 16);
        Add("AirSupports", header.AirSupportCount, 16);
        Add("extra", header.BtlVersion == 3 ? header.Unknown4 : 0, 1);
    }

    private void Add(string name, int count, int stride)
    {
        if (count < 0) throw new InvalidDataException($"Negative record count: {name}.");
        int offset = Sections.Count == 0 ? 0 : Length;
        MapLimits.FileSize(offset + (long)count * stride);
        Sections.Add(new(name, offset, count, stride));
    }
}
