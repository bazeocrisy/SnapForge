using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Redacts a region: places an opaque <see cref="RedactionMaskAnnotation"/> above every existing annotation and
/// burns it into the visible image (<see cref="EditorDocument.Image"/>), from which all exports are produced.
/// <para>
/// Reversible vs. irreversible: the document's <see cref="EditorDocument.BaseImage"/> keeps the original pixels so
/// the redaction can be undone, and the editable library copy stores that base image. Exported files and clipboard
/// images never contain the originals. <see cref="EditorDocument.Sanitize"/> burns redactions into the base
/// image permanently.
/// </para>
/// </summary>
public sealed class RedactCommand : IEditCommand
{
    private int _maskIndex = -1;

    public RedactCommand(PixelRect region, ArgbColor fill)
    {
        if (region.IsEmpty)
        {
            throw new ArgumentException("Redaction region must not be empty.", nameof(region));
        }

        Region = region;
        Fill = fill with { A = 255 };
    }

    public PixelRect Region { get; }

    public ArgbColor Fill { get; }

    /// <summary>The part of <see cref="Region"/> inside the image; set on first apply.</summary>
    public PixelRect AppliedRegion { get; private set; }

    /// <summary>The cover placed above earlier annotations; set on first apply.</summary>
    public RedactionMaskAnnotation? Mask { get; private set; }

    public string Description => "Redact";

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelRect clipped = Region.Intersect(document.Image.Bounds);
        if (clipped.IsEmpty)
        {
            throw new InvalidOperationException("Redaction region lies outside the image.");
        }

        AppliedRegion = clipped;
        Mask ??= RedactionMaskAnnotation.For(clipped, Fill);
        _maskIndex = _maskIndex < 0 ? document.Annotations.Count : Math.Min(_maskIndex, document.Annotations.Count);
        document.InsertAnnotation(_maskIndex, Mask);
        document.FillMask(Mask, clipped);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Mask is null || document.IsDiscarded)
        {
            throw new InvalidOperationException("Cannot revert a redaction that was never applied or whose document was discarded.");
        }

        document.RemoveAnnotation(Mask.Id);
        document.RebuildRegion(AppliedRegion);
    }
}
