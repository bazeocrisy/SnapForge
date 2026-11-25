namespace SnapForge.Core.Editing;

/// <summary>A reversible change to an <see cref="EditorDocument"/>.</summary>
public interface IEditCommand
{
    string Description { get; }

    void Apply(EditorDocument document);

    void Revert(EditorDocument document);
}
