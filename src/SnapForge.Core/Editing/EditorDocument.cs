using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

public sealed class DocumentChangedEventArgs(bool imageChanged) : EventArgs
{
    /// <summary>True when the pixel buffer changed (redaction), so the UI must refresh its bitmap.</summary>
    public bool ImageChanged { get; } = imageChanged;
}

/// <summary>
/// One screenshot being edited: the pixel buffer, the annotation stack (back to front), and history.
/// All changes go through <see cref="Execute"/> so every edit is undoable.
/// </summary>
public sealed class EditorDocument
{
    private readonly List<Annotation> _annotations = [];
    private long _savedStateId;

    public EditorDocument(PixelBuffer image, int historyCapacity = UndoRedoHistory.DefaultCapacity)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        History = new UndoRedoHistory(historyCapacity);
    }

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public PixelBuffer Image { get; }

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public UndoRedoHistory History { get; }

    /// <summary>True when the current state differs from the last state marked as saved (or the original capture).</summary>
    public bool IsDirty => History.CurrentStateId != _savedStateId;

    public void MarkSaved() => _savedStateId = History.CurrentStateId;

    /// <summary>
    /// Best-effort removal of screenshot data from memory when the document is no longer needed:
    /// zeroes the original pixels retained for undoing redactions, clears history and annotations, and
    /// zeroes the image buffer. The document must not be used afterwards.
    /// </summary>
    public void Discard()
    {
        foreach (IEditCommand command in History.AllCommands)
        {
            if (command is RedactCommand redact)
            {
                redact.WipeOriginal();
            }
        }

        History.Clear();
        _annotations.Clear();
        Array.Clear(Image.Pixels);
        IsDiscarded = true;
    }

    public bool IsDiscarded { get; private set; }

    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(IsDiscarded, this);
        History.Execute(command, this);
        RaiseChanged(command);
    }

    public bool Undo()
    {
        IEditCommand? command = History.Undo(this);
        RaiseChanged(command);
        return command is not null;
    }

    public bool Redo()
    {
        IEditCommand? command = History.Redo(this);
        RaiseChanged(command);
        return command is not null;
    }

    public Annotation? FindById(Guid id) => _annotations.FirstOrDefault(a => a.Id == id);

    /// <summary>Topmost annotation under the point, or null.</summary>
    public Annotation? HitTest(Geometry.PointD point, double tolerance)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (_annotations[i].HitTest(point, tolerance))
            {
                return _annotations[i];
            }
        }

        return null;
    }

    internal void InsertAnnotation(int index, Annotation annotation) => _annotations.Insert(index, annotation);

    internal int RemoveAnnotation(Guid id)
    {
        int index = _annotations.FindIndex(a => a.Id == id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Annotation {id} is not in the document.");
        }

        _annotations.RemoveAt(index);
        return index;
    }

    internal void ReplaceAnnotation(Annotation replacement)
    {
        int index = _annotations.FindIndex(a => a.Id == replacement.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Annotation {replacement.Id} is not in the document.");
        }

        _annotations[index] = replacement;
    }

    private void RaiseChanged(IEditCommand? command)
    {
        if (command is not null)
        {
            Changed?.Invoke(this, new DocumentChangedEventArgs(command is RedactCommand));
        }
    }
}
