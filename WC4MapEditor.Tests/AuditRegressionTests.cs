using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;

static class AuditRegressionTests
{
    public static void Run(Action<string, Action> test)
    {
        test("hex neighbors and distances match the camera and radius-one brush", () =>
        {
            var camera = new Camera { OffsetX = 37, OffsetY = -25, ZoomLevel = 1.7 };
            foreach (int width in new[] { 5, 6, 9 })
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < width; col++)
            {
                var cell = new HexCoord(col, row);
                Check(HexCoord.FromIndex(cell.ToIndex(width), width) == cell, "index roundtrip");
                var screen = camera.HexToScreen(col, row);
                Check(camera.ScreenToHex(screen.Item1, screen.Item2) == (col, row), "camera roundtrip");
                var neighbors = cell.GetNeighbors();
                foreach (var other in neighbors)
                {
                    Check(other.GetNeighbors().Contains(cell), "asymmetric adjacency");
                    Check(cell.DistanceTo(other) == 1 && other.DistanceTo(cell) == 1, "adjacent distance");
                    var otherScreen = camera.HexToScreen(other.Col, other.Row);
                    double length = Math.Sqrt(Math.Pow(screen.Item1 - otherScreen.Item1, 2) + Math.Pow(screen.Item2 - otherScreen.Item2, 2));
                    Check(Math.Abs(length - Camera.HexVerticalSpacing * camera.ZoomLevel) < 0.00001, "neighbor does not touch the drawn cell");
                }
                var expected = neighbors.Append(cell).Where(p => p.Col >= 0 && p.Col < width && p.Row >= 0 && p.Row < 4)
                    .Select(p => (p.Col, p.Row)).ToHashSet();
                Check(expected.SetEquals(BrushEngine.GetHexesInRadius(col, row, 1, width, 4)), "brush adjacency");
            }
        });

