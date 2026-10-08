using System.Xml;
using SkiaSharp;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Country;

static class ImageEditorAuditTests
{
    public static void Run(Action<string, Action> test)
    {
        test("image UI imports retain anchors and copying captures source pixels across replacement move and repeated paste", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.png"), xml = Path.Combine(f.Root, "atlas.xml");
            using var source = new SKBitmap(48, 32); source.Erase(SKColors.Green);
            using (var canvas = new SKCanvas(source))
            using (var paint = new SKPaint { Color = SKColors.Red }) canvas.DrawRect(0, 0, 4, 4, paint);
            File.WriteAllBytes(image, AtlasCompositeRenderer.Encode(source, image));
            File.WriteAllText(xml, """<Texture name="atlas.png"/><Images><Image name="red.png" x="0" y="0" w="4" h="4" refx="7" refy="-3" custom="keep"/><Image name="green.png" x="20" y="20" w="4" h="4"/></Images>""");
            var editor = new TacticalMapEditor();
            Check(editor.LoadFromFiles(image, xml), "atlas load failed");
            var original = editor.GetObject("red.png")!;
            using var red = BitmapPixels.Crop(source, 0, 0, 4, 4);
            using var clipboard = new AtlasObjectClipboard(original, red);
            using var replacement = new SKBitmap(6, 5); replacement.Erase(SKColors.Blue);
            var (updated, isUpdate) = editor.ImportImage("red.png", replacement, 30, 25);
            Check(isUpdate && updated.RefX == 7 && updated.RefY == -3 && updated.X == 0 && updated.Y == 0 &&
                updated.Width == 6 && updated.Height == 5, "UI import changed anchors or position");
            var pixels = new Dictionary<string, SKBitmap>
            {
                ["red.png"] = replacement.Copy(),
                ["green.png"] = BitmapPixels.Crop(source, 20, 20, 4, 4)
            };
            try
            {
                editor.RemoveByName("red.png");
                pixels["red.png"].Dispose(); pixels.Remove("red.png");
                var first = clipboard.Paste(editor, pixels);
                var second = clipboard.Paste(editor, pixels);
                Check(first.Name != second.Name && first.X == 20 && first.Y == 20 && first.RefX == 7 && first.RefY == -3 &&
                    first.Width == 4 && pixels[first.Name].GetPixel(0, 0) == SKColors.Red &&
                    pixels[second.Name].GetPixel(0, 0) == SKColors.Red, "paste copied target green pixels or current replacement blue pixels");
                first.X = 28; second.X = 36;
                using var composite = AtlasCompositeRenderer.Compose(source, editor.Objects, pixels, editor.IsAtlas);
                AtomicFile.WriteAll((image, AtlasCompositeRenderer.Encode(composite, image)), (xml, editor.SerializeXml(image)));
                var reopened = new TacticalMapEditor();
                Check(reopened.LoadFromFiles(image, xml), "saved paste cannot reopen");
                using var saved = SKBitmap.Decode(image);
                Check(saved.GetPixel(20, 20) == SKColors.Green && saved.GetPixel(28, 20) == SKColors.Red && saved.GetPixel(36, 20) == SKColors.Red &&
                    reopened.GetObject(first.Name)!.RefY == -3, "save or reopen lost pasted pixels or anchors");
            }
            finally { foreach (var bitmap in pixels.Values) bitmap.Dispose(); }
        });

