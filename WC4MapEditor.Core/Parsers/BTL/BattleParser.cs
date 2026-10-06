using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

// Compatibility API for the GUI and CLI; all binary I/O uses BTLParser.
public abstract class BattleParser
{
    private MapData _source = new();
    public byte[] HexData { get; private set; } = [];
    public string HexFilePath => _source.FilePath;
    public BTLHeader Header => _source.Header;
    public int BtlVersion => Header.BtlVersion;
    public bool BelongOffset => _source.BelongOffset;
    public string? LastError { get; private set; }
    public List<Terrain> Terrains { get; } = [];
    public List<Province> Provinces { get; } = [];
    public List<string> Belongs { get; } = [];
    public List<Legion> Legions { get; } = [];
    public List<Building> Buildings { get; } = [];
    public List<Trap> Traps { get; } = [];
    public List<MapCase> Cases { get; } = [];
    public List<Weather> Weathers { get; } = [];
    public List<MapEvent> Events { get; } = [];
    public List<AirForce> AirForces { get; } = [];
    public List<Capital> Capitals { get; } = [];
    public List<StrategicConstruction> StrategyConstructions { get; } = [];
    public List<AirSupport> AirSupports { get; } = [];
    public List<Army> Armies { get; } = [];
    public List<Army_3> ArmiesV3 { get; } = [];
    public List<Reinforcement> Reinforcements { get; } = [];
    public List<Reinforcement_3> ReinforcementsV3 { get; } = [];
    public List<UnitPlacement> UnitPlaces { get; } = [];
    public BTLHeader GetHeaderData() => Header;
    public List<Terrain> GetTerrainData() => Terrains;
    public List<Province> GetProvinceData() => Provinces;
    public List<string> GetBelongData() => Belongs;
    public List<Legion> GetLegionData() => Legions;
    public List<Building> GetBuildingData() => Buildings;
    public List<Army> GetArmyData() => Armies;
    public List<Army> GetTroopData() => Armies;
    public List<Trap> GetTrapData() => Traps;
    public List<MapCase> GetCaseData() => Cases;
    public List<Weather> GetWeatherData() => Weathers;
    public List<MapEvent> GetEventData() => Events;
    public List<Reinforcement> GetReinforcementData() => Reinforcements;
    public List<AirForce> GetAirForceData() => AirForces;
    public List<UnitPlacement> GetUnitPlaceData() => UnitPlaces;
    public List<Capital> GetCapitalData() => Capitals;
    public List<StrategicConstruction> GetStrategyConstructionData() => StrategyConstructions;
    public List<AirSupport> GetAirSupportData() => AirSupports;

    protected void SetMapData(MapData map, byte[]? bytes = null)
    {
        _source = map;
        HexData = bytes ?? [];
        LastError = null;
        Terrains.Clear();
        Provinces.Clear();
        Belongs.Clear();
        for (int i = 0; i < map.TerrainCount; i++)
        {
            if (map.Header.MapNumber == 0) Terrains.Add(Terrain.FromTerrainData(map.GetTerrain(i)));
            Provinces.Add(map.GetProvince(i));
        }
        Belongs.AddRange(map.Belongs);
        Legions.Clear(); Legions.AddRange(map.Legions);
        Buildings.Clear(); Buildings.AddRange(map.Buildings);
        Traps.Clear(); Traps.AddRange(map.Traps);
        Cases.Clear(); Cases.AddRange(map.Cases);
        Weathers.Clear(); Weathers.AddRange(map.Weathers);
        Events.Clear(); Events.AddRange(map.Events);
        AirForces.Clear(); AirForces.AddRange(map.AirForces);
        Capitals.Clear(); Capitals.AddRange(map.Capitals);
        StrategyConstructions.Clear(); StrategyConstructions.AddRange(map.StrategyConstructions);
        AirSupports.Clear(); AirSupports.AddRange(map.AirSupports);
        Armies.Clear(); Armies.AddRange(map.Armies);
        ArmiesV3.Clear(); ArmiesV3.AddRange(map.ArmiesV3);
        Reinforcements.Clear(); Reinforcements.AddRange(map.Reinforcements);
        ReinforcementsV3.Clear(); ReinforcementsV3.AddRange(map.ReinforcementsV3);
        UnitPlaces.Clear(); UnitPlaces.AddRange(map.UnitPlaces);
    }

    public bool LoadHexFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var bytes = new byte[MapLimits.FileSize(stream.Length)];
            stream.ReadExactly(bytes);
            SetMapData(BTLParser.LoadFromBytes(bytes, filePath), bytes);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public MapData ToMapData()
    {
        var map = _source.DeepClone();
        int area = MapLimits.Area(Header.MapLength, Header.MapWidth);
        if (Provinces.Count != area || Belongs.Count != area ||
            (Header.MapNumber == 0 && Terrains.Count != area))
            throw new InvalidDataException("Resize map planes through the map transformer, not the header.");
        map.InitializeTerrain(Header.MapLength, Header.MapWidth);
        for (int i = 0; i < area; i++)
        {
            if (Header.MapNumber == 0) map.SetTerrain(i, Terrains[i].ToTerrainData());
            map.SetProvince(i, Provinces[i]);
        }
        map.Belongs = new(Belongs);
        map.Legions = new(Legions);
        map.Buildings = new(Buildings);
        map.Traps = new(Traps);
        map.Cases = new(Cases);
        map.Weathers = new(Weathers);
        map.Events = new(Events);
        map.AirForces = new(AirForces);
        map.Capitals = new(Capitals);
        map.StrategyConstructions = new(StrategyConstructions);
        map.AirSupports = new(AirSupports);
        map.Armies = new(Armies);
        map.ArmiesV3 = new(ArmiesV3);
        map.Reinforcements = new(Reinforcements);
        map.ReinforcementsV3 = new(ReinforcementsV3);
        map.UnitPlaces = new(UnitPlaces);
        return map;
    }

    public bool SaveData(string outputPath)
    {
        try
        {
            var map = ToMapData();
            var bytes = BTLParser.SaveToBytes(map);
            AtomicFile.Write(outputPath, bytes);
            map.Header = BTLHeader.Parse(bytes);
            map.FilePath = outputPath;
            map.IsModified = false;
            SetMapData(map, bytes);
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; Debug.WriteLine(ex.Message); return false; }
    }

    public static MapData LoadToMapData(string filePath) => BTLParser.LoadFromFile(filePath);
    public static void PopulateMapData(BattleParser parser, MapData mapData) => mapData.CopyFrom(parser.ToMapData());
    public static bool SaveFromMapData(MapData map, string outputPath)
    {
        // Let the existing UI/CLI exception handler display the actual validation error.
        BTLParser.SaveToFile(map, outputPath);
        return true;
    }

    protected bool Create(int width, int height, int legions, int mapNumber)
    {
        try { SetMapData(BTLParser.CreateNew(width, height, legions, mapNumber)); return true; }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }
}
