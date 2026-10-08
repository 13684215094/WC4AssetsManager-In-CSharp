using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.ArmyGroupSetting;
using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.Skill;

static class EditorDataAuditTests
{
    private static readonly (string Name, Type Model)[] Tables =
    [
        ("ArmySettings", typeof(ArmySettingData)), ("ArmyBuffSettings", typeof(ArmyBuffSettingData)),
        ("SkillSettings", typeof(SkillSettingData)), ("CountryTechSettings", typeof(CountryTechData)),
        ("ArmyGroupEventSettings", typeof(ArmyGroupEventData)), ("EventBuffSettings", typeof(EventBuffData)),
        ("ConquerEventSettings", typeof(ConquerEventData)), ("BuildingSettings", typeof(BuildingSettingData)),
        ("FacilitySettings", typeof(FacilitySettingData)), ("CountrySettings", typeof(CountrySettingData)),
        ("ConquerCountrySettings", typeof(ConquerCountrySettingData)), ("ConquerSettings", typeof(ConquerSettingData)),
        ("ArmyGroupSettings", typeof(ArmyGroupSettingData)), ("ArmyGroupReinforcementSettings", typeof(ArmyGroupReinforcementData)),
        ("EventSettings", typeof(EventSettingData)), ("EventStageSettings", typeof(EventStageSettingData)),
        ("EventCalendarSettings", typeof(EventCalendarSettingData))
    ];

