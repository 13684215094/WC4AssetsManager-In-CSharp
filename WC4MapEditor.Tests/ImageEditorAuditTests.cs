using System.Xml;
using SkiaSharp;
using WC4MapEditor.Rendering.Imaging;

static class ImageEditorAuditTests
{
    public static void Run(Action<string, Action> test)
    {
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
            using var source = new SKBitmap(4, 3);
            source.Erase(SKColors.Red);
            source.SetPixel(2, 1, SKColors.Blue);
            using var composite = AtlasCompositeRenderer.Compose(source, [], new Dictionary<string, SKBitmap>());
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
            Reject(() => AtlasCompositeRenderer.Compose(null, [], new Dictionary<string, SKBitmap>()));
            Reject(() => AtlasCompositeRenderer.Encode(source, "image.pkm"));
            Reject(() => AtlasCompositeRenderer.Compose(source,
                [new TacticalMapObject { Name = "missing", Width = 1, Height = 1 }], new Dictionary<string, SKBitmap>()));
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
            Check(!editor.AddImageAndArrange("new.png", replacement, image, Path.Combine(f.Root, "missing", "atlas.xml")), "pair save ignored failure");
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
