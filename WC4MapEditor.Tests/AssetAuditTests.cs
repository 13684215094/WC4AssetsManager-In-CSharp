using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.General;

internal static class AssetAuditTests
{
    public static void Run(Action<string, Action> test)
    {
        test("audit overlays replace whole files and distinguish absent from missing targets", () =>
        {
            using var f = new Fixture();
            string baseline = f.Assets("base/assets"), overlay = f.Assets("overlay");
            f.Table(baseline, "GeneralSettings", """[{"Id":1,"Skills":[10]}]""");
            f.Table(baseline, "SkillSettings", """[{"Id":10},{"Id":11}]""");
            f.Table(overlay, "GeneralSettings", """[{"Id":2,"Skills":[11]}]""");
            f.Table(overlay, "StageSettings", """[{"Id":4,"GeneralId":1}]""");
            var analyzer = new AssetRelationshipAnalyzer();
            var partial = analyzer.Analyze(overlay);
            Check(partial.Diagnostics.Any(d => d.Code == "target-unavailable" && d.Source == "SkillSettings.json"), "absent target not identified");
            Check(partial.References.Single(r => r.Target == "SkillSettings.json").Status == "unchecked", "absent target treated as missing ID");
            var combined = analyzer.Analyze(overlay, Path.GetDirectoryName(baseline));
            Check(combined.Errors == 1 && combined.Warnings == 0, "wrong overlay diagnostics or reused state");
            Check(combined.References.Single(r => r.Target == "SkillSettings.json").Status == "resolved", "base skill not used");
            Check(combined.GeneralReferences(1).Single().Status == "missing", "base general rows merged with overlay");
            Check(combined.Tables["GeneralSettings.json"].StartsWith(overlay) && combined.Tables["SkillSettings.json"].StartsWith(baseline), "incorrect provenance");
            Reject(() => analyzer.Analyze(Path.Combine(f.Root, "missing")));
        });

        test("audit rejects malformed targets without base fallback and accepts BOM", () =>
        {
            using var f = new Fixture();
            string baseline = f.Assets("base"), overlay = f.Assets("overlay");
            f.Table(baseline, "SkillSettings", """[{"Id":10}]""");
            f.Table(overlay, "GeneralSettings", """[{"Id":1,"Skills":[10]}]""");
            foreach (string malformed in new[] { "invalid", "null", "{}", "[null]", "[10]", "[{\"Id\":10,\"Id\":11}]" })
            {
                f.Table(overlay, "SkillSettings", malformed);
                var report = new AssetRelationshipAnalyzer().Analyze(overlay, baseline);
                Check(report.Diagnostics.Any(d => d.Code == "table-unreadable"), malformed);
                Check(report.References.Single().Status == "unchecked", "malformed overlay fell back to base");
            }
            f.Table(overlay, "SkillSettings", """[{"Id":10}]""", bom: true);
            Check(new AssetRelationshipAnalyzer().Analyze(overlay, baseline).Errors == 0, "UTF-8 BOM rejected");
        });

        test("audit strict reference types preserve ID zero and nonunique family keys", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets");
            f.Table(root, "GeneralSettings", """[{"Id":0,"Skills":null},{"Id":1,"Skills":[0,10,-1,true,1.5,"10",null]}]""");
            f.Table(root, "SkillSettings", """[{"Id":10}]""");
            f.Table(root, "GeneralTitleSettings", """[{"Id":1,"GeneralId":0}]""");
            f.Table(root, "ArmySettings", """[{"Id":1,"Army":19},{"Id":2,"Army":19}]""");
            f.Table(root, "ItemSettings", """[{"Id":1,"Army":19}]""");
            var report = new AssetRelationshipAnalyzer().Analyze(root);
            Check(report.Diagnostics.Count(d => d.Code == "reference-type") == 5, "fraction, bool, negative, text or null slot accepted");
            Check(report.GeneralReferences(0).Single().Status == "resolved", "ID zero treated as empty title reference");
            Check(report.References.Single(r => r.Source == "ItemSettings.json").Status == "resolved", "army family variants treated as duplicate IDs");
            Check(report.Diagnostics.Where(d => d.Severity == "warning").All(d => d.Code == "btl-skill-slots"), "unexpected warning");
        });