        test("river edges update and clear the correct opposite edge on odd and even widths", () =>
        {
            foreach (int width in new[] { 5, 6 })
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < width; col++)
            for (int edge = 0; edge < 6; edge++)
            {
                var map = WorldParser.CreateNew(width, 4);
                var modifier = new TerrainModifier();
                modifier.Initialize(map);
                modifier.SetRiverValue(col, row, (byte)(1 << edge));
                int[,] offsets = (col & 1) == 0
                    ? new[,] { { 0, -1 }, { 1, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 }, { -1, -1 } }
                    : new[,] { { 0, -1 }, { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 } };
                int nc = col + offsets[edge, 0], nr = row + offsets[edge, 1];
                for (int i = 0; i < width * 4; i++)
                {
                    byte expected = i == row * width + col ? (byte)(1 << edge)
                        : nc >= 0 && nc < width && nr >= 0 && nr < 4 && i == nr * width + nc ? (byte)(1 << ((edge + 3) % 6)) : (byte)0;
                    Check(map.GetTerrain(i).RiverValue == expected, $"wrong river cell at {width}/{col},{row}/{edge}/{i}");
                }
                modifier.SetRiverValue(col, row, 0);
                Check(Enumerable.Range(0, width * 4).All(i => map.GetTerrain(i).RiverValue == 0), "opposite edge was not cleared");
            }
        });

        test("F4 F5 and selection move each restore every terrain byte with one undo", () =>
        {
            var manager = EditModeManager.Instance;
            var selector = HexSelector.Instance;
            try
            {
                foreach (string action in new[] { "create_coast", "process_ocean_layer2", "selection" })
                {
                    var map = WorldParser.CreateNew(6, 4);
                    for (int i = 0; i < map.TerrainCount; i++)
                        map.SetTerrain(i, new TerrainData { TileType1 = 1, TileType2 = 0x3F, DecorationType2 = 0xFF, Reserved3 = (byte)i });
                    map.SetTerrain(2, 1, new TerrainData { TileType1 = 3, TileType2 = 4, RiverValue = 27, Reserved3 = 73 });
                    if (action == "process_ocean_layer2") map.SetTerrain(0, new TerrainData { TileType1 = 1, TileType2 = 7 });
                    var undo = new UndoManager();
                    manager.Initialize(map);
                    manager.SetUndoManager(undo);
                    manager.SetSceneType("world");
                    manager.SwitchMode(EditMode.TerrainPaint);
                    selector.ClearSelection();
                    byte[] before = map.TerrainsToBytes();
                    if (action == "selection")
                    {
                        selector.SetSelection([new HexCoord(2, 1), new HexCoord(2, 2)]);
                        manager.HandleKeyAction("move_selection_right", 2, 1).GetAwaiter().GetResult();
                        manager.HandleKeyAction("confirm_selection_move", 2, 1).GetAwaiter().GetResult();
                    }
                    else manager.HandleKeyAction(action, 2, 1).GetAwaiter().GetResult();
                    byte[] after = map.TerrainsToBytes();
                    Check(!before.SequenceEqual(after), $"{action} fixture did not change terrain");
                    Check(undo.UndoCount == 1, $"{action} did not record one undo");
                    Check(undo.Undo() && before.SequenceEqual(map.TerrainsToBytes()), $"{action} undo lost fields");
                    Check(undo.Redo() && after.SequenceEqual(map.TerrainsToBytes()), $"{action} redo differs");
                    manager.Deinitialize();
                }
            }
            finally { manager.Deinitialize(); selector.ClearSelection(); }
        });

        test("province flood fill follows drawn hex adjacency on odd width maps", () =>
        {
            foreach (int width in new[] { 5, 6 })
            {
                var map = WorldParser.CreateNew(width, 4);
                for (int i = 0; i < map.TerrainCount; i++) map.GetTerrainRef(i).TileType1 = 3;
                map.SetProvince(0, Province.Create(9));
                map.SetProvince(2, 1, Province.Create(7));
                map.SetProvince(3, 0, Province.Create(7));
                map.SetProvince(3, 2, Province.Create(7));
                var modifier = new ProvinceModifier();
                modifier.Initialize(map);
                modifier.CopyProvinceValue(0, 0);
                var result = modifier.FloodFill(2, 1);
                Check(result.Success && result.AffectedCount == 2 && map.GetProvince(3, 0).ProvinceValue == 9 &&
                    map.GetProvince(3, 2).ProvinceValue == 7, "province fill follows index parity");
            }
        });

        test("failed terrain and province commands restore data and keep undo history empty", () =>
        {
            var manager = EditModeManager.Instance;
            var map = WorldParser.CreateNew(5, 4);
            var undo = new UndoManager();
            manager.Initialize(map);
            manager.SetUndoManager(undo);
            map.IsModified = false;
            byte[] before = WorldParser.SaveToBytes(map);
            try
            {
                Reject(() => manager.RecordMultiCellChange("fail terrain", () =>
                {
                    map.GetTerrainRef(0).Reserved3 = 73;
                    map.IsModified = true;
                    throw new InvalidOperationException("fail");
                }));
                Reject(() => manager.RecordMultiCellProvinceChange("fail province", () =>
                {
                    map.SetProvince(0, Province.Create(19));
                    throw new InvalidOperationException("fail");
                }));
                Check(before.SequenceEqual(WorldParser.SaveToBytes(map)) && !map.IsModified && undo.UndoCount == 0, "partial failed command remains");
            }
            finally { manager.Deinitialize(); }
        });

        test("failed asset scan preserves the previously active index and revision", () =>
        {
            using var fixture = new Fixture();
            string first = Path.Combine(fixture.Root, "first"), second = Path.Combine(fixture.Root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "old.json"), "[]");
            string locked = Path.Combine(second, "locked.bin");
            File.WriteAllBytes(locked, [1, 2, 3]);
            var cache = new AssetCache();
            cache.Load(first);
            long revision = cache.Revision;
            using var stream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Reject(() => cache.Load(second, forceReload: true));
            Check(cache.AssetsRoot == first && cache.IsLoaded && cache.Count == 1 && cache.Revision == revision &&
                cache.GetByRelativePath("old.json") != null, "failed scan published partial state");
        });

        test("reattaching an external BTL never erases pending terrain or resets its baseline", () =>
        {
            using var fixture = new Fixture();
            string source = Path.Combine(fixture.Root, "source"), assets = Path.Combine(source, "assets");
            Directory.CreateDirectory(assets);
            WorldParser.SaveToFile(WorldParser.CreateNew(8, 6), Path.Combine(assets, "world.bin"));
            BTLParser.SaveToFile(BTLParser.CreateNew(4, 3, mapNumber: 1), Path.Combine(assets, "stage.btl"));
            var project = GameProjectWorkspace.Create(source, Path.Combine(fixture.Root, "output"));
            var document = ProjectMapDocument.Load(project, Path.Combine(project.AssetsRoot, "stage.btl"));
            var terrain = document.Map.GetTerrain(0);
            terrain.Reserved3 = 73;
            document.Map.SetTerrain(0, terrain);
            Reject(() => ProjectMapDocument.Attach(project, document.Map));
            Check(document.Map.GetTerrain(0).Reserved3 == 73, "reattach discarded edits");
            Reject(document.ValidateChanges);
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Expected operation to be rejected.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "tmp", "tests", Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
