using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>A filled numbered circle used to mark the steps of a procedure.</summary>
public sealed record StepMarkerAnnotation(PointD Center, int Number, double Radius) : Annotation
{
    public override RectD Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

    public override bool HitTest(PointD point, double tolerance) => point.DistanceTo(Center) <= Radius + tolerance;

    public override Annotation Translate(double dx, double dy) => this with { Center = Center + new PointD(dx, dy) };

    /// <summary>Radius that keeps markers proportional to the chosen stroke width and legible on large captures.</summary>
    public static double RadiusForStroke(double strokeWidth) => Math.Clamp(8 + strokeWidth * 2.5, 12, 40);
}

public static class StepNumbering
{
    /// <summary>Next step number: one more than the highest marker present, so deleting a middle step never creates a duplicate.</summary>
    public static int Next(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        int max = 0;
        foreach (Annotation a in annotations)
        {
            if (a is StepMarkerAnnotation s && s.Number > max)
            {
                max = s.Number;
            }
        }

        return max + 1;
    }
}