        test("audit features, chains and BTL slot budgets have explicit diagnostics", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets");
            f.Table(root, "GeneralSettings", """[{"Id":1,"SkillsMax":5,"Skills":[10,10,10,10,10,10]}]""");
            f.Table(root, "SkillSettings", """[{"Id":10,"Type":1,"UpgradeId":11},{"Id":11,"Type":2,"UpgradeId":10}]""");
            f.Table(root, "GeneralPromotionSettings", """[{"Id":2,"BaseID":1,"AdvanceID":2}]""");
            f.Table(root, "ArmySettings", """
                [{"Id":1,"Feature":[2],"FeatureLevel":[]},
                 {"Id":2,"Feature":[2,2],"FeatureLevel":[1]},
                 {"Id":3,"Feature":[2],"FeatureLevel":[9]},
                 {"Id":4,"Feature":[2],"FeatureLevel":[0]},
                 {"Id":5,"Feature":[2],"FeatureLevel":false}]
                """);
            f.Table(root, "ArmyFeatureSettings", """[{"Id":21,"Type":2,"Level":1},{"Id":22,"Type":2,"Level":2}]""");
            var report = new AssetRelationshipAnalyzer().Analyze(root);
            foreach (string code in new[] { "chain-owner", "chain-cycle", "feature-length", "missing-feature-level", "feature-level", "feature-shape", "skill-max", "btl-skill-slots" })
                Check(report.Diagnostics.Any(d => d.Code == code), $"missing {code}");
            Check(!report.Diagnostics.Any(d => d.Source == "ArmySettings.json" && d.Id == 1), "implicit ordinary-unit levels rejected");
            Check(!report.Diagnostics.Any(d => d.Code == "ambiguous-reference"), "Type family treated as unique ID");
        });

        test("audit uses independent unsaved general snapshot without writing inputs", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets");
            const string original = """[{"Id":1,"Skills":[10],"Unknown":{"Keep":true}}]""";
            f.Table(root, "GeneralSettings", original);
            f.Table(root, "SkillSettings", """[{"Id":10}]""");
            f.Table(root, "GeneralTitleSettings", """[{"Id":9,"GeneralId":1}]""");
            var manager = new AssetManager(new AssetCache()); manager.Scan(root);
            var parser = new GeneralSettingParser(root, manager);
            parser.All[0].Skills = [99];
            JsonArray snapshot = parser.GetSnapshot();
            var report = new AssetRelationshipAnalyzer().Analyze(root, generalSnapshot: snapshot);
            Check(report.UsesUnsavedGenerals && report.Errors == 1, "saved data used instead of editor state");
            Check(snapshot[0]!["Unknown"]!["Keep"]!.GetValue<bool>(), "snapshot dropped unknown field");
            Check(File.ReadAllText(Path.Combine(root, "json/GeneralSettings.json")) == original, "audit changed source");
            parser.All[0].Skills = [10];
            Check(snapshot[0]!["Skills"]![0]!.GetValue<int>() == 99, "snapshot aliases mutable editor state");
            Check(new AssetRelationshipAnalyzer().Analyze(root).Errors == 0, "audit changed saved data/cache");
            parser.DeleteGeneral(parser.All[0]);
            Check(new AssetRelationshipAnalyzer().Analyze(root, generalSnapshot: parser.GetSnapshot()).GeneralReferences(1).Single().Status == "missing", "deleted snapshot general still resolves");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { new AssetRelationshipAnalyzer().Analyze(root, cancellationToken: cancelled.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
        });

        test("audit map versions respect overlay and exclude unverified reinforcement fields", () =>
        {
            using var f = new Fixture();
            string baseline = f.Assets("base"), overlay = f.Assets("overlay");
            f.Table(baseline, "GeneralSettings", """[{"Id":11}]""");
            f.Table(baseline, "ArmySettings", """[{"Id":1,"Army":114}]""");
            Directory.CreateDirectory(Path.Combine(baseline, "stage"));
            Directory.CreateDirectory(Path.Combine(overlay, "stage"));
            File.WriteAllBytes(Path.Combine(baseline, "stage/v1.btl"), [1, 2, 3]);
            for (int version = 1; version <= 3; version++)
            {
                var map = BTLParser.CreateNew(4, 2); map.Header.BtlVersion = version;
                if (version == 1)
                {
                    map.Armies.Add(new Army { Coordinate = 1, General = 11, UnitType = 114 });
                    map.Reinforcements.Add(new Reinforcement { General = 2, UnitType = 393228 });
                }
                else
                {
                    map.ArmiesV3.Add(new Army_3 { Coordinate = 1, General = 11, UnitType = 114 });
                    map.ReinforcementsV3.Add(new Reinforcement_3 { General = 2, UnitType = 393228 });
                }
                BTLParser.SaveToFile(map, Path.Combine(version == 1 ? overlay : baseline, $"stage/v{version}.btl"));
            }
            var report = new AssetRelationshipAnalyzer().Analyze(overlay, baseline, includeMaps: true);
            Check(report.Errors == 0 && report.MapsChecked == 3 && report.UnverifiedReinforcements == 3, "wrong version/overlay/coverage");
            Check(report.GeneralReferences(11).Count() == 3 && !report.GeneralReferences(2).Any(), "unverified references asserted");
            Check(report.References.All(r => !r.Field.StartsWith("Reinforcements")), "unverified army codes asserted");
            Check(report.Coverage.Any(s => s.Contains("3 reinforcement records skipped")), "skipped coverage not visible");
            File.WriteAllBytes(Path.Combine(overlay, "stage/v2.btl"), [1, 2, 3]);
            var malformed = new AssetRelationshipAnalyzer().Analyze(overlay, baseline, includeMaps: true);
            Check(malformed.MapsChecked == 2 && malformed.Diagnostics.Single(d => d.Code == "map-unreadable").Source == "stage/v2.btl", "invalid overlay map fell back to base");
        });

        test("audit exports refuse assets, existing files and linked roots", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets"), baseline = f.Assets("base");
            f.Table(root, "GeneralSettings", "[]");
            var report = new AssetRelationshipAnalyzer().Analyze(root, baseline);
            Reject(() => report.Save(Path.Combine(root, "report.json")));
            Reject(() => report.Save(Path.Combine(baseline, "json/report.json")));
            string output = Path.Combine(f.Root, "reports/audit.json");
            report.Save(output);
            var json = JsonNode.Parse(File.ReadAllText(output))!;
            Check(json["Tables"]!["GeneralSettings.json"] != null && json["Errors"]!.GetValue<int>() == 0, "invalid export");
            Reject(() => report.Save(output));
            if (!OperatingSystem.IsWindows())
            {
                string alias = Path.Combine(f.Root, "alias");
                Directory.CreateSymbolicLink(alias, root);
                Reject(() => report.Save(Path.Combine(alias, "linked.json")));
                var linkedInput = new AssetRelationshipAnalyzer().Analyze(alias);
                Reject(() => linkedInput.Save(Path.Combine(root, "physical.json")));
                string parentAlias = Path.Combine(f.Root, "parent-alias");
                Directory.CreateSymbolicLink(parentAlias, f.Root);
                var linkedParentInput = new AssetRelationshipAnalyzer().Analyze(Path.Combine(parentAlias, "assets"));
                Reject(() => linkedParentInput.Save(Path.Combine(root, "physical-parent.json")));
                Directory.Delete(parentAlias);
                Directory.Delete(alias);
            }
            Check(!File.Exists(Path.Combine(root, "linked.json")) && !File.Exists(Path.Combine(root, "physical.json")), "guard wrote into assets");
        });

        test("audit CLI exit codes and filtered text retain full JSON report", () =>
        {
            using var f = new Fixture();
            string root = f.Assets("assets");
            f.Table(root, "GeneralSettings", """[{"Id":1}]""");
            f.Table(root, "StageSettings", """[{"Id":10,"GeneralId":1},{"Id":11,"GeneralId":2}]""");
            var stdout = Console.Out; var stderr = Console.Error;
            using var text = new StringWriter();
            try
            {
                Console.SetOut(text); Console.SetError(text);
                string output = Path.Combine(f.Root, "cli.json");
                Check(WC4MapEditor.Cli.Program.Main(["asset", "audit", root, "--general", "1", "--output", output]) == 1, "errors did not produce exit 1");
                var json = JsonNode.Parse(File.ReadAllText(output))!;
                Check(json["References"]!.AsArray().Count == 2 && json["Diagnostics"]!.AsArray().Count == 1, "filtered export lost complete diagnostics");
                Check(text.ToString().Contains("General 1: 1 incoming"), "filtered text missing");
                Check(WC4MapEditor.Cli.Program.Main(["asset", "audit", root, "--output", output]) == 2, "export error did not produce exit 2");
                Check(WC4MapEditor.Cli.Program.Main(["asset", "audit", root, "--general", "-1"]) == 2, "invalid ID accepted");
                Check(WC4MapEditor.Cli.Program.Main(["asset", "audit", Path.Combine(f.Root, "missing")]) == 2, "invalid root accepted");
                f.Table(root, "StageSettings", "[]");
                Check(WC4MapEditor.Cli.Program.Main(["asset", "audit", root]) == 0, "clean report did not produce exit 0");
            }
            finally { Console.SetOut(stdout); Console.SetError(stderr); }
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        throw new Exception("Expected I/O refusal");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public string Assets(string name)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.Combine(path, "json"));
            return path;
        }
        public void Table(string root, string name, string json, bool bom = false)
            => File.WriteAllText(Path.Combine(root, "json", name + ".json"), json, new UTF8Encoding(bom));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
