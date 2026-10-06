using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.World;

static class TransformTests
{
    public static void Run(Action<string, Action> test)
    {
        test("resize all known objects, planes and references", () =>
        {
            foreach (int version in new[] { 1, 2, 3 })
            {
                var map = Fixture(version);
                MapTransform.Resize(map, 6, 4, 1, 1);
                Check(map.TerrainCount == 24 && map.Belongs.Count == 24, "planes");
                Check(map.GetTerrain(15).Reserved3 == 6 && map.Belongs[15] == "02", "plane remap");
                Check(map.GetProvince(15).ProvinceValue == 15, "province target");
                Check(map.Buildings[0].Coordinate == 15 && map.Traps[0].Coordinate == 15, "building/trap");
                Check((version == 1 ? map.Armies[0].Coordinate : map.ArmiesV3[0].Coordinate) == 15, "army");
                Check((version == 1 ? map.Reinforcements[0].Coordinate : map.ReinforcementsV3[0].Coordinate) == 15, "reinforcement");
                Check(map.AirForces[0].Coordinate == 15 && map.UnitPlaces[0].Coordinate == 15, "air/placement");
                Check(map.Cases[0].TargetTile == 15 && map.Capitals[0].Coordinate == 15, "case/capital");
                Check(map.FindBuildingIndex(3, 2) == 0 && map.FindBuildingIndex(2, 1) == -1, "building cache");
                Check(map.Belongs[0] == "FF" && map.GetProvince(0).ProvinceValue == 0xFFFF, "new cells neutral");
                Roundtrip(map);
            }
        });

        test("crop undo and redo restore the full independent state", () =>
        {
            var map = Fixture(3);
            map.Buildings.Add(Building.CreateDefault(11));
            map.UnitPlaces.Add(new UnitPlacement { Coordinate = 11 });
            map.Header.PlacementB = 1;
            map.Cases.Add(new MapCase { TargetTile = 11 });
            map.Capitals.Add(new Capital { Coordinate = 11 });
            byte[] before = BTLParser.SaveToBytes(map);
            var undo = new MapResizeCommand(map, "crop");
            MapTransform.Resize(map, 3, 2);
            Check(map.Buildings.Count == 1 && map.Buildings[0].Coordinate == 5, "crop objects");
            Check(map.Header.PlacementA == 1 && map.Header.PlacementB == 0, "placement boundary");
            Check(map.Cases[1].TargetTile == -1 && map.Capitals[1].Coordinate == -1, "preserve referenced slots");
            Check(map.ProvincePadding.Length == 4 && map.BelongPadding.Length == 2, "nonaligned planes");
            byte[] after = BTLParser.SaveToBytes(map);
            undo.CaptureAfterState();
            for (int i = 0; i < 3; i++)
            {
                map.Header.Unknown7 = 12345;
                undo.Undo();
                Equal(before, BTLParser.SaveToBytes(map));
                undo.Execute();
                Equal(after, BTLParser.SaveToBytes(map));
            }
        });

        test("conquest bottom append preserves world references and opaque data", () =>
        {
            var map = BTLParser.CreateNew(13, 9, mapNumber: 1);
            map.Header.BtlVersion = 3;
            map.OpaqueRecords = new byte[8];
            map.OpaqueRecords[3] = 19;
            map.ExtraData = [18, 92];
            map.SetProvince(1, Province.Create(30000));
            map = BTLParser.LoadFromBytes(BTLParser.SaveToBytes(map), "");
            Check(map.BelongOffset, "conquest display encoding");
            byte[] before = BTLParser.SaveToBytes(map);
            map.Resize(13, 11);
            Check(map.Belongs.Count == 143 && map.Header.SelectableTileCount == 144, "append dimensions");
            Check(map.Belongs[117] == "00" && map.GetProvince(1).ProvinceValue == 30000, "external refs/new owners");
            Check(map.OpaqueRecords[3] == 19 && map.ExtraData[1] == 92, "opaque bytes");
            var saved = BTLParser.SaveToBytes(map);
            var layout = new BtlLayout(BTLHeader.Parse(saved));
            Check(saved[layout["belongs"].Offset + 117] == 255, "new disk owner sentinel");
            RejectUnchanged(map, () => map.Resize(14, 11));
            RejectUnchanged(map, () => MapTransform.Scale(map, 2));
            Roundtrip(map);
        });

        test("fractional scale fills every cell and follows sampled terrain", () =>
        {
            var map = BTLParser.CreateNew(3, 3);
            for (int i = 0; i < 9; i++) map.SetTerrain(i, new TerrainData { Reserved3 = (byte)(i + 1) });
            map.Buildings.Add(Building.CreateDefault(1));
            map.Armies.Add(Army.CreateDefault(1));
            map.SetProvince(1, Province.Create(1));
            MapTransform.Scale(map, 1.5);
            Check(map.MapWidth == 4 && map.MapHeight == 4, "scale dimensions");
            for (int i = 0; i < 16; i++) Check(map.GetTerrain(i).Reserved3 != 0, "unfilled cell");
            Check(map.Buildings[0].Coordinate == 2 && map.GetTerrain(2).Reserved3 == 2, "object source terrain");
            Check(map.Armies[0].Coordinate == 2 && map.GetProvince(2).ProvinceValue == 2, "scale targets");
            Roundtrip(map);
        });

        test("unsafe transforms reject without partial mutation", () =>
        {
            var map = BTLParser.CreateNew(4, 2);
            map.Buildings.Add(Building.CreateDefault(0));
            map.Buildings.Add(Building.CreateDefault(1));
            RejectUnchanged(map, () => MapTransform.Scale(map, 0.5));
            RejectUnchanged(map, () => MapTransform.Scale(map, double.NaN));
            RejectUnchanged(map, () => MapTransform.Scale(map, double.PositiveInfinity));
            RejectUnchanged(map, () => map.Resize(int.MaxValue, 2));
            map.OpaqueRecords = new byte[8];
            RejectUnchanged(map, () => map.Resize(5, 2));
            map.OpaqueRecords = [];
            map.Armies.Add(Army.CreateDefault(7));
            RejectUnchanged(map, () => MapTransform.Resize(map, 40000, 2));
        });

        test("failed operations do not enter history and roll back state", () =>
        {
            var map = Fixture(1);
            map.IsModified = false;
            var manager = new EditModeManager();
            var history = new UndoManager();
            manager.Initialize(map);
            manager.SetUndoManager(history);
            byte[] before = BTLParser.SaveToBytes(map);
            var result = manager.RecordMapResizeChange("failed", () =>
            {
                map.Belongs[0] = "14";
                map.Header.MapClipX = 99;
                map.IsModified = true;
                return ModifierResult.Fail("rejected");
            });
            Check(!result.Success && history.UndoCount == 0 && !map.IsModified, "failure/history");
            Equal(before, BTLParser.SaveToBytes(map));
            result = manager.RecordMapResizeChange("append", () =>
            {
                map.Resize(4, 4);
                return ModifierResult.Ok();
            });
            Check(result.Success && history.UndoCount == 1, "success/history");
            history.Undo();
            Equal(before, BTLParser.SaveToBytes(map));
            history.Redo();
            Check(map.MapHeight == 4, "history redo");
        });

        test("resize snapshot budget discards oldest history", () =>
        {
            var map = Fixture(1);
            var first = new MapResizeCommand(map, "first");
            first.CaptureAfterState();
            var history = new UndoManager(maxResizeSnapshotBytes: first.SnapshotBytes);
            history.Record(first);
            var second = new MapResizeCommand(map, "second");
            second.CaptureAfterState();
            history.Record(second);
            Check(history.UndoCount == 1 && history.LastUndoDescription == "second", "history budget");
        });

        test("object placement guards reject before insert", () =>
        {
            var map = WorldParser.CreateNew(65537, 1);
            foreach (IModifier modifier in new IModifier[] { new ArmyModifier(), new ArmyV3Modifier(), new TrapModifier(), new BuildingModifier() })
            {
                modifier.Initialize(map);
                int limit = modifier is BuildingModifier ? 65535 : 32767;
                Check(!modifier.Apply(limit + 1, 0).Success, "overflow accepted");
                Check(modifier.Apply(limit, 0).Success, "boundary rejected");
                object value = modifier.GetDataAt(limit, 0)!;
                Check(!modifier.SetDataAt(limit + 1, 0, value), "paste overflow");
            }
            Check(map.Buildings.Count == 1 && map.Armies.Count == 1 && map.ArmiesV3.Count == 1 && map.Traps.Count == 1, "mutated overflow");
        });
    }

