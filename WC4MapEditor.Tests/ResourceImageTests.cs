using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Views.Assist;

static class ResourceImageTests
{
    public static void Run(Action<string, Action> test)
    {
        test("existing capital belong and trap flag renderers refresh pixels after atlas save and retry cached misses", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(assets);
            string xml = """<Texture name="tacticalmap.png"/><Images><Image name="flag_1.png" x="0" y="0" w="2" h="2"/></Images>""";
            File.WriteAllText(Path.Combine(assets, "tacticalmap.xml"), xml);
            WriteImage(Path.Combine(assets, "tacticalmap.png"), SKColors.Red);
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true); ConfigManager.Instance.Reload();
            var map = BTLParser.CreateNew(7, 3, numLegions: 1);
            var legion = map.Legions[0]; legion.CountryId = 1; legion.ActionId = 0; map.Legions[0] = legion;
            map.Capitals.Add(new Capital { Coordinate = 0 }); map.SetBelongValue(0, 0, 0);
            map.Traps.Add(Trap.CreateDefault(0));
            using var capital = new CapitalFlagRender { EnableCapitalFlagRender = true };
            using var belong = new BelongFlagRender { EnableBelongFlagRender = true };
            var camera = new Camera { MapWidth = 7, MapHeight = 3, ViewportWidth = 128, ViewportHeight = 128, OffsetX = 50, OffsetY = 50 };
            using var traps = new TrapRender(camera, map);
            using var image = new SKBitmap(128, 128);
            void CheckFlag(Action<SKCanvas> render, SKColor expected, int x, int y)
            {
                using var canvas = new SKCanvas(image); canvas.Clear(SKColors.Transparent); render(canvas);
                Check(image.GetPixel(x, y) == expected, $"flag renderer expected {expected} but got {image.GetPixel(x, y)}");
            }
            CheckFlag(canvas => capital.Render(canvas, map), SKColors.Red, 50, 50);
            CheckFlag(canvas => belong.Render(canvas, map), SKColors.Red, 50, 67);
            CheckFlag(traps.Render, SKColors.Red, 50, 36);
            using var blue = new SKBitmap(4, 4); blue.Erase(SKColors.Blue);
            AtomicFile.Write(Path.Combine(project.AssetsRoot, "tacticalmap.png"), AtlasCompositeRenderer.Encode(blue, "changed.png"));
            CheckFlag(canvas => capital.Render(canvas, map), SKColors.Blue, 50, 50);
            CheckFlag(canvas => belong.Render(canvas, map), SKColors.Blue, 50, 67);
            CheckFlag(traps.Render, SKColors.Blue, 50, 36);
            legion.CountryId = 2; map.Legions[0] = legion;
            using (var canvas = new SKCanvas(image)) capital.Render(canvas, map);
            AtomicFile.Write(Path.Combine(project.AssetsRoot, "tacticalmap.xml"), System.Text.Encoding.UTF8.GetBytes(
                xml.Replace("</Images>", "<Image name=\"flag_2.png\" x=\"2\" y=\"0\" w=\"2\" h=\"2\"/></Images>")));
            CheckFlag(canvas => capital.Render(canvas, map), SKColors.Blue, 50, 50);
            CheckFlag(canvas => belong.Render(canvas, map), SKColors.Blue, 50, 67);
            CheckFlag(traps.Render, SKColors.Blue, 50, 36);
        }));

        test("WPF pixel conversion handles RGBA BGRA premultiplied unpremultiplied and opaque data", () =>
        {
            foreach (var color in new[] { SKColorType.Rgba8888, SKColorType.Bgra8888 })
            foreach (var alpha in new[] { SKAlphaType.Premul, SKAlphaType.Unpremul, SKAlphaType.Opaque })
            {
                using var source = new SKBitmap(new SKImageInfo(3, 2, color, alpha));
                source.Erase(new SKColor(200, 100, 40, alpha == SKAlphaType.Opaque ? (byte)255 : (byte)128));
                using var result = BitmapPixels.ToBgraPremultiplied(source);
                byte[] bytes = result.GetPixelSpan().ToArray();
                int opacity = alpha == SKAlphaType.Opaque ? 255 : 128;
                Check(Math.Abs(bytes[0] - 40 * opacity / 255) <= 1 && Math.Abs(bytes[1] - 100 * opacity / 255) <= 1 &&
                    Math.Abs(bytes[2] - 200 * opacity / 255) <= 1 && bytes[3] == opacity, $"incorrect WPF channels/alpha for {color}/{alpha}");
                Check(result.ColorType == SKColorType.Bgra8888 && result.AlphaType == SKAlphaType.Premul, "output format undefined");
            }
            using var rgb565 = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Rgb565, SKAlphaType.Opaque));
            rgb565.Erase(SKColors.Red);
            using var converted = BitmapPixels.ToBgraPremultiplied(rgb565);
            Check(converted.GetPixel(0, 0) == SKColors.Red, "16-bit source read as 4 bytes per pixel");
            Reject(() => { using var _ = BitmapPixels.Crop(converted, -1, 0, 1, 1); });
            Reject(() => { using var _ = BitmapPixels.Crop(converted, 0, 0, 2, 1); });
        });

        test("nested atlas textures keep relative paths and resolve WebP without crossing directories", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "source/assets");
            Directory.CreateDirectory(Path.Combine(assets, "image/pages"));
            File.WriteAllText(Path.Combine(assets, "image/image_flags_hd.xml"),
                """<?xml version="1.0" encoding="UTF-8"?><Texture name='pages/flags.png'/><Images><Image name='flag_1.png' x='0' y='0' w='2' h='2'/></Images>""");
            WriteImage(Path.Combine(assets, "image/pages/flags.webp"), SKColors.Red);
            WriteImage(Path.Combine(assets, "image/pages/flags.png"), SKColors.Green);
            WriteImage(Path.Combine(assets, "flags.webp"), SKColors.Blue);
            var project = GameProjectWorkspace.Create(Path.Combine(dir, "source"), Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            var parser = HdAtlasParser.Get("image_flags_hd");
            Check(parser.IsLoaded && parser.GetImageDef("flag_1.png") != null && parser.ImagePath!.Replace('\\', '/').EndsWith("image/pages/flags.webp"), parser.LastError ?? "wrong nested atlas image");
            using var image = ProjectImageDecoder.Decode(parser.SurfaceData!);
            Check(image.GetPixel(0, 0).Red > 250, "sibling or PNG image shadowed selected WebP");
            var index = new AtlasResourceIndex(AssetManager.Default);
            Check(index.Resolve("image/flag_1.png")?.Atlas == "image/image_flags_hd", "nested atlas or prefix stripped incorrectly");
            var layout = LayoutImageProvider.Resolve("image/flag_1.png");
            Check(layout?.Ready == true && layout.Page!.GetPixel(0, 0).Red > 250, "production layout loader omitted nested image atlas");
            Directory.CreateDirectory(Path.Combine(assets, "image/generalphoto"));
            WriteImage(Path.Combine(assets, "image/generalphoto/general_Test.webp"), SKColors.Green);
            AssetManager.Default.Scan(project.AssetsRoot, true);
            layout = LayoutImageProvider.Resolve("image/generalphoto/general_Test.png");
            Check(layout?.Ready == true && layout.Page!.GetPixel(0, 0).Green > 100, "layout stripped loose image subdirectory or prefix");
            using var changed = new SKBitmap(4, 4); changed.Erase(SKColors.Blue);
            AtomicFile.Write(Path.Combine(project.AssetsRoot, "image/pages/flags.webp"), AtlasCompositeRenderer.Encode(changed, "changed.webp"));
            layout = LayoutImageProvider.Resolve("image/flag_1.png");
            Check(layout?.Ready == true && layout.Page!.GetPixel(0, 0).Blue > 250, "production layout loader did not refresh saved atlas");
            File.Delete(Path.Combine(project.AssetsRoot, "image/pages/flags.webp"));
            File.Delete(Path.Combine(assets, "image/pages/flags.webp")); File.Delete(Path.Combine(assets, "image/pages/flags.png"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            parser = HdAtlasParser.Get("image/image_flags_hd");
            Check(!parser.IsLoaded && parser.LastError != null && parser.GetImageDef("flag_1.png") != null, "missing page loaded unrelated image or lost valid definitions");
            AtomicFile.Write(Path.Combine(project.AssetsRoot, "image/image_flags_hd.xml"),
                System.Text.Encoding.UTF8.GetBytes("""<Texture name="../../outside.png"/><Images/>"""));
            Check(!HdAtlasParser.Get("image/image_flags_hd").IsLoaded, "texture escaped project");
        }));

        test("layout imported atlases retain texture paths XML declaration and PKM previews", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(assets);
            AssetManager.Default.Scan(assets, true);
            string imports = Path.Combine(dir, "imports"); Directory.CreateDirectory(Path.Combine(imports, "pages"));
            File.WriteAllBytes(Path.Combine(imports, "pages/pkm.pkm"), Pkm(4, 4, 0xFF000000, 0));
            string xml = Path.Combine(imports, "import.xml");
            File.WriteAllText(xml, """<?xml version="1.0"?><Texture name='pages/pkm.png'/><Images><Image name='imported.png' x='0' y='0' w='2' h='2'/></Images>""");
            LayoutImageProvider.IngestFiles([xml]);
            var record = LayoutImageProvider.Resolve("imported.png");
            Check(record?.Ready == true && record.Page!.GetPixel(0, 0).Red == 255 && LayoutImageProvider.ExtraAtlasCount == 1,
                "imported nested PKM or XML declaration rejected");
        }));

        test("country flags and tactical heads refresh the same loader after partial image or XML overrides", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "image"));
            string xml = """<Texture name="tacticalmap.png"/><Images><Image name="flag_1.png" x="0" y="0" w="2" h="2"/><Image name="head_Test.png" x="0" y="0" w="2" h="2"/></Images>""";
            File.WriteAllText(Path.Combine(assets, "tacticalmap.xml"), xml);
            File.WriteAllText(Path.Combine(assets, "image/image_flags_hd.xml"), xml.Replace("tacticalmap.png", "image_flags_hd.png"));
            WriteImage(Path.Combine(assets, "tacticalmap.webp"), SKColors.Red);
            WriteImage(Path.Combine(assets, "image/image_flags_hd.webp"), SKColors.Red);
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            var tactical = new TacticalMapParser(); tactical.EnsureLoadedFromAssets();
            using var service = new FlagImageService();
            using (var flag = service.LoadFlagFromHdAtlas(1)) Check(flag?.GetPixel(0, 0).Red > 250, service.LastError ?? "flag source not read");
            using (var flag = service.LoadBigFlag(1)) Check(flag?.GetPixel(0, 0).Red > 250, "tactical flag not read");
            Check(service.LoadFlagFromHdAtlas(2) == null && service.LastError != null, "missing flag not reported");
            TacticalMapImageCache.Instance.Initialize();
            using (var bitmap = SKBitmap.FromImage(TacticalMapImageCache.Instance.GetImage("head_Test.png")!))
                Check(bitmap.GetPixel(0, 0).Red > 250, "tactical head not read");
            using var blue = new SKBitmap(4, 4); blue.Erase(SKColors.Blue);
            AtomicFile.WriteAll((Path.Combine(project.AssetsRoot, "tacticalmap.webp"), AtlasCompositeRenderer.Encode(blue, "new.webp")),
                (Path.Combine(project.AssetsRoot, "image/image_flags_hd.webp"), AtlasCompositeRenderer.Encode(blue, "new.webp")));
            using (var flag = service.LoadFlagFromHdAtlas(1)) Check(flag?.GetPixel(0, 0).Blue > 250, "HD flag stale after image override");
            using (var flag = service.LoadBigFlag(1)) Check(flag?.GetPixel(0, 0).Blue > 250, "tactical flag stale after image override");
            using (var bitmap = SKBitmap.FromImage(TacticalMapImageCache.Instance.GetImage("head_Test.png")!))
                Check(bitmap.GetPixel(0, 0).Blue > 250, "tactical cache stale after save");
            string hdXml = Path.Combine(project.AssetsRoot, "image/image_flags_hd.xml");
            AtomicFile.Write(hdXml, System.Text.Encoding.UTF8.GetBytes(xml.Replace("tacticalmap.png", "image_flags_hd.png")
                .Replace("</Images>", "<Image name=\"flag_2.png\" x=\"2\" y=\"0\" w=\"2\" h=\"2\"/></Images>")));
            using (var flag = service.LoadFlagFromHdAtlas(2)) Check(flag?.GetPixel(0, 0).Blue > 250, "cached flag miss persisted after XML save");
            Check(tactical.GetImageDef("head_Test.png") != null, "tactical instance failed to refresh");
            var second = new TacticalMapParser(); second.EnsureLoadedFromAssets();
            tactical.Dispose();
            Check(second.GetImageDef("head_Test.png") != null, "disposing one parser cleared another's definitions");
            // External refresh without a revision change must update every existing instance.
            File.WriteAllText(Path.Combine(assets, "tacticalmap.xml"), xml.Replace("head_Test.png", "head_Refreshed.png"));
            HdAtlasParser.ClearCache(); TacticalMapParser.ClearCache();
            tactical.EnsureLoadedFromAssets();
            Check(second.GetImageDef("head_Refreshed.png") != null && second.GetImageDef("head_Test.png") == null,
                "cache clear left another instance's old definitions");
            Check(!File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.xml")), "image override copied unchanged XML");
        }));

        test("ETC1 selectors differential mode flip padding and invalid PKM inputs", () =>
        {
            using var individual = PkmDecoder.Decode(Pkm(4, 4, 0xFF000000, 0));
            Check(individual.GetPixel(0, 0) == new SKColor(255, 2, 2), "individual-mode red decoded incorrectly");
            using var differential = PkmDecoder.Decode(Pkm(4, 4, 0x80000002, 0));
            Check(differential.GetPixel(0, 0) == new SKColor(134, 2, 2), "differential-mode base expansion incorrect");
            using var selectors = PkmDecoder.Decode(Pkm(4, 4, 0x88888800, (1u << 16) | (1u << 4)));
            Check(selectors.GetPixel(0, 0).Red == 134 && selectors.GetPixel(1, 0).Red == 144 && selectors.GetPixel(0, 1).Red == 138, "selector order/sign incorrect");
            using var flip = PkmDecoder.Decode(Pkm(3, 3, 0xF00F0001, 0));
            Check(flip.Width == 3 && flip.Height == 3 && flip.GetPixel(2, 0).Red == 255 && flip.GetPixel(0, 2).Green == 255, "flip/cropped dimensions incorrect");
            byte[] bytes = Pkm(4, 4, 0, 0);
            byte[] reference = Convert.FromHexString("b5a594ff2a1a09ffad8ca5ff22011affb5a594ff2a1a09ff22011affad8ca5ff736352fffeeeddfff6d5eefff6d5eefffeeeddffb5a594ffad8ca5ffad8ca5ff");
            using var referenceBlock = PkmDecoder.Decode(Pkm(4, 4, 0x728597DA, 0x12345678));
            Check(referenceBlock.GetPixelSpan().SequenceEqual(reference), "ETC1 block differs from Texture2DDecoder 1.0.6");
            Reject(() => { using var _ = PkmDecoder.Decode(bytes[..^1]); });
            Reject(() => { using var _ = PkmDecoder.Decode(bytes.Concat(new byte[1]).ToArray()); });
            bytes[7] = 1;
            Reject(() => { using var _ = PkmDecoder.Decode(bytes); });
            Reject(() => { using var _ = PkmDecoder.Decode(Pkm(4, 4, 0x04000002, 0)); });
        });
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        test("1.30.0 all raster images decode and all referenced portraits flags and atlas tiles fit", () => WithFixture(dir =>
        {
            string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true);
            int rasters = 0, pkm = 0, atlasCount = 0, tiles = 0;
            var externalHashes = File.Exists("tmp/etc1-reference-hashes.json")
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("tmp/etc1-reference-hashes.json")) : null;
            foreach (var entry in AssetManager.Default.ListAll().Where(entry => new[] { "png", "webp", "jpg", "jpeg", "pkm" }.Contains(entry.Extension)))
            {
                using var bitmap = ProjectImageDecoder.Decode(ProjectFiles.ReadAllBytes(entry.FullPath));
                using var pixels = BitmapPixels.ToBgraPremultiplied(bitmap);
                Check(pixels.Width > 0 && pixels.Height > 0, $"failed image {entry.RelativePath}");
                rasters++;
                if (entry.Extension == "pkm")
                {
                    pkm++;
                    if (externalHashes != null)
                    {
                        string hash = Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan())).ToLowerInvariant();
                        Check(externalHashes[entry.RelativePath.Replace('\\', '/')] == hash, $"independent ETC1 pixels differ: {entry.RelativePath}");
                    }
                }
            }
            var index = new AtlasResourceIndex(AssetManager.Default);
            foreach (var atlas in index.Atlases)
            {
                var parser = HdAtlasParser.Get(atlas);
                using var page = ProjectImageDecoder.Decode(parser.SurfaceData!);
                foreach (var tile in parser.ImageDefinitions)
                {
                    using var cropped = BitmapPixels.Crop(page, tile.X, tile.Y, tile.Width, tile.Height);
                    tiles++;
                }
                atlasCount++;
            }
            Check(pkm == 417 && atlasCount > 20 && index.Resolve("image/flag_1.png") != null && index.Resolve("image/countrytech_1.png") != null,
                "corpus resources not indexed");
            var generals = new GeneralSettingParser(project.AssetsRoot);
            int missingPhotos = 0, missingHeads = 0, missingTacticalHeads = 0;
            var tactical = HdAtlasParser.Get("tacticalmap");
            foreach (var general in generals.All)
            {
                string key = GeneralSettingParser.GetPhotoKey(general);
                if (generals.GetGeneralPhotoPath(key) == null) missingPhotos++;
                if (generals.GetHeadPath(key) == null) missingHeads++;
                if (tactical.GetImageDef($"head_{key}.png") == null) missingTacticalHeads++;
            }
            using var countries = JsonDocument.Parse(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "json/CountrySettings.json")));
            using var service = new FlagImageService();
            foreach (var country in countries.RootElement.EnumerateArray())
            {
                int id = country.GetProperty("Id").GetInt32();
                using var hd = service.LoadFlagFromHdAtlas(id);
                using var flag = service.LoadBigFlag(id);
                Check(hd != null && flag != null, $"country {id} flag unavailable: {service.LastError}");
            }
            Check(missingPhotos == 262 && missingHeads == 1 && missingTacticalHeads == 1, "new unexpected missing portrait");
            Check(Directory.GetFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Length == 1, "resource preview copied files");
            Console.WriteLine($"Resource corpus: {rasters} raster images ({pkm} PKM), {atlasCount} atlases / {tiles} rectangles; 60 HD + tactical flags.");
            Console.WriteLine($"Portrait source gaps: {missingPhotos} body / {missingHeads} circle / {missingTacticalHeads} tactical; ETC1 reference comparison: {externalHashes != null}.");
        }));
    }

    private static byte[] Pkm(int width, int height, uint high, uint low)
    {
        byte[] bytes = new byte[24]; "PKM 10"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)((width + 3) / 4 * 4));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), (ushort)((height + 3) / 4 * 4));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), (ushort)height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), high); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), low);
        return bytes;
    }
    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(4, 4); bitmap.Erase(color);
        File.WriteAllBytes(path, AtlasCompositeRenderer.Encode(bitmap, path));
    }
    private static void WithFixture(Action<string> action)
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        string previous = AssetManager.Default.AssetsRoot;
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally
        {
            TacticalMapImageCache.Instance.ClearCache();
            LayoutImageProvider.ClearCache(clearExtra: true);
            if (Directory.Exists(previous)) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear();
            Directory.Delete(directory, true);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { return; }
        throw new Exception("Expected image validation failure.");
    }
}
