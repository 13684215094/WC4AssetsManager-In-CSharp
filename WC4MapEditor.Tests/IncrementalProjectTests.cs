using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Core.Parsers.World;

static class IncrementalProjectTests
{
    public static void Run(Action<string, Action> test)
    {
        test("incremental saves omit unchanged resources including BOM JSON and XML formatting", () =>
        {
            using var f = new Fixture();
            string json = f.SourceFile("assets/json/SkillSettings.json", "\uFEFF[ {\"Id\":1,\"CostMedal\":2,\"Unknown\":true} ]\r\n");
            f.SourceFile("assets/config/def_armypos.xml", "<units><unit id=\"1\" x=\"2\" y=\"3\" scale=\"0.90\"/></units>");
            f.SourceFile("res/config.txt", "unchanged");
            string map = Path.Combine(f.Source, "assets", "world.bin");
            WorldParser.SaveToFile(WorldParser.CreateNew(7, 5), map);
            var project = f.Create();
            string logical = project.GetEditablePath(json);
            var table = new JsonTableFile<SkillSettingData>();
            var rows = table.Read(logical, new JsonSerializerOptions { WriteIndented = true });
            Check(!table.HasChanges(rows), "initial table dirty");
            table.Save(logical, rows);
            table.Save(logical, rows);
            AtomicFile.Write(project.GetEditablePath(Path.Combine(f.Source, "assets/config/def_armypos.xml")),
                System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><units>\n<unit scale=\"0.90\" y=\"3\" x=\"2\" id=\"1\"/>\n</units>"));
            var doc = ProjectMapDocument.Load(project, map);
            doc.Save(doc.Map.FilePath);
            Check(f.OutputFiles().SequenceEqual([GameProjectWorkspace.ManifestFileName]), "no-op saves generated outputs");
            Check(ProjectFiles.EnumerateFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Count() == 4,
                "merged project enumeration omitted assets outside resource root or included manifest");
        });

