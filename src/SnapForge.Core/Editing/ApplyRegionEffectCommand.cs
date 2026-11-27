using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>A cosmetic effect for <see cref="ApplyRegionEffectCommand"/>.</summary>
public enum RegionEffect
{
    /// <summary><see cref="ImageOps.BoxBlur"/>; the strength is the blur radius.</summary>
    Blur,

    /// <summary><see cref="ImageOps.Pixelate"/>; the strength is the block size.</summary>
    Pixelate,
}

/// <summary>
/// Blurs or pixelates a region of the capture by changing the base pixels in place.
/// <para>
/// <b>This is not redaction.</b> Blurred or pixelated text can often be reconstructed (for example by trying candidate
/// texts and comparing the result), and the base image the library stores keeps the effect as ordinary pixels with no
/// guarantee about what can be recovered. To hide sensitive content permanently use <see cref="RedactCommand"/>
/// (opaque fill) and <see cref="EditorDocument.Sanitize"/>.
/// </para>
/// <para>
/// The effect is computed from the visible image, so existing redaction covers stay intact and the hidden pixels under
/// them never leak into the blurred surroundings; base pixels under a redaction are left as they were. Undo restores
/// the saved bytes of the region.
/// </para>
/// </summary>
public sealed class ApplyRegionEffectCommand : IEditCommand, IImageEditCommand
{
    private PixelBuffer? _saved;

    /// <param name="region">Area to change (clipped to the image when applied).</param>
    /// <param name="effect">Blur or pixelate.</param>
    /// <param name="strength">Blur radius (1..<see cref="ImageOps.MaxBlurRadius"/>) or pixelation block size (at least 2).</param>
    public ApplyRegionEffectCommand(PixelRect region, RegionEffect effect, int strength)
    {
        if (region.IsEmpty)
        {
            throw new ArgumentException("Effect region must not be empty.", nameof(region));
        }

        switch (effect)
        {
            case RegionEffect.Blur:
                ArgumentOutOfRangeException.ThrowIfLessThan(strength, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(strength, ImageOps.MaxBlurRadius);
                break;
            case RegionEffect.Pixelate:
                ArgumentOutOfRangeException.ThrowIfLessThan(strength, 2);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "Unknown region effect.");
        }

        Region = region;
        Effect = effect;
        Strength = strength;
    }

    public PixelRect Region { get; }

    public RegionEffect Effect { get; }

    public int Strength { get; }

    /// <summary>The part of <see cref="Region"/> inside the image; set on apply.</summary>
    public PixelRect AppliedRegion { get; private set; }

    public string Description => Effect == RegionEffect.Blur ? "Blur" : "Pixelate";

    bool IImageEditCommand.ChangesSize => false;

    public void Apply(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        PixelBuffer baseImage = document.BaseImage;
        PixelRect r = Region.Intersect(baseImage.Bounds);
        if (r.IsEmpty)
        {
            throw new InvalidOperationException("Effect region lies outside the image.");
        }

        PixelBuffer saved = baseImage.Crop(r);
        PixelBuffer visible = document.Image.Crop(r);
        PixelBuffer changed = Effect == RegionEffect.Blur
            ? ImageOps.BoxBlur(visible, visible.Bounds, Strength)
            : ImageOps.Pixelate(visible, visible.Bounds, Strength);
        baseImage.Blit(changed, r.X, r.Y);

        // Keep the original base pixels under redaction covers so undoing those redactions still works.
        foreach (RedactionMaskAnnotation mask in document.Annotations.OfType<RedactionMaskAnnotation>())
        {
            PixelRect covered = mask.Region.Intersect(r);
            if (!covered.IsEmpty)
            {
                baseImage.Blit(saved.Crop(covered.Offset(-r.X, -r.Y)), covered.X, covered.Y);
            }
        }

        _saved = saved;
        AppliedRegion = r;
        document.RebuildRegion(r);
    }

    public void Revert(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_saved is null || document.IsDiscarded)
        {
            throw new InvalidOperationException($"Cannot revert '{Description}': it was never applied or its document was discarded.");
        }

        document.BaseImage.Blit(_saved, AppliedRegion.X, AppliedRegion.Y);
        document.RebuildRegion(AppliedRegion);
        _saved = null;
    }
}
