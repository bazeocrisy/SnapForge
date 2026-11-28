using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>A translucent filled rectangle, like a highlighter pen, drawn at <see cref="Opacity"/>.</summary>
public sealed record HighlightAnnotation(RectD Rect) : Annotation
{
    public const double DefaultOpacity = 0.4;

    public double Opacity { get; init; } = DefaultOpacity;

    public override RectD Bounds => Rect;

    public override bool HitTest(PointD point, double tolerance) => Rect.Inflate(tolerance).Contains(point);

    public override Annotation Translate(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };

    public bool IsDegenerate => Rect.Width < MinimumShapeExtent || Rect.Height < MinimumShapeExtent;
}
