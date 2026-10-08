using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Rendering.Helpers;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Core.Parsers;

static class ProjectRenderingTests
{
    public static void Run(Action<string, Action> test)
    {
        test("reused render engine matches a fresh engine after switching maps and replacing the camera", () =>
        {
            var first = BTLParser.CreateNew(7, 3);
            first.Traps.Add(Trap.CreateDefault(0));
            var second = BTLParser.CreateNew(4, 2);
            second.Traps.Add(Trap.CreateDefault(6));
            var building = Building.CreateDefault(0);
            building.Name = 1;
            second.Buildings.Add(building);
            var firstCamera = new Camera { MapWidth = 7, MapHeight = 3, ViewportWidth = 256, ViewportHeight = 180, OffsetX = 45, OffsetY = 60 };
            var secondCamera = new Camera { MapWidth = 4, MapHeight = 2, ViewportWidth = 256, ViewportHeight = 180, OffsetX = 35, OffsetY = 55 };
            SkiaRenderEngine Create() => new()
            {
                EnableBackgroundRender = false, EnableTerrainsRender = false, EnableProvinceRender = false,
                EnableProvinceCapitalRender = false, EnableArmyRender = false, EnableBuildingRender = true,
                ShowBuildingNames = false,
                EnableSelectionRender = false, EnableTrapRender = true, ShowHelp = false, ShowModeName = false
            };
            using var reused = Create();
            Check(!reused.ShowBuildingNames, "building names switch was lost before renderer creation");
            using var actual = new SKBitmap(256, 180);
            using var expected = new SKBitmap(256, 180);
            using (var canvas = new SKCanvas(actual)) reused.Render(canvas, first, firstCamera);
            SKColor[] firstPixels = actual.Pixels;
            using (var canvas = new SKCanvas(actual)) reused.Render(canvas, second, secondCamera);
            using (var fresh = Create())
            using (var canvas = new SKCanvas(expected)) fresh.Render(canvas, second, secondCamera);
            Check(actual.Pixels.SequenceEqual(expected.Pixels) && !firstPixels.SequenceEqual(actual.Pixels),
                "reused entity renderers still display A's traps or camera");
            var movedCamera = new Camera { MapWidth = 4, MapHeight = 2, ViewportWidth = 256, ViewportHeight = 180, OffsetX = 85, OffsetY = 55 };
            using (var canvas = new SKCanvas(actual)) reused.Render(canvas, second, movedCamera);
            using (var fresh = Create())
            using (var canvas = new SKCanvas(expected)) fresh.Render(canvas, second, movedCamera);
            Check(actual.Pixels.SequenceEqual(expected.Pixels) && !reused.ShowBuildingNames,
                "camera replacement did not rebind entity renderers or retain display settings");
            second.Traps.Clear();
            second.Buildings.Clear();
            using (var canvas = new SKCanvas(actual)) reused.Render(canvas, second, movedCamera);
            Check(actual.Pixels.All(pixel => pixel == SKColors.Black), "removing B's last trap displayed A's object");
        });

        test("one terrain helper refreshes an incremental image override while retaining source XML", () =>
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
            string source = Path.Combine(directory, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "config"));
            string previous = AssetManager.Default.AssetsRoot;
            TerrainHelper? helper = null;
            try
            {
                File.WriteAllText(Path.Combine(assets, "config/def_mapterrain.xml"), "<map><terrain terrain=\"3\"><tile idx=\"0\" image=\"tile.png\"/></terrain></map>");
                File.WriteAllText(Path.Combine(assets, "terrain_hd.xml"), "<Texture name=\"terrain_hd.png\"/><Images><Image name=\"tile.png\" x=\"1\" y=\"1\" w=\"2\" h=\"2\"/></Images>");
                using var image = new SKBitmap(4, 4);
                image.Erase(SKColors.Red);
                File.WriteAllBytes(Path.Combine(assets, "terrain_hd.png"), AtlasCompositeRenderer.Encode(image, "image.png"));
                var project = GameProjectWorkspace.Create(source, Path.Combine(directory, "output"));
                AssetManager.Default.Scan(project.AssetsRoot, true);
                ConfigManager.Instance.Reload();
                helper = new TerrainHelper("MapTerrian");
                using (var red = SKBitmap.FromImage(helper.GetTerrainSkImage(3)!)) Check(red.Pixels.All(pixel => pixel == SKColors.Red), "source atlas not read");
                image.Erase(SKColors.Blue);
                AtomicFile.Write(Path.Combine(project.AssetsRoot, "terrain_hd.png"), AtlasCompositeRenderer.Encode(image, "image.png"));
                using var blue = SKBitmap.FromImage(helper.GetTerrainSkImage(3)!);
                Check(blue.Pixels.All(pixel => pixel == SKColors.Blue) && !File.Exists(Path.Combine(project.AssetsRoot, "terrain_hd.xml")), "helper stale or XML unnecessarily copied");
            }
            finally
            {
                helper?.ClearAllCaches();
                if (Directory.Exists(previous)) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear();
                ConfigManager.Instance.Reload();
                Directory.Delete(directory, true);
            }
        });

        test("project terrain pixels follow the active project's atlas", () =>
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var manager = AssetManager.Default;
            string previous = manager.AssetsRoot;
            try
            {
                foreach (var color in new[] { SKColors.Red, SKColors.Green })
                {
                    string root = Path.Combine(directory, color.ToString(), "assets");
                    Directory.CreateDirectory(Path.Combine(root, "config"));
                    File.WriteAllText(Path.Combine(root, "config", "def_mapterrain.xml"),
                        "<map><terrain terrain=\"3\"><tile idx=\"0\" image=\"tile.png\"/></terrain></map>");
                    File.WriteAllText(Path.Combine(root, "terrain_hd.xml"),
                        "<Texture name=\"terrain_hd.png\"/><Images><Image name=\"tile.png\" x=\"1\" y=\"2\" w=\"3\" h=\"4\"/></Images>");
                    using var surface = new SKBitmap(6, 8);
                    surface.Erase(color);
                    using var encoded = surface.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(root, "terrain_hd.png"), encoded.ToArray());
                    manager.Scan(root, forceReload: true);
                    ConfigManager.Instance.Reload();
                    var helper = new TerrainHelper("MapTerrian");
                    try
                    {
                        var image = helper.GetTerrainSkImage(3)!;
                        using var bitmap = SKBitmap.FromImage(image);
                        Check(bitmap.Width == 3 && bitmap.Height == 4 && bitmap.Pixels.All(pixel => pixel == color),
                            "terrain rendered a placeholder or an earlier project's pixels");
                    }
                    finally { helper.ClearAllCaches(); }
                }
            }
            finally
            {
                if (Directory.Exists(previous)) manager.Scan(previous, forceReload: true); else manager.Clear();
                ConfigManager.Instance.Reload();
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        string assets = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0", "assets");
        if (!Directory.Exists(assets)) return;
        test("WC4 1.30.0 terrain variants decode and match original atlas pixels", () =>
        {
            var manager = AssetManager.Default;
            string previous = manager.AssetsRoot;
            var surfaces = new Dictionary<string, SKBitmap>();
            TerrainHelper? helper = null;
            try
            {
                manager.Scan(assets, forceReload: true);
                ConfigManager.Instance.Reload();
                helper = new TerrainHelper("MapTerrian");
                int count = 0;
                foreach (var terrain in ConfigManager.Instance.GetMapTerrainEntries())
                {
                    Check(helper.GetTerrainVariantCount(terrain.Terrain) == terrain.TileCount, "game variant count changed");
                    foreach (var (index, name) in terrain.TileImages)
                    {
                        string atlas = new[] { "terrain_hd", "plant_hd", "buildings_hd" }
                            .First(value => HdAtlasParser.Get(value).HasImage(name));
                        var parser = HdAtlasParser.Get(atlas);
                        var def = parser.GetImageDef(name)!;
                        if (!surfaces.TryGetValue(atlas, out var source))
                            surfaces[atlas] = source = SKBitmap.Decode(parser.SurfaceData);
                        using var expected = new SKBitmap();
                        Check(source.ExtractSubset(expected, new SKRectI(def.X, def.Y, def.X + def.Width, def.Y + def.Height)),
                            $"invalid crop: {name}");
                        using var actual = SKBitmap.FromImage(helper.GetTerrainSkImage(terrain.Terrain, index)!);
                        Check(actual.Width == expected.Width && actual.Height == expected.Height &&
                            actual.Pixels.SequenceEqual(expected.Pixels), $"terrain pixels differ: {name}");
                        count++;
                    }
                }
                Check(count == 190, $"expected 190 terrain variants, got {count}");
                Console.WriteLine($"1.30.0 rendering: {count} variants match terrain_hd, plant_hd and buildings_hd pixels.");
            }
            finally
            {
                helper?.ClearAllCaches();
                foreach (var surface in surfaces.Values) surface.Dispose();
                if (Directory.Exists(previous)) manager.Scan(previous, forceReload: true); else manager.Clear();
                ConfigManager.Instance.Reload();
            }
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
