using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Commands;

public sealed class UndoManager
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private readonly int _maxHistory;
    private readonly long _maxResizeSnapshotBytes;

    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public long EstimatedBytes => _undoStack.Concat(_redoStack).Sum(command => command.EstimatedBytes);
    public string? LastUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    public string? LastRedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    public event Action? StateChanged;
    public event Action? ContentChanged;

    public UndoManager(int maxHistory = 200, long maxResizeSnapshotBytes = MapLimits.MaxOperationBytes)
    {
        if (maxHistory < 1 || maxResizeSnapshotBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxHistory));
        _maxHistory = maxHistory;
        _maxResizeSnapshotBytes = maxResizeSnapshotBytes;
    }

    public void ExecuteAndRecord(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        TrimHistory();
        ContentChanged?.Invoke();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 执行并记录: {command.Description}, 撤销栈={_undoStack.Count}");
    }

    public void Record(IUndoableCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        TrimHistory();
        ContentChanged?.Invoke();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 记录: {command.Description}, 撤销栈={_undoStack.Count}");
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        var command = _undoStack.Peek();
        command.Undo();
        _undoStack.Pop();
        _redoStack.Push(command);
        ContentChanged?.Invoke();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 撤销: {command.Description}, 撤销栈={_undoStack.Count}, 重做栈={_redoStack.Count}");
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;
        var command = _redoStack.Peek();
        command.Execute();
        _redoStack.Pop();
        _undoStack.Push(command);
        ContentChanged?.Invoke();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 重做: {command.Description}, 撤销栈={_undoStack.Count}, 重做栈={_redoStack.Count}");
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnStateChanged();
    }

    private void TrimHistory()
    {
        var array = _undoStack.ToArray();
        int keep = 0;
        long bytes = 0;
        foreach (var command in array)
        {
            long size = Math.Max(0, command.EstimatedBytes);
            if (keep == _maxHistory || size > _maxResizeSnapshotBytes - bytes) break;
            bytes += size;
            keep++;
        }
        if (keep == array.Length) return;
        _undoStack.Clear();
        // Drop oldest history first; never keep an undo gap across a resize.
        for (int i = keep - 1; i >= 0; i--)
            _undoStack.Push(array[i]);
    }

    private void OnStateChanged() => StateChanged?.Invoke();
}
