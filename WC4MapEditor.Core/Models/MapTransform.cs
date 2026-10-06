using System.Collections.ObjectModel;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Models;

public static class MapTransform
{
    public static void Resize(MapData map, int width, int height, int offsetX = 0, int offsetY = 0, bool useOcean = true)
        => Apply(map, width, height, offsetX, offsetY, scale: false, useOcean);

    public static void Scale(MapData map, double factor)
    {
        if (!double.IsFinite(factor) || factor is < 0.1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(factor), "Scale must be finite and between 0.1 and 10.");
        int width = Math.Max(1, checked((int)(map.MapWidth * factor)));
        int height = Math.Max(1, checked((int)(map.MapHeight * factor)));
        Apply(map, width, height, 0, 0, scale: true, useOcean: true);
    }

    private static void Apply(MapData map, int width, int height, int offsetX, int offsetY, bool scale, bool useOcean)
    {
        int oldWidth = map.MapWidth, oldHeight = map.MapHeight;
        int oldArea = MapLimits.Area(oldWidth, oldHeight);
        int area = MapLimits.Area(width, height);
        if (map.TerrainCount != oldArea)
            throw new InvalidDataException("Source map dimensions do not match its planes.");
        bool battle = map.FileKind == MapFileKind.Battle || map.Belongs.Count != 0;
        bool identity = !scale && width == oldWidth && height >= oldHeight && offsetX == 0 && offsetY == 0;
        if (width == oldWidth && height == oldHeight && offsetX == 0 && offsetY == 0) return;
        if (battle && map.Header.MapNumber != 0 && !identity)
            throw new InvalidOperationException("World-backed conquests only support bottom append here; other transforms require a linked world map.");
        if (!identity && (map.OpaqueRecords.Length != 0 || map.ExtraData.Length != 0))
            throw new InvalidOperationException("Opaque BTL records may contain coordinates. Coordinate-changing transforms are disabled.");
        if (battle && map.Belongs.Count != oldArea)
            throw new InvalidDataException("Owner plane does not match source dimensions.");
        long estimate = map.EstimatedStateBytes();
        if (estimate > (MapLimits.MaxOperationBytes - area * 174L) / 4)
            throw new InvalidOperationException("Transform and undo snapshots exceed the editor memory budget.");

        var result = map.DeepClone();
        result.InitializeTerrain(width, height);
        result.Belongs = battle ? Enumerable.Repeat(map.BelongOffset ? "00" : "FF", area).ToList() : [];
        result.Header.MapLength = width;
        result.Header.MapWidth = height;
        if (battle)
        {
            result.Header.SelectableTileCount = BtlLayout.Align(area);
            result.ProvincePadding = new byte[(result.Header.SelectableTileCount - area) * 2];
            result.BelongPadding = new byte[result.Header.SelectableTileCount - area];
        }
        for (int index = 0; index < area; index++)
        {
            int col = index % width, row = index / width;
            int sourceCol = scale ? (int)((long)col * oldWidth / width) : col - offsetX;
            int sourceRow = scale ? (int)((long)row * oldHeight / height) : row - offsetY;
            if ((uint)sourceCol >= oldWidth || (uint)sourceRow >= oldHeight)
            {
                result.SetTerrain(index, new TerrainData { TileType1 = useOcean ? (byte)1 : (byte)0 });
                result.SetProvince(index, Province.Create(0xFFFF));
                continue;
            }
            int sourceIndex = sourceRow * oldWidth + sourceCol;
            result.SetTerrain(index, map.GetTerrain(sourceIndex));
            var province = map.GetProvince(sourceIndex);
            // Conquest references address the external world, not this local window.
            if (!identity && province.ProvinceValue is not (0 or 0xFFFF))
            {
                int target = Move(province.ProvinceValue);
                if (target == 0) throw new InvalidOperationException("A province target would collide with the editor's zero sentinel.");
                province.ProvinceValue = target < 0 ? (ushort)0xFFFF : (ushort)MapLimits.SignedCoordinate(target);
            }
            result.SetProvince(index, province);
            if (battle) result.Belongs[index] = map.Belongs[sourceIndex];
        }

        if (!identity)
        {
            result.Buildings = Remap(map.Buildings, v => v.Coordinate, (Building v, int c) => { v.Coordinate = MapLimits.BuildingCoordinate(c); return v; });
            result.Armies = Remap(map.Armies, v => v.Coordinate, (Army v, int c) => { v.Coordinate = MapLimits.SignedCoordinate(c); return v; });
            result.ArmiesV3 = Remap(map.ArmiesV3, v => v.Coordinate, (Army_3 v, int c) => { v.Coordinate = MapLimits.SignedCoordinate(c); return v; });
            result.Traps = Remap(map.Traps, v => v.Coordinate, (Trap v, int c) => { v.Coordinate = MapLimits.SignedCoordinate(c); return v; });
            result.Reinforcements = Remap(map.Reinforcements, v => v.Coordinate, (Reinforcement v, int c) => { v.Coordinate = c; return v; });
            result.ReinforcementsV3 = Remap(map.ReinforcementsV3, v => v.Coordinate, (Reinforcement_3 v, int c) => { v.Coordinate = c; return v; });
            result.AirForces = Remap(map.AirForces, v => v.Coordinate, (AirForce v, int c) => { v.Coordinate = c; return v; });
            // Keep referenced list slots stable; only independently placed objects are removed.
            result.Cases = new(map.Cases.Select(v => { v.TargetTile = Move(v.TargetTile); return v; }));
            result.Capitals = new(map.Capitals.Select(v => { v.Coordinate = Move(v.Coordinate); return v; }));
            if ((long)map.Header.PlacementA + map.Header.PlacementB != map.UnitPlaces.Count)
                throw new InvalidDataException("Placement A/B counts do not match the collection.");
            var a = Remap(map.UnitPlaces.Take(map.Header.PlacementA), v => v.Coordinate, (UnitPlacement v, int c) => { v.Coordinate = c; return v; });
            var b = Remap(map.UnitPlaces.Skip(map.Header.PlacementA), v => v.Coordinate, (UnitPlacement v, int c) => { v.Coordinate = c; return v; });
            result.UnitPlaces = new(a.Concat(b));
            result.Header.PlacementA = a.Count;
            result.Header.PlacementB = b.Count;
            result.Header.BuildingCount = result.Buildings.Count;
            result.Header.TroopCount = result.Header.BtlVersion == 1 ? result.Armies.Count : result.ArmiesV3.Count;
            result.Header.TrapCount = result.Traps.Count;
            result.Header.ReinforcementCount = result.Header.BtlVersion == 1 ? result.Reinforcements.Count : result.ReinforcementsV3.Count;
            result.Header.AirRaidCount = result.AirForces.Count;
        }
        result.IsModified = true;
        map.CopyFrom(result);

        int Move(int index)
        {
            if (index == -1) return -1;
            if (index < 0 || index >= oldArea)
                throw new InvalidDataException($"Coordinate/reference {index} is outside the source map.");
            int col = index % oldWidth, row = index / oldWidth;
            long targetCol = scale ? ScaleCoordinate(col, oldWidth, width) : (long)col + offsetX;
            long targetRow = scale ? ScaleCoordinate(row, oldHeight, height) : (long)row + offsetY;
            if (targetCol < 0 || targetRow < 0 || targetCol >= width || targetRow >= height) return -1;
            return checked((int)(targetRow * width + targetCol));
        }

        static long ScaleCoordinate(int value, int sourceSize, int targetSize)
        {
            // On enlargement choose a destination that actually samples this
            // source cell, including non-integral scale factors.
            long product = (long)value * targetSize;
            return targetSize >= sourceSize ? (product + sourceSize - 1) / sourceSize : product / sourceSize;
        }

        ObservableCollection<T> Remap<T>(IEnumerable<T> values, Func<T, int> coordinate, Func<T, int, T> update)
        {
            var output = new ObservableCollection<T>();
            var occupied = new Dictionary<int, int>();
            foreach (T value in values)
            {
                int source = coordinate(value);
                if (source == -1) { output.Add(value); continue; }
                int target = Move(source);
                if (target < 0) continue;
                if (occupied.TryGetValue(target, out int prior) && prior != source)
                    throw new InvalidOperationException("Scaling would overlap independently placed objects.");
                occupied[target] = source;
                output.Add(update(value, target));
            }
            return output;
        }
    }
}
