namespace WC4MapEditor.Views;

public interface IUnsavedScene
{
    bool HasUnsavedChanges { get; }
    bool SaveChanges();
    void DiscardChanges();
}
