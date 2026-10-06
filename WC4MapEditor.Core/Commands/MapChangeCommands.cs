using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Commands;

public sealed class TerrainChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, TerrainData before, TerrainData after)[] _changes;

    public string Description { get; }

    public TerrainChangeCommand(MapData mapData, string description,
        (int col, int row, TerrainData before, TerrainData after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.GetTerrainRef(col, row) = after;
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.GetTerrainRef(col, row) = before;
        _mapData.IsModified = true;
    }
}

public sealed class ProvinceChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, Province before, Province after)[] _changes;

    public string Description { get; }

    public ProvinceChangeCommand(MapData mapData, string description,
        (int col, int row, Province before, Province after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.GetProvinceRef(col, row) = after;
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.GetProvinceRef(col, row) = before;
        _mapData.IsModified = true;
    }
}

public sealed class DelegateCommand : IUndoableCommand
{
    private readonly Action _execute;
    private readonly Action _undo;

    public string Description { get; }

    public DelegateCommand(string description, Action execute, Action undo)
    {
        Description = description;
        _execute = execute;
        _undo = undo;
    }

    public void Execute() => _execute();
    public void Undo() => _undo();
}

public sealed class MapResizeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly MapData _before;
    private MapData? _after;

    public long SnapshotBytes => _before.EstimatedStateBytes() + (_after?.EstimatedStateBytes() ?? 0);

    public string Description { get; }

    public MapResizeCommand(MapData mapData, string description)
    {
        _mapData = mapData;
        Description = description;
        if (mapData.EstimatedStateBytes() > MapLimits.MaxOperationBytes / 4)
            throw new InvalidOperationException("Map and undo snapshots exceed the editor memory budget.");
        _before = mapData.DeepClone();
    }

    public void CaptureAfterState()
    {
        long beforeBytes = _before.EstimatedStateBytes();
        if (beforeBytes > MapLimits.MaxOperationBytes ||
            _mapData.EstimatedStateBytes() > (MapLimits.MaxOperationBytes - beforeBytes) / 3)
            throw new InvalidOperationException("Map and undo snapshots exceed the editor memory budget.");
        _after = _mapData.DeepClone();
    }

    public void Execute()
    {
        if (_after == null) return;
        RestoreState(_after);
    }

    public void Undo()
    {
        RestoreState(_before);
    }

    public void Rollback() => _mapData.CopyFrom(_before);

    private void RestoreState(MapData snapshot)
    {
        string currentPath = _mapData.FilePath;
        _mapData.CopyFrom(snapshot);
        _mapData.FilePath = currentPath;
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 省份完整快照命令 - 用于大范围变更（比逐格记录更省内存）
/// </summary>
public sealed class ProvinceFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly Province[] _beforeSnapshot;
    private Province[]? _afterSnapshot;

    public string Description { get; }

    public ProvinceFullSnapshotCommand(MapData mapData, string description, Province[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new Province[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new Province[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = mapData.GetProvinceRef(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.GetProvinceRef(i) = _afterSnapshot[i];
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.GetProvinceRef(i) = _beforeSnapshot[i];
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 地形完整快照命令 - 用于大范围变更（比逐格记录更省内存）
/// </summary>
public sealed class TerrainFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly TerrainData[] _beforeSnapshot;
    private TerrainData[]? _afterSnapshot;

    public string Description { get; }

    public TerrainFullSnapshotCommand(MapData mapData, string description, TerrainData[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new TerrainData[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new TerrainData[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = mapData.GetTerrainRef(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.GetTerrainRef(i) = _afterSnapshot[i];
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.GetTerrainRef(i) = _beforeSnapshot[i];
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 归属变更命令 - 逐格记录归属变更
/// </summary>
public sealed class BelongChangeCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly (int col, int row, byte before, byte after)[] _changes;

    public string Description { get; }

    public BelongChangeCommand(MapData mapData, string description,
        (int col, int row, byte before, byte after)[] changes)
    {
        _mapData = mapData;
        Description = description;
        _changes = changes;
    }

    public void Execute()
    {
        foreach (var (col, row, _, after) in _changes)
            _mapData.SetBelongValue(col, row, after);
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        foreach (var (col, row, before, _) in _changes)
            _mapData.SetBelongValue(col, row, before);
        _mapData.IsModified = true;
    }
}

/// <summary>
/// 归属完整快照命令 - 用于大范围变更
/// </summary>
public sealed class BelongFullSnapshotCommand : IUndoableCommand
{
    private readonly MapData _mapData;
    private readonly byte[] _beforeSnapshot;
    private byte[]? _afterSnapshot;

    public string Description { get; }

    public BelongFullSnapshotCommand(MapData mapData, string description, byte[] beforeSnapshot)
    {
        _mapData = mapData;
        Description = description;
        _beforeSnapshot = new byte[beforeSnapshot.Length];
        Array.Copy(beforeSnapshot, _beforeSnapshot, beforeSnapshot.Length);

        // 立即捕获 after 状态
        _afterSnapshot = new byte[mapData.MapWidth * mapData.MapHeight];
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _afterSnapshot[i] = (byte)mapData.GetBelongValueByIndex(i);
    }

    public void Execute()
    {
        if (_afterSnapshot == null) return;
        for (int i = 0; i < _afterSnapshot.Length; i++)
            _mapData.SetBelongValueByIndex(i, _afterSnapshot[i]);
        _mapData.IsModified = true;
    }

    public void Undo()
    {
        for (int i = 0; i < _beforeSnapshot.Length; i++)
            _mapData.SetBelongValueByIndex(i, _beforeSnapshot[i]);
        _mapData.IsModified = true;
    }
}
