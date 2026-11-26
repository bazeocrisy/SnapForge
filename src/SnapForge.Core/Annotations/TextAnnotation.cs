using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>
/// A block of text whose top-left corner is <see cref="Position"/>. The UI measures the text with the
/// real font and stores the result in <see cref="MeasuredSize"/> so hit testing matches what is drawn.
/// </summary>
public sealed record TextAnnotation(PointD Position, string Text, double FontSize) : Annotation
{
    public const double Padding = 6;

    public PointD MeasuredSize { get; init; }

    public override RectD Bounds =>
        new(Position.X - Padding, Position.Y - Padding, Math.Max(0, MeasuredSize.X) + 2 * Padding, Math.Max(0, MeasuredSize.Y) + 2 * Padding);

    public override bool HitTest(PointD point, double tolerance) => Bounds.Inflate(tolerance).Contains(point);

    public override Annotation Translate(double dx, double dy) => this with { Position = Position + new PointD(dx, dy) };
}
