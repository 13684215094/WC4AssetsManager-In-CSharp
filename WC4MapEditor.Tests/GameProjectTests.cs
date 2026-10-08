using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.HdAtlas;

static class GameProjectTests
{
    public static void Run(Action<string, Action> test)
    {
        test("project copies full layout and saves BTL, world and general data only in output", () => WithDirectory(dir =>
        {
            string source = Path.Combine(dir, "World Conqueror 4_1.30.0");
            string assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "stage", "nested"));
            Directory.CreateDirectory(Path.Combine(assets, "json"));
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            string battlePath = Path.Combine(assets, "stage", "nested", "stage1.btl");
            string worldPath = Path.Combine(assets, "world.bin");
            string generalsPath = Path.Combine(assets, "json", "GeneralSettings.json");
            BTLParser.SaveToFile(BTLParser.CreateNew(7, 9), battlePath);
            WorldParser.SaveToFile(WorldParser.CreateNew(13, 5), worldPath);
            File.WriteAllText(generalsPath, "[{\"Id\":1001,\"Name\":\"source\",\"Unrecognized\":17}]");
            File.WriteAllBytes(Path.Combine(assets, "map1.bin"), [56, 31, 0, 0]);
            File.WriteAllText(Path.Combine(source, "AndroidManifest.xml"), "<manifest />");
            var original = Fingerprints(source);
            string output = Path.Combine(dir, "edited project");
            Directory.CreateDirectory(output);
            var project = GameProjectWorkspace.Create(source, output);
            Check(project.AssetsRelativePath == "assets", "assets prefix lost");
            Check(Directory.Exists(Path.Combine(output, "empty")), "empty directory lost");
            var copied = Fingerprints(output);
            foreach (var (path, hash) in original)
                Check(copied[path] == hash, $"copied bytes changed: {path}");
            Check(project.GetEditablePath(battlePath) == Path.Combine(project.AssetsRoot, "stage", "nested", "stage1.btl"), "nested path flattened");
            var manager = new AssetManager(new AssetCache());
            manager.Scan(project.AssetsRoot);
            Check(manager.Find("world.bin")?.Kind == AssetKind.World, "world was not recognized");
            Check(manager.Find("map1.bin")?.Kind == AssetKind.Binary, "auxiliary BIN treated as world");
            var battle = BTLParser.LoadFromFile(project.GetEditablePath(battlePath));
            battle.Header.MaxTurns++;
            BTLParser.SaveToFile(battle, battle.FilePath);
            Check(BTLParser.LoadFromFile(battle.FilePath).Header.MaxTurns == battle.Header.MaxTurns, "battle save lost edits");
            var world = WorldParser.LoadFromFile(project.GetEditablePath(worldPath));
            var terrain = world.GetTerrain(1);
            terrain.RiverValue = 13;
            world.SetTerrain(1, terrain);
            WorldParser.SaveToFile(world, world.FilePath);
            Check(WorldParser.LoadFromFile(world.FilePath).GetTerrain(1).RiverValue == 13, "world edit lost");
            var general = new GeneralSettingParser(project.AssetsRoot, manager);
            general.All[0].Name = "edited";
            Check(general.SaveGeneralSettings(), general.LastError ?? "general save failed");
            Check(JsonNode.Parse(File.ReadAllText(project.GetEditablePath(generalsPath)))![0]!["Name"]!.GetValue<string>() == "edited", "general edit lost");
            Check(Fingerprints(source).OrderBy(x => x.Key).SequenceEqual(original.OrderBy(x => x.Key)), "original project changed");
            var reopened = GameProjectWorkspace.Open(output);
            Check(reopened.SourceRoot == source && reopened.OutputRoot == output, "manifest rebind failed");
            Check(File.ReadAllText(project.GetEditablePath(generalsPath)).Contains("edited"), "reopen replaced an edit");
            ExpectFailure(() => GameProjectWorkspace.Create(source, output));
            Check(File.ReadAllText(project.GetEditablePath(generalsPath)).Contains("edited"), "create overwrote an existing project");
        }));

        test("project accepts assets itself and rejects overlapping or escaped output paths", () => WithDirectory(dir =>
        {
            string source = Path.Combine(dir, "input", "assets");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "data.json"), "[]");
            string output = Path.Combine(dir, "output");
            var project = GameProjectWorkspace.Create(source + Path.DirectorySeparatorChar, output);
            Check(project.AssetsRoot == output, "assets selection added an extra directory");
            Check(project.GetEditablePath(Path.Combine(source, "data.json")) == Path.Combine(output, "data.json"), "mapping failed");
            ExpectFailure(() => GameProjectWorkspace.Create(source, source));
            ExpectFailure(() => GameProjectWorkspace.Create(source, Path.Combine(source, "nested")));
            ExpectFailure(() => GameProjectWorkspace.Create(source, Path.GetDirectoryName(source)!));
            ExpectFailure(() => project.ValidateOutputPath(Path.Combine(output, "..", "outside.json")));
            ExpectFailure(() => project.ValidateOutputPath(Path.Combine(dir, "output-extra", "data.json")));
            ExpectFailure(() => project.GetEditablePath(Path.Combine(dir, "input", "other.json")));
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(output, GameProjectWorkspace.ManifestFileName)))!;
            manifest["AssetsRelativePath"] = "../input/assets";
            File.WriteAllText(Path.Combine(output, GameProjectWorkspace.ManifestFileName), manifest.ToJsonString());
            ExpectFailure(() => GameProjectWorkspace.Open(output));
        }));

        test("project external-world BTL loads wrapped capture and refuses silently lost terrain edits", () => WithDirectory(dir =>
        {
            string source = Path.Combine(dir, "source");
            string assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "stage"));
            var world = WorldParser.CreateNew(13, 9);
            for (int i = 0; i < world.TerrainCount; i++) world.SetTerrain(i, new TerrainData { TileType1 = (byte)(i % 31), Reserved3 = (byte)i });
            WorldParser.SaveToFile(world, Path.Combine(assets, "world.bin"));
            var battle = BTLParser.CreateNew(5, 3, mapNumber: 1);
            battle.Header.MapClipX = 11;
            battle.Header.MapClipY = 2;
            string input = Path.Combine(assets, "stage", "frontier1.btl");
            BTLParser.SaveToFile(battle, input);
            byte[] original = File.ReadAllBytes(input);
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            var doc = ProjectMapDocument.Load(project, input);
            Check(doc.Map.GetTerrain(2, 1).Reserved3 == world.GetTerrain(0, 3).Reserved3, "world capture did not wrap");
            doc.Save(doc.Map.FilePath);
            Check(File.ReadAllBytes(doc.Map.FilePath).SequenceEqual(original), "world view changed unchanged BTL");
            doc.Map.Header.MaxTurns++;
            doc.Save(doc.Map.FilePath);
            Check(BTLParser.LoadFromFile(doc.Map.FilePath).Header.MaxTurns == doc.Map.Header.MaxTurns, "object data save failed");
            byte[] saved = File.ReadAllBytes(doc.Map.FilePath);
            ExpectFailure(() => doc.Save(Path.Combine(project.AssetsRoot, "wrong.bin")));
            Check(!File.Exists(Path.Combine(project.AssetsRoot, "wrong.bin")), "BTL was saved as world");
            doc.Map.SetTerrain(0, new TerrainData { TileType1 = 17 });
            ExpectFailure(() => doc.Save(doc.Map.FilePath));
            Check(File.ReadAllBytes(doc.Map.FilePath).SequenceEqual(saved), "failed linked save damaged BTL");
            doc = ProjectMapDocument.Load(project, input);
            doc.Map.Header.MapClipX++;
            ExpectFailure(() => doc.Save(doc.Map.FilePath));
            Check(File.ReadAllBytes(input).SequenceEqual(original), "source BTL changed");
            var standalone = BTLParser.CreateNew(5, 3);
            var standaloneDoc = ProjectMapDocument.Attach(project, standalone);
            standalone.Header.MapNumber = 1;
            ExpectFailure(standaloneDoc.ValidateChanges);
            var newWorld = ProjectMapDocument.Attach(project, WorldParser.CreateNew(5, 3));
            string newWorldPath = Path.Combine(project.AssetsRoot, "new.bin");
            newWorld.Save(newWorldPath);
            Check(newWorld.Map.FilePath == newWorldPath, "new map's first save path lost");
        }));

        test("terrain definition preserves game variant indices and atlas cache follows resource switches", () => WithDirectory(dir =>
        {
            var entries = DefMapTerrainParser.Parse("<map><terrain terrain=\"3\" type=\"1\"><tile idx=\"0\" image=\"a.png\"/><tile idx=\"1\" image=\"b.png\"/></terrain></map>");
            Check(entries.Single().TileImages[1] == "b.png" && entries.Single().TileCount == 2, "variant mapping lost");
            var manager = AssetManager.Default;
            string previousRoot = manager.AssetsRoot;
            try
            {
                foreach (int x in new[] { 7, 23 })
                {
                    string root = Path.Combine(dir, x.ToString(), "assets");
                    Directory.CreateDirectory(root);
                    File.WriteAllText(Path.Combine(root, "terrain_hd.xml"), $"<Texture name=\"terrain_hd.png\"/><Images><Image name=\"a.png\" x=\"{x}\" y=\"0\" w=\"5\" h=\"7\"/></Images>");
                    manager.Scan(root, forceReload: true);
                    Check(HdAtlasParser.Get("terrain_hd").GetImageDef("a.png")?.X == x, "old project's atlas retained");
                }
            }
            finally
            {
                if (Directory.Exists(previousRoot)) manager.Scan(previousRoot, forceReload: true); else manager.Clear();
            }
        }));

        test("project CLI creates and reopens a copy, rejecting overwrite without altering edits", () => WithDirectory(dir =>
        {
            string source = Path.Combine(dir, "source");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            File.WriteAllText(Path.Combine(source, "assets", "data.json"), "[]");
            string output = Path.Combine(dir, "output");
            var oldOut = Console.Out;
            var oldError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Check(WC4MapEditor.Cli.Program.Main(["project", "create", source, output]) == 0, stderr.ToString());
                File.WriteAllText(Path.Combine(output, "assets", "data.json"), "[1]");
                Check(WC4MapEditor.Cli.Program.Main(["project", "info", output]) == 0, stderr.ToString());
                Check(WC4MapEditor.Cli.Program.Main(["project", "create", source, output]) == 2, "CLI accepted overwrite");
                Check(File.ReadAllText(Path.Combine(output, "assets", "data.json")) == "[1]", "CLI overwrote edits");
                Check(File.ReadAllText(Path.Combine(source, "assets", "data.json")) == "[]", "CLI changed source");
            }
            finally { Console.SetOut(oldOut); Console.SetError(oldError); }
        }));

        test("cancelled project creation leaves destination empty and removes staging copy", () => WithDirectory(dir =>
        {
            string source = Path.Combine(dir, "source");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            File.WriteAllBytes(Path.Combine(source, "assets", "file.bin"), new byte[1000]);
            string output = Path.Combine(dir, "output");
            Directory.CreateDirectory(output);
            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress(value => { if (value.CompletedFiles > 0) cancellation.Cancel(); });
            ExpectFailure(() => GameProjectWorkspace.Create(source, output, progress, cancellation.Token));
            Check(!Directory.EnumerateFileSystemEntries(output).Any(), "cancelled output contains a partial project");
            Check(!Directory.EnumerateDirectories(dir, ".wc4-copy-*").Any(), "staging copy was retained");
            Check(File.Exists(Path.Combine(source, "assets", "file.bin")), "source was deleted");
        }));

        test("project rejects directory links in source and output", () => WithDirectory(dir =>
        {
            if (OperatingSystem.IsWindows()) return;
            string source = Path.Combine(dir, "source"), output = Path.Combine(dir, "output");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            string outside = Path.Combine(dir, "outside");
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(source, "assets", "linked"), outside);
            ExpectFailure(() => GameProjectWorkspace.Create(source, output));
            Directory.Delete(Path.Combine(source, "assets", "linked"));
            var project = GameProjectWorkspace.Create(source, output);
            Directory.CreateSymbolicLink(Path.Combine(output, "assets", "linked"), outside);
            ExpectFailure(() => project.ValidateOutputPath(Path.Combine(output, "assets", "linked", "data.json")));
            ExpectFailure(() => GameProjectWorkspace.Open(output));
            Check(!Directory.EnumerateFileSystemEntries(outside).Any(), "linked directory was changed");
        }));
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
        if (!Directory.Exists(source)) return;
        test("WC4 1.30.0 full project copy preserves all input files and editable map roundtrip", () => WithDirectory(dir =>
        {
            var before = Fingerprints(source);
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "wc4-edited"));
            var copied = Fingerprints(project.OutputRoot);
            foreach (var (path, hash) in before) Check(copied[path] == hash, $"real project copy changed {path}");
            var assets = new AssetManager(new AssetCache());
            assets.Scan(project.AssetsRoot);
            var battles = assets.ListByExtension("btl");
            Check(battles.Count == 1383, $"1.30.0 BTL count: {battles.Count}");
            Check(assets.Find("world.bin")?.Kind == AssetKind.World, "real world not classified");
            int externalCount = 0;
            foreach (var file in battles)
            {
                var doc = ProjectMapDocument.Load(project, file.FullPath);
                if (doc.ExternalWorldPath != null) externalCount++;
                byte[] original = File.ReadAllBytes(file.FullPath);
                doc.Save(file.FullPath);
                Check(File.ReadAllBytes(file.FullPath).SequenceEqual(original), $"project map roundtrip changed {file.RelativePath}");
            }
            var stage = battles.First(file => BTLParser.LoadFromFile(file.FullPath).Header.MapNumber == 0);
            var map = BTLParser.LoadFromFile(stage.FullPath);
            byte reserved = (byte)(map.GetTerrain(0).Reserved3 ^ 0x80);
            var terrain = map.GetTerrain(0);
            terrain.Reserved3 = reserved;
            map.SetTerrain(0, terrain);
            BTLParser.SaveToFile(map, stage.FullPath);
            Check(BTLParser.LoadFromFile(stage.FullPath).GetTerrain(0).Reserved3 == reserved, "real stage terrain edit lost");
            var worldEntry = assets.Find("world.bin")!;
            var world = WorldParser.LoadFromFile(worldEntry.FullPath);
            terrain = world.GetTerrain(0);
            terrain.Reserved3 ^= 0x40;
            world.SetTerrain(0, terrain);
            WorldParser.SaveToFile(world, worldEntry.FullPath);
            Check(WorldParser.LoadFromFile(worldEntry.FullPath).GetTerrain(0).Reserved3 == terrain.Reserved3, "real world edit lost");
            var after = Fingerprints(source);
            Check(before.OrderBy(x => x.Key).SequenceEqual(after.OrderBy(x => x.Key)), "real source project changed");
            Check(GameProjectWorkspace.Open(project.OutputRoot).AssetsRoot == project.AssetsRoot, "real project reopen failed");
            Console.WriteLine($"1.30.0 project: {before.Count} files copied; {battles.Count} BTL maps ({externalCount} external world); source hashes unchanged.");
        }));
    }

    private static Dictionary<string, string> Fingerprints(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(root, file),
            file => { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); });

    private sealed class InlineProgress(Action<ProjectCopyProgress> action) : IProgress<ProjectCopyProgress>
    {
        public void Report(ProjectCopyProgress value) => action(value);
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException) { return; }
        throw new Exception("Expected project validation failure.");
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
