using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Rendering.Imaging;

static class GameUiPreviewTests
{
    private const string Layout = """
        <Layouts><Template id="tpl_general_photo"><GroupBox id="gb_photo" width="100" height="100" clipped="true">
        <Image id="img_bg_general"/><Image id="img_quality"><Label id="lbl_name"/></Image><Image id="img_general_photo"/>
        <Image id="img_general_head"><Image id="img_head"/></Image></GroupBox>
        <GroupBox id="gb_rank"><Image id="icon_rank"/><Label id="lbl_rank"/><Label id="lbl_hp"/></GroupBox></Template>
        <TmpWindow id="form_generalinfo" width="300" height="200" text="general_info">
        <PlaceHolder templateid="tpl_general_photo"/><Repeater id="rpt_ability" width="180" height="60" itemwidth="30" itemheight="60" itemcount="6">
        <Image id="img_ability_logo"/><GroupBox id="gb_ability_star"/></Repeater>
        <Repeater id="rpt_skill" itemwidth="50" itemheight="60" itemcount="5" width="250" height="60" margin="0,110">
        <Image id="img_back"/><Button id="btn_empty_skill"/><Image id="img_skill_icon"/><Image id="img_bg_level"><Label id="lbl_level"/></Image></Repeater>
        </TmpWindow>
        <TmpWindow id="form_generalpromotion" width="300" height="200"><PlaceHolder templateid="tpl_general_photo"/>
        <Repeater id="rpt_skill" itemwidth="50" itemheight="60" itemcount="5" width="250" height="60">
        <Image id="img_back"/><Button id="btn_empty_skill"/><Image id="img_skill_icon"/><Image id="img_bg_level"><Label id="lbl_level"/></Image></Repeater></TmpWindow>
        <TmpWindow id="form_upgradeskill" width="830" height="387">
        <GroupBox id="gb_left" width="360" height="285" margin="30,75"><Image id="img_skill_icon"/><Label id="lbl_skill_level"/>
        <Label id="lbl_skill_name"/><Label id="lbl_skill_info" font="font_text_2" breakwords="true"/></GroupBox>
        <GroupBox id="gb_right" width="360" height="285" margin="0,75,30,0" halign="right"><Image id="img_skill_icon"/><Label id="lbl_skill_level"/>
        <Label id="lbl_skill_name"/><Label id="lbl_skill_info" font="font_text_2" breakwords="true"/></GroupBox><Label id="lbl_medal"/></TmpWindow></Layouts>
        """;
    private const string FontXml = """<FontResource><PHONE><HD><file name="font_text_2" lang="true" size="18" file="NotoSans" ext="otf" outline="1"/></HD></PHONE><PAD><HD><file name="font_text_2" lang="true" size="36" file="NotoSans" ext="otf" outline="2"/></HD></PAD></FontResource>""";