        test("incremental overrides are merged on reopen and restoring source removes only that override", () =>
        {
            using var f = new Fixture();
            string original = f.SourceFile("assets/json/table.json", "[{\"Id\":1,\"CostMedal\":2}]");
            f.SourceFile("assets/other.txt", "source");
            var project = f.Create();
            string path = project.GetEditablePath(original);
            var table = new JsonTableFile<SkillSettingData>();
            var rows = table.Read(path, new());
            rows[0].CostMedal = 7;
            Check(table.HasChanges(rows), "edit not dirty");
            table.Save(path, rows);
            Check(File.Exists(path) && !table.HasChanges(rows), "saved edit baseline not accepted");
            string added = Path.Combine(project.AssetsRoot, "new", "nested", "new.txt");
            AtomicFile.Write(added, [11, 12]);
            project = GameProjectWorkspace.Open(project.OutputRoot);
            Check(ProjectFiles.ReadAllText(path).Contains("7") && ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "other.txt")) == "source", "reopen lost merged reads");
            var manager = new AssetManager(new AssetCache());
            manager.Scan(project.AssetsRoot);
            Check(manager.Count == 3 && manager.Find("new/nested/new.txt")?.FullPath == added, "merged index duplicate or missing entries");
            rows[0].CostMedal = 2;
            table.Save(path, rows);
            Check(!File.Exists(path) && !table.HasChanges(rows) && File.Exists(added), "restoring source left an override or removed unrelated additions");
            Check(File.ReadAllText(original) == "[{\"Id\":1,\"CostMedal\":2}]", "source changed");
        });

        test("failed incremental batch retains previous overrides and pending JSON baseline", () =>
        {
            using var f = new Fixture();
            string input = f.SourceFile("assets/json/table.json", "[{\"Id\":1,\"CostMedal\":2}]");
            var project = f.Create();
            string path = project.GetEditablePath(input);
            var table = new JsonTableFile<SkillSettingData>();
            var rows = table.Read(path, new());
            rows[0].CostMedal = 3;
            table.Save(path, rows);
            byte[] saved = File.ReadAllBytes(path);
            string blocked = Path.Combine(project.AssetsRoot, "blocked");
            File.WriteAllText(blocked, "cannot be directory");
            rows[0].CostMedal = 2;
            ExpectFailure(() => AtomicFile.WriteAll((path, table.Serialize(path, rows)), (Path.Combine(blocked, "file.txt"), [42])));
            Check(saved.SequenceEqual(File.ReadAllBytes(path)) && table.HasChanges(rows), "failed batch removed override or accepted dirty baseline");
            table.Save(path, rows);
            Check(!File.Exists(path) && !table.HasChanges(rows), "failed batch prevented retry");
            Check(!Directory.EnumerateFiles(project.OutputRoot, "*.tmp*", SearchOption.AllDirectories).Any(), "staging files retained");
        });

        test("JSON and general saves reject external changes while retaining unsaved model", () =>
        {
            using var f = new Fixture();
            string json = f.SourceFile("assets/json/SkillSettings.json", "[{\"Id\":1,\"CostMedal\":2}]");
            f.SourceFile("assets/json/GeneralSettings.json", "[{\"Id\":1001,\"Name\":\"initial\"}]");
            f.SourceFile("assets/config/def_portraitpos.xml", "<Portraits><general name=\"test\" posx=\"2\"/></Portraits>");
            var project = f.Create();
            AssetManager.Default.Scan(project.AssetsRoot, true);
            string path = project.GetEditablePath(json);
            var table = new JsonTableFile<SkillSettingData>();
            var rows = table.Read(path, new());
            rows[0].CostMedal = 7;
            byte[] candidate = table.Serialize(path, rows);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "[{\"Id\":1,\"CostMedal\":99}]");
            ExpectFailure(() => AtomicFile.Write(path, candidate));
            Check(ProjectFiles.ReadAllText(path).Contains("99") && table.HasChanges(rows), "external JSON overwritten");
            var general = new GeneralSettingParser(project.AssetsRoot);
            general.All[0].Name = "pending";
            File.WriteAllText(general.ConfigPath, "[{\"Id\":1001,\"Name\":\"external\"}]");
            Check(!general.SaveAll() && general.HasChanges && ProjectFiles.ReadAllText(general.ConfigPath).Contains("external"), "external generals overwritten");
            general.Reload();
            general.All[0].Name = "retry";
            Check(general.SaveAll() && !general.HasChanges, general.LastError ?? "reload save failed");
            Directory.CreateDirectory(Path.GetDirectoryName(general.PortraitPosPath)!);
            File.WriteAllText(general.PortraitPosPath, "<Portraits/>");
            general.EnsurePortraitDefault("test").PosX++;
            Check(!general.SaveAll() && ProjectFiles.ReadAllText(general.PortraitPosPath) == "<Portraits/>", "portrait conflict ignored");
        });

        test("country deletion saves both tables and rolls back memory on failure", () =>
        {
            using var f = new Fixture();
            f.SourceFile("assets/json/CountrySettings.json", "[{\"Id\":1,\"Name\":\"one\",\"Unknown\":42},{\"Id\":2}]");
            f.SourceFile("assets/json/ConquerCountrySettings.json", "[{\"CountryId\":1,\"ConquerId\":10},{\"CountryId\":2,\"ConquerId\":10}]");
            f.SourceFile("assets/json/ConquerSettings.json", "[]");
            var project = f.Create();
            AssetManager.Default.Scan(project.AssetsRoot, true);
            CountrySettingParser.ClearInstance();
            var parser = CountrySettingParser.Instance;
            Directory.CreateDirectory(parser.ConquerCountrySettingsPath);
            Check(!parser.RemoveCountry(1) && parser.LastError != null && parser.Countries.Count == 2 && parser.ConquerCountries.Count == 2,
                "failed delete removed in-memory country or ignored failure");
            Check(!File.Exists(parser.CountrySettingsPath), "partial country save");
            Directory.Delete(parser.ConquerCountrySettingsPath);
            Check(parser.RemoveCountry(1) && parser.Countries.Count == 1 && parser.ConquerCountries.Count == 1 && !parser.HasChanges, "transaction retry failed");
            Check(JsonNode.Parse(ProjectFiles.ReadAllText(parser.CountrySettingsPath))!.AsArray().Count == 1 &&
                JsonNode.Parse(ProjectFiles.ReadAllText(parser.ConquerCountrySettingsPath))!.AsArray().Count == 1, "tables diverged");
            File.WriteAllText(parser.CountrySettingsPath, "external");
            Check(!parser.AddCountry(new CountrySettingData { Id = 3 }) && parser.Countries.Count == 1 && parser.LastError != null, "failed insertion retained row");
        });

        test("text resource saves use incremental output and reject conflicts without materializing no-op edits", () =>
        {
            using var f = new Fixture();
            string source = f.SourceFile("assets/stringtable_tw.ini", "\uFEFFfirst=one\r\nsecond=two\r\n");
            var project = f.Create();
            string path = project.GetEditablePath(source);
            var doc = new TextResourceDocument(path);
            doc.Text = doc.Text.Replace("\r\n", "\n");
            doc.Save();
            Check(!File.Exists(path) && !doc.HasChanges, "opening or newline conversion generated output");
            doc.Text = "\uFEFFfirst=changed\r\nsecond=two\r\n".TrimStart('\uFEFF');
            doc.Save();
            Check(File.Exists(path) && !doc.HasChanges && File.ReadAllBytes(path).AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble), "text or BOM not saved");
            doc.Text = "first=one\r\nsecond=two\r\n";
            doc.Save();
            Check(!File.Exists(path), "restored text left override");
            doc.Text = "pending";
            File.WriteAllText(path, "external");
            ExpectFailure(doc.Save);
            Check(doc.HasChanges && File.ReadAllText(path) == "external", "text conflict lost changes");
        });

        test("same-key strings conflict is rejected and reverting pending edits is clean", () =>
        {
            using var f = new Fixture();
            string path = f.SourceFile("assets/stringtable_tw.ini", "first=one\nsecond=two\n");
            var first = new StringTableParser(path);
            var second = new StringTableParser(path);
            first.SetValue("first", "changed");
            first.Save();
            second.SetValue("first", "pending");
            ExpectFailure(second.Save);
            Check(second.IsDirty && File.ReadAllText(path).Contains("first=changed"), "same key overwritten");
            second.Reload();
            second.SetValue("first", "new");
            second.SetValue("first", "changed");
            Check(!second.IsDirty, "reverting key stayed dirty");
            second.SetValue("first", "prepared");
            byte[] candidate = second.SerializePending()!;
            var other = new StringTableParser(path);
            other.SetValue("second", "external");
            other.Save();
            ExpectFailure(() => AtomicFile.Write(path, candidate));
            Check(File.ReadAllText(path).Contains("second=external") && second.IsDirty, "prepared strings reverted concurrent unrelated key");
            second.Save();
            Check(File.ReadAllText(path).Contains("second=external") && !second.IsDirty, "string remerge did not retain unrelated key");
        });

        test("string table edits preserve BOM spacing and CRLF and restoring values removes the override", () =>
        {
            using var f = new Fixture();
            string source = f.SourceFile("assets/stringtable_tw.ini", "\uFEFF;comment\r\nfirst = one \r\nsecond=two");
            var project = f.Create();
            string path = project.GetEditablePath(source);
            var strings = new StringTableParser(path);
            strings.SetValue("first", "changed");
            strings.Save();
            Check(File.ReadAllText(path) == ";comment\r\nfirst = changed \r\nsecond=two" &&
                File.ReadAllBytes(path).AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble), "string formatting or BOM changed");
            strings.SetValue("first", "one");
            strings.Save();
            Check(!File.Exists(path) && !strings.IsDirty, "restoring strings left override");
        });

        test("legacy version one projects open without source while version two requires source", () =>
        {
            using var f = new Fixture();
            f.SourceFile("assets/data.txt", "source");
            var project = f.Create();
            Directory.Delete(f.Source, true);
            ExpectFailure(() => GameProjectWorkspace.Open(project.OutputRoot));
            File.WriteAllText(Path.Combine(project.AssetsRoot, "data.txt"), "legacy");
            string manifestPath = Path.Combine(project.OutputRoot, GameProjectWorkspace.ManifestFileName);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["Version"] = 1;
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            project = GameProjectWorkspace.Open(project.OutputRoot);
            Check(!project.IsIncremental && ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "data.txt")) == "legacy", "legacy reopen failed");
        });

        test("project enumeration refuses newly introduced source and output directory links", () =>
        {
            if (OperatingSystem.IsWindows()) return;
            using var f = new Fixture();
            f.SourceFile("assets/data.txt", "source");
            var project = f.Create();
            string outside = Path.Combine(f.Root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
            foreach (string directory in new[] { project.SourceAssetsRoot, project.AssetsRoot })
            {
                string link = Path.Combine(directory, "linked");
                Directory.CreateSymbolicLink(link, outside);
                ExpectFailure(() => ProjectFiles.EnumerateFiles(project.AssetsRoot, "*", SearchOption.AllDirectories).ToArray());
                ExpectFailure(() => ProjectFiles.DirectoryExists(Path.Combine(project.AssetsRoot, "linked")));
                Directory.Delete(link);
            }
        });
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Expected rejection.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        private readonly string _previous = AssetManager.Default.AssetsRoot;
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "source");
        public string Output => Path.Combine(Root, "output");
        public Fixture() => Directory.CreateDirectory(Path.Combine(Source, "assets"));
        public string SourceFile(string relative, string text)
        {
            string path = Path.Combine(Source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }
        public GameProjectWorkspace Create()
        {
            var project = GameProjectWorkspace.Create(Source, Output);
            AssetManager.Default.Scan(project.AssetsRoot, true);
            return project;
        }
        public string[] OutputFiles() => Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Output, path)).Order().ToArray();
        public void Dispose()
        {
            if (Directory.Exists(_previous)) AssetManager.Default.Scan(_previous, true); else AssetManager.Default.Clear();
            CountrySettingParser.ClearInstance();
            Directory.Delete(Root, true);
        }
    }
}
