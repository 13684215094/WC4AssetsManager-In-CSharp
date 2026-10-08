using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;

static class JsonTableDocumentTests
{
    public static void Run(Action<string, Action> test)
    {
        test("JSON fields preserve missing null booleans nested data large integers and reject partial edits", () => WithFixture(dir =>
        {
            const string original = """
                [{"Id":7,"Name":"a","Count":2,"Open":true,"Rate":1.5,"Unknown":{"extra":[1,null,"x"]},"Big":9007199254740993},
                 {"Id":8,"Name":null,"Count":null,"Open":false}]
                """;
            string file = Path.Combine(dir, "table.json");
            File.WriteAllText(file, original, new UTF8Encoding(true));
            var document = new JsonTableDocument(file);
            document.ApplyFields(0, [("Name", "新名称"), ("Count", "3"), ("Open", "false")]);
            Check(document.GetRow(0)["Open"]!.GetValue<bool>() == false, "false rejected or converted");
            var before = document.Rows.DeepClone();
            Reject(() => document.ApplyFields(0, [("Name", "must not apply"), ("Count", "1.5")]));
            Reject(() => document.ApplyFields(0, [("Count", "\"3\"")]));
            Check(JsonNode.DeepEquals(before, document.Rows), "invalid field set partly applied");
            document.ApplyEdits(1, [("Count", "9", false), ("Name", "null", false)]);
            Check(document.GetRow(1)["Name"]!.GetValue<string>() == "null", "literal null string became null");
            document.Save();
            Check(File.ReadAllBytes(file).AsSpan().StartsWith(Encoding.UTF8.Preamble), "UTF-8 BOM lost");
            var loaded = new JsonTableDocument(file);
            Check(loaded.GetRow(0)["Big"]!.GetValue<long>() == 9007199254740993 &&
                loaded.GetRow(0)["Unknown"]!["extra"]![1] == null && !loaded.GetRow(1).ContainsKey("Unknown"), "unknown/missing/numeric value changed");
            loaded.ApplyEdits(0, [("Name", "unused", true)]);
            Check(loaded.GetRow(0)["Name"] == null, "explicit null toggle failed");
            int added = loaded.AddRow(false);
            loaded.ApplyFields(added, [("Rate", "2.5")]);
            Check(loaded.GetRow(added)["Rate"]!.GetValue<double>() == 2.5, "new row inferred float field as integer");
        }));

        test("JSON cloned records use distinct IDs and preserve original duplicate IDs by row", () => WithFixture(dir =>
        {
            string file = Path.Combine(dir, "table.json");
            File.WriteAllText(file, """[{"Id":4,"Name":"first"},{"Id":4,"Name":"second"},{"Id":8,"Skills":[1,2]}]""");
            var document = new JsonTableDocument(file);
            int clone = document.AddRow(true, 2);
            int next = document.AddRow(true, 2);
            Check(document.GetRow(clone)["Id"]!.GetValue<long>() == 9 && document.GetRow(next)["Id"]!.GetValue<long>() == 10,
                "multiple clones reused ID");
            document.ApplyFields(1, [("Name", "changed second")]);
            document.GetRow(clone)["Skills"]![0] = 77;
            Reject(() => document.ApplyFields(clone, [("Skills", "[\"bad\"]")]));
            Reject(() => document.ApplyFields(clone, [("Skills", "[1.5]")]));
            Check(document.GetRow(2)["Skills"]![0]!.GetValue<int>() == 1, "clone aliases original values");
            document.Save();
            document.ApplyFields(next, [("Id", "4")]);
            Check(document.Validate().Count == 1, "new duplicate ID accepted");
            string saved = File.ReadAllText(file);
            Reject(document.Save);
            Check(File.ReadAllText(file) == saved, "duplicate save overwrote output");
            document.Discard();
            document.RemoveRow(1);
            Check(document.GetRow(0)["Name"]!.GetValue<string>() == "first", "deleted wrong original duplicate");
        }));

        test("JSON incremental save writes one changed table reverts cleanly detects conflicts and rejects closed project", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"), assets = Path.Combine(source, "assets/json");
            Directory.CreateDirectory(assets);
            string input = Path.Combine(assets, "GeneralTitleSettings.json");
            File.WriteAllText(input, """[{"Id":1,"GeneralId":11,"AddAttack":12}]""");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            string output = project.GetEditablePath(input);
            var document = new JsonTableDocument(output);
            document.Save();
            Check(!File.Exists(output), "unmodified table copied");
            document.ApplyFields(0, [("AddAttack", "18")]);
            document.Save();
            Check(File.Exists(output) && !document.HasChanges && File.ReadAllText(input).Contains("12"), "edit not written or source changed");
            document.ApplyFields(0, [("AddAttack", "12")]);
            document.Save();
            Check(!File.Exists(output) && !document.HasChanges, "reverted table retained override");
            var stale = new JsonTableDocument(output);
            document.ApplyFields(0, [("AddAttack", "15")]); document.Save();
            stale.ApplyFields(0, [("AddAttack", "19")]);
            Reject(stale.Save);
            Check(JsonNode.Parse(File.ReadAllText(output))![0]!["AddAttack"]!.GetValue<int>() == 15, "conflict overwrote newer file");
            AssetManager.Default.Clear();
            Reject(document.Save);
        }));

        test("JSON object documents invalid rows and duplicate properties are rejected", () => WithFixture(dir =>
        {
            string path = Path.Combine(dir, "invalid.json");
            foreach (string text in new[] { "{}", "[null]", "[1]", "[{bad]", "[{\"Id\":1,\"Id\":2}]" })
            {
                File.WriteAllText(path, text);
                Check(!JsonTableDocument.CanOpen(path), "unsupported table silently accepted");
            }
            File.WriteAllText(path, "[]");
            var document = new JsonTableDocument(path);
            document.AddRow(false);
            document.ApplyFields(0, [("Id", "1"), ("New", "[1,null]")]);
            document.Save();
            Check(new JsonTableDocument(path).Rows.Count == 1, "empty table could not add its first row");
        }));
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        test("1.30.0 all 81 JSON tables open without copying and extension tables support changed-only save", () => WithFixture(dir =>
        {
            string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            int tables = 0, rows = 0;
            foreach (string file in ProjectFiles.EnumerateFiles(Path.Combine(project.AssetsRoot, "json"), "*.json"))
            {
                var document = new JsonTableDocument(file);
                document.Save(); tables++; rows += document.Rows.Count;
            }
            Check(tables == 81 && rows == 22055 && Directory.GetFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Length == 1,
                $"wrong corpus or unedited copies: {tables} / {rows}");
            foreach (string table in new[] { "GeneralPromotionSettings", "GeneralTitleSettings", "GeneralStageSettings", "TechnologySettings" })
            {
                string path = Path.Combine(project.AssetsRoot, "json", table + ".json");
                var document = new JsonTableDocument(path);
                string field = table switch { "GeneralPromotionSettings" => "CostMedal", "GeneralTitleSettings" => "AddAttack",
                    "GeneralStageSettings" => "PrizeMedals", _ => "CostGold" };
                long old = document.GetRow(0)[field]!.GetValue<long>();
                document.ApplyFields(0, [(field, (old + 1).ToString())]); document.Save();
                var loaded = new JsonTableDocument(path);
                Check(loaded.GetRow(0)[field]!.GetValue<long>() == old + 1 && loaded.Rows.Count == document.Rows.Count, "extension save failed");
                document.ApplyFields(0, [(field, old.ToString())]); document.Save();
                Check(!File.Exists(path), "extension revert left copy");
            }
            GameProjectDataSession.ReloadEditors();
            var oldGeneral = WC4MapEditor.Core.Parsers.General.GeneralSettingParser.Instance;
            string generalPath = Path.Combine(project.AssetsRoot, "json/GeneralSettings.json");
            var generic = new JsonTableDocument(generalPath);
            string oldName = oldGeneral.All[0].Name ?? "";
            generic.ApplyFields(0, [("Name", "通用表修改")]); generic.Save();
            GameProjectDataSession.ReloadEditors();
            Check(WC4MapEditor.Core.Parsers.General.GeneralSettingParser.Instance.All[0].Name == "通用表修改", "specialized parser did not see generic save");
            generic.ApplyFields(0, [("Name", oldName)]); generic.Save();
            Check(!File.Exists(generalPath), "general revert retained output");
            Console.WriteLine($"JSON table corpus: {tables} tables / {rows} rows; 4 extension edits and reverts.");
        }));
    }

    private static void WithFixture(Action<string> action)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        string previous = AssetManager.Default.AssetsRoot;
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            if (Directory.Exists(previous)) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear();
            Directory.Delete(directory, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or JsonException or ArgumentException) { return; }
        throw new Exception("Expected validation failure.");
    }
}