    private static MapData Fixture(int version)
    {
        var map = BTLParser.CreateNew(4, 3);
        map.Header.BtlVersion = version;
        for (int i = 0; i < 12; i++) map.SetTerrain(i, new TerrainData { Reserved3 = (byte)i });
        map.SetProvince(6, Province.Create(6));
        map.Belongs[6] = "02";
        map.Buildings.Add(Building.CreateDefault(6));
        if (version == 1)
        {
            map.Armies.Add(Army.CreateDefault(6));
            map.Reinforcements.Add(new Reinforcement { Coordinate = 6, ReservedTail2 = 67 });
        }
        else
        {
            map.ArmiesV3.Add(Army_3.CreateDefault(6));
            map.ReinforcementsV3.Add(new Reinforcement_3 { Coordinate = 6, Ribbon3 = 67 });
        }
        map.Traps.Add(Trap.CreateDefault(6));
        map.Cases.Add(new MapCase { TargetTile = 6 });
        map.Capitals.Add(new Capital { Coordinate = 6 });
        map.UnitPlaces.Add(new UnitPlacement { Coordinate = 6 });
        map.Header.PlacementA = 1;
        map.AirForces.Add(new AirForce { Coordinate = 6 });
        map.Events.Add(new MapEvent { Sequence = 17 });
        map.Weathers.Add(new Weather { Duration = 23 });
        map.StrategyConstructions.Add(new StrategicConstruction { LegionId = 2 });
        map.AirSupports.Add(new AirSupport { AirForceSequence = 1 });
        return map;
    }

    private static void Roundtrip(MapData map)
    {
        byte[] bytes = BTLParser.SaveToBytes(map);
        Equal(bytes, BTLParser.SaveToBytes(BTLParser.LoadFromBytes(bytes, "")));
    }

    private static void RejectUnchanged(MapData map, Action action)
    {
        byte[] before = BTLParser.SaveToBytes(map);
        bool rejected = false;
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or OverflowException) { rejected = true; }
        Check(rejected, "expected validation failure");
        Equal(before, BTLParser.SaveToBytes(map));
    }

    private static void Equal(byte[] expected, byte[] actual) => Check(expected.SequenceEqual(actual), "state/byte mismatch");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
