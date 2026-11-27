using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Changes the canvas size without scaling (<see cref="ImageOps.ResizeCanvas"/>). Annotations move by the offset at
/// which the old image lands; redaction masks are moved too and clipped to the new canvas (dropped if nothing is
/// left). A smaller canvas cuts the image like a crop. Undo restores the previous images and annotations.
/// </summary>
public sealed class ResizeCanvasCommand : IEditCommand, IImageEditCommand
{
    private readonly ContentUndo _undo = new();

    /// <exception cref="ArgumentOutOfRangeException">The size is outside <see cref="ImageOps.ValidateSize"/>'s range.</exception>
    public ResizeCanvasCommand(int newWidth, int newHeight, CanvasAnchor anchor, ArgbColor fill)
    {
        ImageOps.ValidateSize(newWidth, newHeight);
        if (!Enum.IsDefined(anchor))
        {
            throw new ArgumentOutOfRangeException(nameof(anchor), anchor, "Unknown canvas anchor.");
        }

        NewSize = new PixelSize(newWidth, newHeight);
        Anchor = anchor;
        Fill = fill;
    }

    public PixelSize NewSize { get; }

    public CanvasAnchor Anchor { get; }

    public ArgbColor Fill { get; }

    public string Description => "Resize canvas";

    bool IImageEditCommand.ChangesSize => true;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CanvasResult b = ImageOps.ResizeCanvas(document.BaseImage, NewSize.Width, NewSize.Height, Anchor, Fill);
        PixelBuffer? working = ReferenceEquals(document.Image, document.BaseImage)
            ? null
            : ImageOps.ResizeCanvas(document.Image, NewSize.Width, NewSize.Height, Anchor, Fill).Image;
        List<Annotation> annotations = AnnotationGeometry.MapAll(document.Annotations, a => AnnotationGeometry.Offset(a, b.OffsetX, b.OffsetY, b.Image.Bounds));
        _undo.Replace(document, b.Image, working, annotations);
    }

    public void Revert(EditorDocument document) => _undo.Restore(document, Description);
}

/// <summary>
/// Frames the capture with a solid border (<see cref="ImageOps.AddBorder"/>). The canvas grows by the thickness on
/// every side and all annotations (including redaction masks) move right and down by it. Undo restores the previous
/// images and annotations.
/// </summary>
public sealed class AddBorderCommand : IEditCommand, IImageEditCommand
{
    private readonly ContentUndo _undo = new();

    public AddBorderCommand(int thickness, ArgbColor color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(thickness, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(thickness, ImageOps.MaxDimension);
        Thickness = thickness;
        Color = color;
    }

    public int Thickness { get; }

    public ArgbColor Color { get; }

    public string Description => "Add border";

    bool IImageEditCommand.ChangesSize => true;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelBuffer newBase = ImageOps.AddBorder(document.BaseImage, Thickness, Color);
        PixelBuffer? working = ReferenceEquals(document.Image, document.BaseImage) ? null : ImageOps.AddBorder(document.Image, Thickness, Color);
        List<Annotation> annotations = AnnotationGeometry.MapAll(document.Annotations, a => AnnotationGeometry.Offset(a, Thickness, Thickness, newBase.Bounds));
        _undo.Replace(document, newBase, working, annotations);
    }

    public void Revert(EditorDocument document) => _undo.Restore(document, Description);
}

/// <summary>
/// Adds a drop shadow (<see cref="ImageOps.AddDropShadow"/>): the canvas grows to hold the image and its blurred,
/// offset shadow, and all annotations (including redaction masks) move by the growth on the left and top. The image
/// pixels themselves are unchanged. Undo restores the previous images and annotations.
/// </summary>
public sealed class AddDropShadowCommand : IEditCommand, IImageEditCommand
{
    private readonly ContentUndo _undo = new();

    public AddDropShadowCommand(int offsetX, int offsetY, int blurRadius, ArgbColor shadowColor, ArgbColor background)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(blurRadius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blurRadius, ImageOps.MaxBlurRadius);
        OffsetX = offsetX;
        OffsetY = offsetY;
        BlurRadius = blurRadius;
        ShadowColor = shadowColor;
        Background = background;
    }

    public int OffsetX { get; }

    public int OffsetY { get; }

    public int BlurRadius { get; }

    public ArgbColor ShadowColor { get; }

    public ArgbColor Background { get; }

    public string Description => "Add drop shadow";

    bool IImageEditCommand.ChangesSize => true;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CanvasResult b = ImageOps.AddDropShadow(document.BaseImage, OffsetX, OffsetY, BlurRadius, ShadowColor, Background);
        PixelBuffer? working = ReferenceEquals(document.Image, document.BaseImage)
            ? null
            : ImageOps.AddDropShadow(document.Image, OffsetX, OffsetY, BlurRadius, ShadowColor, Background).Image;
        List<Annotation> annotations = AnnotationGeometry.MapAll(document.Annotations, a => AnnotationGeometry.Offset(a, b.OffsetX, b.OffsetY, b.Image.Bounds));
        _undo.Replace(document, b.Image, working, annotations);
    }

    public void Revert(EditorDocument document) => _undo.Restore(document, Description);
}
