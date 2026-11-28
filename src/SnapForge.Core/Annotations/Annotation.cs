using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Annotations;

/// <summary>
/// A vector mark drawn over the screenshot. Annotations are immutable; edits produce a new instance
/// with the same <see cref="Id"/>, which keeps undo/redo trivial and safe.
/// Coordinates are image pixels.
/// </summary>
public abstract record Annotation
{
    /// <summary>Smallest drag (in image pixels) that creates a shape; shorter drags are treated as stray clicks.</summary>
    public const double MinimumShapeExtent = 3;

    public Guid Id { get; init; } = Guid.NewGuid();

    public ArgbColor Color { get; init; } = ArgbColor.Parse("#E53935");

    public double StrokeWidth { get; init; } = 4;

    /// <summary>Area that the rendered annotation may cover (used for hit testing and selection outlines).</summary>
    public abstract RectD Bounds { get; }

    public abstract bool HitTest(PointD point, double tolerance);

    public abstract Annotation Translate(double dx, double dy);
}
