using SnapForge.Core.Annotations;

namespace SnapForge.Core.Editing;

public sealed class AddAnnotationCommand(Annotation annotation) : IEditCommand
{
    private int _index = -1;

    public Annotation Annotation { get; } = annotation ?? throw new ArgumentNullException(nameof(annotation));

    public string Description => $"Add {AnnotationNames.Of(Annotation)}";

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _index = _index < 0 ? document.Annotations.Count : Math.Min(_index, document.Annotations.Count);
        document.InsertAnnotation(_index, Annotation);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.RemoveAnnotation(Annotation.Id);
    }
}

public sealed class RemoveAnnotationCommand(Annotation annotation) : IEditCommand
{
    private int _index;

    public Annotation Annotation { get; } = annotation ?? throw new ArgumentNullException(nameof(annotation));

    public string Description => $"Delete {AnnotationNames.Of(Annotation)}";

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _index = document.RemoveAnnotation(Annotation.Id);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.InsertAnnotation(_index, Annotation);
    }
}

/// <summary>Replaces an annotation with an edited copy (same Id), e.g. after moving it.</summary>
public sealed class ReplaceAnnotationCommand : IEditCommand
{
    public ReplaceAnnotationCommand(Annotation before, Annotation after, string description)
    {
        Before = before ?? throw new ArgumentNullException(nameof(before));
        After = after ?? throw new ArgumentNullException(nameof(after));
        if (before.Id != after.Id)
        {
            throw new ArgumentException("Replacement must keep the annotation Id.", nameof(after));
        }

        Description = description;
    }

    public Annotation Before { get; }

    public Annotation After { get; }

    public string Description { get; }

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.ReplaceAnnotation(After);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.ReplaceAnnotation(Before);
    }
}

internal static class AnnotationNames
{
    public static string Of(Annotation annotation) => annotation switch
    {
        ArrowAnnotation => "arrow",
        LineAnnotation => "line",
        RectangleAnnotation => "rectangle",
        EllipseAnnotation => "ellipse",
        TextAnnotation => "text",
        StepMarkerAnnotation s => $"step {s.Number}",
        RedactionMaskAnnotation => "redaction",
        _ => "annotation",
    };
}
