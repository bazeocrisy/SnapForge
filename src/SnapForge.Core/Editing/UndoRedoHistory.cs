namespace SnapForge.Core.Editing;

/// <summary>Bounded linear undo/redo history. Executing a new command discards the redo branch.</summary>
public sealed class UndoRedoHistory
{
    public const int DefaultCapacity = 200;

    private readonly LinkedList<(IEditCommand Command, long StateId)> _undo = new();
    private readonly Stack<(IEditCommand Command, long StateId)> _redo = new();
    private readonly int _capacity;
    private long _nextStateId = 1;
    private long _baseStateId;

    public UndoRedoHistory(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? UndoDescription => _undo.Last?.Value.Command.Description;

    public string? RedoDescription => _redo.Count > 0 ? _redo.Peek().Command.Description : null;

    /// <summary>
    /// Identifies the document state; 0 is the freshly captured image. When old steps are evicted the oldest
    /// reachable state is no longer the fresh capture, so it keeps the id of the last evicted step.
    /// </summary>
    public long CurrentStateId => _undo.Last?.Value.StateId ?? _baseStateId;

    /// <summary>All commands currently held (undo and redo branches).</summary>
    internal IEnumerable<IEditCommand> AllCommands => _undo.Select(e => e.Command).Concat(_redo.Select(e => e.Command));

    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    internal void Execute(IEditCommand command, EditorDocument document)
    {
        command.Apply(document);
        _undo.AddLast((command, _nextStateId++));
        _redo.Clear();
        while (_undo.Count > _capacity)
        {
            // The oldest step becomes permanent; the document state itself is unaffected.
            _baseStateId = _undo.First!.Value.StateId;
            _undo.RemoveFirst();
        }
    }

    internal IEditCommand? Undo(EditorDocument document)
    {
        if (_undo.Last is null)
        {
            return null;
        }

        var entry = _undo.Last.Value;
        _undo.RemoveLast();
        entry.Command.Revert(document);
        _redo.Push(entry);
        return entry.Command;
    }

    internal IEditCommand? Redo(EditorDocument document)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var entry = _redo.Pop();
        entry.Command.Apply(document);
        _undo.AddLast(entry);
        return entry.Command;
    }
}