    public static void Run(Action<string, Action> test)
    {
        test("JSON editing preserves unknown fields nulls missing values duplicate IDs and unchanged token types", () =>
        {
            using var f = new Fixture();
            string path = Path.Combine(f.Root, "table.json");
            string previous = AssetManager.Default.AssetsRoot;
            AssetManager.Default.Scan(f.Root, true);
            try
            {
                File.WriteAllText(path, """[{"Id":0,"Name":null,"Type":"3","Custom":{"v":[null,true,7]}},{"Id":0,"Name":"same ID"}]""");
                var table = new JsonTableFile<SkillSettingData>();
                var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
                var rows = table.Read(path, options);
                var original = JsonNode.Parse(File.ReadAllText(path));
                Check(JsonNode.DeepEquals(original, JsonNode.Parse(table.Serialize(path, rows))), "no-op rewrite changed semantics");
                rows[0].Level = 9;
                var expected = original!.DeepClone();
                expected[0]!["Level"] = 9;
                table.Save(path, rows);
                Check(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))), "edit changed unrelated fields");
                rows.RemoveAt(1);
                rows.Add(new SkillSettingData { Id = 21, Name = "new" });
                table.Save(path, rows);
                var saved = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
                Check(saved.Count == 2 && saved[0]!["Custom"] != null && (int)saved[1]!["Id"]! == 21, "row identity or insertion");
            }
            finally { Restore(previous); }
        });

        test("specialized JSON editors refuse damaged tables and stale project paths", () =>
        {
            using var f = new Fixture();
            var manager = AssetManager.Default;
            string previous = manager.AssetsRoot;
            try
            {
                string a = Path.Combine(f.Root, "a", "assets"), b = Path.Combine(f.Root, "b", "assets");
                Directory.CreateDirectory(Path.Combine(a, "json"));
                Directory.CreateDirectory(Path.Combine(b, "json"));
                string path = Path.Combine(a, "json", "SkillSettings.json");
                File.WriteAllText(path, """[{"Id":1,"Custom":42}]""");
                manager.Scan(a, true);
                SkillSettingParser.ClearInstance();
                var parser = SkillSettingParser.Instance;
                Check(parser.All.Count == 1, "valid table did not load");
                manager.Scan(b, true);
                Check(!parser.SaveAll() && File.ReadAllText(path).Contains("Custom"), "stale editor wrote an old project");
                manager.Scan(a, true);
                foreach (string damaged in new[] { "invalid", "null", "{}", "[null]", "[{\"Id\":1},{\"Id\":2,\"Level\":{}}]" })
                {
                    File.WriteAllText(path, damaged);
                    parser.Reload();
                    Check(!parser.SaveAll() && File.ReadAllText(path) == damaged, "failed load overwrote a table");
                }
                File.Delete(path);
                parser.Reload();
                Check(!parser.SaveAll() && !File.Exists(path), "missing table replaced with stale rows");
            }
            finally { Restore(previous); ClearParsers(); }
        });

        test("multi-table config save validates every table before replacing any file", () =>
        {
            using var f = new Fixture();
            string previous = AssetManager.Default.AssetsRoot;
            try
            {
                string assets = Path.Combine(f.Root, "assets");
                Directory.CreateDirectory(Path.Combine(assets, "json"));
                string buildings = Path.Combine(assets, "json", "BuildingSettings.json");
                File.WriteAllText(buildings, """[{"Id":1,"Custom":42}]""");
                File.WriteAllText(Path.Combine(assets, "json", "FacilitySettings.json"), "broken");
                AssetManager.Default.Scan(assets, true);
                BuildingFacilitySettingParser.ClearInstance();
                var parser = BuildingFacilitySettingParser.Instance;
                parser.BuildingItems[0].ProduceMoney = 5;
                Check(!parser.SaveAll() && File.ReadAllText(buildings) == """[{"Id":1,"Custom":42}]""", "partial multi-table save");
            }
            finally { Restore(previous); ClearParsers(); }
        });

        test("string table saves merge pending keys without reverting another editor's changes", () =>
        {
            using var f = new Fixture();
            string path = Path.Combine(f.Root, "strings.ini");
            File.WriteAllText(path, "; keep\n[section]\nfirst=old\nsecond=old\nother = spaced\n");
            var first = new StringTableParser(path);
            var second = new StringTableParser(path);
            first.SetValue("first", "new first");
            first.Save();
            second.SetValue("second", "new second");
            second.Save();
            var loaded = new StringTableParser(path);
            Check(loaded.GetValue("first") == "new first" && loaded.GetValue("second") == "new second" &&
                File.ReadAllText(path).Contains("other = spaced"), "stale strings reverted another editor");
        });

        test("unit position saves retain unknown XML and reject partial JSON XML saves", () =>
        {
            using var f = new Fixture();
            string previous = AssetManager.Default.AssetsRoot;
            try
            {
                string assets = Path.Combine(f.Root, "assets"), path = Path.Combine(assets, "config", "def_armypos.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Directory.CreateDirectory(Path.Combine(assets, "json"));
                string json = Path.Combine(assets, "json", "ArmySettings.json");
                File.WriteAllText(json, """[{"Id":1,"Custom":"keep"}]""");
                File.WriteAllText(path, """<units custom="keep"><meta value="yes"/><unit id="1" x="2" y="3" scale="0.90" extra="keep"/></units>""");
                AssetManager.Default.Scan(assets, true);
                ArmySettingParser.ClearInstance();
                var parser = ArmySettingParser.Instance;
                parser.GetPos(1)!.PosX = 8;
                Check(parser.SaveAll(), "unit position save failed");
                var document = System.Xml.Linq.XDocument.Load(path);
                Check((string?)document.Root!.Attribute("custom") == "keep" && document.Root.Element("meta") != null &&
                    (string?)document.Root.Element("unit")!.Attribute("extra") == "keep" &&
                    (string?)document.Root.Element("unit")!.Attribute("scale") == "0.90" &&
                    (int?)document.Root.Element("unit")!.Attribute("x") == 8, "unit XML metadata changed");
                string original = File.ReadAllText(json);
                File.WriteAllText(path, "broken");
                parser.LoadXmlPos();
                parser.Items[0].HP = 99;
                Check(!parser.SaveAll() && File.ReadAllText(json) == original && File.ReadAllText(path) == "broken", "partial position save");
            }
            finally { Restore(previous); ClearParsers(); }
        });
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0", "assets");
        if (!Directory.Exists(source)) return;
        test("WC4 1.30.0 specialized JSON editors preserve all original fields and untouched values", () =>
        {
            using var f = new Fixture();
            string previous = AssetManager.Default.AssetsRoot;
            try
            {
                string assets = Path.Combine(f.Root, "assets");
                Directory.CreateDirectory(Path.Combine(assets, "json"));
                Directory.CreateDirectory(Path.Combine(assets, "config"));
                var originals = new Dictionary<string, JsonNode>();
                int totalRows = 0, unknownProperties = 0;
                foreach (var (name, model) in Tables)
                {
                    string input = Path.Combine(source, "json", name + ".json");
                    File.Copy(input, Path.Combine(assets, "json", name + ".json"));
                    var original = JsonNode.Parse(File.ReadAllText(input).TrimStart('\uFEFF'))!;
                    originals[name] = original;
                    var keys = model.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    int unknown = original.AsArray().Sum(row => row!.AsObject().Count(property => !keys.Contains(property.Key)));
                    totalRows += original.AsArray().Count;
                    unknownProperties += unknown;
                    Console.WriteLine($"JSON audit: {name}, {original.AsArray().Count} rows, {unknown} properties absent from the typed model.");
                }
                File.Copy(Path.Combine(source, "config", "def_armypos.xml"), Path.Combine(assets, "config", "def_armypos.xml"));
                File.Copy(Path.Combine(source, "stringtable_tw.ini"), Path.Combine(assets, "stringtable_tw.ini"));
                AssetManager.Default.Scan(assets, true);
                ClearParsers();
                Check(ArmySettingParser.Instance.SaveAll(), "army save");
                Check(ArmyBuffSettingParser.Instance.SaveAll(), "army buff save");
                Check(SkillSettingParser.Instance.SaveAll(), "skill save");
                Check(CountryTechSettingParser.Instance.SaveAll(), "tech save");
                Check(ArmyGroupEventSettingParser.Instance.SaveAll(), "group event save");
                Check(EventBuffSettingParser.Instance.SaveAll(), "event buff save");
                Check(ConquerEventSettingParser.Instance.SaveAll(), "conquer event save");
                Check(BuildingFacilitySettingParser.Instance.SaveAll(), "building save");
                Check(CountrySettingParser.Instance.SaveCountries() && CountrySettingParser.Instance.SaveConquerCountries(), "country save");
                Check(ArmyGroupSettingParser.Instance.SaveAll(), "group save");
                foreach (var (name, original) in originals)
                    Check(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "json", name + ".json")).TrimStart('\uFEFF'))), $"unrelated data changed: {name}");
                var skill = SkillSettingParser.Instance.Items[0];
                skill.CostMedal++;
                var changed = originals["SkillSettings"].DeepClone();
                changed[0]!["CostMedal"] = skill.CostMedal;
                Check(SkillSettingParser.Instance.SaveAll() && JsonNode.DeepEquals(changed,
                    JsonNode.Parse(File.ReadAllText(Path.Combine(assets, "json", "SkillSettings.json")))), "real edit changed other fields");
                Console.WriteLine($"1.30.0 JSON: 16 writable tables plus ConquerSettings, {totalRows} rows preserved; {unknownProperties} unmodeled property occurrences retained.");
            }
            finally { Restore(previous); ClearParsers(); }
        });
    }

    private static void ClearParsers()
    {
        ArmySettingParser.ClearInstance(); ArmyBuffSettingParser.ClearInstance(); SkillSettingParser.ClearInstance();
        CountryTechSettingParser.ClearInstance(); ArmyGroupEventSettingParser.ClearInstance(); EventBuffSettingParser.ClearInstance();
        ConquerEventSettingParser.ClearInstance(); BuildingFacilitySettingParser.ClearInstance(); CountrySettingParser.ClearInstance();
        ArmyGroupSettingParser.ClearInstance();
    }

    private static void Restore(string previous)
    {
        if (Directory.Exists(previous)) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear();
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
