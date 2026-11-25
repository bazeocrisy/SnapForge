using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Overwrites a region of the screenshot pixels with an opaque color and places an opaque
/// <see cref="RedactionMaskAnnotation"/> on top of all existing annotations.
/// <para>
/// Reversible vs. irreversible: while editing, the original pixels are kept only inside this command
/// object (in memory) so the user can undo. Exports (save, copy) are built from the redacted pixel buffer
/// plus the mask, so an exported image never contains the original pixels or annotations beneath the
/// redaction. The originals are wiped by <see cref="WipeOriginal"/> when the document is discarded.
/// </para>
/// </summary>
public sealed class RedactCommand : IEditCommand
{
    private PixelBuffer? _original;
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

    public string Description => "Redact";

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelRect clipped = Region.Intersect(document.Image.Bounds);
        if (clipped.IsEmpty)
        {
            throw new InvalidOperationException("Redaction region lies outside the image.");
        }

        _original ??= document.Image.Crop(clipped);
        AppliedRegion = document.Image.FillRect(clipped, Fill);
        Mask ??= RedactionMaskAnnotation.For(AppliedRegion, Fill);
        _maskIndex = _maskIndex < 0 ? document.Annotations.Count : Math.Min(_maskIndex, document.Annotations.Count);
        document.InsertAnnotation(_maskIndex, Mask);
    }

    /// <summary>The cover placed above earlier annotations; set on first apply.</summary>
    public RedactionMaskAnnotation? Mask { get; private set; }

    /// <summary>
    /// Zeroes and drops the retained original pixels. After this the redaction can no longer be undone;
    /// called when the document is discarded so the sensitive pixels do not linger in memory.
    /// </summary>
    public void WipeOriginal()
    {
        if (_original is not null)
        {
            Array.Clear(_original.Pixels);
            _original = null;
        }
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_original is null || Mask is null)
        {
            throw new InvalidOperationException("Cannot revert a redaction that was never applied or whose original pixels were wiped.");
        }

        document.RemoveAnnotation(Mask.Id);
        document.Image.Blit(_original, AppliedRegion.X, AppliedRegion.Y);
    }
}
