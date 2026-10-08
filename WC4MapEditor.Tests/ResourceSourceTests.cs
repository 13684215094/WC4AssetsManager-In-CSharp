using System.IO.Compression;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Rendering.Helpers;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Rendering.Skia;

static class ResourceSourceTests
{
    public static void Run(Action<string, Action> test)
    {
        test("unloaded resources stay empty and CLI asset commands require an explicit source every time", () => WithFixture(dir =>
        {
            var manager = AssetManager.Default;
            manager.Clear(); ConfigManager.Instance.Reload();
            Check(!manager.IsLoaded && ConfigManager.Instance.GetTerrainTypes().Count == 0 &&
                ConfigManager.Instance.GetCountryIds().Count == 0, "initialization loaded bundled game data");
            try { manager.GetActiveAssetsRoot(); throw new Exception("missing source accepted"); }
            catch (InvalidOperationException) { }
            string first = Source(dir, "A"), second = Source(dir, "B");
            File.WriteAllText(Path.Combine(first, "assets", "only-A.json"), "[]");
            File.WriteAllText(Path.Combine(second, "assets", "only-B.json"), "[]");
            foreach (string command in new[] { "scan", "summary", "list", "query" })
            {
                Check(Cli("asset", command).Code != 0, $"{command} accepted missing path");
                Check(Cli("asset", command, first).Code == 0, $"{command} rejected A");
                var result = Cli("asset", command, second);
                Check(result.Code == 0 && !result.Output.Contains("only-A"), $"{command} reused A");
                Check(manager.Find("only-B.json") != null && manager.Find("only-A.json") == null, "CLI selected wrong root");
                Check(Cli("asset", command).Code != 0, $"{command} reused active root for an omitted argument");
                Check(Cli("asset", command, Path.Combine(dir, "missing")).Code != 0, "missing directory reported success");
            }
        }));

        test("explicit CLI APK and saved output queries read the selected overlay without copying the source", () => WithFixture(dir =>
        {
            string apk = Path.Combine(dir, "source.apk");
            using (var archive = ZipFile.Open(apk, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("assets/source.json").Open());
                writer.Write("[]");
            }
            byte[] original = File.ReadAllBytes(apk);
            Check(Cli("asset", "query", apk, "--format", "paths").Output.Contains("source.json"), "APK query failed");
            var project = GameProjectWorkspace.Create(apk, Path.Combine(dir, "output"));
            File.WriteAllText(Path.Combine(project.AssetsRoot, "new.json"), "[]");
            var result = Cli("asset", "query", project.OutputRoot, "--format", "paths");
            Check(result.Code == 0 && result.Output.Contains("source.json") && result.Output.Contains("new.json"), "saved output query lost base or overlay");
            Check(File.ReadAllBytes(apk).SequenceEqual(original) && Directory.GetFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Length == 2,
                "query copied or modified the source");
        }));

        test("game lookup caches refresh after saves and project switches without discarding string drafts", () => WithFixture(dir =>
        {
            string first = Source(dir, "A"), second = Source(dir, "B");
            WriteDefinitions(first, 3, "A", 1); WriteDefinitions(second, 8, "B", 2);
            var manager = AssetManager.Default; var config = ConfigManager.Instance;
            manager.Scan(first, true); config.Reload();
            Check(config.GetCountryIds().SetEquals([1]) && config.GetTerrainTypes()[3] == "A", "A definitions missing");
            var strings = config.GetStringTableParser(); strings.SetValue("city_1", "draft");
            AtomicFile.Write(Path.Combine(first, "assets/json/CountrySettings.json"), "[{\"Id\":7}]"u8.ToArray());
            Check(config.GetCountryIds().SetEquals([7]), "saved country IDs remained stale");
            manager.Scan(first, true);
            Check(ReferenceEquals(strings, config.GetStringTableParser()) && strings.IsDirty && strings.GetValue("city_1") == "draft",
                "rescan discarded string draft");
            manager.Scan(second, true);
            Check(config.GetTerrainTypes().Keys.SequenceEqual([8]) && config.GetCountryIds().SetEquals([2]), "B inherited A lookup caches");
        }));
    }

