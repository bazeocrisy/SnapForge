using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Blends an overlay image (for example a rendered text watermark or a logo) onto the base image at (x, y) with the
/// given opacity. Undo restores the exact original pixels. Pixels under redaction covers keep their originals in the
/// base image (the cover is drawn on top anyway), so undoing those redactions still works.
/// </summary>
public sealed class OverlayImageCommand(PixelBuffer overlay, int x, int y, double opacity, string description = "Watermark") : IEditCommand, IImageEditCommand
{
    private PixelBuffer? _saved;
    private PixelRect _applied;

    public string Description { get; } = description;

    bool IImageEditCommand.ChangesSize => false;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(overlay);
        PixelBuffer baseImage = document.BaseImage;
        PixelRect r = new PixelRect(x, y, overlay.Width, overlay.Height).Intersect(baseImage.Bounds);
        if (r.IsEmpty)
        {
            throw new InvalidOperationException("The overlay lies outside the image.");
        }

        PixelBuffer saved = baseImage.Crop(r);
        PixelBuffer blended = ImageOps.DrawImage(saved, overlay, x - r.X, y - r.Y, Math.Clamp(opacity, 0, 1));
        baseImage.Blit(blended, r.X, r.Y);
        foreach (RedactionMaskAnnotation mask in document.Annotations.OfType<RedactionMaskAnnotation>())
        {
            PixelRect covered = mask.Region.Intersect(r);
            if (!covered.IsEmpty)
            {
                baseImage.Blit(saved.Crop(covered.Offset(-r.X, -r.Y)), covered.X, covered.Y);
            }
        }

        _saved = saved;
        _applied = r;
        document.RebuildRegion(r);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_saved is null || document.IsDiscarded)
        {
            throw new InvalidOperationException($"Cannot revert '{Description}'.");
        }

        document.BaseImage.Blit(_saved, _applied.X, _applied.Y);
        document.RebuildRegion(_applied);
        _saved = null;
    }
}
