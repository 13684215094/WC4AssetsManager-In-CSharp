using System.Text.Json;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.World;

static class CliTests
{
    public static void Run(Action<string, Action> test)
    {
        test("CLI detailed world exports more than 10000 complete cells", () => WithDirectory(dir =>
        {
            var map = WorldParser.CreateNew(131, 79);
            int last = map.TerrainCount - 1;
            map.SetProvince(last, Province.Create(65534));
            map.SetTerrain(last, new TerrainData { Reserved1 = 17, Reserved2 = 92, Reserved3 = 138 });
            string file = Path.Combine(dir, "world.bin"), output = Path.Combine(dir, "world.json");
            WorldParser.SaveToFile(map, file);
            Invoke("world", file, "--detailed", "--output", output);
            using var json = JsonDocument.Parse(File.ReadAllText(output));
            var cells = json.RootElement.GetProperty("terrains");
            Check(cells.GetArrayLength() == 10349, "truncated world export");
            Check(cells[last].GetProperty("province_value").GetInt32() == 65534, "truncated province value");
            Check(cells[last].GetProperty("reserved_3").GetInt32() == 138, "missing reserved byte");
        }));

        test("CLI v2 export preserves extended armies and int coordinates", () => WithDirectory(dir =>
        {
            var map = BTLParser.CreateNew(4, 2);
            map.Header.BtlVersion = 2;
            map.ArmiesV3.Add(Army_3.CreateDefault(1));
            map.ReinforcementsV3.Add(new Reinforcement_3 { Coordinate = 40000 });
            map.Cases.Add(new MapCase { TargetTile = 50000 });
            map.Capitals.Add(new Capital { Coordinate = 60000 });
            string file = Path.Combine(dir, "v2.btl"), output = Path.Combine(dir, "v2.json");
            BTLParser.SaveToFile(map, file);
            Invoke("stage", file, "--detailed", "--output", output);
            using var json = JsonDocument.Parse(File.ReadAllText(output));
            Check(json.RootElement.GetProperty("armies_v3").GetArrayLength() == 1, "missing v2 army");
            Check(json.RootElement.GetProperty("reinforcements_v3")[0].GetProperty("coordinate").GetInt32() == 40000, "narrowed reinforcement");
            Check(json.RootElement.GetProperty("capitals")[0].GetProperty("coordinate").GetInt32() == 60000, "narrowed capital");
        }));

        test("analyzer uses exact shared offsets and native world header", () =>
        {
            var map = BTLParser.CreateNew(13, 9, mapNumber: 1);
            map.Header.BtlVersion = 3;
            map.Header.PlacementA = 1;
            map.UnitPlaces.Add(new UnitPlacement { Coordinate = 3 });
            map.OpaqueRecords = new byte[8];
            map.ExtraData = [5, 1];
            var bytes = BTLParser.SaveToBytes(map);
            var layout = new BtlLayout(BTLHeader.Parse(bytes));
            string report = new BTLAnalyzer().AnalyzeConquestParser(new ConquestParser(bytes));
            Check(report.Contains($"0x{layout["opaque"].Offset:X8}") && report.Contains("【extra】"), "missing layout sections");
            string world = new BTLAnalyzer().AnalyzeWorldParser(WorldParser.CreateNew(148, 64));
            Check(world.Contains("YSAE") && world.Contains("Int32 LE") && world.Contains("省份/尾索引"), "world report");
        });

        test("fork BTL checker uses shared offsets and full length", () =>
        {
            foreach (int version in new[] { 1, 2, 3 })
            foreach (int mapId in new[] { 0, 1 })
            {
                var map = BTLParser.CreateNew(13, 9, mapNumber: mapId);
                map.Header.BtlVersion = version;
                map.Header.PlacementA = 1;
                map.UnitPlaces.Add(new UnitPlacement { Coordinate = 2 });
                map.OpaqueRecords = new byte[8];
                if (version == 3) map.ExtraData = [3, 4];
                var bytes = BTLParser.SaveToBytes(map);
                var report = BTLFormatChecker.CheckBytes(bytes, validTerrainIds: [0, 1, 63]);
                Check(report.ExpectedSize == bytes.Length && report.ErrorCount == 0,
                    $"layout v{version}/map{mapId}: {report.ToText()}");
                var shortData = bytes[..^1];
                Check(BTLFormatChecker.CheckBytes(shortData).ErrorCount > 0, "truncation accepted");
                Check(BTLRuleChecker.Check(shortData).Any(i => i.Level == BTLIssueLevel.Error),
                    "rule checker accepted truncation");
            }
        });

        test("fork BTL repair does not change v3 army or aligned capacity", () =>
        {
            var map = BTLParser.CreateNew(13, 9);
            map.Header.BtlVersion = 3;
            map.ArmiesV3.Add(Army_3.CreateDefault(1));
            var data = BTLParser.SaveToBytes(map);
            var layout = new BtlLayout(BTLHeader.Parse(data));
            var armyBefore = data.AsSpan(layout["armies"].Offset, layout["armies"].Size).ToArray();
            var headerBefore = data.AsSpan(0, 128).ToArray();
            BTLRuleChecker.Fix(data);
            Check(data.AsSpan(layout["armies"].Offset, layout["armies"].Size).SequenceEqual(armyBefore),
                "v3 army was rewritten by v1 repair");
            Check(data.AsSpan(0, 128).SequenceEqual(headerBefore), "aligned capacity header changed");
        });

        test("external-world BTL repair preserves sea buildings and owner encoding", () =>
        {
            var map = BTLParser.CreateNew(13, 9, mapNumber: 1);
            var building = Building.CreateDefault(1);
            building.BuildingType = 31;
            map.Buildings.Add(building);
            var data = BTLParser.SaveToBytes(map);
            var layout = new BtlLayout(BTLHeader.Parse(data));
            var owners = data.AsSpan(layout["belongs"].Offset, layout["belongs"].Size).ToArray();
            BTLRuleChecker.Fix(data);
            Check(Building.FromBytes(data, layout["Buildings"].Offset).BuildingType == 31,
                "sea building changed without world terrain");
            Check(data.AsSpan(layout["belongs"].Offset, layout["belongs"].Size).SequenceEqual(owners),
                "external-world owner encoding changed");
        });

        test("BTL fix command keeps an original backup", () => WithDirectory(dir =>
        {
            var map = BTLParser.CreateNew(13, 9);
            string path = Path.Combine(dir, "map.btl");
            BTLParser.SaveToFile(map, path);
            byte[] original = File.ReadAllBytes(path);
            var host = CliCommandHost.Instance;
            using var output = new StringWriter();
            host.SetOutput(output);
            try { host.Execute($"fix {path}"); }
            finally { host.SetOutput(Console.Out); }
            Check(File.Exists(path + ".bak"), "backup not created");
            Check(File.ReadAllBytes(path + ".bak").SequenceEqual(original), "backup differs from original");
            Check(BTLFormatChecker.CheckFile(path).ExpectedSize == new FileInfo(path).Length,
                "repaired file has invalid layout");
        }));
    }

    private static void Invoke(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            int code = WC4MapEditor.Cli.Program.Main(args);
            Check(code == 0, output.ToString());
        }
        finally { Console.SetOut(original); }
    }

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
