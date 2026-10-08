using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Views.Assist;
using SkiaSharp;

static class ApkAndTechTests
{
    private const string Layout = """
        <Layouts><Form id="form_technique"><Repeater id="rpt_items" width="2300" height="525"
        itemwidth="230" itemheight="106" itemcount="50"><Image id="tech_item" width="181"/></Repeater></Form></Layouts>
        """;

    public static void Run(Action<string, Action> test)
    {
        test("APK project keeps extraction outside output and resumes with source fingerprint", () => WithFixture(dir =>
        {
            string apk = Path.Combine(dir, "World Conqueror 4_1.30.0.apk");
            WriteApk(apk, ("assets/json/TechnologySettings.json", "[]"), ("lib/arm64-v8a/game.so", "native"),
                ("com/appsflyer/internal/a.", "SDK metadata ignored"));
            var original = File.ReadAllBytes(apk);
            var project = GameProjectWorkspace.LoadOrCreate(apk);
            Check(project.IsApkSource && SamePath(project.SourcePath, apk) && !SamePath(project.SourceRoot, project.OutputRoot) &&
                SamePath(project.OutputRoot, Path.Combine(dir, "World Conqueror 4_1.30.0-edited")), "APK source/default path lost");
            Check(Files(project.OutputRoot) == 1 && File.Exists(Path.Combine(project.SourceRoot, "lib/arm64-v8a/game.so")), "APK copied into output or omitted native");
            string logical = Path.Combine(project.AssetsRoot, "json/TechnologySettings.json");
            var document = new JsonTableDocument(logical);
            document.Rows.Add(new JsonObject { ["Id"] = 1, ["CostGold"] = 123 }); document.Save();
            Check(Files(project.OutputRoot) == 2 && File.ReadAllBytes(apk).SequenceEqual(original), "APK/source modified or unrelated output written");
            var reopened = GameProjectWorkspace.Open(project.OutputRoot, apk);
            Check(ProjectFiles.ReadAllText(logical).Contains("123") && SamePath(GameProjectWorkspace.ReadSourceDirectory(reopened.OutputRoot), apk), "APK output did not reopen");
            var restored = new JsonTableDocument(logical); restored.Rows.Clear(); restored.Save();
            Check(Files(project.OutputRoot) == 1, "restored source table kept an override");
            string cached = Path.Combine(project.SourceAssetsRoot, "json/TechnologySettings.json");
            File.WriteAllText(cached, "corrupted cache");
            Check(GameProjectWorkspace.Open(project.OutputRoot).SourceRoot == project.SourceRoot && File.ReadAllText(cached) == "[]", "corrupted extraction was reused");
            File.Delete(Path.Combine(project.SourceRoot, "lib/arm64-v8a/game.so"));
            GameProjectWorkspace.Open(project.OutputRoot);
            Check(File.Exists(Path.Combine(project.SourceRoot, "lib/arm64-v8a/game.so")), "missing cache resource was not reconstructed");
            WriteApk(apk, ("assets/json/TechnologySettings.json", "[{\"Id\":9}]"));
            Reject(() => GameProjectWorkspace.Open(project.OutputRoot));
            Check(Files(project.OutputRoot) == 1, "changed APK rejection mutated output");
            DeleteCache(project.SourceRoot);
        }));

        test("APK rejects traversal links duplicate paths and non-assets packages without publishing", () => WithFixture(dir =>
        {
            int index = 0;
            foreach (var entryNames in new[]
            {
                new[] { "assets/../../escape.txt" }, new[] { "/assets/data.json" }, new[] { "assets\\evil.txt" },
                new[] { "assets/a.txt", "assets/A.txt" }, new[] { "assets/a", "assets/a/b" },
                new[] { "assets/CON.txt" }, new[] { "AndroidManifest.xml" }
            })
            {
                string apk = Path.Combine(dir, $"bad{index}.apk"), output = Path.Combine(dir, $"output{index++}");
                WriteApk(apk, entryNames.Select(name => (name, "data")).ToArray());
                Reject(() => GameProjectWorkspace.Create(apk, output));
                Check(!Directory.Exists(output), "unsafe APK published output");
            }
            string linked = Path.Combine(dir, "linked.apk");
            using (var archive = ZipFile.Open(linked, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("assets/link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var writer = new StreamWriter(entry.Open()); writer.Write("/outside");
            }
            Reject(() => GameProjectWorkspace.Create(linked, Path.Combine(dir, "linked-output")));
            Check(!File.Exists(Path.Combine(dir, "escape.txt")), "traversal wrote outside staging");
        }));

        test("APK cancellation and occupied output leave files intact", () => WithFixture(dir =>
        {
            string apk = Path.Combine(dir, "input.apk"), output = Path.Combine(dir, "output");
            WriteApk(apk, ("assets/data.txt", Guid.NewGuid().ToString()));
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress(value => { if (value.RelativePath.StartsWith("提取 APK")) cancellation.Cancel(); });
            try { GameProjectWorkspace.Create(apk, output, progress, cancellation.Token); throw new Exception("cancellation ignored"); }
            catch (OperationCanceledException) { }
            Check(!Directory.Exists(output), "cancelled APK published output");
            Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "keep");
            Reject(() => GameProjectWorkspace.Create(apk, output));
            Check(File.ReadAllText(Path.Combine(output, "keep.txt")) == "keep", "occupied output overwritten");
            Reject(() => GameProjectWorkspace.Create(apk, dir));
        }));

        test("HQ preview follows layout row-column geometry and finds overlap missing references and cycles", () =>
        {
            var grid = HeadquartersTechGrid.FromLayout(Layout, "layout.xml");
            var rows = JsonNode.Parse("""
                [{"Id":1,"Level":1,"Type":0,"Categorys":0,"Position":[1,1],"UpgradeID":2,"NeedID":[]},
                 {"Id":2,"Level":2,"Type":0,"Categorys":0,"Position":[4,10],"UpgradeID":0,"NeedID":[1]}]
                """)!.AsArray();
            var tree = HeadquartersTechTree.Build(rows, grid);
            Check(grid.Columns == 10 && grid.Rows == 5 && tree.Nodes[1].X == 2070 && tree.Nodes[1].Y == 318 &&
                tree.Edges.Count == 2 && tree.Issues.Count == 0, "layout grid mapping or valid dependency failed");
            rows[1]!["Position"] = new JsonArray(1, 1); rows[1]!["Type"] = 1;
            Check(HeadquartersTechTree.Build(rows, grid).Issues.Count == 0, "legitimate dual-icon cell reported as collision");
            rows[1]!["Type"] = 0;
            rows[1]!["Position"] = new JsonArray(1, 1);
            rows[1]!["UpgradeID"] = 1;
            rows[0]!["NeedID"] = new JsonArray(2, 999);
            tree = HeadquartersTechTree.Build(rows, grid);
            Check(tree.Issues.Any(issue => issue.Contains("重叠")) && tree.Issues.Any(issue => issue.Contains("999")) &&
                tree.Issues.Count(issue => issue.Contains("存在环")) == 2, "relation warnings missing");
            rows[1]!["Position"] = new JsonArray(1, 11);
            Check(HeadquartersTechTree.Build(rows, grid).Issues.Any(issue => issue.Contains("超出")), "overflow not flagged");
        });

        test("HQ draft preview is isolated from pending edits and saved state", () => WithFixture(dir =>
        {
            string path = Path.Combine(dir, "TechnologySettings.json");
            File.WriteAllText(path, "[{\"Id\":1,\"Position\":[1,1],\"Type\":0}]");
            var document = new JsonTableDocument(path);
            var preview = document.CreatePreviewRows(0, [("Position", "[2,9]", false)]);
            Check(preview[0]!["Position"]!.ToJsonString() == "[2,9]" && !document.HasChanges, "preview mutated editable document");
            Reject(() => document.CreatePreviewRows(0, [("Position", "[2,9]", false), ("Type", "invalid", false)]));
            Check(!document.HasChanges, "invalid preview partially applied");
        }));

        test("skill append connects maximum level preserving unknown null fields without guessing balance", () => WithFixture(dir => WithAssets(() =>
        {
            string source = Path.Combine(dir, "source"), json = Path.Combine(source, "assets/json"); Directory.CreateDirectory(json);
            File.WriteAllText(Path.Combine(json, "SkillSettings.json"), """
                [{"Id":1244,"Name":"钢铁洪流","Type":24,"Level":4,"UpgradeId":1245,"SkillEffect":20},
                 {"Id":1245,"Name":"钢铁洪流","Type":24,"Level":5,"UpgradeId":0,"CostMedal":400,"SkillEffect":25,"Unknown":{"Nested":[1,null]},"UnknownNull":null}]
                """);
            File.WriteAllText(Path.Combine(source, "assets/stringtable_tw.ini"), "skill_name_24=钢铁洪流\n");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true); SkillSettingParser.ClearInstance();
            var parser = SkillSettingParser.Instance;
            Reject(() => parser.AppendNextLevel(parser.Items[0]));
            var tail = parser.GetById(1245)!;
            var next = parser.AppendNextLevel(tail);
            Check(next.Id == 1246 && next.Level == 6 && next.Type == 24 && next.SkillEffect == 25 && next.CostMedal == 400 &&
                tail.UpgradeId == 1246 && next.UpgradeId == 0, "append chose wrong level chain or balance");
            Check(parser.SaveAll(), parser.LastError ?? "save failed");
            var saved = JsonNode.Parse(ProjectFiles.ReadAllText(parser.ConfigPath))!.AsArray();
            Check(saved[2]!["Unknown"]!.ToJsonString() == "{\"Nested\":[1,null]}" && saved[2]!.AsObject().ContainsKey("UnknownNull") && saved[2]!["UnknownNull"] == null &&
                Files(project.OutputRoot) == 2, "clone lost original fields or copied other resources");
            parser.Reload(); Check(parser.GetById(1246)?.Level == 6, "new skill did not reload");
            parser.Items.Add(new SkillSettingData { Id = 1247, Type = 99, Level = 1 });
            var seventh = parser.AppendNextLevel(parser.GetById(1246)!);
            Check(seventh.Id != 1247 && seventh.Level == 7, "occupied next Id reused");
            parser.Items.Add(new SkillSettingData { Id = 9999, Type = 24, Level = 7 });
            int count = parser.Items.Count;
            Reject(() => parser.AppendNextLevel(seventh));
            Check(parser.Items.Count == count && seventh.UpgradeId == 0, "ambiguous family partially changed");
        })));
    }

