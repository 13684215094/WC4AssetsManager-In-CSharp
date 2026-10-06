using System.Buffers.Binary;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.Conquest;

int passed = 0;
var failures = new List<string>();
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name); Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
void Equal(byte[] expected, byte[] actual)
{
    int mismatch = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], -1);
    if (mismatch >= 0 || expected.Length != actual.Length)
        throw new Exception($"Byte mismatch at {mismatch}; lengths {expected.Length}/{actual.Length}");
}
void Reject(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException) { return; }
    throw new Exception("Expected validation failure.");
}

Test("world nonzero planes and 32-bit width", () =>
{
    var map = WorldParser.CreateNew(65536, 1);
    map.SetTerrain(65535, new TerrainData { Reserved3 = 37 });
    map.SetProvince(17, Province.Create(1234));
    var bytes = WorldParser.SaveToBytes(map);
    Equal(bytes, WorldParser.SaveToBytes(WorldParser.LoadFromBytes(bytes, "test")));
    Reject(() => WorldParser.LoadFromBytes(bytes[..^1], ""));
    Reject(() => WorldParser.LoadFromBytes(bytes.Concat(new byte[1]).ToArray(), ""));
    bytes[0] = 0;
    Reject(() => WorldParser.LoadFromBytes(bytes, ""));
});

Test("dimension validation before allocation", () =>
{
    foreach (var size in new[] { (0, 5), (-2, -3), (int.MaxValue, int.MaxValue), (1001, 1000) })
        Reject(() => new MapData(size.Item1, size.Item2));
    var map = WorldParser.CreateNew(7, 9);
    Reject(() => map.InitializeTerrain(int.MaxValue, 2));
    if (map.MapWidth != 7 || map.TerrainCount != 63) throw new Exception("Failed allocation mutated dimensions.");
    Reject(() => Army.CreateDefault(32768));
    Reject(() => Trap.CreateDefault(-1));
    Reject(() => { var b = Building.CreateDefault(65536); b.ToBytes(new byte[32], 0); });
});

for (int version = 1; version <= 3; version++)
for (int mapId = 0; mapId <= 1; mapId++)
{
    int v = version, id = mapId;
    Test($"BTL v{v} map{id} randomized records and padding", () =>
    {
        var h = new BTLHeader
        {
            BtlVersion = v, MapNumber = id, MapLength = 13, MapWidth = 9,
            SelectableTileCount = 120, ArmyCount = 1, BuildingCount = 2,
            TroopCount = 3, TrapCount = 1, PlanCount = 1, EventCount = 1,
            WeatherCount = 1, ReinforcementCount = 2, AirRaidCount = 1,
            PlacementA = 1, PlacementB = 2, ConqueredFlagPosition = 1,
            Unknown5 = 2, StrategyCount = 1, AirSupportCount = 1,
            Unknown4 = v == 3 ? 9 : 37
        };
        var layout = new BtlLayout(h);
        var bytes = new byte[layout.Length];
        new Random(800 + v * 2 + id).NextBytes(bytes);
        h.ToBytes().CopyTo(bytes, 0);
        var map = BTLParser.LoadFromBytes(bytes, "renamed.btl");
        Equal(bytes, BTLParser.SaveToBytes(map));
        Equal(bytes, BTLParser.SaveToBytes(map.DeepClone()));
        var facade = new ConquestParser(bytes);
        Equal(bytes, BTLParser.SaveToBytes(facade.ToMapData()));
        Reject(() => BTLParser.LoadFromBytes(bytes[..^1], ""));
        Reject(() => BTLParser.LoadFromBytes(bytes.Concat(new byte[1]).ToArray(), ""));
    });
}

Test("non-square constructors and zero capacity", () =>
{
    var parser = new ConquestParser();
    if (!parser.CreateNew(148, 60, 3, 1)) throw new Exception(parser.LastError);
    if (parser.Header.MapLength != 148 || parser.Header.MapWidth != 60) throw new Exception("Dimensions swapped.");
    var map = StageParser.CreateNewMapData(16, 7, 2);
    map.Header.SelectableTileCount = 0;
    var bytes = BTLParser.SaveToBytes(map);
    Equal(bytes, BTLParser.SaveToBytes(BTLParser.LoadFromBytes(bytes, "")));
});

Test("invalid save leaves destination unchanged", () =>
{
    string dir = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, "map.btl");
    try
    {
        File.WriteAllBytes(path, [42, 81, 3]);
        var map = BTLParser.CreateNew(13, 9);
        map.Belongs.RemoveAt(0);
        Reject(() => BTLParser.SaveToFile(map, path));
        Equal([42, 81, 3], File.ReadAllBytes(path));
        var world = WorldParser.CreateNew(3, 3);
        world.MapWidth = 0;
        Reject(() => WorldParser.SaveToFile(world, path));
        Equal([42, 81, 3], File.ReadAllBytes(path));
    }
    finally { Directory.Delete(dir, recursive: true); }
});

TransformTests.Run(Test);
CliTests.Run(Test);
GeneralDataTests.Run(Test);
AssetAuditTests.Run(Test);

int corpus = Array.IndexOf(args, "--corpus");
if (corpus >= 0)
{
    string root = Path.GetFullPath(args[corpus + 1]);
    GeneralDataTests.RunCorpus(Test, root);
    Test("original BTL corpus lossless roundtrip", () =>
    {
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "wc4"), "*.btl", SearchOption.AllDirectories))
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                Equal(bytes, BTLParser.SaveToBytes(BTLParser.LoadFromBytes(bytes, file)));
                count++;
            }
            catch (Exception ex) { throw new Exception(file, ex); }
        }
        if (count == 0) throw new Exception("No BTL fixtures found.");
        Console.WriteLine($"Corpus: {count} BTL files, byte-identical.");
    });
    Test("original world corpus lossless roundtrip", () =>
    {
        var worlds = Directory.EnumerateFiles(Path.Combine(root, "wc4"), "world.bin", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "map_5.13_Dev", "Maps", "world2.bin"));
        foreach (var file in worlds)
        {
            var bytes = File.ReadAllBytes(file);
            Equal(bytes, WorldParser.SaveToBytes(WorldParser.LoadFromBytes(bytes, file)));
        }
    });
}
Console.WriteLine($"RESULT: {passed} passed, {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;
