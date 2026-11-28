using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Rotates (90°, 180°, 270°) or flips the whole capture losslessly. The base and visible images are transformed and
/// every annotation moves with the content: line and arrow end points, rectangle/ellipse/highlight rectangles,
/// step-marker centers and redaction regions map exactly. Text is not drawn rotated or mirrored, so a text block
/// keeps its horizontal orientation and its box center (from <see cref="TextAnnotation.MeasuredSize"/>) follows the
/// transform. Undo restores the previous images and annotations from memory.
/// </summary>
public sealed class TransformImageCommand : IEditCommand, IImageEditCommand
{
    private readonly ContentUndo _undo = new();

    public TransformImageCommand(ImageTransform transform)
    {
        if (!Enum.IsDefined(transform))
        {
            throw new ArgumentOutOfRangeException(nameof(transform), transform, "Unknown image transform.");
        }

        Transform = transform;
    }

    public ImageTransform Transform { get; }

    public string Description => Transform switch
    {
        ImageTransform.Rotate90 => "Rotate right",
        ImageTransform.Rotate180 => "Rotate 180°",
        ImageTransform.Rotate270 => "Rotate left",
        ImageTransform.FlipHorizontal => "Flip horizontal",
        _ => "Flip vertical",
    };

    /// <summary>Quarter turns swap the dimensions (a size change); 180° and flips keep them.</summary>
    bool IImageEditCommand.ChangesSize => Transform is ImageTransform.Rotate90 or ImageTransform.Rotate270;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelBuffer oldBase = document.BaseImage;
        int w = oldBase.Width, h = oldBase.Height;
        PixelBuffer newBase = ImageOps.Transform(oldBase, Transform);
        PixelBuffer? newWorking = ReferenceEquals(document.Image, oldBase) ? null : ImageOps.Transform(document.Image, Transform);
        List<Annotation> annotations = AnnotationGeometry.MapAll(document.Annotations, a => AnnotationGeometry.Transform(a, w, h, Transform));
        _undo.Replace(document, newBase, newWorking, annotations);
    }

    public void Revert(EditorDocument document) => _undo.Restore(document, Description);
}