        test("renaming and negative anchors update name lookup preserve metadata and reject duplicate names", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.png"), xml = Path.Combine(f.Root, "atlas.xml");
            WriteImage(image, SKColors.Red);
            File.WriteAllText(xml, """<Texture name="atlas.png"/><Images><Image name="old" x="0" y="0" w="2" h="2" custom="keep"/><Image name="other" x="2" y="0" w="2" h="2"/></Images>""");
            var editor = new TacticalMapEditor(); Check(editor.LoadFromFiles(image, xml), "atlas load failed");
            var obj = editor.GetObject("old")!;
            var draft = TacticalMapObject.Clone(obj); draft.Name = "renamed"; draft.RefX = 7; draft.RefY = -3;
            editor.UpdateObject(obj, draft);
            Check(editor.GetObject("old") == null && ReferenceEquals(editor.GetObject("renamed"), obj), "rename left a stale name index");
            var duplicate = TacticalMapObject.Clone(obj); duplicate.Name = "other";
            Reject(() => editor.UpdateObject(obj, duplicate));
            Check(obj.Name == "renamed" && editor.Objects.Count == 2, "failed rename changed the document");
            byte[] bytes = editor.SerializeXml(image);
            AtomicFile.Write(xml, bytes); editor.MarkSaved(image, xml, File.ReadAllBytes(image));
            draft.Name = "again"; editor.UpdateObject(obj, draft);
            AtomicFile.Write(xml, editor.SerializeXml(image));
            var node = (XmlElement)ReadXml(xml).SelectSingleNode("//Image[@name='again']")!;
            Check(node.GetAttribute("custom") == "keep" && node.GetAttribute("refy") == "-3", "second rename discarded unknown metadata or anchor");
        });

        test("deleting the last atlas object saves transparent pixels and reopening an empty atlas retains its document kind", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.png"), xml = Path.Combine(f.Root, "atlas.xml");
            WriteImage(image, SKColors.Red);
            File.WriteAllText(xml, """<Texture name="atlas.png"/><Images><Image name="only" x="0" y="0" w="4" h="2"/></Images>""");
            var editor = new TacticalMapEditor(); Check(editor.LoadFromFiles(image, xml) && editor.IsAtlas, "atlas kind missing");
            editor.GetObject("only")!.IsSelected = true; editor.DeleteSelectedObjects();
            using var source = SKBitmap.Decode(image);
            using var composite = AtlasCompositeRenderer.Compose(source, editor.Objects, new Dictionary<string, SKBitmap>(), editor.IsAtlas);
            Check(composite.Pixels.All(pixel => pixel.Alpha == 0), "deleted atlas restored original pixels");
            AtomicFile.WriteAll((image, AtlasCompositeRenderer.Encode(composite, image)), (xml, editor.SerializeXml(image)));
            Check(editor.LoadFromFiles(image, xml) && editor.IsAtlas && editor.Objects.Count == 0, "empty atlas became a plain raster on reopen");
            using var saved = SKBitmap.Decode(image);
            Check(saved.Pixels.All(pixel => pixel.Alpha == 0) && ReadXml(xml).SelectNodes("//Image")!.Count == 0,
                "image and XML disagree after deleting the last object");
            string plain = Path.Combine(f.Root, "plain.png"); WriteImage(plain, SKColors.Red);
            Check(editor.LoadFromFiles(plain) && !editor.IsAtlas, "plain raster marked as atlas");
            using var raster = SKBitmap.Decode(plain);
            using var plainComposite = AtlasCompositeRenderer.Compose(raster, editor.Objects, new Dictionary<string, SKBitmap>(), editor.IsAtlas);
            Check(plainComposite.GetPixel(0, 0) == SKColors.Red, "plain raster cleared");
        });

        test("flag workflow rejects damaged HD XML and a blocked HD output without publishing tactical changes", () =>
        {
            using var f = new Fixture();
            string source = Path.Combine(f.Root, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "image"));
            foreach (string stem in new[] { "tacticalmap", "image/image_flags_hd" })
            {
                WriteImage(Path.Combine(assets, stem + ".png"), SKColors.Red);
                File.WriteAllText(Path.Combine(assets, stem + ".xml"), $"<Texture name=\"{Path.GetFileName(stem)}.png\"/><Images><Image name=\"flag_1.png\" x=\"0\" y=\"0\" w=\"2\" h=\"2\"/></Images>");
            }
            string overlay = Path.Combine(f.Root, "overlay.png");
            using (var white = new SKBitmap(47, 47)) { white.Erase(SKColors.White); File.WriteAllBytes(overlay, AtlasCompositeRenderer.Encode(white, overlay)); }
            var project = GameProjectWorkspace.Create(source, Path.Combine(f.Root, "output"));
            string previous = AssetManager.Default.AssetsRoot; bool loaded = AssetManager.Default.IsLoaded;
            var session = new GameProjectDataSession();
            try
            {
                session.Activate(project);
                using var canvas = new SKBitmap(150, 150); canvas.Erase(SKColors.Blue);
                var service = new FlagEditorService(overlay);
                var originals = Directory.GetFiles(assets, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
                string hdXml = Path.Combine(project.AssetsRoot, "image", "image_flags_hd.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(hdXml)!); File.WriteAllText(hdXml, "damaged XML");
                var result = service.MakeFlagWorkflow(canvas, 2, project.AssetsRoot);
                Check(!result.Success && result.ErrorMessage != null && result.FlagPath == null &&
                    !File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.png")) && File.ReadAllText(hdXml) == "damaged XML",
                    "damaged HD XML published tactical atlas or returned partial success");
                File.Delete(hdXml); Directory.CreateDirectory(hdXml);
                result = service.MakeFlagWorkflow(canvas, 2, project.AssetsRoot);
                Check(!result.Success && !File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.png")) &&
                    !File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.xml")), "blocked HD XML output published tactical candidates");
                Directory.Delete(hdXml);
                string blockedImageDirectory = Path.GetDirectoryName(hdXml)!;
                Directory.Delete(blockedImageDirectory);
                File.WriteAllText(blockedImageDirectory, "cannot be a directory");
                result = service.MakeFlagWorkflow(canvas, 2, project.AssetsRoot);
                Check(!result.Success && !File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.png")) &&
                    !File.Exists(Path.Combine(project.AssetsRoot, "tacticalmap.xml")), "HD staging failure published tactical candidates");
                File.Delete(blockedImageDirectory);
                result = service.MakeFlagWorkflow(canvas, 2, project.AssetsRoot);
                Check(result.Success && new[] { "tacticalmap.png", "tacticalmap.xml", "image/image_flags_hd.png", "image/image_flags_hd.xml" }
                    .All(path => File.Exists(Path.Combine(project.AssetsRoot, path))), result.ErrorMessage ?? "flag retry did not publish all four files");
                Check(originals.All(item => item.Value.SequenceEqual(File.ReadAllBytes(item.Key))), "flag workflow wrote the source project");
            }
            finally
            {
                if (loaded) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear();
                GameProjectDataSession.ReloadEditors();
            }
        });

        test("general images use source extension and heads directory with no partial outputs on failure", () =>
        {
            using var f = new Fixture();
            string source = Path.Combine(f.Root, "source"), assets = Path.Combine(source, "assets");
            string photos = Path.Combine(assets, "image/generalphoto"), heads = Path.Combine(assets, "image/heads");
            Directory.CreateDirectory(photos);
            Directory.CreateDirectory(heads);
            WriteImage(Path.Combine(photos, "general_Test.webp"), SKColors.Red);
            WriteImage(Path.Combine(heads, "general_circle_Test.webp"), SKColors.Red);
            var project = GameProjectWorkspace.Create(source, Path.Combine(f.Root, "output"));
            var parser = new GeneralSettingParser(project.AssetsRoot);
            var service = new ImageEditorService();
            using var replacement = new SKBitmap(4, 2);
            replacement.Erase(SKColors.Blue);
            using var circle = new SKBitmap(2, 2);
            circle.Erase(SKColors.Green);
            string blocked = Path.Combine(project.AssetsRoot, "image/heads");
            Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
            File.WriteAllText(blocked, "not a directory");
            var result = service.SaveGeneralImages(replacement, circle, "Test", parser.GeneralPhotoDir, 2, 2, 1, 1, parser.HeadsDir);
            Check(!result.Success && result.ErrorMessage != null && result.GeneralPath == null &&
                !File.Exists(Path.Combine(parser.GeneralPhotoDir, "general_Test.webp")), "partial portrait output or false success");
            File.Delete(blocked);
            result = service.SaveGeneralImages(replacement, circle, "Test", parser.GeneralPhotoDir, 2, 2, 1, 1, parser.HeadsDir);
            Check(result.Success && result.GeneralPath == parser.GetGeneralPhotoPath("Test") && result.CirclePath == parser.GetHeadPath("Test") &&
                Path.GetExtension(result.GeneralPath) == ".webp" && result.HeadPath == Path.Combine(parser.HeadsDir, "head_Test.png"), result.ErrorMessage ?? "portrait paths mismatched");
            using var codec = SKCodec.Create(new SKMemoryStream(File.ReadAllBytes(result.GeneralPath!)));
            Check(codec.EncodedFormat == SKEncodedImageFormat.Webp, "portrait extension or encoder mismatched");
            Check(!service.SaveGeneralImages(replacement, circle, "../escape", parser.GeneralPhotoDir, 2, 2, 1, 1).Success, "path traversal accepted");
        });

        test("flag multi-output save rejects invalid thumbnail size before publishing main image", () =>
        {
            using var f = new Fixture();
            using var main = new SKBitmap(4, 4);
            main.Erase(SKColors.Red);
            var service = new FlagEditorService();
            var result = service.SaveFlagImages(main, main, "1", f.Root, 2, 2, 0, 0);
            Check(!result.Success && result.ErrorMessage != null && !File.Exists(Path.Combine(f.Root, "flag_1.png")), "failed thumbnail left flag or false success");
        });

        test("two atlas candidates leave both documents intact and a failed batch publishes neither", () =>
        {
            using var f = new Fixture();
            string source = Path.Combine(f.Root, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(assets);
            foreach (string atlas in new[] { "tacticalmap", "image_flags_hd" })
            {
                WriteImage(Path.Combine(assets, atlas + ".png"), SKColors.Red);
                File.WriteAllText(Path.Combine(assets, atlas + ".xml"), $"<Texture name=\"{atlas}.png\"/><Images><Image name=\"flag_1.png\" x=\"0\" y=\"0\" w=\"2\" h=\"2\"/></Images>");
            }
            var project = GameProjectWorkspace.Create(source, Path.Combine(f.Root, "output"));
            var tactical = new TacticalMapEditor();
            var hd = new TacticalMapEditor();
            Check(tactical.LoadFromFiles(Path.Combine(project.AssetsRoot, "tacticalmap.png")) && hd.LoadFromFiles(Path.Combine(project.AssetsRoot, "image_flags_hd.png")), "merged atlas load failed");
            using var replacement = new SKBitmap(2, 2);
            replacement.Erase(SKColors.Blue);
            var first = tactical.PrepareImagesAndArrange([("flag_2.png", replacement)]);
            var second = hd.PrepareImagesAndArrange([("flag_2.png", replacement)]);
            Check(tactical.GetObject("flag_2.png") == null && hd.GetObject("flag_2.png") == null, "prepare mutated live documents");
            Directory.CreateDirectory(second[1].Path);
            bool rejected = false;
            try { AtomicFile.WriteAll([.. first, .. second]); } catch (IOException) { rejected = true; }
            Check(rejected && !Directory.EnumerateFiles(project.AssetsRoot, "*", SearchOption.AllDirectories).Any(), "second atlas failure published first");
            Directory.Delete(second[1].Path);
            AtomicFile.WriteAll([.. first, .. second]);
            foreach (string atlas in new[] { "tacticalmap", "image_flags_hd" })
                Check(ReadXml(Path.Combine(project.AssetsRoot, atlas + ".xml")).SelectSingleNode("//Image[@name='flag_2.png']") != null, "batch did not publish both atlases");
        });

        test("same pixels keep original encoded bytes and produce no incremental raster output", () =>
        {
            using var f = new Fixture();
            string source = Path.Combine(f.Root, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(assets);
            string input = Path.Combine(assets, "plain.webp");
            WriteImage(input, SKColors.Blue);
            var project = GameProjectWorkspace.Create(source, Path.Combine(f.Root, "output"));
            string output = project.GetEditablePath(input);
            using var decoded = SKBitmap.Decode(input);
            byte[] bytes = AtlasCompositeRenderer.Encode(decoded, output);
            Check(bytes.SequenceEqual(File.ReadAllBytes(input)), "unchanged pixels were reencoded");
            AtomicFile.Write(output, bytes);
            Check(!File.Exists(output), "unchanged raster copied");
        });

        test("undecodable images and malformed XML leave image editors unloaded and unsavable", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.png"), xml = Path.Combine(f.Root, "atlas.xml");
            WriteImage(image, SKColors.Red);
            var editor = new TacticalMapEditor();
            Check(editor.LoadFromFiles(image), "valid image rejected");
            string invalid = Path.Combine(f.Root, "invalid.pkm");
            File.WriteAllBytes(invalid, [1, 2, 3, 4]);
            Check(!editor.LoadFromFiles(invalid) && !editor.IsLoaded && editor.ImageData == null && editor.ImageWidth == 0 &&
                editor.Objects.Count == 0 && !editor.SaveXml(xml), "failed decode can still save");
            File.WriteAllText(xml, "invalid XML");
            Check(!editor.LoadFromFiles(image, xml) && !editor.IsLoaded && !editor.SaveXml(xml) &&
                File.ReadAllText(xml) == "invalid XML", "failed XML load can overwrite its input");
            var hd = new HdAtlasEditor();
            Check(!hd.LoadFromFiles(invalid, xml) && !hd.IsLoaded, "HD editor accepts failed decoding");
        });

        test("saving a plain raster preserves pixels and uses the selected image format", () =>
        {
            using var f = new Fixture();
            using var source = new SKBitmap(4, 3);
            source.Erase(SKColors.Red);
            source.SetPixel(2, 1, SKColors.Blue);
            using var composite = AtlasCompositeRenderer.Compose(source, [], new Dictionary<string, SKBitmap>(), isAtlas: false);
            Check(source.Pixels.SequenceEqual(composite.Pixels), "plain raster cleared");
            foreach (string path in new[] { "image.png", "image.webp", "image.jpg" })
            {
                byte[] bytes = AtlasCompositeRenderer.Encode(composite, path);
                using var codec = SKCodec.Create(new SKMemoryStream(bytes));
                var expected = Path.GetExtension(path) switch
                {
                    ".png" => SKEncodedImageFormat.Png, ".webp" => SKEncodedImageFormat.Webp, _ => SKEncodedImageFormat.Jpeg
                };
                Check(codec.EncodedFormat == expected && codec.Info.Width == 4 && codec.Info.Height == 3, "extension does not match content");
            }
            Reject(() => AtlasCompositeRenderer.Compose(null, [], new Dictionary<string, SKBitmap>(), isAtlas: false));
            Reject(() => AtlasCompositeRenderer.Encode(source, "image.pkm"));
            string mismatch = Path.Combine(f.Root, "wrong-extension.webp");
            using var png = source.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(mismatch, png.ToArray());
            byte[] corrected = AtlasCompositeRenderer.Encode(source, mismatch);
            using var correctedCodec = SKCodec.Create(new SKMemoryStream(corrected));
            Check(correctedCodec.EncodedFormat == SKEncodedImageFormat.Webp, "identical pixels kept incorrect original encoding");
            Reject(() => AtlasCompositeRenderer.Compose(source,
                [new TacticalMapObject { Name = "missing", Width = 1, Height = 1 }], new Dictionary<string, SKBitmap>(), isAtlas: true));
        });

        test("atlas imports preserve other pixels Texture anchors and unknown XML nodes and attributes", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.webp"), xml = Path.Combine(f.Root, "atlas.xml");
            WriteImage(image, SKColors.Red);
            File.WriteAllText(xml, """<Texture name="atlas.webp" custom="yes"/><Meta keep="true"/><Images note="keep"><Image name="flag_1.png" x="0" y="0" w="2" h="2" refx="7" refy="-3" custom="keep"/><Image name="other.png" x="2" y="0" w="2" h="2" refx="1" refy="2"/></Images>""");
            var editor = new TacticalMapEditor();
            Check(editor.LoadFromFiles(image, xml), "atlas load failed");
            using var replacement = new SKBitmap(2, 2);
            replacement.Erase(SKColors.Blue);
            Check(editor.AddImageAndArrange("flag_1.png", replacement), "atlas import failed");
            using var codec = SKCodec.Create(new SKMemoryStream(File.ReadAllBytes(image)));
            Check(codec.EncodedFormat == SKEncodedImageFormat.Webp, "WebP file replaced with PNG content");
            var document = ReadXml(xml);
            Check(document.SelectSingleNode("//Meta")?.Attributes?["keep"]?.Value == "true" &&
                document.SelectSingleNode("//Texture")?.Attributes?["custom"]?.Value == "yes" &&
                document.SelectSingleNode("//Images")?.Attributes?["note"]?.Value == "keep", "unknown metadata discarded");
            var flag = (XmlElement)document.SelectSingleNode("//Image[@name='flag_1.png']")!;
            Check(flag.GetAttribute("refx") == "7" && flag.GetAttribute("refy") == "-3" && flag.GetAttribute("custom") == "keep", "anchors or custom attributes discarded");
            using var saved = SKBitmap.Decode(image);
            foreach (var obj in editor.Objects)
                Check(saved.GetPixel(obj.X, obj.Y) == (obj.Name == "flag_1.png" ? SKColors.Blue : SKColors.Red), "unrelated atlas pixels changed");
            var hd = new HdAtlasEditor();
            Check(hd.LoadFromFiles(image, xml) && hd.AddImageAndSave(2, replacement), "HD import failed");
            document = ReadXml(xml);
            Check(document.SelectNodes("//Image")!.Count == 3 && document.SelectSingleNode("//Image[@name='other.png']") != null &&
                document.SelectSingleNode("//Texture") != null, "HD save removed nonflag entries or Texture");
        });

        test("failed atlas pair save restores memory and leaves the image unchanged", () =>
        {
            using var f = new Fixture();
            string image = Path.Combine(f.Root, "atlas.png"), xml = Path.Combine(f.Root, "atlas.xml");
            WriteImage(image, SKColors.Red);
            File.WriteAllText(xml, """<Texture name="atlas.png"/><Images><Image name="old.png" x="0" y="0" w="2" h="2"/></Images>""");
            var editor = new TacticalMapEditor();
            Check(editor.LoadFromFiles(image, xml), "load failed");
            byte[] original = File.ReadAllBytes(image);
            int width = editor.ImageWidth, height = editor.ImageHeight;
            using var replacement = new SKBitmap(2, 2);
            replacement.Erase(SKColors.Blue);
            string blocked = Path.Combine(f.Root, "blocked");
            File.WriteAllText(blocked, "cannot be a directory");
            Check(!editor.AddImageAndArrange("new.png", replacement, image, Path.Combine(blocked, "atlas.xml")), "pair save ignored failure");
            Check(original.SequenceEqual(File.ReadAllBytes(image)) && editor.GetObject("new.png") == null &&
                editor.GetObject("old.png") != null && editor.ImageWidth == width && editor.ImageHeight == height, "failed save changed disk or memory");
        });
    }

    private static XmlDocument ReadXml(string path)
    {
        var doc = new XmlDocument();
        doc.LoadXml("<root>" + File.ReadAllText(path).TrimStart('\uFEFF') + "</root>");
        return doc;
    }

    private static void WriteImage(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(4, 2);
        bitmap.Erase(color);
        File.WriteAllBytes(path, AtlasCompositeRenderer.Encode(bitmap, path));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Expected rejection.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
