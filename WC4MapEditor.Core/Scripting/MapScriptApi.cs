using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.Core.Scripting;

/// <summary>
/// <see cref="IMapScriptApi"/> 的 Core 实现：把脚本调用转发到既有的 Modifier。
/// <para>
/// 转发而不是直接改数据，是为了复用 Modifier 里的校验、索引维护与
/// <c>MarkModified()</c> 通知逻辑，避免脚本绕过常规编辑路径。
/// </para>
/// </summary>
public sealed class MapScriptApi : IMapScriptApi
{
    private readonly MapData _mapData;
    private readonly EditModeManager _modeManager;

    public MapScriptApi(MapData mapData, EditModeManager modeManager)
    {
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
    }

    // ---------- 地图信息 ----------

    public int MapWidth => _mapData.MapWidth;
    public int MapHeight => _mapData.MapHeight;
    public int TileCount => _mapData.MapWidth * _mapData.MapHeight;
    public int BuildingCount => _mapData.Buildings.Count;

    // ---------- 建筑 ----------

    public void ForEachBuilding(Func<ScriptBuildingInfo, bool> visitor)
    {
        if (visitor == null) return;

        // 遍历副本：脚本回调里可能会增删建筑（比如边遍历边删），
        // 直接枚举原集合会抛"集合已修改"异常。
        var snapshot = _mapData.Buildings.ToList();
        foreach (var building in snapshot)
        {
            if (!visitor(ToInfo(building))) return;
        }
    }

    public ScriptBuildingInfo? GetBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;

        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.Coordinate == row * _mapData.MapWidth + col)
                return ToInfo(building);
        }
        return null;
    }

    public ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        if (buildingType < 0 || buildingType > 255)
            return ScriptOperationResult.Fail($"建筑类型 {buildingType} 超出范围 (0-255)");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        int index = row * _mapData.MapWidth + col;
        var building = Building.CreateDefault(index);
        building.BuildingType = (byte)buildingType;
        building.Name = BuildingNameResolver.Resolve(name);

        var result = modifier.Apply(col, row, building);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已添加建筑", 1)
            : ScriptOperationResult.Fail(result.Message ?? "添加建筑失败");
    }

    public ScriptOperationResult RemoveBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var result = modifier.Remove(col, row);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已删除建筑", 1)
            : ScriptOperationResult.Fail(result.Message ?? "删除建筑失败");
    }

    public ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false)
    {
        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var result = modifier.RemoveBuildingsByProbability(probability, keepNamed);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已按概率删除建筑", result.AffectedCount)
            : ScriptOperationResult.Fail(result.Message ?? "按概率删除建筑失败");
    }

    // ---------- 归属 / 省区 / 地形 ----------

    public int GetBelong(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetBelongValue(col, row) : 0xFF;

    public ScriptOperationResult SetBelong(int col, int row, int belongValue)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        if (belongValue < 0 || belongValue > 255)
            return ScriptOperationResult.Fail($"归属值 {belongValue} 超出范围 (0-255)");

        // SetBelongValue 在格子不归属数据范围内时会直接返回 false（不写入也不报错），
        // 这里必须把结果透出去，否则脚本以为写成功了，实际是静默丢失。
        if (!_mapData.SetBelongValue(col, row, belongValue))
            return ScriptOperationResult.Fail($"设置归属失败：({col},{row}) 不在归属数据范围内");

        return ScriptOperationResult.Ok($"已设置归属 ({col},{row}) = {belongValue}", 1);
    }

    public int GetProvince(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetProvinceRef(col, row).ProvinceValue : 0xFFFF;

    public int GetTerrain(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetTerrainRef(col, row).TileType1 : -1;

    // ---------- 内部 ----------

    private bool IsValidCoord(int col, int row)
        => col >= 0 && row >= 0 && col < _mapData.MapWidth && row < _mapData.MapHeight;

    private ScriptBuildingInfo ToInfo(Building building)
    {
        int col = _mapData.MapWidth > 0 ? building.Coordinate % _mapData.MapWidth : 0;
        int row = _mapData.MapWidth > 0 ? building.Coordinate / _mapData.MapWidth : 0;

        int nameId = building.Name;
        bool hasName = nameId != 0 && nameId != BuildingNameResolver.NoName;

        return new ScriptBuildingInfo
        {
            Col = col,
            Row = row,
            Index = building.Coordinate,
            BuildingType = building.BuildingType,
            BuildingTypeName = building.GetBuildingTypeName(),
            NameId = nameId,
            Name = hasName ? GetCityName(nameId) : "",
            HasName = hasName,
            Belong = _mapData.GetBelongValue(col, row)
        };
    }

    private static string GetCityName(int nameId)
    {
        try
        {
            var parser = Config.ConfigManager.Instance.GetStringTableParser();
            foreach (var kvp in parser.FindCityNames())
            {
                if (kvp.Key == nameId) return kvp.Value;
            }
        }
        catch
        {
            // 名称表不可用时退化为空字符串，不影响脚本继续跑
        }
        return "";
    }
}
