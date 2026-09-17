namespace WC4MapEditor.Scripting;

/// <summary>
/// 脚本可以操作的地图数据接口。
/// <para>
/// 由 Core 层实现（内部转发到 BuildingModifier / BelongModifier 等）。
/// 接口放在 Scripting 项目里，是为了让依赖方向保持
/// <c>Core → Scripting</c>（若反过来就会与 Core 形成循环依赖）。
/// </para>
/// <para>
/// <b>扩展方式</b>：以后要开放新的数据操作，只要在这里加一个方法、
/// 在 Core 的 <c>MapScriptApi</c> 里实现，脚本里立刻可用 —— 不需要再设计任何命令语法。
/// </para>
/// </summary>
public interface IMapScriptApi
{
    // ---------- 地图信息 ----------

    /// <summary>地图列数</summary>
    int MapWidth { get; }

    /// <summary>地图行数</summary>
    int MapHeight { get; }

    /// <summary>建筑总数</summary>
    int BuildingCount { get; }

    /// <summary>格子总数</summary>
    int TileCount { get; }

    // ---------- 建筑 ----------

    /// <summary>
    /// 遍历所有建筑。<paramref name="visitor"/> 返回 false 可提前结束。
    /// </summary>
    void ForEachBuilding(Func<ScriptBuildingInfo, bool> visitor);

    /// <summary>取指定格子的建筑，没有则返回 null</summary>
    ScriptBuildingInfo? GetBuilding(int col, int row);

    /// <summary>
    /// 在指定格子添加建筑。
    /// <paramref name="name"/> 可为数字 ID、中文名称或空（表示无名称）。
    /// </summary>
    ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null);

    /// <summary>删除指定格子的建筑</summary>
    ScriptOperationResult RemoveBuilding(int col, int row);

    /// <summary>
    /// 按概率删除建筑（每个建筑独立掷骰）。
    /// <paramref name="keepNamed"/> 为 true 时保留有名称的建筑（城市/据点）。
    /// </summary>
    ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false);

    // ---------- 归属 ----------

    /// <summary>取指定格子的归属值（0xFF = 无归属）</summary>
    int GetBelong(int col, int row);

    /// <summary>设置指定格子的归属值</summary>
    ScriptOperationResult SetBelong(int col, int row, int belongValue);

    /// <summary>取指定格子的省区值（0xFFFF = 无省区）</summary>
    int GetProvince(int col, int row);

    // ---------- 地形 ----------

    /// <summary>取指定格子的第一层地形类型</summary>
    int GetTerrain(int col, int row);
}