    public static void RunCorpus(Action<string, Action> test, string root, string? apkPath = null, bool requireApk = false)
    {
        test("real 1.30.0 APK opens all project resources with incremental HQ and steel-flood edits", () => WithFixture(dir => WithAssets(() =>
        {
            string apk = Path.GetFullPath(apkPath ?? Path.Combine(root, "wc4", "World Conqueror 4_1.30.0.apk"));
            if (!File.Exists(apk))
            {
                string reason = $"真实 APK 样本不存在：{apk}。可用 --apk 指定；CI 可用 --require-apk 要求必须提供。";
                if (requireApk || apkPath != null) throw new FileNotFoundException(reason, apk);
                throw new TestSkippedException(reason);
            }
            string initialHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(apk)));
            var project = GameProjectWorkspace.LoadOrCreate(apk, Path.Combine(dir, "apk-output"));
            AssetManager.Default.Scan(project.AssetsRoot, true); GameProjectDataSession.ReloadEditors();
            Check(AssetManager.Default.Count == 3474 && Files(project.OutputRoot) == 1, "APK assets not fully indexed or copied to output");
            Check(new ProjectEditorCatalog(AssetManager.Default).Options.All(option => option.IsAvailable), "APK editors missing resources");
            var document = new JsonTableDocument(Path.Combine(project.AssetsRoot, "json/TechnologySettings.json"));
            var grid = HeadquartersTechGrid.FromLayout(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "layout.xml")), "layout.xml");
            var tree = HeadquartersTechTree.Build(document.Rows, grid);
            Check(tree.Nodes.Count == 191 && !tree.Issues.Any(issue => issue.Contains("超出") || issue.Contains("重叠")),
                string.Join("\n", tree.Issues));
            Check(tree.Nodes.GroupBy(node => (node.Category, node.GridRow, node.GridColumn)).Count(group => group.Count() == 2) == 46 &&
                tree.Issues.Count == 1 && tree.Issues[0].Contains("305063"), "legitimate dual cells or original missing upgrade target lost");
            document.ApplyFields(0, [("CostGold", "501")]); document.Save();
            var parser = SkillSettingParser.Instance;
            var next = parser.AppendNextLevel(parser.GetById(1245)!); next.SkillEffect = 30;
            Check(parser.SaveAll(), parser.LastError ?? "skill save failed");
            Check(next.Id == 1246 && next.Level == 6 && Files(project.OutputRoot) == 3, "APK edits not limited to two tables");
            GameProjectWorkspace.Open(project.OutputRoot);
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(apk))) == initialHash, "original APK changed");
            Console.WriteLine($"APK: 3474 assets; HQ {tree.Nodes.Count} nodes; Steel Flood 1245 → 1246 Lv6; output = manifest + 2 tables.");
        })));
    }

    public static void RunRendering(Action<string, Action> test)
    {
        test("HQ preview renders current project sprites and moves the selected node with draft position", () =>
        {
            var grid = HeadquartersTechGrid.FromLayout(Layout, "layout.xml");
            var rows = JsonNode.Parse("""
                [{"Id":1,"Level":1,"Type":0,"Categorys":0,"Position":[1,1],"UpgradeID":0,"NeedID":[]}]
                """)!.AsArray();
            using var sprite = new SKBitmap(4, 4); sprite.Erase(SKColors.Red);
            HeadquartersTechPreviewRenderer.Sprite? Lookup(string reference) => reference == "bg_tech_item.png"
                ? new(sprite, SKRect.Create(4, 4)) : null;
            using var first = HeadquartersTechPreviewRenderer.Render(HeadquartersTechTree.Build(rows, grid), grid, 0, 1, Lookup, _ => "name");
            Check(first.Width == 2300 && first.Height == 525 && first.GetPixel(40, 60).Red > 240 &&
                first.GetPixel(500, 60).Red < 240, "preview sprites or dimensions incorrect");
            rows[0]!["Position"] = new JsonArray(2, 3);
            using var second = HeadquartersTechPreviewRenderer.Render(HeadquartersTechTree.Build(rows, grid), grid, 0, 1, Lookup, _ => "name");
            Check(second.GetPixel(40, 60).Red < 240 && second.GetPixel(500, 166).Red > 240, "draft position did not move rendered node");
            Check(!first.GetPixelSpan().SequenceEqual(second.GetPixelSpan()), "preview retained stale pixels");
        });
    }

    public static void RunRenderingCorpus(Action<string, Action> test, string root)
    {
        test("real HQ preview renders all eight categories using project art and Chinese names", () => WithFixture(dir => WithAssets(() =>
        {
            var project = GameProjectWorkspace.Create(Path.Combine(root, "wc4/World Conqueror 4_1.30.0"), Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            var document = new JsonTableDocument(Path.Combine(project.AssetsRoot, "json/TechnologySettings.json"));
            var grid = HeadquartersTechGrid.FromLayout(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "layout_x.xml")), "layout_x.xml");
            var tree = HeadquartersTechTree.Build(document.Rows, grid);
            var strings = AssetManager.Default.GetStringTable("cn");
            using var font = SKTypeface.FromFile(ProjectFiles.ResolveRead(Path.Combine(project.AssetsRoot, "font/NotoSans_cn.otf")));
            foreach (int type in tree.Nodes.Select(node => node.Type).Distinct())
                Check(new[] { $"tech_{type}.png", $"tech_{type}_1.png" }.Any(reference => LayoutImageProvider.Resolve(reference)?.Ready == true), $"missing real HQ icon Type {type}");
            for (int category = 0; category < 8; category++)
            {
                using var bitmap = HeadquartersTechPreviewRenderer.Render(tree, grid, category, null, reference =>
                {
                    var image = LayoutImageProvider.Resolve(reference);
                    return image?.Ready == true ? new(image.Page!, image.SrcRect) : null;
                }, key => strings.GetValueOrDefault(key), font);
                using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                Check(encoded.Size > 5_000, $"HQ category {category} rendered blank");
                if (category == 0) File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "tmp/headquarters-tech-preview.png"), encoded.ToArray());
            }
            Check(Files(project.OutputRoot) == 1, "preview wrote project resources");
            Console.WriteLine($"HQ preview: 8 categories; {tree.Nodes.Count} nodes; layout_x {grid.Columns} columns / {grid.Rows} rows; {tree.Issues.Count} diagnostics.");
        })));
    }

    private static void WriteApk(string path, params (string Name, string Text)[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var entry in entries)
        { using var writer = new StreamWriter(archive.CreateEntry(entry.Name).Open(), new UTF8Encoding(false)); writer.Write(entry.Text); }
    }
    private static int Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
    private static void DeleteCache(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void WithAssets(Action action)
    {
        var manager = AssetManager.Default; bool loaded = manager.IsLoaded; string previous = manager.AssetsRoot;
        try { manager.Clear(); GameProjectDataSession.ReloadEditors(); action(); }
        finally { if (loaded) manager.Scan(previous, true); else manager.Clear(); GameProjectDataSession.ReloadEditors(); }
    }
    private static void WithFixture(Action<string> action)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); } finally { Directory.Delete(directory, true); }
    }
    private sealed class ImmediateProgress(Action<ProjectCopyProgress> action) : IProgress<ProjectCopyProgress>
    { public void Report(ProjectCopyProgress value) => action(value); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException) { return; }
        throw new Exception("Expected APK/editor validation failure.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
