using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Scales the whole capture with <see cref="ImageOps.Resize"/>. Annotations scale with it: positions, end points and
/// rectangles per axis; <see cref="Annotation.StrokeWidth"/>, <see cref="TextAnnotation.FontSize"/>,
/// <see cref="TextAnnotation.MeasuredSize"/> and <see cref="StepMarkerAnnotation.Radius"/> by the geometric mean of the
/// two factors (the plain factor when the aspect ratio is kept). Text is not stretched by a non-uniform resize.
/// <para>
/// Redaction: the base image is resized and each redaction region becomes every resized pixel that its original pixels
/// contribute to (so no resampled trace of redacted content is visible next to the cover); the covers are then burned
/// into the new visible image. Undo restores the previous images and annotations from memory.
/// </para>
/// </summary>
public sealed class ResizeImageCommand : IEditCommand, IImageEditCommand
{
    private readonly ContentUndo _undo = new();

    /// <exception cref="ArgumentOutOfRangeException">The size is outside <see cref="ImageOps.ValidateSize"/>'s range.</exception>
    public ResizeImageCommand(int newWidth, int newHeight)
    {
        ImageOps.ValidateSize(newWidth, newHeight);
        NewSize = new PixelSize(newWidth, newHeight);
    }

    public PixelSize NewSize { get; }

    public string Description => "Resize image";

    bool IImageEditCommand.ChangesSize => true;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelBuffer oldBase = document.BaseImage;
        var oldSize = new PixelSize(oldBase.Width, oldBase.Height);
        PixelBuffer newBase = ImageOps.Resize(oldBase, NewSize.Width, NewSize.Height);
        List<Annotation> annotations = AnnotationGeometry.MapAll(document.Annotations, a => AnnotationGeometry.Scale(a, oldSize, NewSize));
        _undo.Replace(document, newBase, AnnotationGeometry.BurnMasks(newBase, annotations), annotations);
    }

    public void Revert(EditorDocument document) => _undo.Restore(document, Description);
}
