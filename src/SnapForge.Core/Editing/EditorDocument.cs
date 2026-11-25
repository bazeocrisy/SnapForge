using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

public sealed class DocumentChangedEventArgs(bool imageChanged, bool sizeChanged = false) : EventArgs
{
    /// <summary>True when the visible pixels changed (redaction, crop), so the UI must refresh its bitmap.</summary>
    public bool ImageChanged { get; } = imageChanged;

    /// <summary>True when the image dimensions changed (crop).</summary>
    public bool SizeChanged { get; } = sizeChanged;
}

/// <summary>
/// One screenshot being edited.
/// <list type="bullet">
/// <item><see cref="BaseImage"/>: the captured pixels without redactions (what the editable library copy stores, so
/// redactions stay reversible until the capture is sanitized).</item>
/// <item><see cref="Image"/>: the visible pixels = base with every <see cref="RedactionMaskAnnotation"/> burned in.
/// Exports are built only from this.</item>
/// <item><see cref="Annotations"/>: vector marks, back to front, including redaction masks.</item>
/// </list>
/// All changes go through <see cref="Execute"/> and are undoable.
/// </summary>
public sealed class EditorDocument
{
    private readonly List<Annotation> _annotations = [];
    private PixelBuffer _base;
    private PixelBuffer? _working; // null until the first redaction: the visible image is the base itself
    private long _savedStateId;

    public EditorDocument(PixelBuffer image, int historyCapacity = UndoRedoHistory.DefaultCapacity)
        : this(image, [], historyCapacity)
    {
    }

    /// <summary>Re-creates a document from stored content (base pixels + annotations, masks re-applied).</summary>
    public EditorDocument(PixelBuffer baseImage, IEnumerable<Annotation> annotations, int historyCapacity = UndoRedoHistory.DefaultCapacity)
    {
        _base = baseImage ?? throw new ArgumentNullException(nameof(baseImage));
        ArgumentNullException.ThrowIfNull(annotations);
        History = new UndoRedoHistory(historyCapacity);
        foreach (Annotation a in annotations)
        {
            _annotations.Add(a);
            if (a is RedactionMaskAnnotation mask)
            {
                FillMask(mask, mask.Region);
            }
        }
    }

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>Captured pixels without redactions applied. May contain sensitive content under redactions.</summary>
    public PixelBuffer BaseImage => _base;

    /// <summary>Visible pixels with all redactions burned in.</summary>
    public PixelBuffer Image => _working ?? _base;

    public IReadOnlyList<Annotation> Annotations => _annotations;

    public UndoRedoHistory History { get; }

    /// <summary>True when the current state differs from the last state marked as saved (or the original capture).</summary>
    public bool IsDirty => History.CurrentStateId != _savedStateId;

    /// <summary>True if any redaction exists whose original pixels are still present in <see cref="BaseImage"/>.</summary>
    public bool HasReversibleRedactions =>
        _annotations.OfType<RedactionMaskAnnotation>().Any(m => !RegionMatches(m));

    public bool IsDiscarded { get; private set; }

    public void MarkSaved() => _savedStateId = History.CurrentStateId;

    /// <summary>Marks a specific earlier state as saved (used when a background save of that state completes).</summary>
    public void MarkSaved(long stateId) => _savedStateId = stateId;

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

