using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Annotations;

/// <summary>
/// An opaque block placed at the top of the annotation stack by a redaction, so that annotations drawn
/// <em>before</em> the redaction (for example text the user typed over a password) are covered too.
/// It matches the pixels the redaction already overwrote. It cannot be selected, moved or edited;
/// it is removed only by undoing the redaction.
/// </summary>
public sealed record RedactionMaskAnnotation(PixelRect Region) : Annotation
{
    public override RectD Bounds => new(Region.X, Region.Y, Region.Width, Region.Height);

    public override bool HitTest(PointD point, double tolerance) => false;

    public override Annotation Translate(double dx, double dy) => this;

    public static RedactionMaskAnnotation For(PixelRect region, ArgbColor fill) =>
        new(region) { Color = fill with { A = 255 }, StrokeWidth = 0 };
}
