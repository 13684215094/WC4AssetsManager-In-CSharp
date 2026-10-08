using System.Text.Json.Nodes;
using System.Text;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;

static class ProjectWorkflowTests
{
    public static void Run(Action<string, Action> test)
    {
        test("project workflow requires source and creates a sibling default output without copying", () => WithFixture(dir =>
        {
            string source = WriteProject(dir, "World Conqueror 4_1.30.0", 1001, "first");
            string expected = Path.Combine(dir, "World Conqueror 4_1.30.0-edited");
            Check(GameProjectWorkspace.GetDefaultOutputDirectory(source + Path.DirectorySeparatorChar) == expected, "wrong sibling default");
            Reject(() => GameProjectWorkspace.LoadOrCreate("", expected));
            Check(!Directory.Exists(expected), "missing source published an output");
            var project = GameProjectWorkspace.LoadOrCreate($"\"{source}\"", " ");
            Check(project.OutputRoot == expected && project.SourceRoot == source && project.IsIncremental, "default load failed");
            Check(Directory.EnumerateFiles(expected, "*", SearchOption.AllDirectories).Count() == 1, "unchanged resources copied");
            string custom = Path.Combine(dir, "custom output", "nested");
            Check(GameProjectWorkspace.LoadOrCreate(source, custom).OutputRoot == custom, "custom output not created");
        }));

        test("project workflow resumes matching output and rejects another source or occupied directory", () => WithFixture(dir =>
        {
            string first = WriteProject(dir, "first", 1001, "first");
            string second = WriteProject(dir, "second", 2001, "second");
            var project = GameProjectWorkspace.LoadOrCreate(first);
            string changed = Path.Combine(project.AssetsRoot, "json", "GeneralSettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(changed)!);
            File.WriteAllText(changed, "[{\"Id\":1001,\"Name\":\"saved edit\"}]");
            var resumed = GameProjectWorkspace.LoadOrCreate(first, project.OutputRoot);
            Check(ProjectFiles.ReadAllText(changed).Contains("saved edit") && resumed.OutputRoot == project.OutputRoot, "saved edits lost");
            Check(GameProjectWorkspace.ReadSourceDirectory(project.OutputRoot) == first, "manifest preview lost source");
            Reject(() => GameProjectWorkspace.LoadOrCreate(second, project.OutputRoot));
            Check(GameProjectWorkspace.ReadSourceDirectory(project.OutputRoot) == first && File.ReadAllText(changed).Contains("saved edit"), "mismatch changed output");
            string occupied = Path.Combine(dir, "occupied");
            Directory.CreateDirectory(occupied);
            File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
            Reject(() => GameProjectWorkspace.LoadOrCreate(first, occupied));
            Check(File.ReadAllText(Path.Combine(occupied, "keep.txt")) == "keep", "unmanaged output overwritten");
        }));

        test("desktop session stays empty until load and switches all data without fallback", () => WithFixture(dir => WithDesktopMode(() =>
        {
            var session = new GameProjectDataSession();
            session.Activate(null);
            Check(session.Current == null && !AssetManager.Default.IsLoaded, "startup loaded bundled game data");
            Reject(() => { _ = GeneralSettingParser.Instance; });
            Reject(() => { _ = CountrySettingParser.Instance; });
            string first = WriteProject(dir, "first", 1001, "first");
            string second = WriteProject(dir, "second", 2001, "second");
            var a = GameProjectWorkspace.LoadOrCreate(first);
            var b = GameProjectWorkspace.LoadOrCreate(second);
            session.Activate(a);
            var oldGeneral = GeneralSettingParser.Instance;
            var oldSkill = SkillSettingParser.Instance;
            Check(oldGeneral.All.Single().Name == "first" && CountrySettingParser.Instance.Countries.Single().Id == 1001 &&
                oldSkill.All.Single().Id == 1001 && ConfigManager.Instance.GetStringTableValue("workflow_name") == "first" &&
                ConfigManager.Instance.GetTerrainTypes().Single().Value == "first 地形" &&
                ConfigManager.Instance.GetTerrainVariantCount(3) == 2, "first project provenance lost");
            oldGeneral.All[0].Name = "first saved";
            Check(oldGeneral.SaveGeneralSettings(), oldGeneral.LastError ?? "save failed");
            session.Activate(b);
            Check(GeneralSettingParser.Instance.All.Single().Name == "second" && CountrySettingParser.Instance.Countries.Single().Id == 2001 &&
                SkillSettingParser.Instance.All.Single().Id == 2001 && ArmySettingParser.Instance.All.Single().Id == 2001 &&
                ConfigManager.Instance.GetStringTableValue("workflow_name") == "second" &&
                ConfigManager.Instance.GetTerrainTypes().Single().Value == "second 地形", "second project reused data");
            Check(!oldGeneral.SaveGeneralSettings() && !oldSkill.SaveAll(), "stale parsers wrote an old project");
            session.Activate(a);
            Check(GeneralSettingParser.Instance.All.Single().Name == "first saved", "reopened override not loaded");
            var reopenedGeneral = GeneralSettingParser.Instance;
            session.Activate(null);
            reopenedGeneral.All[0].Name = "must not save after close";
            Check(!reopenedGeneral.SaveGeneralSettings(), "closed project could be written by a stale general parser");
            session.Activate(a);
            var broken = GameProjectWorkspace.LoadOrCreate(WriteProject(dir, "broken", 3001, "broken"));
            Directory.Delete(broken.SourceAssetsRoot, true);
            Reject(() => session.Activate(broken));
            Check(session.Current == a && GeneralSettingParser.Instance.All.Single().Name == "first saved", "failed activation lost current project");
            string missingSource = Path.Combine(dir, "missing");
            Directory.CreateDirectory(Path.Combine(missingSource, "assets"));
            session.Activate(GameProjectWorkspace.LoadOrCreate(missingSource));
            Check(GeneralSettingParser.Instance.All.Count == 0 && CountrySettingParser.Instance.Countries.Count == 0 &&
                SkillSettingParser.Instance.All.Count == 0 && ArmySettingParser.Instance.All.Count == 0 &&
                ConfigManager.Instance.GetTerrainTypes().Count == 0, "missing data reused previous or bundled rows");
            Check(!GeneralSettingParser.Instance.SaveGeneralSettings() && !SkillSettingParser.Instance.SaveAll(), "missing resources could overwrite output");
            session.Activate(null);
            Check(session.Current == null && !AssetManager.Default.IsLoaded, "close loaded fallback assets");
            Reject(() => { _ = SkillSettingParser.Instance; });
        })));

        test("asset browser refresh retains unsaved parser records and detects newer disk data at save", () => WithFixture(dir => WithDesktopMode(() =>
        {
            string source = WriteProject(dir, "source", 1, "first");
            string skillPath = Path.Combine(source, "assets", "json", "SkillSettings.json");
            File.WriteAllText(skillPath, """[{"Id":1,"Type":24,"Level":2,"Unknown":"before"}]""");
            var session = new GameProjectDataSession();
            var project = GameProjectWorkspace.LoadOrCreate(source);
            session.Activate(project);
            var skills = SkillSettingParser.Instance;
            skills.Items[0].Level = 9;
            Check(skills.HasChanges, "skill draft was not dirty");
            session.RefreshAssets();
            Check(ReferenceEquals(skills, SkillSettingParser.Instance) && skills.Items[0].Level == 9 && skills.HasChanges,
                "file-list refresh discarded or replaced a pending skill editor");
            string output = project.GetEditablePath(skillPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, """[{"Id":1,"Type":24,"Level":2,"Unknown":"outside"},{"Id":2,"Level":3,"New":true}]""");
            byte[] disk = File.ReadAllBytes(output);
            session.RefreshAssets();
            Check(!skills.SaveAll() && skills.LastError!.Contains("重新加载") && skills.Items[0].Level == 9 && skills.HasChanges &&
                disk.SequenceEqual(File.ReadAllBytes(output)), "external unknown field or new row was overwritten");
            skills.Reload();
            Check(skills.Items.Count == 2 && skills.Items[0].Level == 2 && !skills.HasChanges, "explicit reload did not accept latest disk data");
            skills.Items[0].Level = 9;
            Check(skills.SaveAll(), skills.LastError ?? "save after reload failed");
            var json = JsonNode.Parse(File.ReadAllText(output))!;
            Check(json[0]!["Unknown"]!.GetValue<string>() == "outside" && json[1]!["New"]!.GetValue<bool>(), "retry lost external data");
            session.Activate(null);
        })));

        test("project conquest creation saves reopens and uses the active world capture", () => WithFixture(dir =>
        {
            string source = WriteProject(dir, "source", 1, "first");
            var world = WorldParser.CreateNew(7, 9);
            world.SetTerrain(0, 2, new TerrainData { Reserved3 = 81 });
            WorldParser.SaveToFile(world, Path.Combine(source, "assets", "world.bin"));
            var project = GameProjectWorkspace.LoadOrCreate(source);
            var document = ProjectMapDocument.CreateConquest(project, 2);
            Check(document.Map.Header.MapNumber == 1 && document.Map.Header.MapClipY == 2 && document.Map.MapHeight == 5 &&
                document.Map.GetTerrain(0).Reserved3 == 81, "new conquest did not use selected world capture");
            string path = Path.Combine(project.AssetsRoot, "stage", "conquest-new.btl");
            document.Save(path);
            var reopened = ProjectMapDocument.Load(project, path);
            Check(reopened.Map.Header.MapNumber == 1 && reopened.Map.GetTerrain(0).Reserved3 == 81 && reopened.Map.Legions.Count == 2,
                "saved conquest could not reopen with active world and legions");
            Reject(() => ProjectMapDocument.Attach(project, BTLParser.CreateNew(7, 3, mapNumber: 2)));
            AtomicFile.Write(Path.Combine(project.AssetsRoot, "world.bin"), WorldParser.SaveToBytes(WorldParser.CreateNew(7, 4)));
            Reject(() => ProjectMapDocument.CreateConquest(project, 2));
        }));

        test("project catalog uses effective overrides and disables only missing or malformed editor data", () => WithFixture(dir =>
        {
            string source = WriteProject(dir, "source", 1001, "source");
            var project = GameProjectWorkspace.LoadOrCreate(source);
            var manager = new AssetManager(new AssetCache());
            manager.Scan(project.AssetsRoot);
            var catalog = new ProjectEditorCatalog(manager);
            Check(catalog[ProjectEditorKind.General].IsAvailable && catalog[ProjectEditorKind.Country].IsAvailable &&
                catalog[ProjectEditorKind.Army].IsAvailable && catalog[ProjectEditorKind.Campaign].IsAvailable &&
                catalog[ProjectEditorKind.World].IsAvailable && !catalog[ProjectEditorKind.Conquest].IsAvailable, "wrong project availability");
            File.WriteAllText(Path.Combine(project.AssetsRoot, "world-new.bin"), "invalid binary");
            string skill = Path.Combine(project.AssetsRoot, "json", "SkillSettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(skill)!);
            File.WriteAllText(skill, "invalid JSON");
            manager.Scan(project.AssetsRoot, true);
            catalog = new ProjectEditorCatalog(manager);
            Check(!catalog[ProjectEditorKind.Skill].IsAvailable && catalog[ProjectEditorKind.Skill].Details.Contains("SkillSettings.json") &&
                catalog[ProjectEditorKind.General].IsAvailable, "bad override ignored or unrelated editor disabled");
            File.WriteAllText(skill, "[]");
            File.Delete(Path.Combine(project.SourceAssetsRoot, "config", "def_armypos.xml"));
            manager.Scan(project.AssetsRoot, true);
            catalog = new ProjectEditorCatalog(manager);
            Check(catalog[ProjectEditorKind.Skill].IsAvailable && !catalog[ProjectEditorKind.Army].IsAvailable &&
                catalog[ProjectEditorKind.Army].Details.Contains("def_armypos.xml"), "empty writable table disabled or required positions ignored");
        }));
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        test("1.30.0 project workflow loads all editor data and writes only changed or added resources", () => WithFixture(dir => WithDesktopMode(() =>
        {
            string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
            var project = GameProjectWorkspace.LoadOrCreate(source, Path.Combine(dir, "output"));
            var session = new GameProjectDataSession();
            session.Activate(project);
            var catalog = new ProjectEditorCatalog(AssetManager.Default);
            Check(catalog.Options.All(o => o.IsAvailable), string.Join("\n", catalog.Options.Where(o => !o.IsAvailable).Select(o => o.Details)));
            Check(AssetManager.Default.Count == 3474 && GeneralSettingParser.Instance.All.Count == 1189 &&
                CountrySettingParser.Instance.Countries.Count == 60 && CountrySettingParser.Instance.ConquerCountries.Count == 356 &&
                SkillSettingParser.Instance.All.Count == 901 && ArmySettingParser.Instance.All.Count == 736, "wrong 1.30.0 data loaded");
            var parser = GeneralSettingParser.Instance;
            int before = parser.All.Count;
            parser.All[0].Name += " workflow-test";
            parser.AddNewGeneral("workflow-new", "Workflow.Test", 999999);
            Check(parser.SaveAll(), parser.LastError ?? "general edit or addition failed");
            Check(CountrySettingParser.Instance.AddCountry(new WC4MapEditor.Core.Models.CountrySettingData
                { Id = 9999, Name = "workflow-test", Camp = 1, InfantryMod = 1, ArmoredMod = 1, ArtilleryMod = 1, Logo = 1 }),
                CountrySettingParser.Instance.LastError ?? "country addition failed");
            Check(CountrySettingParser.Instance.SaveAll(), CountrySettingParser.Instance.LastError ?? "country save failed");
            session.Activate(project);
            Check(GeneralSettingParser.Instance.All.Count == before + 1 && GeneralSettingParser.Instance.All[0].Name.EndsWith("workflow-test") &&
                GeneralSettingParser.Instance.All.Any(g => g.Id == 999999) &&
                CountrySettingParser.Instance.Countries.Any(c => c.Id == 9999), "edits or additions not reloaded");
            string[] output = Directory.GetFiles(project.OutputRoot, "*", SearchOption.AllDirectories);
            Check(output.Length == 4 && output.All(p => p.EndsWith(GameProjectWorkspace.ManifestFileName) ||
                p.EndsWith("GeneralSettings.json") || p.EndsWith("CountrySettings.json") || p.EndsWith("def_portraitpos.xml")), "unmodified resources emitted");
            Console.WriteLine("Workflow 1.30.0: 3,474 assets, 1,189 generals, 60 countries, 356 conquest countries, 901 skills, 736 armies; general/country additions and 4 output files.");
        })));
    }

    private static string WriteProject(string dir, string name, int id, string value)
    {
        string source = Path.Combine(dir, name), assets = Path.Combine(source, "assets");
        Directory.CreateDirectory(Path.Combine(assets, "json"));
        Directory.CreateDirectory(Path.Combine(assets, "config"));
        Directory.CreateDirectory(Path.Combine(assets, "stage"));
        foreach (string table in new[] { "GeneralSettings", "CountrySettings", "ConquerCountrySettings", "ConquerSettings", "SkillSettings", "ArmySettings" })
            File.WriteAllText(Path.Combine(assets, "json", table + ".json"), new JsonArray(new JsonObject { ["Id"] = id, ["Name"] = value }).ToJsonString());
        File.WriteAllText(Path.Combine(assets, "config", "def_armypos.xml"), "<units/>");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(Path.Combine(assets, "config", "def_mapterrain.xml"),
            $"<map><terrain terrain=\"3\" name=\"{value} 地形\"><tile idx=\"0\" image=\"tile0.png\"/><tile idx=\"1\" image=\"tile1.png\"/></terrain></map>", Encoding.GetEncoding("GB18030"));
        File.WriteAllText(Path.Combine(assets, "stringtable_tw.ini"), $"workflow_name={value}\n");
        BTLParser.SaveToFile(BTLParser.CreateNew(7, 5), Path.Combine(assets, "stage", "stage1.btl"));
        WorldParser.SaveToFile(WorldParser.CreateNew(7, 5), Path.Combine(assets, "world.bin"));
        return source;
    }

    private static void WithDesktopMode(Action action)
    {
        var manager = AssetManager.Default;
        string previous = manager.AssetsRoot;
        bool loaded = manager.IsLoaded;
        try { manager.Clear(); GameProjectDataSession.ReloadEditors(); action(); }
        finally
        {
            if (loaded) manager.Scan(previous, true); else manager.Clear();
            GameProjectDataSession.ReloadEditors();
        }
    }

    private static void WithFixture(Action<string> action)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or InvalidDataException) { return; }
        throw new Exception("Expected workflow validation failure.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