    public static void RunRendering(Action<string, Action> test)
    {
        test("terrain refresh retains pending strings and missing project artwork stays a reusable placeholder", () => WithFixture(dir =>
        {
            string source = Source(dir, "A"); WriteDefinitions(source, 3, "A", 1);
            WriteAtlas(source, "terrain_hd", "tile.png", SKColors.Red);
            AssetManager.Default.Scan(source, true); ConfigManager.Instance.Reload();
            var strings = ConfigManager.Instance.GetStringTableParser(); strings.SetValue("city_1", "draft");
            var helper = new TerrainHelper("MapTerrian");
            try
            {
                using (var original = SKBitmap.FromImage(helper.GetTerrainSkImage(3)!)) Check(original.Pixels.All(p => p == SKColors.Red), "source terrain missing");
                WriteImage(Path.Combine(source, "assets/terrain_hd.png"), SKColors.Blue, atomic: true);
                using (var updated = SKBitmap.FromImage(helper.GetTerrainSkImage(3)!)) Check(updated.Pixels.All(p => p == SKColors.Blue), "saved image stayed red");
                Check(ReferenceEquals(strings, ConfigManager.Instance.GetStringTableParser()) && strings.IsDirty && strings.GetValue("city_1") == "draft",
                    "renderer discarded strings on image save");
                string other = Source(dir, "missing-artwork"); WriteDefinitions(other, 3, "B", 2);
                AssetManager.Default.Scan(other, true);
                var placeholder = helper.GetTerrainSkImage(3)!;
                using var pixels = SKBitmap.FromImage(placeholder);
                Check(ReferenceEquals(placeholder, helper.GetTerrainSkImage(3)) && pixels.Pixels.All(p => p == new SKColor(128, 128, 128)),
                    "missing project image reused A or allocated new placeholders every frame");
                Check(helper.GetTerrainTypeName(3) == "B" && helper.GetTerrainVariantCount(63) == 1, "helper retained old terrain definitions");
            }
            finally { helper.ClearAllCaches(); }
        }));

        test("reused map renderers reload building and unit artwork after a project switch and atlas save", () => WithFixture(dir =>
        {
            string first = Source(dir, "A"), second = Source(dir, "B");
            foreach (var (root, color) in new[] { (first, SKColors.Red), (second, SKColors.Green) })
            {
                WriteAtlas(root, "buildings_hd", "building_1.png", color);
                WriteAtlas(root, "image/image_legion_icon_hd", "legion_icon_1.png", color);
            }
            var map = BTLParser.CreateNew(5, 3);
            var army = Army.CreateDefault(6); army.UnitType = 1; map.Armies.Add(army);
            var building = Building.CreateDefault(8); building.BuildingType = 1; map.Buildings.Add(building);
            var camera = new Camera { MapWidth = 5, MapHeight = 3, OffsetX = 35, OffsetY = 30, ViewportWidth = 220, ViewportHeight = 140 };
            SkiaRenderEngine Engine() => new()
            {
                EnableBackgroundRender = false, EnableTerrainsRender = false, EnableProvinceRender = false,
                EnableProvinceCapitalRender = false, EnableBuildingRender = true, EnableArmyRender = true,
                EnableSelectionRender = false, EnableTrapRender = false, ShowBuildingNames = false, ShowHelp = false, ShowModeName = false
            };
            using var reused = Engine(); using var actual = new SKBitmap(220, 140); using var expected = new SKBitmap(220, 140);
            AssetManager.Default.Scan(first, true); ConfigManager.Instance.Reload();
            using (var canvas = new SKCanvas(actual)) reused.Render(canvas, map, camera);
            Check(actual.Pixels.Contains(SKColors.Red), "selected project's icons were invisible");
            AssetManager.Default.Scan(second, true);
            Compare(SKColors.Green);
            WriteImage(Path.Combine(second, "assets/buildings_hd.png"), SKColors.Blue, atomic: true);
            WriteImage(Path.Combine(second, "assets/image/image_legion_icon_hd.png"), SKColors.Blue, atomic: true);
            Compare(SKColors.Blue);
            void Compare(SKColor color)
            {
                using (var canvas = new SKCanvas(actual)) reused.Render(canvas, map, camera);
                using (var fresh = Engine()) using (var canvas = new SKCanvas(expected)) fresh.Render(canvas, map, camera);
                Check(actual.Pixels.SequenceEqual(expected.Pixels) && actual.Pixels.Contains(color) && !actual.Pixels.Contains(SKColors.Red),
                    "same-map rendering retained old project/atlas pixels");
            }
        }));

        test("sea land coast and mask loaders follow the active project and reject missing sources", () => WithFixture(dir =>
        {
            string first = Source(dir, "A"), second = Source(dir, "B");
            foreach (var (root, color) in new[] { (first, SKColors.Red), (second, SKColors.Green) })
            {
                WriteImage(Path.Combine(root, "assets/map_land@2x.png"), color);
                WriteImage(Path.Combine(root, "assets/map_sea@2x.png"), color);
                WriteAtlas(root, "coast_hd", "coast001.png", color);
                WriteAtlas(root, "coastmask_hd", "coast001.png", color);
            }
            AssetManager.Default.Scan(first, true);
            using var coast = new CoastHelper();
            using var background = new BackGroundRender { EnableCoastRender = false, EnableTerrainsRender = false };
            var map = BTLParser.CreateNew(4, 3);
            using var bitmap = new SKBitmap(180, 140);
            foreach (var (root, color) in new[] { (first, SKColors.Red), (second, SKColors.Green) })
            {
                AssetManager.Default.Scan(root, true);
                using var sea = ProjectTextureLoader.Load("map_sea@2x");
                using var mask = SKBitmap.FromImage(coast.GetGrayLevelCoastImage("coast001.png")!);
                using var tile = SKBitmap.FromImage(coast.GetCoastImageByDoodad(31, 11)!);
                Check(sea != null && sea.Pixels.All(p => p == color) && mask.Pixels.All(p => p == color) && tile.Pixels.All(p => p == color), "wrong project sea/coast/mask");
                using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Black);
                background.RenderFullMap(canvas, map, 35, 40, 1, 180, 140);
                Check(bitmap.Pixels.Contains(color), "land texture did not refresh");
            }
            AssetManager.Default.Scan(Source(dir, "empty"), true);
            Check(ProjectTextureLoader.Load("map_land@2x") == null && !coast.IsAtlasLoaded && !coast.IsGrayLevelAtlasLoaded,
                "missing artwork reused another project");
        }));
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
        test("real 1.30.0 default flag generation uses project borders and publishes exactly four override files", () => WithFixture(dir =>
        {
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            using var flags = new FlagImageService();
            using var original = flags.LoadBigFlag(1)!;
            Check(original != null && original.Width == 52 && original.Height == 54, "real template missing");
            using var canvas = new SKBitmap(150, 150); canvas.Erase(SKColors.Blue);
            var result = new FlagEditorService().MakeFlagWorkflow(canvas, 1, project.AssetsRoot);
            Check(result.Success, result.ErrorMessage ?? "default flag workflow failed");
            Check(Directory.GetFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Length == 5, "unrelated resources copied");
            using var big = flags.LoadBigFlag(1)!; using var hd = flags.LoadFlagFromHdAtlas(1)!; using var small = flags.LoadSmallFlag(1)!;
            Check(big != null && hd != null && small != null && big.GetPixel(26, 25).Blue > 240 && big.GetPixel(26, 25).Red < 15,
                "default template overwrote the new design");
            Check(big!.GetPixel(26, 1) == original!.GetPixel(26, 1) && big.Pixels.SequenceEqual(hd!.Pixels) && small!.Width == 37,
                "project border or synchronized flags lost");
            using var sprites = new ProjectSpriteLoader();
            using var unit = sprites.Load("legion_icon_1.png", "image/image_legion_icon_hd");
            using var building = sprites.Load("building_1.png", "buildings_hd");
            using var facility = sprites.Load("facility_factory.png", "battleres_hd");
            using var sea = ProjectTextureLoader.Load("map_sea@2x");
            using var coast = new CoastHelper();
            Check(unit != null && building != null && facility != null && sea != null && coast.IsAtlasLoaded && coast.IsGrayLevelAtlasLoaded,
                "real project render resources were unavailable after cleanup");
        }));
    }

    private static (int Code, string Output) Cli(params string[] args)
    {
        var stdout = Console.Out; var stderr = Console.Error;
        using var output = new StringWriter();
        try { Console.SetOut(output); Console.SetError(output); return (WC4MapEditor.Cli.Program.Main(args), output.ToString()); }
        finally { Console.SetOut(stdout); Console.SetError(stderr); }
    }

    private static string Source(string directory, string name)
    {
        string path = Path.Combine(directory, name); Directory.CreateDirectory(Path.Combine(path, "assets/config"));
        Directory.CreateDirectory(Path.Combine(path, "assets/json")); return path;
    }

    private static void WriteDefinitions(string root, int terrain, string name, int country)
    {
        File.WriteAllText(Path.Combine(root, "assets/config/def_mapterrain.xml"), $"<map><terrain name=\"{name}\" terrain=\"{terrain}\"><tile idx=\"0\" image=\"tile.png\"/></terrain></map>");
        File.WriteAllText(Path.Combine(root, "assets/json/CountrySettings.json"), $"[{{\"Id\":{country}}}]");
        File.WriteAllText(Path.Combine(root, "assets/stringtable_tw.ini"), "city_1=original\n");
    }

    private static void WriteAtlas(string root, string stem, string tile, SKColor color)
    {
        string path = Path.Combine(root, "assets", stem); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".xml", $"<Texture name=\"{Path.GetFileName(stem)}.png\"/><Images><Image name=\"{tile}\" x=\"0\" y=\"0\" w=\"8\" h=\"8\" refx=\"4\" refy=\"4\"/></Images>");
        WriteImage(path + ".png", color);
    }

    private static void WriteImage(string path, SKColor color, bool atomic = false)
    {
        using var bitmap = new SKBitmap(8, 8); bitmap.Erase(color);
        byte[] bytes = AtlasCompositeRenderer.Encode(bitmap, path);
        if (atomic) AtomicFile.Write(path, bytes); else File.WriteAllBytes(path, bytes);
    }

    private static void WithFixture(Action<string> action)
    {
        var manager = AssetManager.Default; string previous = manager.AssetsRoot; bool loaded = manager.IsLoaded;
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            if (loaded) manager.Scan(previous, true); else manager.Clear();
            ConfigManager.Instance.Reload(); Directory.Delete(directory, true);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
