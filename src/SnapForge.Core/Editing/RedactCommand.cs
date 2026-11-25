using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>
/// Permanently overwrites a region of the screenshot pixels with an opaque color.
/// The original pixels are kept only inside this command object (in memory) so the user can undo
/// within the current session; they are never written to disk, and exports are built from the
/// redacted pixel buffer, so a saved or copied image cannot contain them.
/// </summary>
public sealed class RedactCommand : IEditCommand
{
    private PixelBuffer? _original;

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
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_original is null)
        {
            throw new InvalidOperationException("Cannot revert a redaction that was never applied.");
        }

        document.Image.Blit(_original, AppliedRegion.X, AppliedRegion.Y);
    }
}
