using System.Text.Json;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Skia;

internal static class WindowsReviewTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Windows review: external coordinates preserve raw records and match camera, brush and lookup", () =>
        {
            foreach (int capture in new[] { 0, 33, 145 })
            {
                var map = ExternalMap(capture);
                var camera = new Camera { HexColumnOffset = capture, OffsetX = 47, OffsetY = 59, ZoomLevel = 1.3 };
                for (int row = 0; row < map.MapHeight; row++)
                for (int col = 0; col < map.MapWidth; col++)
                {
                    int raw = (row + 5) * 148 + (capture + col) % 148;
                    Check(map.ToRecordCoordinate(col, row) == raw && map.ToLocalCoordinate(raw) == new HexCoord(col, row), "world/capture conversion");
                    var screen = camera.HexToScreen(col, row);
                    Check(camera.ScreenToHex(screen.Item1, screen.Item2) == (col, row), "click/draw mismatch");
                    Check(Math.Abs(screen.Item2 - (59 + (row + (((capture + col) & 1) == 1 ? 0.5 : 0)) * Camera.HexVerticalSpacing * 1.3)) < 1e-6,
                        "camera does not match world column parity");
                    var expected = map.GetNeighbors(col, row).Append(new(col, row))
                        .Where(cell => (uint)cell.Col < (uint)map.MapWidth && (uint)cell.Row < (uint)map.MapHeight).Select(cell => (cell.Col, cell.Row)).ToHashSet();
                    Check(expected.SetEquals(BrushEngine.GetHexesInRadius(col, row, 1, map.MapWidth, map.MapHeight, map.HexColumnOffset)), "brush shape parity");
                }
                var building = new BuildingModifier(); building.Initialize(map);
                Check(building.Apply(11, 0).Success && map.Buildings.Single().Coordinate == 5 * 148 + (capture + 11) % 148 &&
                    map.GetBuildingAt(11, 0).HasValue && map.FindBuildingIndex(11, 0) == 0, "building creation/lookup");
                map.SetProvince(1, 0, Province.Create((ushort)map.Buildings[0].Coordinate));
                map.SetBelongValue(11, 0, 1);
                Check(map.GetBelongIndex(11, 0) == 11 && map.GetProvinceCapitalBelong(1, 0) == 1, "province owner indexed into raw plane");
                Check(building.MoveBuilding(11, 0, 2, 1).Success && !map.GetBuildingAt(11, 0).HasValue && map.GetBuildingAt(2, 1).HasValue &&
                    map.GetBelongValue(2, 1) == 1, "move left stale coord index");
                var army = new ArmyModifier(); army.Initialize(map);
                var army3 = new ArmyV3Modifier(); army3.Initialize(map);
                Check(army.Apply(4, 1).Success && army3.Apply(5, 1).Success && map.GetArmyAt(4, 1).HasValue && map.GetArmyV3At(5, 1).HasValue,
                    "troop creation/lookup");
                Check(map.DeepClone().ExternalWorldWidth == 148, "clone discarded runtime context");
                map.ArmiesV3.Clear();
                byte[] bytes = BTLParser.SaveToBytes(map);
                Check(bytes.SequenceEqual(BTLParser.SaveToBytes(BTLParser.LoadFromBytes(bytes, ""))), "raw BTL changed after serialize");
            }
        });

        test("Windows review: batch generation does not duplicate raw coordinates or overflow narrow records", () =>
        {
            var map = ExternalMap(145);
            for (int i = 0; i < map.TerrainCount; i++) map.SetBelongValueByIndex(i, 0);
            var trap = new TrapModifier(); trap.Initialize(map);
            Check(trap.Apply(2, 1).Success, "trap setup");
            int raw = map.Traps[0].Coordinate;
            Check(trap.BatchGenerateTraps(100).Success && map.Traps.Count == map.TerrainCount && map.Traps.Count(t => t.Coordinate == raw) == 1,
                "trap batch duplicate/capture gap");
            var reinforcement = new ReinforcementModifier(); reinforcement.Initialize(map);
            var reinforcement3 = new ReinforcementV3Modifier(); reinforcement3.Initialize(map);
            Check(reinforcement.Apply(2, 1).Success && reinforcement3.Apply(2, 1).Success, "reinforcement setup");
            reinforcement.AddBelongToReinforcementCells(0); reinforcement3.AddBelongToReinforcementCells(0);
            Check(map.Reinforcements.Count == map.TerrainCount && map.ReinforcementsV3.Count == map.TerrainCount &&
                map.Reinforcements.Count(r => r.Coordinate == raw) == 1 && map.ReinforcementsV3.Count(r => r.Coordinate == raw) == 1, "reinforcement duplicate");
            map.ReinforcementsV3.Clear();
            map.Header.MapClipY = 225;
            byte[] before = BTLParser.SaveToBytes(map);
            Check(!trap.BatchGenerateTraps(100).Success && before.SequenceEqual(BTLParser.SaveToBytes(map)), "signed world coordinate overflow was partial");
        });

        test("Windows review: building overwrite undo and redo use complete independent snapshots", () =>
            BuildingCommands(BTLParser.CreateNew(8, 5)));

        test("Windows review: capital toggles and both province generators retain raw world references", () =>
        {
            foreach (int capture in new[] { 0, 33, 145 })
            {
                var map = ExternalMap(capture);
                int raw = map.ToRecordCoordinate(1, 1);
                var capital = new CapitalModifier(); capital.Initialize(map);
                Check(capital.ToggleCapital(raw).Success && capital.GetDataAt(1, 1) is Capital { Coordinate: var coordinate } && coordinate == raw,
                    "capital toggle uses local reference");
                Check(capital.ToggleCapital(raw).Success && map.Capitals.Count == 0 && capital.Apply(1, 1).Success, "capital toggle/remove/apply");
                for (int i = 0; i < map.TerrainCount; i++) map.GetTerrainRef(i).TileType1 = 0;
                map.SetProvince(1, 1, Province.Create((ushort)raw));
                int width = (int)Math.Ceiling((map.MapWidth + 1) * Camera.HexHorizontalSpacing);
                int height = (int)Math.Ceiling((map.MapHeight + 1) * Camera.HexVerticalSpacing);
                var mask = new bool[width * height];
                var screen = new Camera { HexColumnOffset = capture }.HexToScreen(map.MapWidth - 1, 0);
                mask[(int)Math.Round(screen.Item2) * width + (int)Math.Round(screen.Item1)] = true;
                var drawn = DrawnBoundaryProvinceGenerator.Generate(map, mask, width, height, dilateCells: 0);
                Check(drawn is { FilledCells: > 0 } && map.Provinces.All(province => province.ProvinceValue == raw), "drawn boundary used local capital ID");
                for (int i = 0; i < map.TerrainCount; i++) map.SetProvince(i, Province.CreateDefault());
                map.SetProvince(1, 1, Province.Create((ushort)raw));
                var background = BackgroundProvinceGenerator.Generate(map, new WhiteImage(), darkThreshold: 127, gradientThreshold: 0,
                    minRegionPixels: 1, minBoundaryPixels: 1);
                Check(background is { FilledCells: > 0 } && map.Provinces.All(province => province.ProvinceValue == raw), "background generator used local capital ID");
                map.Header.MapClipY = 500;
                map.Capitals.Clear(); map.Capitals.Add(new Capital { Coordinate = map.ToRecordCoordinate(1, 1) });
                var before = map.Provinces.Select(province => province.ProvinceValue).ToArray();
                Reject(() => DrawnBoundaryProvinceGenerator.Generate(map, mask, width, height));
                Reject(() => BackgroundProvinceGenerator.Generate(map, new WhiteImage()));
                Check(before.SequenceEqual(map.Provinces.Select(province => province.ProvinceValue)), "invalid raw province reference partially changed map");
            }
        });

        test("Windows review: building empty clipboard, failures, levels and groups obey history and notifications", () =>
            BuildingClipboard(BTLParser.CreateNew(8, 5)));
        test("Windows review: no-history building operations still detect no-op and roll back failures", () =>
        {
            var manager = new EditModeManager();
            var map = BTLParser.CreateNew(4, 3); map.IsModified = false;
            manager.Initialize(map);
            try
            {
                Check(!manager.RecordBuildingsSnapshot("empty", () => map.IsModified = true) && !map.IsModified, "no history reports fake change");
                byte[] before = BTLParser.SaveToBytes(map);
                Reject(() => manager.RecordBuildingsSnapshot("fail", () =>
                {
                    map.AddBuilding(Building.CreateDefault(1));
                    map.SetBelongValue(1, 0, 0);
                    throw new InvalidOperationException("failure");
                }));
                Check(before.SequenceEqual(BTLParser.SaveToBytes(map)) && !map.IsModified, "no-history exception mutated data");
            }
            finally { manager.Deinitialize(); }
        });

        test("Windows review: WPF literal key values trigger their advertised modes", () =>
        {
            var manager = new EditModeManager(); manager.Initialize(BTLParser.CreateNew(4, 3)); manager.SetSceneType("stage");
            var keyboard = KeyboardManager.Instance;
            try
            {
                foreach (var (key, mode) in new[] { (35, EditMode.TerrainPaint), (36, EditMode.ProvinceEdit),
                    (37, EditMode.BuildingDeploy), (38, EditMode.ArmyDeploy), (39, EditMode.BelongEdit), (40, EditMode.LegionEdit), (42, EditMode.ReinforcementDeploy) })
                {
                    var binding = keyboard.GetBinding($"MODE_SWITCH_{mode}")!;
                    Check(binding.Matches(key, KeyModifiers.None), $"WPF key {key} contract differs");
                    binding.Callback!(); Check(manager.CurrentMode == mode, $"WPF key {key} switched wrong mode");
                }
                Check(KeyCodes.D0 == 34 && KeyCodes.D9 == 43, "WPF numeric endpoints");
            }
            finally { manager.Deinitialize(); }
        });

        test("Windows review: building numeric names reject truncation and messages are never null", () =>
        {
            Check(BuildingNameResolver.Resolve("-1") == 65535 && BuildingNameResolver.Resolve("0") == 0 &&
                BuildingNameResolver.Resolve("65535") == 65535, "valid name IDs");
            foreach (string invalid in new[] { "-2", "65536", "2147483648", "999999999999999999999999" }) Reject(() => BuildingNameResolver.Resolve(invalid));
            Check(default(ModifierResult).Message == "" && ModifierResult.Ok().Message == "", "nullable result message");
        });

        test("Windows review: skill explanations bind names, live levels and formatted project parameters", () =>
        {
            var skill = new SkillSettingData { Id = 10016, Name = "internal", Type = 1, Level = 6, SkillEffect = 48, ActivatesChance = 70 };
            string? Lookup(string key) => key switch { "skill_name_1" => "钢铁洪流", "skill_info_1" => "[#FFFF00#伤害+%d%%]", _ => null };
            var detail = ProjectDataDescriptions.Skill(10016, [skill], Lookup);
            Check(detail.Title.Contains("钢铁洪流") && detail.Title.Contains("Lv6") && detail.Description == "伤害+48%" && detail.Warning == null, "skill description");
            skill.SkillEffect = 63;
            Check(ProjectDataDescriptions.Skill(10016, [skill], Lookup).Description == "伤害+63%", "unsaved skill ignored");
            Check(ProjectDataDescriptions.Skill(0, [skill], Lookup).RecordId == null && ProjectDataDescriptions.Skill(42, [skill], Lookup).Warning != null, "missing/empty skill");
        });

        test("Windows review: feature descriptions use Type plus Level and report implicit/missing levels", () =>
        {
            ArmyFeatureData[] rows = [new() { Id = 201001, Type = 1, Level = 1, ActivatesChance = 100, FeatureEffect = 60 },
                new() { Id = 201002, Type = 1, Level = 2, ActivatesChance = 85, FeatureEffect = 75, SecondEffect = 3 }];
            string? Lookup(string key) => key switch { "feat_name_1" => "工兵", "feat_desc_1" => "地雷伤害-{1}%",
                "feat_desc_1_Lv2" => "概率{0}% · 减伤{1}% · 距离{2}", _ => null };
            var detail = ProjectDataDescriptions.Feature(1, 2, rows, Lookup);
            Check(detail.RecordId == 201002 && detail.Title.Contains("Lv2") && detail.Description == "概率85% · 减伤75% · 距离3" && detail.Warning == null, "feature parameters/suffix");
            var implicitLevel = ProjectDataDescriptions.Feature(1, null, rows, Lookup);
            Check(implicitLevel.Title.Contains("参考") && implicitLevel.Description == "地雷伤害-60%" && implicitLevel.Warning!.Contains("FeatureLevel"), "implicit runtime level invented");
            Check(ProjectDataDescriptions.Feature(1, 6, rows, Lookup).RecordId == null && ProjectDataDescriptions.Feature(99, 1, rows, Lookup).Warning != null,
                "missing feature was assigned another level");
        });

        test("Windows review: cached homepage scan preserves revision, explicit refresh finds full filenames", () =>
        {
            using var fixture = new Fixture();
            string assets = fixture.CreateSource(); var cache = new AssetCache();
            cache.Load(assets); long revision = cache.Revision;
            File.WriteAllBytes(Path.Combine(assets, "coastmask_hd.pkm"), [1, 2]);
            Check(cache.Load(assets) == 2 && cache.Revision == revision && cache.Query(nameContains: "coastmask_hd.pkm").Count == 0, "homepage rescan");
            cache.Load(assets, true);
            Check(cache.Revision > revision && cache.Query(nameContains: "coastmask_hd.pkm").Count == 1, "explicit filename refresh");
        });

        test("Windows review: cancelled activation retains project, scan revision and string draft", () =>
        {
            using var fixture = new Fixture();
            var session = new GameProjectDataSession();
            var first = GameProjectWorkspace.Create(fixture.CreateSource("first"), Path.Combine(fixture.Root, "out-a"));
            var second = GameProjectWorkspace.Create(fixture.CreateSource("second"), Path.Combine(fixture.Root, "out-b"));
            using var state = new AssetState();
            session.Activate(first); var draft = ConfigManager.Instance.GetStringTableParser(); draft.SetValue("draft", "keep");
            long revision = AssetManager.Default.Revision;
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Reject(() => session.Activate(second, cancellationToken: cancellation.Token));
            Check(session.Current == first && AssetManager.Default.AssetsRoot == first.AssetsRoot && AssetManager.Default.Revision == revision &&
                ReferenceEquals(draft, ConfigManager.Instance.GetStringTableParser()) && draft.IsDirty, "cancel discarded active state");
        });

        test("Windows review: JSON save preserves unrelated and conflicting drafts, reloads clean tables and isolates projects", () =>
        {
            using var fixture = new Fixture(); using var state = new AssetState();
            var session = new GameProjectDataSession();
            string source = fixture.CreateSource("first");
            File.WriteAllText(Path.Combine(source, "json", "SkillSettings.json"), "[{\"Id\":1245,\"Type\":24,\"Level\":5,\"SkillEffect\":25}]");
            File.WriteAllText(Path.Combine(source, "json", "OtherSettings.json"), "[{\"Id\":1,\"Value\":1}]");
            var first = GameProjectWorkspace.Create(source, Path.Combine(fixture.Root, "out-a"));
            session.Activate(first);
            var draft = ConfigManager.Instance.GetStringTableParser(); draft.SetValue("draft", "keep");
            var skills = SkillSettingParser.Instance;
            skills.GetById(1245)!.SkillEffect = 30;
            skills.SetSkillName(24, "skill draft");
            var other = new JsonTableDocument(Path.Combine(first.AssetsRoot, "json", "OtherSettings.json"));
            other.ApplyFields(0, [("Value", "2")]); other.Save();
            GameProjectDataSession.RefreshEditorFor(other.FilePath);
            Check(ReferenceEquals(draft, ConfigManager.Instance.GetStringTableParser()) && draft.GetValue("draft") == "keep" && draft.IsDirty,
                "generic save discarded string draft");
            Check(ReferenceEquals(skills, SkillSettingParser.Instance) && skills.GetById(1245)!.SkillEffect == 30 && skills.GetSkillName(24) == "skill draft",
                "unrelated table save discarded skill draft");
            var skillTable = new JsonTableDocument(skills.ConfigPath);
            skillTable.ApplyFields(0, [("SkillEffect", "40")]); skillTable.Save();
            GameProjectDataSession.RefreshEditorFor(skillTable.FilePath);
            Check(ReferenceEquals(skills, SkillSettingParser.Instance) && skills.HasChanges && skills.GetById(1245)!.SkillEffect == 30 && !skills.SaveAll(),
                "same-table save discarded draft or overwrote disk conflict");
            skills.Reload();
            Check(skills.GetById(1245)!.SkillEffect == 40 && !skills.HasChanges, "explicit reload did not resolve conflict");
            skillTable.ApplyFields(0, [("SkillEffect", "50")]); skillTable.Save();
            GameProjectDataSession.RefreshEditorFor(skillTable.FilePath);
            Check(!ReferenceEquals(skills, SkillSettingParser.Instance) && SkillSettingParser.Instance.GetById(1245)!.SkillEffect == 50,
                "clean related parser retained stale table");
            session.Activate(GameProjectWorkspace.Create(fixture.CreateSource("second"), Path.Combine(fixture.Root, "out-b")));
            Check(!ReferenceEquals(draft, ConfigManager.Instance.GetStringTableParser()) && ConfigManager.Instance.GetStringTableParser().GetValue("draft") == "", "draft leaked to another project");
            Check(SkillSettingParser.Instance.All.Count == 0, "skill draft leaked to another project");
        });

        test("Windows review: settings template has no credentials and environment supplies local key", () =>
        {
            string? previous = Environment.GetEnvironmentVariable("WC4_EDITOR_AI_API_KEY");
            try
            {
                foreach (string line in File.ReadAllLines(Path.Combine(Directory.GetCurrentDirectory(), "setting.txt")))
                {
                    int equals = line.IndexOf('=');
                    if (equals >= 0 && line[..equals].Trim().Equals("AIApiKey", StringComparison.OrdinalIgnoreCase))
                        Check(string.IsNullOrWhiteSpace(line[(equals + 1)..]), "template contains a credential");
                }
                Environment.SetEnvironmentVariable("WC4_EDITOR_AI_API_KEY", null);
                Check(ConfigManager.Instance.GetText("AIApiKey") == "", "template key read");
                Environment.SetEnvironmentVariable("WC4_EDITOR_AI_API_KEY", "local-test-value");
                Check(ConfigManager.Instance.GetText("AIApiKey") == "local-test-value", "environment key not read");
            }
            finally { Environment.SetEnvironmentVariable("WC4_EDITOR_AI_API_KEY", previous); }
        });
    }

    public static void RunRendering(Action<string, Action> test)
    {
        test("Windows review: external and local object layers render the same pixels at the same clicked cells", () =>
        {
            using var fixture = new Fixture(); using var state = new AssetState();
            AssetManager.Default.Scan(fixture.CreateSource()); ConfigManager.Instance.Reload();
            var external = ExternalMap(33);
            var local = BTLParser.CreateNew(external.MapWidth, external.MapHeight);
            var building = Building.CreateDefault(external.ToRecordCoordinate(11, 0)); building.BuildingType = 11; building.KeyPoint = 1;
            external.AddBuilding(building); building.Coordinate = 11; local.AddBuilding(building);
            var army = Army.CreateDefault(external.ToRecordCoordinate(2, 1)); external.AddArmy(army); army.Coordinate = 14; local.AddArmy(army);
            external.Capitals.Add(new() { Coordinate = external.ToRecordCoordinate(8, 2) }); local.Capitals.Add(new() { Coordinate = 32 });
            external.SetProvince(8, 2, Province.Create((ushort)external.Capitals[0].Coordinate)); local.SetProvince(8, 2, Province.Create(32));
            // The local fixture uses the same world parity to isolate raw-to-view conversion.
            var externalCamera = new Camera { HexColumnOffset = 33, OffsetX = 45, OffsetY = 65, ViewportWidth = 450, ViewportHeight = 260 };
            var localCamera = new Camera { HexColumnOffset = 33, OffsetX = 45, OffsetY = 65, ViewportWidth = 450, ViewportHeight = 260 };
            using var actual = new SKBitmap(450, 260); using var expected = new SKBitmap(450, 260);
            using (var canvas = new SKCanvas(actual))
            {
                canvas.Clear(SKColors.Transparent);
                using var buildings = new BuildingRender(externalCamera, external) { ShowBuildingNames = false }; buildings.Render(canvas);
                using var armies = new ArmyRender(externalCamera, external); armies.Render(canvas);
                using var capitals = new CapitalFlagRender { EnableCapitalFlagRender = true, OffsetX = 45, OffsetY = 65, ViewportWidth = 450, ViewportHeight = 260 };
                capitals.Render(canvas, external);
            }
            using (var canvas = new SKCanvas(expected))
            {
                canvas.Clear(SKColors.Transparent);
                using var buildings = new BuildingRender(localCamera, local) { ShowBuildingNames = false }; buildings.Render(canvas);
                using var armies = new ArmyRender(localCamera, local); armies.Render(canvas);
                // Capitals compute map parity directly, so compare them independently against their expected cell.
            }
            Check(actual.Pixels.Any(pixel => pixel.Alpha > 0), "objects invisible");
            var capitalScreen = externalCamera.HexToScreen(8, 2);
            Check(actual.GetPixel((int)capitalScreen.Item1, (int)capitalScreen.Item2).Alpha > 0, "capital world index did not draw at clicked cell");
            external.Capitals.Clear();
            using (var canvas = new SKCanvas(actual))
            {
                canvas.Clear(SKColors.Transparent);
                using var buildings = new BuildingRender(externalCamera, external) { ShowBuildingNames = false }; buildings.Render(canvas);
                using var armies = new ArmyRender(externalCamera, external); armies.Render(canvas);
            }
            Check(actual.Pixels.SequenceEqual(expected.Pixels), "external objects displaced relative to local reference");
        });
    }

    public static void RunCorpus(Action<string, Action> test, string root)
    {
        string source = Path.Combine(root, "wc4", "World Conqueror 4_1.30.0");
        test("Windows review: all 310 external 1.30.0 maps locate 5988 buildings and 15617 troops losslessly", () =>
        {
            using var fixture = new Fixture();
            var project = GameProjectWorkspace.Create(source, Path.Combine(fixture.Root, "output"));
            int maps = 0, buildings = 0, armies = 0;
            foreach (string file in Directory.EnumerateFiles(Path.Combine(source, "assets", "stage"), "*.btl"))
            {
                byte[] original = File.ReadAllBytes(file); var map = BTLParser.LoadFromBytes(original, file);
                if (map.Header.MapNumber == 0) continue;
                ProjectMapDocument.Attach(project, map); maps++;
                foreach (var building in map.Buildings)
                {
                    Check(map.TryGetLocalCoordinate(building.Coordinate, out var cell), $"building outside {file}");
                    Check(map.ToRecordCoordinate(cell.Col, cell.Row) == building.Coordinate && map.GetBuildingAt(cell.Col, cell.Row)!.Value.Equals(building), $"building lookup {file}"); buildings++;
                }
                foreach (int raw in map.Armies.Select(a => (int)a.Coordinate).Concat(map.ArmiesV3.Select(a => (int)a.Coordinate)))
                {
                    Check(map.TryGetLocalCoordinate(raw, out var cell) && map.ToRecordCoordinate(cell.Col, cell.Row) == raw &&
                        (map.GetArmyAt(cell.Col, cell.Row).HasValue || map.GetArmyV3At(cell.Col, cell.Row).HasValue), $"troop lookup {file}"); armies++;
                }
                Check(original.SequenceEqual(BTLParser.SaveToBytes(map)), $"untouched world coordinates rewritten {file}");
            }
            Check(maps == 310 && buildings == 5988 && armies == 15617, $"corpus count changed: {maps}/{buildings}/{armies}");
            Console.WriteLine($"External objects: {maps} maps / {buildings} buildings / {armies} troops, all located and raw bytes preserved.");
        });
        test("Windows review: real stage10001 conquest1 and wrapped stage60023 edits reopen with world coordinates", () =>
        {
            using var fixture = new Fixture();
            var project = GameProjectWorkspace.Create(source, Path.Combine(fixture.Root, "output"));
            foreach (string name in new[] { "stage10001", "conquest1", "stage60023" })
            {
                string relative = Path.Combine("stage", name + ".btl");
                var document = ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, relative)); var map = document.Map;
                if (name == "stage10001") Check(map.ToLocalCoordinate(784) == new HexCoord(11, 0), "stage10001 landmark");
                if (name == "conquest1") Check(map.ToLocalCoordinate(348) == new HexCoord(52, 0), "conquest1 landmark");
                var modifier = new BuildingModifier(); modifier.Initialize(map);
                var original = map.Buildings[0]; var cell = map.ToLocalCoordinate(original.Coordinate);
                var changed = original; changed.Reserved3 ^= 0x13579;
                Check(modifier.Apply(cell.Col, cell.Row, changed).Success, "real object edit failed");
                document.Save(Path.Combine(project.AssetsRoot, relative));
                var reopened = ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, relative)).Map;
                Check(reopened.GetBuildingAt(cell.Col, cell.Row)!.Value.Equals(changed) && reopened.Buildings.Skip(1).SequenceEqual(map.Buildings.Skip(1)), "edit reopened displaced/lost records");
                var empty = Enumerable.Range(0, reopened.TerrainCount).Select(i => new HexCoord(i % reopened.MapWidth, i / reopened.MapWidth))
                    .First(p => !reopened.GetBuildingAt(p.Col, p.Row).HasValue);
                modifier.Initialize(reopened); Check(modifier.Apply(empty.Col, empty.Row).Success, "new building failed");
                ProjectMapDocument.Attach(project, reopened).Save(Path.Combine(project.AssetsRoot, relative));
                Check(ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, relative)).Map.GetBuildingAt(empty.Col, empty.Row)!.Value.Coordinate ==
                    reopened.ToRecordCoordinate(empty.Col, empty.Row), "new building not world-indexed");
            }
            Check(Directory.EnumerateFiles(project.OutputRoot, "*.btl", SearchOption.AllDirectories).Count() == 3 &&
                !File.Exists(Path.Combine(project.AssetsRoot, "world.bin")), "incremental save copied world/source");
        });
        test("Windows review: real stage10004 production building commands undo complete source records", () =>
        {
            string path = Path.Combine(source, "assets", "stage", "stage10004.btl");
            BuildingCommands(BTLParser.LoadFromFile(path)); BuildingClipboard(BTLParser.LoadFromFile(path));
        });
        test("Windows review: project skill and feature catalogs produce readable Chinese with actual parameters", () =>
        {
            var manager = new AssetManager(new AssetCache()); manager.Scan(source);
            var skills = JsonSerializer.Deserialize<List<SkillSettingData>>(manager.ReadText(manager.Find("json/SkillSettings.json")!))!;
            var rows = JsonSerializer.Deserialize<List<ArmyFeatureData>>(manager.ReadText(manager.Find("json/ArmyFeatureSettings.json")!))!;
            string? Lookup(string key) => manager.GetStringTable("cn").GetValueOrDefault(key);
            Check(ProjectDataDescriptions.Skill(skills.First(s => s.Type == 24).Id, skills, Lookup).Title.Contains("钢铁洪流"), "real skill name");
            Check(ProjectDataDescriptions.Feature(1, 1, rows, Lookup).Description.Contains("60") &&
                ProjectDataDescriptions.Feature(13, 1, rows, Lookup).Description.Contains("50"), "real feature description parameters");
            Check(ProjectDataDescriptions.Feature(47, 3, rows, Lookup).Description.Contains('{') == false, "real level suffix not bound");
        });
    }

    private static void BuildingCommands(MapData map)
    {
        var manager = EditModeManager.Instance; var undo = new UndoManager();
        manager.Initialize(map); manager.SetUndoManager(undo); manager.SetSceneType("stage"); manager.SwitchMode(EditMode.BuildingDeploy);
        var modifier = manager.GetModifier<BuildingModifier>()!;
        try
        {
            // Keep real unrelated buildings/planes intact; use deterministic source and target records.
            var a = Building.CreateDefault(map.ToRecordCoordinate(1, 1)); a.BuildingType = 11; a.Name = 300; a.FactoryLevel = 7; a.Reserved3 = 123456;
            var b = Building.CreateDefault(map.ToRecordCoordinate(3, 1)); b.BuildingType = 15; b.Name = 301; b.Reserved4 = 791;
            modifier.Apply(1, 1, a); modifier.Apply(3, 1, b);
            map.SetBelongValue(1, 1, 0); map.SetBelongValue(3, 1, 1); map.SetProvince(3, 1, Province.Create((ushort)a.Coordinate));
            byte[] before = BTLParser.SaveToBytes(map);
            Check(Action("copy", 1, 1) && Action("paste", 3, 1) && undo.UndoCount == 1, "overwrite command not recorded");
            byte[] after = BTLParser.SaveToBytes(map);
            Check(map.GetBuildingAt(3, 1)!.Value.Reserved3 == a.Reserved3 && map.GetBelongValue(3, 1) == 0, "overwrite fields/owner");
            Check(undo.Undo() && before.SequenceEqual(BTLParser.SaveToBytes(map)), "undo deleted original/owner");
            Check(Action("copy", 3, 1) && undo.Redo() && after.SequenceEqual(BTLParser.SaveToBytes(map)), "redo reads later clipboard");
            undo.Clear(); before = BTLParser.SaveToBytes(map);
            Check(Action("apply", 3, 1) && undo.UndoCount == 1 && undo.Undo() && before.SequenceEqual(BTLParser.SaveToBytes(map)), "apply overwrite undo");
        }
        finally { modifier.ClearClipboard(); manager.Deinitialize(); HexSelector.Instance.ClearSelection(); }
        bool Action(string action, int col, int row) => manager.HandleKeyAction(action, col, row).GetAwaiter().GetResult();
    }

    private static void BuildingClipboard(MapData map)
    {
        var manager = EditModeManager.Instance; var undo = new UndoManager();
        manager.Initialize(map); manager.SetUndoManager(undo); manager.SetSceneType("stage"); manager.SwitchMode(EditMode.BuildingDeploy);
        var modifier = manager.GetModifier<BuildingModifier>()!; modifier.ClearClipboard(); var selector = HexSelector.Instance; selector.ClearSelection();
        int notifications = 0; void Changed(object? sender, EventArgs args) => notifications++;
        manager.DataModified += Changed;
        try
        {
            var a = Building.CreateDefault(map.ToRecordCoordinate(1, 1)); a.BuildingType = 11; a.FactoryLevel = 7; a.Reserved3 = 6789;
            var b = Building.CreateDefault(map.ToRecordCoordinate(3, 1)); b.BuildingType = 15; b.Name = 301; b.FactoryLevel = 0; b.Reserved4 = 791;
            modifier.Apply(1, 1, a); modifier.Apply(3, 1, b); modifier.Remove(5, 3);
            map.IsModified = false; byte[] before = BTLParser.SaveToBytes(map);
            Check(!Action("paste", 3, 1) && undo.UndoCount == 0 && notifications == 0 && !map.IsModified && before.SequenceEqual(BTLParser.SaveToBytes(map)), "empty clipboard mutated history");
            Check(!Action("apply", -1, 0) && undo.UndoCount == 0, "invalid coord registered history");
            Check(Action("copy_levels", 1, 1) && Action("paste", 3, 1) && undo.UndoCount == 1 && notifications == 1, "levels history/notification");
            var changed = map.GetBuildingAt(3, 1)!.Value;
            Check(changed.FactoryLevel == 7 && changed.Name == b.Name && changed.Reserved4 == b.Reserved4 && changed.BuildingType == b.BuildingType, "levels overwrote other fields");
            byte[] after = BTLParser.SaveToBytes(map);
            Check(undo.Undo() && before.SequenceEqual(BTLParser.SaveToBytes(map)) && undo.Redo() && after.SequenceEqual(BTLParser.SaveToBytes(map)), "levels snapshot");
            undo.Clear(); notifications = 0; map.IsModified = false;
            Check(!Action("paste", 3, 1) && undo.UndoCount == 0 && notifications == 0 && !map.IsModified, "no-op levels in history");
            selector.SetSelection([new(1, 1), new(3, 1)]); Check(Action("copy", 1, 1) && modifier.HasCopiedGroup, "group copy");
            selector.ClearSelection(); Check(Action("copy_levels", 1, 1) && !modifier.HasCopiedGroup && modifier.Clipboard == BuildingModifier.ClipboardKind.Levels,
                "levels retained old group");
            before = BTLParser.SaveToBytes(map);
            Check(!Action("paste", 5, 3) && before.SequenceEqual(BTLParser.SaveToBytes(map)) && undo.UndoCount == 0, "levels to empty pasted old group");
            selector.SetSelection([new(1, 1), new(3, 1)]); Check(Action("copy", 1, 1), "group recopy"); selector.ClearSelection();
            Check(Action("paste", 4, 3) && undo.UndoCount == 1 && notifications == 1, "group missing one history/notification");
            after = BTLParser.SaveToBytes(map);
            Check(undo.Undo() && before.SequenceEqual(BTLParser.SaveToBytes(map)) && undo.Redo() && after.SequenceEqual(BTLParser.SaveToBytes(map)), "group snapshot undo/redo");
            undo.Clear(); before = BTLParser.SaveToBytes(map);
            Check(Action("paste", map.MapWidth - 1, map.MapHeight - 1) && undo.UndoCount == 1 && undo.Undo() && before.SequenceEqual(BTLParser.SaveToBytes(map)), "partial bounded group");
        }
        finally { manager.DataModified -= Changed; modifier.ClearClipboard(); manager.Deinitialize(); selector.ClearSelection(); }
        bool Action(string action, int col, int row) => manager.HandleKeyAction(action, col, row).GetAwaiter().GetResult();
    }

    private static MapData ExternalMap(int capture)
    {
        var map = BTLParser.CreateNew(12, 4, mapNumber: 1);
        map.Header.MapClipX = capture; map.Header.MapClipY = 5; map.SetExternalWorldWidth(148);
        return map;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class WhiteImage : IViewLayerImageProvider
    {
        public bool HasImage => true;
        public int Width => 128;
        public int Height => 64;
        public byte[] ExtractRegionData(int x, int y, int width, int height) => throw new NotSupportedException();
        public GrayscaleImage ExtractGrayscale() => new(Enumerable.Repeat((byte)255, Width * Height).ToArray(), Width, Height, Width);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException) { return; }
        throw new Exception("Expected rejection.");
    }

    private sealed class AssetState : IDisposable
    {
        private readonly string _previous = AssetManager.Default.AssetsRoot;
        public void Dispose()
        {
            if (Directory.Exists(_previous)) AssetManager.Default.Scan(_previous, true); else AssetManager.Default.Clear();
            GameProjectDataSession.ReloadEditors();
        }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public string CreateSource(string name = "source")
        {
            string assets = Path.Combine(Root, name, "assets"); Directory.CreateDirectory(Path.Combine(assets, "json"));
            File.WriteAllText(Path.Combine(assets, "json", "SkillSettings.json"), "[]");
            File.WriteAllText(Path.Combine(assets, "stringtable_tw.ini"), "base=original\n");
            return assets;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
