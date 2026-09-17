using MoonSharp.Interpreter;

namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给 Lua 的 <c>map</c> 对象。
/// <para>
/// 它只是 <see cref="IMapScriptApi"/> 的薄包装：把 C# 方法转成 Lua 可调用的形式，
/// 并统一处理日志输出与"影响条数"统计。真正的数据操作在 Core 里。
/// </para>
/// <para>
/// 成员名保持 PascalCase 与 C# 一致，脚本里写作
/// <c>map.AddBuilding(10, 20, 11, "北京")</c>、<c>map.RemoveBuildingsByProbability(30, true)</c>。
/// </para>
/// </summary>
[MoonSharpUserData]
public sealed class LuaMapApi
{
    private readonly IMapScriptApi _api;
    private readonly Action<string> _log;
    private readonly Action<int> _addAffected;

    internal LuaMapApi(IMapScriptApi api, Action<string> log, Action<int> addAffected)
    {
        _api = api;
        _log = log;
        _addAffected = addAffected;
    }

    // ---------- 地图信息 ----------

    /// <summary>地图列数</summary>
    public int Width => _api.MapWidth;

    /// <summary>地图行数</summary>
    public int Height => _api.MapHeight;

    /// <summary>建筑总数</summary>
    public int BuildingCount => _api.BuildingCount;

    /// <summary>格子总数</summary>
    public int TileCount => _api.TileCount;

    // ---------- 建筑 ----------

    /// <summary>
    /// 遍历所有建筑：<c>map.ForEachBuilding(function(b) ... end)</c>。
    /// <para>回调返回 false 可提前结束遍历。</para>
    /// </summary>
    public void ForEachBuilding(DynValue callback)
    {
        if (callback == null || callback.Type != DataType.Function)
        {
            _log("[脚本] ForEachBuilding 需要一个函数作为参数");
            return;
        }

        _api.ForEachBuilding(info =>
        {
            var result = callback.Function.Call(info);
            // 只有显式返回 false 才中断，nil / 无返回值都视为继续
            return !(result.Type == DataType.Boolean && result.Boolean == false);
        });
    }

    /// <summary>取指定格子的建筑，没有则返回 nil</summary>
    public ScriptBuildingInfo? GetBuilding(int col, int row) => _api.GetBuilding(col, row);

    /// <summary>
    /// 在指定格子添加建筑。
    /// <para>name 可以是数字 ID、中文名称，或省略表示无名称。</para>
    /// </summary>
    public ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null)
    {
        var result = _api.AddBuilding(col, row, buildingType, name);
        Report(result);
        return result;
    }

    /// <summary>删除指定格子的建筑</summary>
    public ScriptOperationResult RemoveBuilding(int col, int row)
    {
        var result = _api.RemoveBuilding(col, row);
        Report(result);
        return result;
    }

    /// <summary>
    /// 按概率删除建筑（每个建筑独立掷骰）。
    /// <para>keepNamed 为 true 时保留有名称的建筑（城市/据点）。</para>
    /// </summary>
    public ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false)
    {
        var result = _api.RemoveBuildingsByProbability(probability, keepNamed);
        Report(result);
        return result;
    }

    // ---------- 归属 / 省区 / 地形 ----------

    /// <summary>取指定格子的归属值（255 = 无归属）</summary>
    public int GetBelong(int col, int row) => _api.GetBelong(col, row);

    /// <summary>设置指定格子的归属值</summary>
    public ScriptOperationResult SetBelong(int col, int row, int belongValue)
    {
        var result = _api.SetBelong(col, row, belongValue);
        Report(result);
        return result;
    }

    /// <summary>取指定格子的省区值（65535 = 无省区）</summary>
    public int GetProvince(int col, int row) => _api.GetProvince(col, row);

    /// <summary>取指定格子的第一层地形类型</summary>
    public int GetTerrain(int col, int row) => _api.GetTerrain(col, row);

    // ---------- 输出 ----------

    /// <summary>输出一行到控制台</summary>
    public void Log(string message) => _log(message ?? "");

    /// <summary>统一上报：累加影响条数，失败时把原因打到输出里</summary>
    private void Report(ScriptOperationResult result)
    {
        if (result.AffectedCount > 0)
            _addAffected(result.AffectedCount);

        if (!result.Success && !string.IsNullOrEmpty(result.Message))
            _log($"[脚本] {result.Message}");
    }
}
