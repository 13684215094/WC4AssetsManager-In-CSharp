namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给脚本的建筑快照（只读值对象）。
/// <para>
/// 刻意用纯数据 + 基础类型，让脚本层不必引用 Core 的程序集，
/// 从而保持 "Core 引用 Scripting" 的依赖方向不出现循环。
/// </para>
/// </summary>
public sealed class ScriptBuildingInfo
{
    /// <summary>格子列号</summary>
    public int Col { get; init; }

    /// <summary>格子行号</summary>
    public int Row { get; init; }

    /// <summary>格子线性索引（row * MapWidth + col）</summary>
    public int Index { get; init; }

    /// <summary>建筑类型（11~15 为一级~五级城等）</summary>
    public int BuildingType { get; init; }

    /// <summary>建筑类型的中文名</summary>
    public string BuildingTypeName { get; init; } = "";

    /// <summary>名称 ID（无符号 16 位，0xFFFF 表示无名称）</summary>
    public int NameId { get; init; }

    /// <summary>名称（查不到时为空字符串）</summary>
    public string Name { get; init; } = "";

    /// <summary>是否有名称（NameId 不为 0 且不为 0xFFFF）</summary>
    public bool HasName { get; init; }

    /// <summary>建筑所在格子的归属值（0xFF 表示无归属）</summary>
    public int Belong { get; init; }
}
