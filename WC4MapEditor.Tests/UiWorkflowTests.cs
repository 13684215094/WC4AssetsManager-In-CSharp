using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Rendering.Imaging;

internal static class UiWorkflowTests
{
    public static void Run(Action<string, Action> test)
    {
        test("UI workflow: focus reset clears external modifiers and permits ordinary key tracking again", () =>
        {
            var keyboard = new KeyboardManager(); int ordinary = 0, modified = 0;
            keyboard.RegisterBinding("ordinary", 77, callback: () => ordinary++);
            keyboard.RegisterBinding("modified", 77, KeyModifiers.Ctrl, () => modified++);
            keyboard.SetModifierState(KeyModifiers.Ctrl); keyboard.ProcessKeyDown(77);
            Check(modified == 1 && keyboard.IsKeyDown(77), "modifier fixture failed");
            keyboard.ResetState();
            Check(!keyboard.IsKeyDown(77) && keyboard.GetCurrentModifiers() == KeyModifiers.None, "pressed state survived focus loss");
            keyboard.ProcessKeyDown(77);
            Check(ordinary == 1 && modified == 1, "stale external Ctrl fired after reset");
            keyboard.ProcessKeyUp(77); keyboard.ProcessKeyDown(KeyboardManager.ShiftKeyCode);
            Check(keyboard.IsShiftPressed, "reset prevented ordinary modifier tracking");
        });

        test("UI workflow: preview resource loading isolates unreadable layouts and observes overlay updates", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"); Directory.CreateDirectory(Path.Combine(source, "assets"));
            string layout = """<Layouts><TmpWindow id="form_technique"><Repeater id="rpt_items" width="460" height="212" itemwidth="230" itemheight="106" itemcount="4"><GroupBox id="tech_item" width="181"/></Repeater></TmpWindow><TmpWindow id="form_unitinfo" width="650" height="340"/></Layouts>""";
            File.WriteAllText(Path.Combine(source, "assets/layout_x.xml"), layout);
            File.WriteAllText(Path.Combine(source, "assets/layout_missing.xml"), layout);
            File.WriteAllText(Path.Combine(source, "assets/layout_other.xml"), """<Layouts><Form id="unrelated"/></Layouts>""");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            var manager = new AssetManager(new AssetCache()); manager.Scan(project.AssetsRoot);
            File.Delete(Path.Combine(source, "assets/layout_missing.xml"));
            var catalog = GameUiPreviewResources.LoadLayouts(manager, "form_unitinfo", "form_technique");
            Check(catalog.Layouts.Count == 1 && catalog.Layouts.ContainsKey("layout_x.xml"), "an unreadable or unrelated layout blocked a valid form");
            Check(catalog.Issues.Count == 1 && catalog.Issues[0].Contains("layout_missing.xml"), "unreadable layout not diagnosed");
            var original = HeadquartersTechGrid.FromLayout(catalog.Layouts["layout_x.xml"]);
            File.WriteAllText(Path.Combine(project.AssetsRoot, "layout_x.xml"), layout.Replace("width=\"460\"", "width=\"690\""));
            manager.InvalidateData();
            var updated = GameUiPreviewResources.LoadLayouts(manager, "form_technique");
            Check(HeadquartersTechGrid.FromLayout(updated.Layouts["layout_x.xml"]).Columns == 3 && original.Columns == 2,
                "preview layout failed to use incremental overlay or mutated old snapshot");
            File.WriteAllText(Path.Combine(project.AssetsRoot, "layout_x.xml"), "<Layouts/>"); manager.InvalidateData();
            Check(GameUiPreviewResources.LoadLayouts(manager, "form_technique").Layouts.Count == 0, "removed form survived resource reload");
            Check(File.ReadAllText(Path.Combine(source, "assets/layout_x.xml")) == layout, "preview loader wrote source");
        }));

        test("UI workflow: optional preview tables accept editor JSON syntax and diagnose invalid rows", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json"));
            string path = Path.Combine(assets, "json/ArmyFeatureSettings.json");
            File.WriteAllText(path, """[/* draft */{"id":"441","type":4,"level":1,"FeatureEffect":25,},]""");
            var manager = new AssetManager(new AssetCache()); manager.Scan(assets);
            var issues = new List<string>();
            var rows = GameUiPreviewResources.ReadRows<ArmyFeatureData>(manager, "json/ArmyFeatureSettings.json", issues);
            Check(rows.Count == 1 && rows[0].Id == 441 && rows[0].FeatureEffect == 25 && issues.Count == 0, "preview rejected comments, trailing commas or numeric strings");
            File.WriteAllText(path, "\uFEFF" + File.ReadAllText(path));
            rows = GameUiPreviewResources.ReadRows<ArmyFeatureData>(manager, "json/ArmyFeatureSettings.json", issues);
            Check(rows.Count == 1 && rows[0].Type == 4 && issues.Count == 0, "preview rejected editor-compatible UTF-8 BOM");
            string titles = Path.Combine(assets, "json/GeneralTitleSettings.json");
            File.WriteAllText(titles, """[{"GeneralId":"1049"},{"generalid":1050}]""");
            manager.Scan(assets, true);
            Check(GameUiPreviewResources.ReadRows<GeneralTitlePreviewData>(manager, "json/GeneralTitleSettings.json", issues)
                .Select(row => row.GeneralId).SequenceEqual([1049, 1050]) && issues.Count == 0,
                "title preview bypassed numeric-string or case-insensitive JSON options");
            File.WriteAllText(titles, """[{"GeneralId":{}}]""");
            Check(GameUiPreviewResources.ReadRows<GeneralTitlePreviewData>(manager, "json/GeneralTitleSettings.json", issues).Count == 0 &&
                issues.Single().Contains("GeneralTitleSettings.json"), "malformed title id escaped optional-table diagnostics");
            foreach (string text in new[] { "null", "[null]", "{broken" })
            {
                File.WriteAllText(path, text); issues.Clear();
                Check(GameUiPreviewResources.ReadRows<ArmyFeatureData>(manager, "json/ArmyFeatureSettings.json", issues).Count == 0 &&
                    issues.Count == 1 && issues[0].Contains("ArmyFeatureSettings.json"), "bad optional data escaped diagnostics");
            }
        }));

        test("UI workflow: slot changes preserve runtime levels, pairs and source lists", () =>
        {
            int[] refs = [17, 4, 9], levels = [2, 6, 1];
            var moved = ReferenceSlots.Move(refs, levels, 0, 2);
            Check(moved.References.SequenceEqual([4, 9, 17]) && moved.Levels.SequenceEqual([6, 1, 2]), "paired move lost association");
            Check(refs.SequenceEqual([17, 4, 9]) && levels.SequenceEqual([2, 6, 1]), "input lists mutated");
            var runtime = ReferenceSlots.Move(refs, [], 2, 0);
            Check(runtime.References.SequenceEqual([9, 17, 4]) && runtime.Levels.Count == 0, "runtime levels manufactured");
            var removed = ReferenceSlots.Remove(refs, levels, 1);
            Check(removed.References.SequenceEqual([17, 9]) && removed.Levels.SequenceEqual([2, 1]), "remove broke pairs");
            var replaced = ReferenceSlots.Set(refs, levels, 1, 50, 7);
            Check(replaced.References.SequenceEqual([17, 50, 9]) && replaced.Levels.SequenceEqual([2, 7, 1]), "replace lost actual level");
            var added = ReferenceSlots.Set(refs, [], 3, 50, null);
            Check(added.Levels.Count == 0 && added.References.Last() == 50, "append normalized runtime levels");
            Check(ReferenceSlots.Set([1245, 1246], [], 0, 0, null).References.SequenceEqual([0, 1246]), "empty skill slot removed sequence");
            Reject(() => ReferenceSlots.Move(refs, [2], 0, 1));
            Reject(() => ReferenceSlots.Remove(refs, [1, 2, 3, 4], 0));
            Reject(() => ReferenceSlots.Set(refs, [], 0, 5, 6));
            Reject(() => ReferenceSlots.Set(refs, levels, 0, 5, null));
        });

        test("UI workflow: output change snapshot distinguishes physical outputs without reading source-only files", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"), output = Path.Combine(dir, "output");
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            File.WriteAllText(Path.Combine(source, "assets/unchanged.txt"), "source-only");
            File.WriteAllText(Path.Combine(source, "assets/different.txt"), "old");
            File.WriteAllText(Path.Combine(source, "assets/same.txt"), "same");
            var project = GameProjectWorkspace.Create(source, output);
            Check(ProjectOutputChanges.Scan(project).Count == 0, "manifest/source-only files counted as outputs");
            File.WriteAllText(Path.Combine(project.AssetsRoot, "different.txt"), "new");
            File.WriteAllText(Path.Combine(project.AssetsRoot, "same.txt"), "same");
            File.WriteAllText(Path.Combine(project.AssetsRoot, "added.txt"), "new");
            var changes = ProjectOutputChanges.Scan(project).ToDictionary(row => row.RelativePath);
            Check(changes.Count == 3 && changes["assets/different.txt"].Kind == ProjectOutputChangeKind.Modified &&
                changes["assets/same.txt"].Kind == ProjectOutputChangeKind.Identical && changes["assets/added.txt"].Kind == ProjectOutputChangeKind.Added, "wrong output states");
            Check(File.ReadAllText(Path.Combine(source, "assets/different.txt")) == "old" && File.Exists(Path.Combine(project.AssetsRoot, "same.txt")), "comparison changed files");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { ProjectOutputChanges.Scan(project, cancel.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { }
        }));

        test("UI workflow: output comparison rejects linked directories", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"); Directory.CreateDirectory(Path.Combine(source, "assets"));
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            string linked = Path.Combine(project.AssetsRoot, "linked");
            try { Directory.CreateSymbolicLink(linked, source); }
            catch (Exception ex) when (OperatingSystem.IsWindows() &&
                (ex is UnauthorizedAccessException || ex is IOException && (ex.HResult & 0xFFFF) == 1314))
            { throw new TestSkippedException("Windows 未授予创建符号链接的权限；其他输出比较测试仍会执行。"); }
            try { Reject(() => ProjectOutputChanges.Scan(project)); }
            finally { Directory.Delete(linked); }
        }));

        test("UI workflow: output comparison reports a source directory collision as unreadable", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"); Directory.CreateDirectory(Path.Combine(source, "assets/collision"));
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            File.WriteAllText(Path.Combine(project.AssetsRoot, "collision"), "output file");
            var row = ProjectOutputChanges.Scan(project).Single();
            Check(row.Kind == ProjectOutputChangeKind.Unreadable && row.Error?.Contains("目录") == true,
                "source directory incorrectly shown as a new resource");
        }));

        test("UI workflow: changed general filter follows saved baseline and preserves unknown and mixed-case fields", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json")); Directory.CreateDirectory(Path.Combine(assets, "config"));
            File.WriteAllText(Path.Combine(assets, "json/GeneralSettings.json"), """[{"id":1,"name":"one","Future":true},{"Id":2,"Name":"two"},{"Name":"no-explicit-id"}]""");
            File.WriteAllText(Path.Combine(assets, "config/def_portraitpos.xml"), "<Portraits/>");
            var manager = new AssetManager(new AssetCache()); manager.Scan(assets);
            var parser = new GeneralSettingParser(assets, manager);
            Check(parser.GetChangedGeneralIds().Count == 0, "loaded records dirty");
            parser.All[0].Name = "draft";
            Check(parser.GetChangedGeneralIds().SetEquals([1]), "changed record not identified");
            parser.All[0].Name = "one"; Check(parser.GetChangedGeneralIds().Count == 0, "reverted edit remained dirty");
            parser.All[2].Name = "draft-default-id"; Check(parser.GetChangedGeneralIds().SetEquals([0]), "implicit zero id lost");
            parser.All[2].Name = "no-explicit-id";
            parser.All[0].Name = "saved";
            Check(parser.SaveAll() && parser.GetChangedGeneralIds().Count == 0, parser.LastError ?? "saved filter baseline stale");
            Check(JsonNode.Parse(File.ReadAllText(parser.ConfigPath))![0]!["Future"]!.GetValue<bool>(), "unknown field changed");
        }));

        test("UI workflow: changed general filter keeps duplicate record identities through save and deletion", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json")); Directory.CreateDirectory(Path.Combine(assets, "config"));
            File.WriteAllText(Path.Combine(assets, "json/GeneralSettings.json"), """[{"Id":11,"Name":"first"},{"Id":11,"Name":"second"}]""");
            File.WriteAllText(Path.Combine(assets, "config/def_portraitpos.xml"), "<Portraits/>");
            var manager = new AssetManager(new AssetCache()); manager.Scan(assets);
            var parser = new GeneralSettingParser(assets, manager);
            parser.All[1].Name = "first";
            Check(parser.GetChangedGeneralIds().SetEquals([11]), "edit became invisible by matching the other duplicate record");
            Check(parser.GetChangedGenerals().SetEquals([parser.All[1]]), "unchanged duplicate appeared in the changed-only list");
            Check(parser.SaveAll() && parser.GetChangedGeneralIds().Count == 0, "duplicate save baseline stale");
            parser.DeleteGeneral(parser.All[0]); parser.All[0].Name = "second";
            Check(parser.GetChangedGeneralIds().SetEquals([11]), "deletion shifted the surviving record's baseline");
        }));

        test("UI workflow: HQ clicks share rendered card segments and ignore cell padding", () =>
        {
            var grid = new HeadquartersTechGrid("test", 460, 212, 230, 106, 4, 181);
            var rows = JsonNode.Parse("""
                [{"Id":1,"Type":1,"Level":1,"Categorys":0,"Position":[1,1],"UpgradeID":0,"NeedID":[]},
                 {"Id":2,"Type":2,"Level":1,"Categorys":0,"Position":[1,1],"UpgradeID":0,"NeedID":[]},
                 {"Id":3,"Type":3,"Level":1,"Categorys":1,"Position":[1,1],"UpgradeID":0,"NeedID":[]}]
                """)!.AsArray();
            var regions = HeadquartersTechPreviewRenderer.GetCardRegions(HeadquartersTechTree.Build(rows, grid), grid, 0);
            Check(regions.Count == 2 && regions[0].Node.Id == 1 && regions[1].Node.Id == 2, "dual-card hit association");
            Check(regions[0].Bounds.Right == regions[1].Bounds.Left && !regions.Any(region => region.Bounds.Contains(5, 50)) &&
                !regions.Any(region => region.Bounds.Contains(50, 0)) && !regions.Any(region => region.Bounds.Contains(300, 40)), "padding or empty cell is clickable");
            Check(regions[1].Bounds.Contains(regions[1].Bounds.MidX, 50), "second half not selectable");
        });

        test("UI workflow: detail frames use explicit context, retain drafts and never bind the source layout", () =>
        {
            var layout = Parse("""
                <Layouts><TmpWindow id="form_unitinfo" width="650" height="340"><Label id="lbl_item_name"/>
                <Label id="lbl_value_atk"/><Label id="lbl_value_hp"/><GroupBox id="gbox_model"><MotionalArmy id="motion_unit"/></GroupBox>
                <Button id="btn_feat_1"/></TmpWindow><TmpWindow id="form_conquestcountry" width="600" height="264">
                <Image id="img_flag"/><Image id="img_camp"/><Label id="lbl_countryname"/><GroupBox id="gbox_star1"/>
                <GroupBox id="gbox_star2"/><GroupBox id="gbox_star3"/><GroupBox id="gbox_gengeral"/></TmpWindow></Layouts>
                """);
            string before = layout.Dump();
            var army = new ArmySettingData { Id = 10, Army = 2, HP = 150, MinAttack = 20, MaxAttack = 33, Feature = [4], FeatureLevel = [] };
            var frame = ProjectDetailPreview.Army(layout, army, "draft unit", [new() { Id = 441, Type = 4, Level = 1 }], _ => true);
            Check(GameUiPreview.Find(frame.Root, "lbl_value_hp")?.Get("string") == "150" &&
                GameUiPreview.Find(frame.Root, "btn_feat_1")?.Get("previewFeatureId") == "441", "draft or feature type not bound");
            Check(army.FeatureLevel.Count == 0 && frame.Issues.Any(issue => issue.Contains("未补写等级")), "preview normalized runtime levels");
            var country = new CountrySettingData { Id = 12, Name = "C" };
            var context = new ConquerCountrySettingData { Id = 100, CountryId = 12, ConquerId = 3, Camp = 2, Star = 3 };
            var countryFrame = ProjectDetailPreview.Country(layout, country, context, "country");
            Check(GameUiPreview.Find(countryFrame.Root, "img_flag")?.Get("image")?.EndsWith("flag_12.png") == true &&
                GameUiPreview.Find(countryFrame.Root, "gbox_star3")?.Get("visible") == "true" &&
                GameUiPreview.Find(countryFrame.Root, "gbox_gengeral")?.Get("visible") == "false", "country context not bound");
            context.CountryId = 11; Reject(() => ProjectDetailPreview.Country(layout, country, context, "country"));
            Check(layout.Dump() == before, "preview bindings persisted into layout");
        });
    }

    public static void RunRenderingCorpus(Action<string, Action> test, string root)
    {
        test("UI workflow: real army and conquest-country layouts render draft values with source art", () => WithFixture(dir =>
        {
            var manager = AssetManager.Default; string previous = manager.AssetsRoot; bool loaded = manager.IsLoaded;
            try
            {
                manager.Scan(Path.Combine(root, "wc4/World Conqueror 4_1.30.0/assets"), true);
                var layout = Parse(manager.ReadText(manager.Find("layout_x.xml")!)); string baseline = layout.Dump();
                var fonts = GameUiFontCatalog.Parse(manager.ReadText(manager.Find("font_resource.xml")!));
                var army = JsonSerializer.Deserialize<List<ArmySettingData>>(manager.ReadText(manager.Find("json/ArmySettings.json")!))![1];
                var features = JsonSerializer.Deserialize<List<ArmyFeatureData>>(manager.ReadText(manager.Find("json/ArmyFeatureSettings.json")!))!;
                var country = JsonSerializer.Deserialize<List<CountrySettingData>>(manager.ReadText(manager.Find("json/CountrySettings.json")!))!.First(row => row.Id == 12);
                var context = JsonSerializer.Deserialize<List<ConquerCountrySettingData>>(manager.ReadText(manager.Find("json/ConquerCountrySettings.json")!))!.First(row => row.CountryId == 12);
                using var images = new ProjectUiImageSource(); using var typefaces = new ProjectUiFontSource();
                string? Lookup(string key) => manager.GetStringTable("cn").GetValueOrDefault(key);
                Directory.CreateDirectory(Path.Combine("tmp", "ui-20261019-previews"));
                images.BeginFrame();
                var frame = ProjectDetailPreview.Army(layout, army, "突击步兵", features, image => images.Resolve(image, false) != null);
                using var rendered = GameUiLayoutRenderer.Render(frame, fonts, image => images.Resolve(image), Lookup, typefaces.Get);
                Check(rendered.HitRegions.Count(region => region.Kind == GameUiHitKind.ArmyFeature) == army.Feature.Count, "feature hit regions missing");
                Check(!images.Issues.Any(issue => issue.Contains("legion_icon_2") || issue.Contains("feat_4_1")), "real army art not found");
                using (var png = rendered.Bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(Path.Combine("tmp", "ui-20261019-previews", "army.png"), png.ToArray());
                army.HP += 100; var changed = ProjectDetailPreview.Army(layout, army, "草稿突击步兵", features, image => images.Resolve(image, false) != null);
                using var updated = GameUiLayoutRenderer.Render(changed, fonts, image => images.Resolve(image), Lookup, typefaces.Get);
                Check(!updated.Bitmap.GetPixelSpan().SequenceEqual(rendered.Bitmap.GetPixelSpan()), "army preview stale after draft edit");
                images.BeginFrame();
                var countryFrame = ProjectDetailPreview.Country(layout, country, context, Lookup($"country_{country.Id}") ?? country.Name);
                using var countryImage = GameUiLayoutRenderer.Render(countryFrame, fonts, image => images.Resolve(image), Lookup, typefaces.Get);
                Check(!images.Issues.Any(issue => issue.Contains("flag_12")), "country flag not read from current project");
                using (var png = countryImage.Bitmap.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(Path.Combine("tmp", "ui-20261019-previews", "country.png"), png.ToArray());
                Check(layout.Dump() == baseline && army.FeatureLevel.Count == 0, "render wrote layout or levels");
                Console.WriteLine("Detail preview diagnostics: " + string.Join(" | ", rendered.Issues.Concat(countryImage.Issues).Concat(images.Issues)));
            }
            finally { if (loaded && Directory.Exists(previous)) manager.Scan(previous, true); else manager.Clear(); }
        }));
    }

    public static void RunRendering(Action<string, Action> test)
    {
        test("UI workflow: static layout images retain project tint and alpha", () =>
        {
            var root = new LayoutNode { Tag = "TmpWindow" }; root.Set("width", "80"); root.Set("height", "120");
            var image = new LayoutNode { Tag = "Image", Parent = root };
            image.Set("image", "white.png"); image.Set("width", "40"); image.Set("height", "30");
            image.Set("margin", "10,65"); image.Set("color", "41,75,98,255"); root.Children.Add(image);
            var frame = new GameUiPreviewFrame(root, "", "", []);
            using var bitmap = new SKBitmap(4, 4); bitmap.Erase(SKColors.White);
            using var result = GameUiLayoutRenderer.Render(frame, new GameUiFontCatalog(),
                reference => reference == "white.png" ? new(bitmap, SKRect.Create(4, 4), "test") : null, _ => null, _ => SKTypeface.Default);
            var pixel = result.Bitmap.GetPixel((int)image.X + 2, (int)image.Y + 2);
            Check(pixel.Red == 41 && pixel.Green == 75 && pixel.Blue == 98 && pixel.Alpha == 255, "project RGBA tint ignored");
        });
    }

    private static LayoutDocument Parse(string text) { var document = new LayoutDocument(); document.Load(text, "layout.xml"); return document; }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException) { return; }
        throw new Exception("Expected operation rejection.");
    }
    private static void WithFixture(Action<string> action)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { action(dir); } finally { Directory.Delete(dir, true); }
    }
}
