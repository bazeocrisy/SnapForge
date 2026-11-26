using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Crops the capture to a rectangle: base and visible images are cut, annotations are shifted, redaction masks are
/// clipped (dropped if entirely outside). The removed area is gone from the library copy after the next save.
/// Undo restores the previous images and annotations from memory.
/// </summary>
public sealed class CropCommand : IEditCommand
{
    private (PixelBuffer Base, PixelBuffer? Working, Annotation[] Annotations)? _previous;

    public CropCommand(PixelRect region)
    {
        if (region.Width < 1 || region.Height < 1)
        {
            throw new ArgumentException("Crop region must not be empty.", nameof(region));
        }

        Region = region;
    }

    public PixelRect Region { get; }

    public string Description => "Crop";

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelRect r = Region.Intersect(document.BaseImage.Bounds);
        if (r.Width < 1 || r.Height < 1)
        {
            throw new InvalidOperationException("Crop region lies outside the image.");
        }

        PixelBuffer newBase = document.BaseImage.Crop(r);
        PixelBuffer? newWorking = ReferenceEquals(document.Image, document.BaseImage) ? null : document.Image.Crop(r);
        var annotations = new List<Annotation>();
        foreach (Annotation a in document.Annotations)
        {
            if (a is RedactionMaskAnnotation mask)
            {
                PixelRect clipped = mask.Region.Intersect(r);
                if (!clipped.IsEmpty)
                {
                    annotations.Add(mask with { Region = clipped.Offset(-r.X, -r.Y) });
                }
            }
            else
            {
                annotations.Add(a.Translate(-r.X, -r.Y));
            }
        }

        _previous = document.ReplaceContent(newBase, newWorking, annotations);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previous is not { } p)
        {
            throw new InvalidOperationException("Cannot revert a crop that was never applied.");
        }

        document.ReplaceContent(p.Base, p.Working, p.Annotations);
        _previous = null;
    }
}