    /// <summary>
    /// Destructive sanitization. Afterwards nothing hidden by a redaction exists in this document any more:
    /// <list type="number">
    /// <item>Annotations lying underneath a redaction (earlier in z-order and overlapping it) are rasterized into the
    /// image by <paramref name="rasterize"/> and removed as editable objects, so hidden text is not kept in the
    /// annotation data. Without a rasterizer they are simply removed.</item>
    /// <item>Every redaction is burned into <see cref="BaseImage"/>, so the original pixels are gone.</item>
    /// <item>Undo/redo history (which may reference originals) is cleared.</item>
    /// </list>
    /// Cannot be undone. Returns the number of annotations that were flattened.
    /// </summary>
    public int Sanitize(Func<PixelBuffer, IReadOnlyList<Annotation>, PixelBuffer>? rasterize = null)
    {
        ObjectDisposedException.ThrowIf(IsDiscarded, this);
        var covered = new List<Annotation>();
        for (int i = 0; i < _annotations.Count; i++)
        {
            Annotation a = _annotations[i];
            if (a is RedactionMaskAnnotation)
            {
                continue;
            }

            bool hidden = _annotations.Skip(i + 1).OfType<RedactionMaskAnnotation>()
                .Any(m => Intersects(a.Bounds, m.Region));
            if (hidden)
            {
                covered.Add(a);
            }
        }

        PixelBuffer visible = Image;
        if (covered.Count > 0)
        {
            if (rasterize is not null)
            {
                visible = rasterize(visible, covered);
            }

            foreach (Annotation a in covered)
            {
                _annotations.Remove(a);
            }
        }

        // Re-burn every mask on top (rasterized annotations may have painted over a redacted area).
        foreach (RedactionMaskAnnotation mask in _annotations.OfType<RedactionMaskAnnotation>())
        {
            visible.FillRect(mask.Region, mask.Color);
        }

        if (!ReferenceEquals(visible, _base))
        {
            Buffer.BlockCopy(visible.Pixels, 0, _base.Pixels, 0, _base.Pixels.Length);
            Array.Clear(visible.Pixels);
        }

        if (_working is not null && !ReferenceEquals(_working, visible))
        {
            Array.Clear(_working.Pixels);
        }

        _working = null;
        History.Clear();
        MarkSaved();
        Changed?.Invoke(this, new DocumentChangedEventArgs(imageChanged: true));
        return covered.Count;
    }

    private static bool Intersects(RectD r, PixelRect p) =>
        r.X < p.Right && r.Right > p.X && r.Y < p.Bottom && r.Bottom > p.Y;

    /// <summary>
    /// Best-effort removal of screenshot data from memory when the document is no longer needed: zeroes the base
    /// and visible images, clears history and annotations. The document must not be used afterwards.
    /// </summary>
    public void Discard()
    {
        History.Clear();
        _annotations.Clear();
        Array.Clear(_base.Pixels);
        if (_working is not null)
        {
            Array.Clear(_working.Pixels);
        }

        IsDiscarded = true;
    }

    public Annotation? FindById(Guid id) => _annotations.FirstOrDefault(a => a.Id == id);

    /// <summary>Topmost annotation under the point, or null.</summary>
    public Annotation? HitTest(PointD point, double tolerance)
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

    /// <summary>Burns a mask into the visible image.</summary>
    internal void FillMask(RedactionMaskAnnotation mask, PixelRect area)
    {
        _working ??= _base.Clone();
        _working.FillRect(area.Intersect(mask.Region), mask.Color);
    }

    /// <summary>Restores a region of the visible image from the base, then re-applies the masks that remain.</summary>
    internal void RebuildRegion(PixelRect region)
    {
        ObjectDisposedException.ThrowIf(IsDiscarded, this);
        PixelRect r = region.Intersect(_base.Bounds);
        if (r.IsEmpty || _working is null)
        {
            return;
        }

        _working.Blit(_base.Crop(r), r.X, r.Y);
        foreach (RedactionMaskAnnotation mask in _annotations.OfType<RedactionMaskAnnotation>())
        {
            if (mask.Region.IntersectsWith(r))
            {
                FillMask(mask, r);
            }
        }
    }

    /// <summary>Swaps in new images and annotations (crop and its undo).</summary>
    internal (PixelBuffer Base, PixelBuffer? Working, Annotation[] Annotations) ReplaceContent(PixelBuffer newBase, PixelBuffer? newWorking, IEnumerable<Annotation> newAnnotations)
    {
        var previous = (_base, _working, _annotations.ToArray());
        _base = newBase;
        _working = newWorking;
        _annotations.Clear();
        _annotations.AddRange(newAnnotations);
        return previous;
    }

    private bool RegionMatches(RedactionMaskAnnotation mask)
    {
        // A mask is "burned" (irreversible) if the base already holds the fill color everywhere in its region.
        PixelRect r = mask.Region.Intersect(_base.Bounds);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            for (int x = r.X; x < r.Right; x++)
            {
                if (_base.GetPixel(x, y) != mask.Color)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void RaiseChanged(IEditCommand? command)
    {
        if (command is not null)
        {
            bool size = command is CropCommand;
            Changed?.Invoke(this, new DocumentChangedEventArgs(command is RedactCommand || size, size));
        }
    }
}
