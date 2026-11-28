using SnapForge.Core.Geometry;

namespace SnapForge.Core.Annotations;

/// <summary>Built-in stamps. All artwork is original, simple vector shapes drawn by SnapForge.</summary>
public enum StampKind
{
    Check,
    Cross,
    Warning,
    Info,
    Question,
    Star,
}

/// <summary>A stamp centred on <see cref="Center"/>, <see cref="Size"/> pixels wide and high.</summary>
public sealed record StampAnnotation(PointD Center, StampKind Kind, double Size) : Annotation
{
    public override RectD Bounds => new(Center.X - (Size / 2), Center.Y - (Size / 2), Size, Size);

    public override bool HitTest(PointD point, double tolerance) => Bounds.Inflate(tolerance).Contains(point);

    public override Annotation Translate(double dx, double dy) => this with { Center = new PointD(Center.X + dx, Center.Y + dy) };
}