    public static void Run(Action<string, Action> test)
    {
        test("live general binding isolates source layout, respects fallback portraits and diagnoses true slot overflow", () =>
        {
            var layout = Parse(Layout); string original = layout.Dump();
            var general = new GeneralSettingData { Id = 1, Name = "draft name", EName = "Test", Type = 3, Skills = [1245], SkillsMax = 0, Armor = 6, ArmorMax = 6 };
            var skill = new SkillSettingData { Id = 1245, Type = 24, Level = 5 };
            var frame = Build(layout, general, [skill], image => !image.Contains("generalphoto"));
            Check(GameUiPreview.Find(frame.Root, "lbl_name")?.Get("string") == "draft name", "unsaved name not bound");
            Check(GameUiPreview.Find(frame.Root, "img_general_photo")?.Get("visible") == "false" &&
                GameUiPreview.Find(frame.Root, "img_general_head")?.Get("visible") == "true", "missing half portrait not handled");
            Check(!frame.Issues.Any(issue => issue.Contains("超过 SkillsMax")), "normal NPC SkillsMax=0 treated as error");
            general.SkillsMax = 6; general.Skills = [1245, 1245, 1245, 1245, 1245, 1245]; general.Name = "changed";
            var next = Build(layout, general, [skill]);
            Check(next.Issues.Any(issue => issue.Contains("布局只有 5")), "layout budget not enforced");
            Check(GameUiPreview.Find(frame.Root, "lbl_name")?.Get("string") == "draft name", "prior preview aliases new binding");
            Check(layout.Dump() == original && layout.Forms.All(form => form.Items == null), "runtime binding changed source XML");
        });

        test("skill UI follows appended unsaved Lv6, handles broken chains and probability descriptions", () => WithFixture(dir => WithAssets(() =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json"));
            File.WriteAllText(Path.Combine(assets, "json/SkillSettings.json"), """[{"Id":1245,"Type":24,"Level":5,"SkillEffect":25,"UpgradeId":0,"Custom":{"keep":true}}]""");
            File.WriteAllText(Path.Combine(assets, "stringtable_tw.ini"), "skill_name_24=鋼鐵洪流\nskill_info_24=傷害+[#00FF00#%d]\n");
            AssetManager.Default.Scan(assets, true); SkillSettingParser.ClearInstance();
            var parser = SkillSettingParser.Instance; var previous = parser.GetById(1245)!; var added = parser.AppendNextLevel(previous);
            added.SkillEffect = 30; added.CostMedal = 500;
            var layout = Parse(Layout);
            var frame = GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, Mode = GameUiPreviewMode.SkillUpgrade, SkillId = 1245,
                Skills = parser.All, StringLookup = key => key == "skill_info_24" ? parser.GetSkillDesc(24) : parser.GetSkillName(24) });
            var right = GameUiPreview.Find(frame.Root, "gb_right")!;
            Check(GameUiPreview.Find(right, "lbl_skill_level")?.Get("string") == "LV 6" &&
                GameUiPreview.Find(right, "lbl_skill_info")?.Get("string")?.Contains("30") == true &&
                GameUiPreview.Find(frame.Root, "lbl_medal")?.Get("string") == "500", "appended draft ignored or wrong upgrade cost");
            Check(!File.ReadAllText(Path.Combine(assets, "json/SkillSettings.json")).Contains("1246"), "preview saved the skill draft");
            added.UpgradeId = 1245;
            Check(GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, Mode = GameUiPreviewMode.SkillUpgrade,
                SkillId = 1245, Skills = parser.All }).Issues.Any(issue => issue.Contains("有环")), "chain cycle not diagnosed");
            added.UpgradeId = 9999;
            Check(GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, Mode = GameUiPreviewMode.SkillUpgrade,
                SkillId = added.Id, Skills = parser.All }).Issues.Any(issue => issue.Contains("不存在")), "dangling UpgradeId ignored");
            var probability = new SkillSettingData { IfPercent = 1, ActivatesChance = 12, SkillEffect = 0 };
            Check(GameUiPreview.FormatSkillDescription("%d%%", probability) == "12%", "probability displayed as zero");
            var commanderChance = new SkillSettingData { Type = 19, IfPercent = 1, ActivatesChance = 40, SkillEffect = 150 };
            Check(GameUiPreview.FormatSkillDescription("有[#00FF00#%d%%]概率", commanderChance) == "有[#00FF00#40%]概率" &&
                GameUiPreview.FormatSkillDescription("%d%% chance", commanderChance) == "40% chance", "commander probability used SkillEffect or locale changed its parameter");
            Check(GameUiPreview.FormatSkillDescription("12%%概率回避%d%%伤害", new SkillSettingData { Type = 32, SkillEffect = 90, ActivatesChance = 12, IfPercent = 1 }) ==
                "12%概率回避90%伤害", "fixed probability template substituted the wrong field");
            var issues = new List<string>();
            Check(GameUiPreview.FormatSkillDescription("%d/%d", probability, issues) == "12/%d" && issues.Count == 1, "unknown second argument guessed");
        })));

        test("layout entities font profiles invalid repeaters and template cycles are checked", () =>
        {
            var layout = Parse("""<Layouts><Form id="a"><Label string="A &amp; B &lt; C &#65;"/></Form></Layouts>""");
            Check(layout.Forms[0].Children[0].Get("string") == "A & B < C A", "XML entities displayed literally");
            Check(Parse(layout.Dump()).Dump() == layout.Dump(), "XML entities repeatedly escaped on save");
            var entities = Parse("""<Layouts><Form id="a" width="100"><Label string="&quot;&apos;&gt;&#x4E2D;">A &amp; B &lt; C &#65; &#x4E2D;</Label></Form></Layouts>""");
            entities.Forms[0].Set("width", "120");
            var label = System.Xml.Linq.XDocument.Parse(entities.Dump()).Descendants("Label").Single();
            Check((string?)label.Attribute("string") == "\"'>中" && label.Value == "A & B < C A 中", "saved attribute/text XML semantics changed");
            Check(Parse(entities.Dump()).Dump() == entities.Dump(), "text/numeric entities escaped on second save");
            var phone = GameUiFontCatalog.Parse(FontXml); var pad = GameUiFontCatalog.Parse(FontXml, "tw", "PAD/HD");
            Check(phone.Get("font_text_2").Size == 18 && pad.Get("font_text_2").Size == 36 &&
                pad.Get("font_text_2").RelativePath == "font/NotoSans_tw.otf", "game font profile or locale ignored");
            Reject(() => GameUiFontCatalog.Parse(FontXml.Replace("size=\"18\"", "size=\"99999\"")));
            Reject(() => GameUiFontCatalog.Parse(FontXml.Replace("size=\"18\"", "size=\"NaN\"")));
            Reject(() => Build(Parse(Layout.Replace("itemcount=\"5\"", "itemcount=\"999999\"")), new GeneralSettingData(), []));
            Reject(() => Build(Parse("""<Layouts><Form id="form_generalinfo" width="100" height="100"><Repeater itemcount="128"><Repeater itemcount="128"><Label/></Repeater></Repeater></Form></Layouts>"""), new GeneralSettingData(), []));
            Reject(() => Build(Parse("""<Layouts><Template id="a"><PlaceHolder templateid="a"/></Template><Form id="form_generalinfo" width="100" height="100"><PlaceHolder templateid="a"/></Form></Layouts>"""), new GeneralSettingData(), []));
        });

        test("general promotion binding checks ownership, links and keeps the original general draft", () =>
        {
            var general = new GeneralSettingData { Id = 1018, Name = "朱可夫", Photo = "Zhukov", Evaluate = 3, Skills = [1245] };
            var promotion = new GeneralPromotionData { Id = 10181, BaseID = 1018, AdvanceID = 99999, Photo = "Zhukov2", Evaluate = 3, Skills = [1246], SkillsMax = 5, CostMedal = 123 };
            var frame = GameUiPreview.Build(new GameUiPreviewInput { Layout = Parse(Layout), Mode = GameUiPreviewMode.Promotion, General = general,
                PromotionId = promotion.Id, Promotions = [promotion], Skills = [new() { Id = 1246, Type = 24, Level = 6 }] });
            Check(GameUiPreview.Find(frame.Root, "img_general_photo")?.Get("image") == "image/generalphoto/general_Zhukov2.png" &&
                frame.Issues.Any(issue => issue.Contains("AdvanceID=99999")) && frame.Details.Contains("勋章 123"), "promotion data not bound");
            Check(general.Photo == "Zhukov" && general.Skills.SequenceEqual([1245]), "preview mutated base general");
            Reject(() => GameUiPreview.Build(new GameUiPreviewInput { Layout = Parse(Layout), Mode = GameUiPreviewMode.Promotion,
                General = general, PromotionId = 1, Promotions = [promotion] }));
        });

        test("audit permits NPC zero skill budget while detecting player and promotion overflows", () => WithFixture(dir =>
        {
            string assets = Path.Combine(dir, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json"));
            File.WriteAllText(Path.Combine(assets, "json/GeneralSettings.json"), """[{"Id":1,"Type":3,"SkillsMax":0,"Skills":[10]},{"Id":2,"Type":1,"SkillsMax":0,"Skills":[10]}]""");
            File.WriteAllText(Path.Combine(assets, "json/SkillSettings.json"), """[{"Id":10}]""");
            File.WriteAllText(Path.Combine(assets, "json/GeneralPromotionSettings.json"), """[{"Id":3,"BaseID":2,"SkillsMax":0,"Skills":[10]}]""");
            var report = new AssetRelationshipAnalyzer().Analyze(assets);
            Check(!report.Diagnostics.Any(d => d.Code == "skill-max" && d.Source == "GeneralSettings.json" && d.Id == 1) &&
                report.Diagnostics.Count(d => d.Code == "skill-max") == 2, "NPC exception hides real player/promotion problems");
        }));

        test("preview PNG export refuses project paths and aliases while allowing a separate review directory", () => WithFixture(dir =>
        {
            string source = Path.Combine(dir, "source"); Directory.CreateDirectory(Path.Combine(source, "assets/json"));
            File.WriteAllText(Path.Combine(source, "assets/json/SkillSettings.json"), "[]");
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            Reject(() => GameUiPreviewExport.ValidateDestination(Path.Combine(source, "preview.png"), project.AssetsRoot));
            Reject(() => GameUiPreviewExport.ValidateDestination(Path.Combine(project.OutputRoot, "preview.png"), project.AssetsRoot));
            Reject(() => GameUiPreviewExport.ValidateDestination(Path.Combine(dir, "preview.webp"), project.AssetsRoot));
            Check(GameUiPreviewExport.ValidateDestination(Path.Combine(dir, "preview.png"), project.AssetsRoot) == Path.Combine(dir, "preview.png"), "separate review directory rejected");
            if (!OperatingSystem.IsWindows())
            {
                string alias = Path.Combine(dir, "alias"); Directory.CreateSymbolicLink(alias, source);
                Reject(() => GameUiPreviewExport.ValidateDestination(Path.Combine(alias, "preview.png"), project.AssetsRoot));
                Directory.Delete(alias);
            }
        }));
    }

    public static void RunRendering(Action<string, Action> test)
    {
        test("project UI image scope, overlay revision and missing pages preserve correct pixels", () => WithFixture(dir => WithAssets(() =>
        {
            string source = Path.Combine(dir, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(Path.Combine(assets, "image"));
            foreach (var (name, color) in new[] { ("image_ui_hd5", SKColors.Red), ("image_warzone_hd", SKColors.Green) })
            {
                File.WriteAllText(Path.Combine(assets, $"image/{name}.xml"), $"<Texture name=\"{name}.png\"/><Images><Image name=\"window_title_bg.png\" x=\"0\" y=\"0\" w=\"2\" h=\"2\"/></Images>");
                using var bitmap = new SKBitmap(4, 4); bitmap.Erase(color);
                File.WriteAllBytes(Path.Combine(assets, $"image/{name}.png"), AtlasCompositeRenderer.Encode(bitmap, "image.png"));
            }
            var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output")); AssetManager.Default.Scan(project.AssetsRoot, true);
            using var images = new ProjectUiImageSource();
            Check(images.Resolve("window_title_bg.png")?.Page.GetPixel(0, 0) == SKColors.Red &&
                images.Resolve("image/image_warzone_hd/window_title_bg.png")?.Page.GetPixel(0, 0) == SKColors.Green, "atlas scope ignored");
            var scoped = WC4MapEditor.Views.Assist.LayoutImageProvider.Resolve("image/image_warzone_hd/window_title_bg.png");
            Check(scoped?.Ready == true && scoped.Page!.GetPixel(0, 0) == SKColors.Green, "layout editor and preview disagree on atlas scope");
            using var changed = new SKBitmap(4, 4); changed.Erase(SKColors.Blue);
            WC4MapEditor.Core.Parsers.AtomicFile.Write(Path.Combine(project.AssetsRoot, "image/image_ui_hd5.png"), AtlasCompositeRenderer.Encode(changed, "image.png"));
            Check(images.Resolve("window_title_bg.png")?.Page.GetPixel(0, 0) == SKColors.Blue, "saved overlay did not invalidate UI pixels");
            Check(images.Resolve("image/generalphoto/window_title_bg.png") == null, "unrelated atlas shadowed explicit loose image path");
        })));

        test("UI rich text wrapping clipping and skill hit regions render from bound layout", () =>
        {
            var layout = Parse("""<Layouts><Form id="form_upgradeskill" width="250" height="150"><GroupBox id="gb_left" width="180" height="100" clipped="true"><Label id="lbl_skill_info" width="150" height="70" breakwords="true" font="font_text_2"/></GroupBox><GroupBox id="gb_right" width="10" height="10"/></Form></Layouts>""");
            var skill = new SkillSettingData { Id = 1, Type = 24, Level = 5, SkillEffect = 25 };
            var frame = GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, Mode = GameUiPreviewMode.SkillUpgrade, SkillId = 1, Skills = [skill],
                StringLookup = _ => "prefix [#00FF00#%d] and a long description that should wrap into multiple lines" });
            using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.Black);
            using var result = GameUiLayoutRenderer.Render(frame, GameUiFontCatalog.Parse(FontXml), _ => new(bitmap, SKRect.Create(2, 2), "fixture"), _ => "text", _ => SKTypeface.Default);
            Check(result.HitRegions.Single().SkillId == 1 && result.HitRegions.Single().Bounds.Width <= 180, "skill click area not clipped");
            Check(result.Bitmap.Pixels.Any(pixel => pixel.Green > pixel.Red + 30 && pixel.Green > pixel.Blue + 30), "game colored text not rendered");
            frame.Root.Set("width", "99999"); Reject(() => { using var _ = GameUiLayoutRenderer.Render(frame, GameUiFontCatalog.Parse(FontXml), _ => null, _ => null, _ => SKTypeface.Default); });
        });

        test("clipped portrait scaling does not inflate the exported UI canvas", () =>
        {
            var layout = Parse("""<Layouts><Form width="250" height="150"><GroupBox width="100" height="100" clipped="true"><Image image="portrait.png" width="1500" height="1000"/></GroupBox></Form></Layouts>""");
            using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.Red);
            using var result = GameUiLayoutRenderer.Render(new(layout.Forms[0], "", "", []), GameUiFontCatalog.Parse(FontXml),
                _ => new(bitmap, SKRect.Create(2, 2), "fixture"), _ => "", _ => SKTypeface.Default);
            Check(result.Bitmap.Width == 274 && result.Bitmap.Height == 174, "clipped body expanded the exported canvas");
        });
    }

    public static void RunRenderingCorpus(Action<string, Action> test, string corpus)
    {
        test("real 1.30.0 general promotion and Lv5-to-Lv6 previews use project art without source writes", () => WithFixture(dir => WithAssets(() =>
        {
            string source = Path.Combine(corpus, "wc4", "World Conqueror 4_1.30.0");
            var hashes = Hashes(source); var project = GameProjectWorkspace.Create(source, Path.Combine(dir, "output"));
            AssetManager.Default.Scan(project.AssetsRoot, true); SkillSettingParser.ClearInstance();
            var skills = SkillSettingParser.Instance; var generals = new GeneralSettingParser(project.AssetsRoot);
            var promotions = Read<GeneralPromotionData>("GeneralPromotionSettings.json");
            var medals = Read<GeneralMedalPreviewData>("GeneralMedalSettings.json");
            var layout = Parse(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "layout_x.xml")));
            string originalLayout = layout.Dump();
            var strings = AssetManager.Default.GetStringTable("cn");
            var fonts = GameUiFontCatalog.Parse(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "font_resource.xml")));
            using var images = new ProjectUiImageSource(); using var faces = new ProjectUiFontSource();
            string outputs = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "general-ui-preview"); Directory.CreateDirectory(outputs);
            var samples = new List<(string Name, GeneralSettingData? General, GameUiPreviewMode Mode, int? Promotion, int? Skill)>
            {
                ("guderian", generals.All.Single(g => g.Id == 1049), GameUiPreviewMode.General, null, null),
                ("rommel", generals.All.Single(g => g.Id == 1051), GameUiPreviewMode.General, null, null),
                ("zhukov", generals.All.Single(g => g.Id == 1018), GameUiPreviewMode.General, null, null),
                ("zhukov-promotion", generals.All.Single(g => g.Id == 1018), GameUiPreviewMode.Promotion, 10181, null),
                ("npc-head-only", generals.All.First(g => g.Type == 3 && !ProjectFiles.Exists(generals.GetGeneralPhotoPath(GeneralSettingParser.GetPhotoKey(g))) &&
                    ProjectFiles.Exists(generals.GetHeadPath(GeneralSettingParser.GetPhotoKey(g)))), GameUiPreviewMode.General, null, null)
            };
            var fifth = skills.GetById(1245)!; var sixth = skills.AppendNextLevel(fifth); sixth.SkillEffect = 30; sixth.CostMedal = 500;
            samples.Add(("steel-flood-lv5-to-lv6", null, GameUiPreviewMode.SkillUpgrade, null, fifth.Id));
            foreach (var sample in samples)
            {
                images.BeginFrame();
                var frame = GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, Mode = sample.Mode, General = sample.General,
                    PromotionId = sample.Promotion, SkillId = sample.Skill, Skills = skills.All, Promotions = promotions, Medals = medals,
                    PortraitLookup = generals.GetPortrait, StringLookup = key => strings.GetValueOrDefault(key), ImageExists = reference => images.Resolve(reference, false) != null });
                using var rendered = GameUiLayoutRenderer.Render(frame, fonts, reference => images.Resolve(reference), key => strings.GetValueOrDefault(key), faces.Get);
                using var encoded = rendered.Bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(Path.Combine(outputs, sample.Name + ".png"), encoded.ToArray());
                File.WriteAllText(Path.Combine(outputs, sample.Name + "-diagnostics.txt"), string.Join("\n", rendered.Issues.Concat(images.Issues)));
                var missing = rendered.Issues.Concat(images.Issues).Where(issue => issue.StartsWith("缺少图片") || issue.StartsWith("图片无法解码")).ToArray();
                Check(missing.Length == 0, sample.Name + " missing art: " + string.Join("; ", missing));
                Check(rendered.Bitmap.Width > 800 && rendered.Bitmap.Height > 380 && rendered.Bitmap.Pixels.Distinct().Take(100).Count() == 100, "blank real preview");
                if (sample.Mode == GameUiPreviewMode.General) Check(rendered.HitRegions.Count == sample.General!.Skills.Count(id => id != 0), "general skill regions missing");
                if (sample.Name == "steel-flood-lv5-to-lv6") Check(GameUiPreview.Find(GameUiPreview.Find(frame.Root, "gb_right")!, "lbl_skill_level")?.Get("string") == "LV 6", "real Lv6 draft absent");
            }
            Check(faces.MissingFonts.Count == 0 && layout.Dump() == originalLayout, "project fonts absent or original layout changed");
            Check(Directory.EnumerateFiles(project.OutputRoot, "*", SearchOption.AllDirectories).Count() == 1, "preview wrote game resources");
            Check(hashes.OrderBy(pair => pair.Key).SequenceEqual(Hashes(source).OrderBy(pair => pair.Key)), "preview changed source files");
            Console.WriteLine($"General UI corpus: {samples.Count} PNG previews; {hashes.Count} original files unchanged; Lv6 draft not saved. Images: {outputs}");

            List<T> Read<T>(string name) => JsonSerializer.Deserialize<List<T>>(ProjectFiles.ReadAllText(Path.Combine(project.AssetsRoot, "json", name)))!;
        })));
    }

    private static GameUiPreviewFrame Build(LayoutDocument layout, GeneralSettingData general, IReadOnlyList<SkillSettingData> skills, Func<string, bool>? images = null) =>
        GameUiPreview.Build(new GameUiPreviewInput { Layout = layout, General = general, Skills = skills, ImageExists = images ?? (_ => true), StringLookup = key => key });
    private static LayoutDocument Parse(string xml) { var layout = new LayoutDocument(); layout.Load(xml); return layout; }
    private static Dictionary<string, string> Hashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file =>
        Path.GetRelativePath(root, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    private static void WithAssets(Action action)
    {
        string previous = AssetManager.Default.AssetsRoot;
        try { action(); }
        finally { SkillSettingParser.ClearInstance(); if (Directory.Exists(previous)) AssetManager.Default.Scan(previous, true); else AssetManager.Default.Clear(); }
    }
    private static void WithFixture(Action<string> action)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { action(dir); } finally { Directory.Delete(dir, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException) { return; }
        throw new Exception("Expected UI preview validation failure.");
    }
}
