namespace WC4MapEditor.Core.Commands;

public interface IUndoableCommand
{
    string Description { get; }
    long EstimatedBytes { get; }
    void Execute();
    void Undo();
}
