using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Analyzers;

internal static class GeneralDataTests
{
    private const string Skills = """
        [{"Id":1321,"Type":32,"Level":1},{"Id":1320,"Type":32,"Level":10},
         {"Id":1371,"Type":37,"Level":1},{"Id":1370,"Type":37,"Level":10}]
        """;
    private const string Armies = """
        [{"Id":362001,"Army":114,"Type":4,"Name":"Ohio","MaxFormation":1},
         {"Id":360001,"Army":112,"Type":13,"Name":"Aircraft","MaxFormation":1},
         {"Id":104041,"Army":19,"Type":4,"Name":"Submarine","MaxFormation":2},
         {"Id":104071,"Army":19,"Type":4,"Name":"Carrier","MaxFormation":1},
         {"Id":500001,"Army":40,"Type":8,"Name":"Fort","MaxFormation":0}]
        """;

    public static void Run(Action<string, Action> test)
    {
        test("asset caches refresh across roots, force, clear, locales and shared managers", () =>
        {
            using var f = new Fixture();
            string a = f.Assets("a", """[{"Id":11,"Name":"A"}]""");
            string b = f.Assets("b", """[{"Id":12,"Name":"B"}]""");
            File.WriteAllText(Path.Combine(a, "stringtable_tw.ini"), "key=TW", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(a, "stringtable_en.ini"), "key=EN");
            File.WriteAllText(Path.Combine(a, "json/GeneralSettings.json"), """[{"Id":11,"Name":"A"}]""", new UTF8Encoding(true));
            var cache = new AssetCache();
            var first = new AssetManager(cache);
            var second = new AssetManager(cache);
            first.Scan(a);
            Check(first.GetGeneralSettings()[0].Id == 11 && second.GetGeneralSettings()[0].Id == 11, "BOM/general load");
            Check(first.GetStringTableValue("key", locale: "tw") == "TW", "TW locale");
            Check(first.GetStringTableValue("key", locale: "en") == "EN", "EN locale");
            Check(first.GetArmy(114)?.Name == "Ohio" && first.GetSkill(1320)?.Level == 10, "catalog load");
            first.GetConquerCountrySettings();
            second.Scan(b);
            Check(first.GetGeneralSettings()[0].Id == 12, "shared root change");
            Check(first.GetStringTable("tw").Count == 0, "old localized strings leaked");
            File.WriteAllText(Path.Combine(b, "json/GeneralSettings.json"), """[{"Id":13}]""");
            second.Scan(b, forceReload: true);
            Check(first.GetGeneralSettings()[0].Id == 13, "forced reload");
            File.WriteAllText(Path.Combine(b, "json/SkillSettings.json"), """[{"Id":1320,"Type":32,"Level":9}]""");
            first.InvalidateData();
            Check(second.GetSkill(1320)?.Level == 9, "shared invalidation");
            first.Clear();
            Check(second.GetGeneralSettings().Count == 0 && first.GetArmySettings().Count == 0 &&
                second.GetSkillSettings().Count == 0 && first.GetConquerCountrySettings().Count == 0, "clear stale catalogs");
            File.WriteAllText(Path.Combine(b, "json/SkillSettings.json"), "[null]");
            File.WriteAllText(Path.Combine(b, "json/ArmySettings.json"), "null");
            first.Scan(b);
            Reject(() => first.GetSkillSettings());
            Reject(() => first.GetArmySettings());
        });

        test("strict skill tokens and actual Level 10 template selection", () =>
        {
            using var f = new Fixture();
            var manager = f.Manager(f.Assets("assets"));
            Check(GeneralSkillRules.ParseIds("0, 1320;\t1371\n").SequenceEqual(new[] { 0, 1320, 1371 }), "valid ID list");
            Reject(() => GeneralSkillRules.ParseIds("1320, invalid, 1371"));
            Reject(() => GeneralSkillRules.ParseIds("-1"));
            Reject(() => GeneralSkillRules.ParseIds("2147483648"));
            Check(GeneralSkillRules.GetLevel(manager, 1320) == 10 && GeneralSkillRules.GetLevel(manager, 0) == 0, "level lookup");
            Reject(() => GeneralSkillRules.GetLevel(manager, 9999));
            var result = GeneralSkillRules.RandomizeLevels(manager, [1321, 0, 1371, 1371], 10, 10, 3);
            Check(result.SequenceEqual(new[] { 1320, 0, 1370, 1371 }), "randomizer invented IDs or dropped trailing slots");
            Reject(() => GeneralSkillRules.RandomizeLevels(manager, [1321], 6, 9, 1));
            Reject(() => GeneralSkillRules.RandomizeLevels(manager, [9999], 1, 5, 1));
        });

        test("five-slot application preserves other fields and rejects overflow before mutation", () =>
        {
            using var f = new Fixture();
            var manager = f.Manager(f.Assets("assets"));
            var general = new GeneralSettings { Id = 11, MilitaryRank = 7, Skills = [1320, 0] };
            var army = Army.CreateDefault(1);
            army.Nobility = 9;
            army.SkillLevel5 = 4;
            GeneralSkillRules.Apply(ref army, general, manager);
            Check(army.General == 11 && army.Rank == 7 && army.Nobility == 9, "v1 rank/nobility");
            Check(army.SkillLevel1 == 10 && army.SkillLevel2 == 0 && army.SkillLevel5 == 0, "v1 skill slots");
            var army3 = Army_3.CreateDefault(2);
            army3.HpLevel = 8;
            army3.SkillLevel4 = 5;
            GeneralSkillRules.Apply(ref army3, general, manager);
            Check(army3.General == 11 && army3.Rank == 7 && army3.HpLevel == 8 && army3.SkillLevel4 == 0, "v3 rank/slots");
            foreach (var bad in new[]
            {
                new GeneralSettings { Id = 32768 }, new GeneralSettings { Id = -1 },
                new GeneralSettings { Id = 11, MilitaryRank = 256 },
                new GeneralSettings { Id = 11, Skills = [1321,1321,1321,1321,1321,1321] },
                new GeneralSettings { Id = 11, Skills = [9999] }
            })
            {
                Check(!GeneralSkillRules.CanAssign(bad, manager), "unsafe general accepted");
                var before = army;
                Reject(() => GeneralSkillRules.Apply(ref army, bad, manager));
                Check(army.Equals(before), "rejected assignment mutated army");
            }
        });

        test("general JSON preserves missing, null, mixed-case and extension fields", () =>
        {
            using var f = new Fixture();
            const string original = """
                [{"Id":0,"Name":null,"EName":null,"Photo":null,"Skills":null,"Medals":null},
                 {"Id":11,"name":"first","EName":"shared","Photo":"portrait","ResetSkills":10,
                  "SkillsMax":10,"Skills":[1321,0,0,0,0,0,0,0,0,1371],"Future":{"Cost":[3,4]}}]
                """;
            string root = f.Assets("assets", original);
            var parser = new GeneralSettingParser(root, f.Manager(root));
            string output = Path.Combine(f.Root, "output.json");
            Check(parser.SaveGeneralSettings(output), parser.LastError);
            JsonEqual(JsonNode.Parse(original), JsonNode.Parse(File.ReadAllText(output)));
            parser.All[1].Name = "changed";
            Check(parser.SaveGeneralSettings(output), parser.LastError);
            var expected = JsonNode.Parse(original)!;
            expected[1]!["name"] = "changed";
            JsonEqual(expected, JsonNode.Parse(File.ReadAllText(output)));
            Check(parser.All[1].ResetSkills == 10 && parser.All[1].Skills.Count == 10, "expanded JSON data truncated");
            Check(GeneralSettingParser.GetPhotoKey(parser.All[1]) == "portrait", "Photo != EName");
            parser.All[1].Id++;
            string before = File.ReadAllText(output);
            Check(!parser.SaveGeneralSettings(output) && before == File.ReadAllText(output), "ID edit must fail before write");
        });

        test("duplicate row identity, shared portraits and new-general defaults", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets", """[{"Id":11,"Name":"first","EName":"shared"},{"Id":11,"Name":"second","EName":"shared"}]""");
            File.WriteAllText(Path.Combine(root, "config/def_portraitpos.xml"), """
                <Portraits custom="keep"><general name="shared" posx="3" posy="4" scale="1.00" extra="keep"/>
                <future value="7"/></Portraits>
                """);
            var parser = new GeneralSettingParser(root, f.Manager(root));
            parser.All[1].Name = "second-updated";
            Check(parser.All[0].Name == "first", "duplicate row aliased");
            Check(parser.DeleteGeneral(parser.All[1]) && parser.All.Count == 1 && parser.All[0].Name == "first", "wrong duplicate deleted");
            Check(parser.GetPortrait("shared")?.PosX == 3, "shared portrait deleted");
            var (added, portrait) = parser.AddNewGeneral("third", "shared");
            Check(added.Id == 12 && added.InfantryMax == 6 && added.MarchMax == 6 &&
                added.SkillsMax == 5 && added.ResetSkills == 5 && portrait.PosX == 3, "new default/shared photo");
            Check(parser.SaveAll(), parser.LastError);
            var xml = XDocument.Load(parser.PortraitPosPath);
            Check((string?)xml.Root!.Attribute("custom") == "keep" && xml.Root.Element("future") != null &&
                (string?)xml.Root.Element("general")!.Attribute("extra") == "keep", "XML extensions lost");
            Check((string?)xml.Root.Element("general")!.Attribute("scale") == "1.00", "unchanged XML scale rewritten");
            added.Id = 11;
            Check(!parser.SaveAll(), "new row ID changed into duplicate");
        });

        test("failed general reload and malformed portrait cannot overwrite valid JSON", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets", """[{"Id":11}]""");
            var parser = new GeneralSettingParser(root, f.Manager(root));
            File.WriteAllText(parser.ConfigPath, "malformed");
            parser.Reload();
            Check(parser.LastError != null && parser.All.Count == 1, "failed load lost prior data or error");
            Check(!parser.SaveAll() && File.ReadAllText(parser.ConfigPath) == "malformed", "failed load overwrote source");
            var fresh = new GeneralSettingParser(root, f.Manager(root));
            Check(!fresh.SaveGeneralSettings(Path.Combine(f.Root, "bad-output.json")), "fresh invalid load saved empty list");
            File.WriteAllText(parser.ConfigPath, """[{"Id":11}]""");
            File.WriteAllText(parser.PortraitPosPath, "<bad");
            parser.Reload();
            parser.All[0].Hp = 8;
            string before = File.ReadAllText(parser.ConfigPath);
            Check(!parser.SaveAll() && before == File.ReadAllText(parser.ConfigPath), "JSON saved before XML validation");
            File.WriteAllText(parser.PortraitPosPath, "<Portraits><general name=\"bad\" scale=\"NaN\"/></Portraits>");
            parser.Reload();
            Check(parser.LastError != null && !parser.SaveAll(), "nonfinite portrait loaded for UI");
        });

        test("multi-file staging failure and invalid portrait leave original files intact", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets", """[{"Id":11}]""");
            var parser = new GeneralSettingParser(root, f.Manager(root));
            string before = File.ReadAllText(parser.ConfigPath);
            parser.All[0].Hp = 7;
            parser.EnsurePortraitDefault("portrait").Scale = double.NaN;
            Check(!parser.SaveAll() && before == File.ReadAllText(parser.ConfigPath), "invalid scale changed JSON");
            parser.GetPortrait("portrait")!.Scale = 1;
            File.Delete(parser.PortraitPosPath);
            Directory.CreateDirectory(parser.PortraitPosPath);
            Check(!parser.SaveAll() && before == File.ReadAllText(parser.ConfigPath), "second destination failure changed JSON");
            Check(!Directory.EnumerateFiles(root, "*.tmp*", SearchOption.AllDirectories).Any(), "staging files leaked");
            Reject(() => AtomicFile.WriteAll((parser.ConfigPath, new byte[] { 1 }), (parser.ConfigPath, new byte[] { 2 })));
            Check(before == File.ReadAllText(parser.ConfigPath), "duplicate destination changed JSON");
        });

        test("active asset root change blocks stale save and reload rebinds paths", () =>
        {
            using var f = new Fixture();
            string a = f.Assets("a", """[{"Id":11}]""");
            string b = f.Assets("b", """[{"Id":12}]""");
            var manager = f.Manager(a);
            var parser = new GeneralSettingParser(null, manager);
            manager.Scan(b);
            Check(!parser.SaveAll(), "stale editor saved previous asset root");
            parser.Reload();
            Check(parser.All.Single().Id == 12 && parser.ConfigPath.StartsWith(b, StringComparison.Ordinal), "reload did not rebind");
            Check(parser.SaveAll(), parser.LastError);
        });

        test("unit catalogs use Army code and safe country-variant formation limits", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets");
            WithDefaultAssets(root, cfg =>
            {
                Check(new Army { UnitType = 114 }.GetUnitTypeName() == "Ohio", "unit name lookup by wrong ID");
                Check(cfg.GetUnitSpecialtyType(114) == "Navy" && cfg.GetUnitSpecialtyType(112) == "AirForce", "extended unit specialty");
                Check(cfg.GetUnitSpecialtyType(40) == null, "fort classified as normal unit");
                Check(cfg.GetMaxFormation(114) == 1 && cfg.GetMaxFormation(19) == 1, "unsafe formation limit");
            });
        });

        test("relationship audit reports missing, ambiguous, chain and map references", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("audit", """[{"Id":1,"Name":"G","Skills":[1321]}]""");
            File.WriteAllText(Path.Combine(root, "json/GeneralTitleSettings.json"), "[{\"Id\":1,\"GeneralId\":1}]");
            File.WriteAllText(Path.Combine(root, "json/GeneralStageSettings.json"), "[{\"Id\":1,\"GeneralId\":1,\"UnlockStageId\":99}]");
            File.WriteAllText(Path.Combine(root, "json/GeneralPromotionSettings.json"), "[{\"Id\":1,\"BaseID\":1,\"AdvanceID\":1,\"Skills\":[9999]}]");
            File.WriteAllText(Path.Combine(root, "json/SkillSettings.json"), "[{\"Id\":1321,\"Type\":32,\"Level\":1,\"UpgradeId\":1322},{\"Id\":1321,\"Type\":32,\"Level\":2,\"UpgradeId\":0}]");
            File.WriteAllText(Path.Combine(root, "json/ArmySettings.json"), "[{\"Id\":1,\"Army\":114,\"Type\":4,\"Name\":\"Ohio\",\"Feature\":[],\"FeatureLevel\":[],\"MaxFormation\":1}]");
            Directory.CreateDirectory(Path.Combine(root, "stage"));
            var map = BTLParser.CreateNew(4, 2);
            map.Header.BtlVersion = 1;
            map.Armies.Add(new Army { Coordinate = 1, UnitType = 114, General = 99 });
            BTLParser.SaveToFile(map, Path.Combine(root, "stage/test.btl"));
            var report = new AssetRelationshipAnalyzer().Analyze(root, includeMaps: true);
            Check(report.Errors > 0 && report.MapsChecked == 1, "audit did not report errors/map");
            Check(report.Diagnostics.Any(d => d.Code == "missing-reference" && d.Field.Contains("Skills")), "missing skill not reported");
            Check(report.Diagnostics.Any(d => d.Code == "duplicate-id" && d.Source == "SkillSettings.json"), "duplicate skill not reported");
            Check(report.Diagnostics.Any(d => d.Code == "chain-cycle"), "promotion cycle not reported");
            Check(report.Diagnostics.Any(d => d.Code == "missing-reference" && d.Source == "stage/test.btl"), "map general reference not reported");
            Check(report.GeneralReferences(1).Any(r => r.Source == "GeneralTitleSettings.json"), "reverse general references missing");
            string text = report.ToText(1);
            Check(text.Contains("General 1") && text.Contains("GeneralTitleSettings.json"), "reverse report text");
        });

        foreach (bool v3 in new[] { false, true })
        foreach (bool adapt in new[] { false, true })
        {
            bool version = v3, adaptive = adapt;
            test($"assignment v{(version ? 3 : 1)} adapt={adaptive}: country filter, rank, reserved IDs", () =>
                TestAssignment(version, adaptive));
        }
    }

    private static void TestAssignment(bool v3, bool adapt)
    {
        using var f = new Fixture();
        var generals = Enumerable.Range(11, 11).Select(id => new GeneralSettings
            { Id = id, Navy = 6, MilitaryRank = 7, Skills = [1320, 0] }).ToList();
        generals.Single(g => g.Id == 13).Skills = Enumerable.Repeat(1320, 10).ToList();
        generals.Single(g => g.Id == 14).Skills = [9999];
        generals.Add(new GeneralSettings { Id = 15 });
        generals.Single(g => g.Id == 16).MilitaryRank = 256;
        generals.Add(new GeneralSettings { Id = 90000 });
        string root = f.Assets("assets", JsonSerializer.Serialize(generals));
        WithDefaultAssets(root, cfg =>
        {
            var countries = cfg.GetGeneralInCountryData();
            var saved = countries.ToList();
            try
            {
                countries.Clear();
                countries.Add(new CountryGeneralsConfig { CountryId = 1, Generals = [11, 11, 12, 13, 14, 15, 16, 18, 19, 20, 90000, 9999] });
                countries.Add(new CountryGeneralsConfig { CountryId = 2, Generals = [11] });
                var map = new MapData(8, 8);
                map.Legions.Add(new Legion { ActionId = 1, CountryId = 1 });
                map.Legions.Add(new Legion { ActionId = 2, CountryId = 2 });
                if (v3)
                {
                    map.ArmiesV3.Add(Army3(1, 1, 21));
                    map.ArmiesV3.Add(Army3(2, 1, 21));
                    map.ArmiesV3.Add(Army3(3, 2, 12));
                    map.Armies.Add(Army1(4, 2, 18));
                }
                else
                {
                    map.Armies.Add(Army1(1, 1, 21));
                    map.Armies.Add(Army1(2, 1, 21));
                    map.Armies.Add(Army1(3, 2, 12));
                    map.ArmiesV3.Add(Army3(4, 2, 18));
                }
                map.Reinforcements.Add(new Reinforcement { General = 19 });
                map.ReinforcementsV3.Add(new Reinforcement_3 { General = 20 });
                var other1 = map.Armies.Last();
                var other3 = map.ArmiesV3.Last();
                cfg.AddAssignedGeneral(11); // A stale registry entry from another map must not reserve it.
                var result = Assign(map, v3, 1, -1, true, adapt);
                Check(result.Success, result.Message);
                var ids = v3 ? map.ArmiesV3.Take(2).Select(a => a.General).ToList() : map.Armies.Take(2).Select(a => a.General).ToList();
                Check(ids.Count(id => id == 11) == 1 && ids.Count(id => id == 0) == 1, "duplicate/used/invalid general assigned or uncleared stale struct");
                Check(map.Armies.Last().Equals(other1) && map.ArmiesV3.Last().Equals(other3), "other country or version changed");
                if (v3)
                {
                    var assigned = map.ArmiesV3.First(a => a.General == 11);
                    Check(assigned.Rank == 7 && assigned.HpLevel == 9 && assigned.SkillLevel1 == 10 && assigned.SkillLevel5 == 0, "v3 payload");
                }
                else
                {
                    var assigned = map.Armies.First(a => a.General == 11);
                    Check(assigned.Rank == 7 && assigned.Nobility == 9 && assigned.SkillLevel1 == 10 && assigned.SkillLevel5 == 0, "v1 payload");
                }
                var snapshot1 = map.Armies.ToArray();
                var snapshot3 = map.ArmiesV3.ToArray();
                Check(Assign(map, v3, 1, 0, true, adapt).Success, "zero count should be a no-op");
                Check(!Assign(map, v3, 1, -2, true, adapt).Success, "negative count accepted");
                Assign(map, v3, 1, -1, false, adapt);
                Check(map.Armies.SequenceEqual(snapshot1) && map.ArmiesV3.SequenceEqual(snapshot3), "repeat reused assigned general");

                var twoCountries = new MapData(8, 8);
                twoCountries.Legions.Add(new Legion { ActionId = 1, CountryId = 1 });
                twoCountries.Legions.Add(new Legion { ActionId = 2, CountryId = 2 });
                countries[0].Generals = [11, 11];
                if (v3) { twoCountries.ArmiesV3.Add(Army3(1, 1, 0)); twoCountries.ArmiesV3.Add(Army3(2, 2, 0)); }
                else { twoCountries.Armies.Add(Army1(1, 1, 0)); twoCountries.Armies.Add(Army1(2, 2, 0)); }
                Check(Assign(twoCountries, v3, -1, -1, false, adapt).Success, "multi-country assignment");
                int assignedCount = twoCountries.Armies.Count(a => a.General == 11) + twoCountries.ArmiesV3.Count(a => a.General == 11);
                Check(assignedCount == 1, "same general reused across country groups");

                if (v3) twoCountries.ArmiesV3.Add(Army3(1, 1, 11));
                else twoCountries.Armies.Add(Army1(1, 1, 11));
                var duplicates1 = twoCountries.Armies.ToArray();
                var duplicates3 = twoCountries.ArmiesV3.ToArray();
                Check(!Assign(twoCountries, v3, 1, -1, true, adapt).Success, "ambiguous coordinates accepted");
                Check(twoCountries.Armies.SequenceEqual(duplicates1) && twoCountries.ArmiesV3.SequenceEqual(duplicates3), "coordinate failure mutated map");

                File.WriteAllText(Path.Combine(root, "json/SkillSettings.json"), "malformed");
                AssetManager.Default.InvalidateData();
                Check(!Assign(map, v3, 1, -1, true, adapt).Success, "invalid catalog accepted");
                Check(map.Armies.SequenceEqual(snapshot1) && map.ArmiesV3.SequenceEqual(snapshot3), "invalid catalog cleared generals");
            }
            finally { countries.Clear(); countries.AddRange(saved); cfg.ClearAssignedGenerals(); }
        });
    }

    public static void RunCorpus(Action<string, Action> test, string gameRoot)
    {
        test("real general corpus JSON semantic roundtrip and portrait preservation", () =>
        {
            using var f = new Fixture();
            int tables = 0, records = 0;
            foreach (string path in Directory.EnumerateFiles(Path.Combine(gameRoot, "wc4"), "GeneralSettings.json", SearchOption.AllDirectories))
            {
                string root = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
                var manager = f.Manager(root);
                var parser = new GeneralSettingParser(root, manager);
                string output = Path.Combine(f.Root, "general-output.json");
                byte[] original = File.ReadAllBytes(path);
                Check(parser.SaveGeneralSettings(output), $"{path}: {parser.LastError}");
                JsonEqual(JsonNode.Parse(File.ReadAllText(path)), JsonNode.Parse(File.ReadAllText(output)));
                Check(original.SequenceEqual(File.ReadAllBytes(path)), "original corpus JSON mutated");
                if (File.Exists(parser.PortraitPosPath))
                {
                    string xml = Path.Combine(f.Root, "portraits-output.xml");
                    Check(parser.SavePortraitPos(xml), $"{parser.PortraitPosPath}: {parser.LastError}");
                    Check(XNode.DeepEquals(XDocument.Load(parser.PortraitPosPath).Root, XDocument.Load(xml).Root), "portrait semantic mismatch");
                }
                records += parser.All.Count;
                tables++;
            }
            Check(tables >= 4, "missing original/integrated general fixtures");
            Console.WriteLine($"General corpus: {tables} tables, {records} records; semantic identity.");
        });

        test("real skills and bundled specialty seeds resolve without synthetic IDs", () =>
        {
            using var f = new Fixture();
            var templates = ConfigManager.Instance.GetGeneralSpecialtyTemplates();
            Check(templates.Count >= 5, "editor templates were not loaded");
            int levels10 = 0;
            foreach (string path in Directory.EnumerateFiles(Path.Combine(gameRoot, "wc4"), "SkillSettings.json", SearchOption.AllDirectories))
            {
                string root = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
                var manager = f.Manager(root);
                foreach (var skill in manager.GetSkillSettings().Where(s => s.Id != 0))
                {
                    Check(GeneralSkillRules.GetLevel(manager, skill.Id) == skill.Level, $"{path}: level {skill.Id}");
                    if (skill.Level == 10) levels10++;
                }
                foreach (var t in templates)
                {
                    var ids = GeneralSkillRules.RandomizeLevels(manager, t.SkillPool, t.SkillLevelMin, t.SkillLevelMax, t.SkillPool.Count);
                    Check(ids.All(id => manager.GetSkill(id) != null), $"{path}: invalid template {t.Key}");
                }
            }
            Check(levels10 > 0, "Level 10 corpus cases not exercised");
        });

        test("integrated extended units resolve using real Army codes and types", () =>
        {
            string root = Directory.EnumerateFiles(Path.Combine(gameRoot, "wc4"), "ArmySettings.json", SearchOption.AllDirectories)
                .Select(path => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName)
                .Single(path => HasArmy114(new AssetManager(new AssetCache()), path));
            WithDefaultAssets(root, cfg =>
            {
                var expected = new Dictionary<int, string>
                {
                    [104] = "Armor", [105] = "Artillery", [106] = "Infantry", [107] = "Armor",
                    [108] = "Artillery", [109] = "Navy", [110] = "Infantry", [111] = "Artillery",
                    [112] = "AirForce", [113] = "Navy", [114] = "Navy"
                };
                foreach (var (id, specialty) in expected)
                {
                    Check(cfg.GetUnitSpecialtyType(id) == specialty, $"army {id} classification");
                    var rows = AssetManager.Default.GetArmySettings().Where(a => a.Army == id).ToList();
                    Check(rows.Count > 0 && cfg.GetMaxFormation(id) == rows.Min(a => a.MaxFormation), $"army {id} formation");
                    Check(new Army { UnitType = (byte)id }.GetUnitTypeName() == rows[0].Name, $"army {id} catalog name");
                }
            });
        });
    }

    private static bool HasArmy114(AssetManager manager, string root) { manager.Scan(root); return manager.GetArmy(114) != null; }

    private static Army Army1(int index, int legion, short general)
    {
        var army = Army.CreateDefault(index);
        army.UnitType = 114; army.LegionId = legion; army.General = general;
        army.Rank = 2; army.Nobility = 9; army.SkillLevel5 = 9;
        return army;
    }

    private static Army_3 Army3(int index, int legion, short general)
    {
        var army = Army_3.CreateDefault(index);
        army.UnitType = 114; army.LegionId = legion; army.General = general;
        army.Rank = 2; army.HpLevel = 9; army.SkillLevel5 = 9;
        return army;
    }

    private static ModifierResult Assign(MapData map, bool v3, int country, int count, bool clear, bool adapt)
    {
        if (v3)
        {
            var modifier = new ArmyV3Modifier(); modifier.Initialize(map);
            return modifier.AutoAssignGeneralsToArmies3(country, count, clear, adapt);
        }
        var legacy = new ArmyModifier(); legacy.Initialize(map);
        return legacy.AutoAssignGeneralsToArmies(country, count, clear, adapt);
    }

    private static void WithDefaultAssets(string root, Action<ConfigManager> action)
    {
        var manager = AssetManager.Default;
        string previous = manager.AssetsRoot;
        bool loaded = manager.IsLoaded;
        try { manager.Scan(root, forceReload: true); action(ConfigManager.Instance); }
        finally { if (loaded) manager.Scan(previous, forceReload: true); else manager.Clear(); }
    }

    private static void JsonEqual(JsonNode? expected, JsonNode? actual)
        => Check(JsonNode.DeepEquals(expected, actual), "JSON semantic mismatch");

    private static void Check(bool condition, string? message)
    {
        if (!condition) throw new Exception(message ?? "Assertion failed");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or FormatException or OverflowException) { return; }
        throw new Exception("Expected validation failure");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public Fixture() { Directory.CreateDirectory(Root); }
        public string Assets(string name, string generals = "[]")
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(path, "json"));
            Directory.CreateDirectory(Path.Combine(path, "config"));
            File.WriteAllText(Path.Combine(path, "json/GeneralSettings.json"), generals);
            File.WriteAllText(Path.Combine(path, "json/SkillSettings.json"), Skills);
            File.WriteAllText(Path.Combine(path, "json/ArmySettings.json"), Armies);
            File.WriteAllText(Path.Combine(path, "json/ConquerCountrySettings.json"), "[{\"Id\":1}]");
            File.WriteAllText(Path.Combine(path, "config/def_portraitpos.xml"), "<Portraits/>");
            return path;
        }
        public AssetManager Manager(string root) { var manager = new AssetManager(new AssetCache()); manager.Scan(root); return manager; }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
