using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Editing;

/// <summary>Moves annotations along with whole-image edits (rotate, flip, resize, canvas changes).</summary>
internal static class AnnotationGeometry
{
    /// <summary>
    /// Maps an annotation through a lossless transform of a <paramref name="width"/> × <paramref name="height"/> image.
    /// Points, rectangles, step-marker centers and redaction regions map exactly. Text is never drawn rotated or
    /// mirrored, so a text block keeps its orientation and only the center of its measured box follows the transform.
    /// Unknown annotation types are translated so that the center of their bounds follows the transform.
    /// </summary>
    public static Annotation Transform(Annotation annotation, int width, int height, ImageTransform transform)
    {
        PointD P(PointD p) => ImageOps.TransformPoint(p, width, height, transform);
        RectD R(RectD r) => ImageOps.TransformRect(r, width, height, transform);
        switch (annotation)
        {
            case ArrowAnnotation a:
                return a with { Start = P(a.Start), End = P(a.End) };
            case LineAnnotation l:
                return l with { Start = P(l.Start), End = P(l.End) };
            case RectangleAnnotation r:
                return r with { Rect = R(r.Rect) };
            case EllipseAnnotation e:
                return e with { Rect = R(e.Rect) };
            case HighlightAnnotation h:
                return h with { Rect = R(h.Rect) };
            case StepMarkerAnnotation s:
                return s with { Center = P(s.Center) };
            case TextAnnotation t:
                var half = new PointD(Math.Max(0, t.MeasuredSize.X) / 2, Math.Max(0, t.MeasuredSize.Y) / 2);
                return t with { Position = P(t.Position + half) - half };
            case RedactionMaskAnnotation m:
                return m with { Region = ImageOps.TransformRect(m.Region, width, height, transform) };
            default:
                PointD c = annotation.Bounds.Center;
                PointD moved = P(c);
                return annotation.Translate(moved.X - c.X, moved.Y - c.Y);
        }
    }

    /// <summary>
    /// Scales an annotation for an image resized from <paramref name="oldSize"/> to <paramref name="newSize"/>.
    /// Positions and rectangles scale per axis; stroke widths, font sizes, measured text sizes and step-marker radii
    /// scale by the geometric mean of the two factors (equal to the factor for an aspect-preserving resize).
    /// A redaction region becomes every resized pixel its original pixels contribute to, so the cover stays complete;
    /// returns null if nothing of it remains.
    /// </summary>
    public static Annotation? Scale(Annotation annotation, PixelSize oldSize, PixelSize newSize)
    {
        double sx = (double)newSize.Width / oldSize.Width, sy = (double)newSize.Height / oldSize.Height;
        double u = Math.Sqrt(sx * sy);
        PointD P(PointD p) => new(p.X * sx, p.Y * sy);
        RectD R(RectD r) => new(r.X * sx, r.Y * sy, r.Width * sx, r.Height * sy);
        switch (annotation)
        {
            case RedactionMaskAnnotation m:
                PixelRect region = ImageOps.ResizeFootprint(m.Region, oldSize.Width, oldSize.Height, newSize.Width, newSize.Height);
                return region.IsEmpty ? null : m with { Region = region };
            case ArrowAnnotation a:
                return a with { Start = P(a.Start), End = P(a.End), StrokeWidth = a.StrokeWidth * u };
            case LineAnnotation l:
                return l with { Start = P(l.Start), End = P(l.End), StrokeWidth = l.StrokeWidth * u };
            case RectangleAnnotation r:
                return r with { Rect = R(r.Rect), StrokeWidth = r.StrokeWidth * u };
            case EllipseAnnotation e:
                return e with { Rect = R(e.Rect), StrokeWidth = e.StrokeWidth * u };
            case HighlightAnnotation h:
                return h with { Rect = R(h.Rect), StrokeWidth = h.StrokeWidth * u };
            case StepMarkerAnnotation s:
                return s with { Center = P(s.Center), Radius = s.Radius * u, StrokeWidth = s.StrokeWidth * u };
            case TextAnnotation t:
                return t with
                {
                    Position = P(t.Position),
                    FontSize = t.FontSize * u,
                    MeasuredSize = new PointD(t.MeasuredSize.X * u, t.MeasuredSize.Y * u),
                    StrokeWidth = t.StrokeWidth * u,
                };
            default:
                PointD c = annotation.Bounds.Center;
                PointD moved = P(c);
                return annotation.Translate(moved.X - c.X, moved.Y - c.Y);
        }
    }

    /// <summary>
    /// Moves an annotation by a whole-pixel offset. Redaction masks (which ignore <see cref="Annotation.Translate"/>)
    /// are moved and clipped to <paramref name="newBounds"/>; null when nothing of a mask remains.
    /// </summary>
    public static Annotation? Offset(Annotation annotation, int dx, int dy, PixelRect newBounds)
    {
        if (annotation is RedactionMaskAnnotation m)
        {
            PixelRect region = m.Region.Offset(dx, dy).Intersect(newBounds);
            return region.IsEmpty ? null : m with { Region = region };
        }

        return annotation.Translate(dx, dy);
    }

    /// <summary>Applies <paramref name="map"/> to every annotation, dropping those it returns null for.</summary>
    public static List<Annotation> MapAll(IEnumerable<Annotation> annotations, Func<Annotation, Annotation?> map)
    {
        var result = new List<Annotation>();
        foreach (Annotation a in annotations)
        {
            if (map(a) is { } mapped)
            {
                result.Add(mapped);
            }
        }

        return result;
    }

    /// <summary>Burns all masks into a copy of <paramref name="baseImage"/>, or returns null when there are none.</summary>
    public static PixelBuffer? BurnMasks(PixelBuffer baseImage, IEnumerable<Annotation> annotations)
    {
        PixelBuffer? working = null;
        foreach (RedactionMaskAnnotation mask in annotations.OfType<RedactionMaskAnnotation>())
        {
            working ??= baseImage.Clone();
            working.FillRect(mask.Region, mask.Color);
        }

        return working;
    }
}

/// <summary>Remembers the document content replaced by a whole-image command so undo restores it exactly.</summary>
internal sealed class ContentUndo
{
    private (PixelBuffer Base, PixelBuffer? Working, Annotation[] Annotations)? _previous;

    public void Replace(EditorDocument document, PixelBuffer newBase, PixelBuffer? newWorking, IEnumerable<Annotation> annotations) =>
        _previous = document.ReplaceContent(newBase, newWorking, annotations);

    public void Restore(EditorDocument document, string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previous is not { } p)
        {
            throw new InvalidOperationException($"Cannot revert '{description}': it was never applied.");
        }

        document.ReplaceContent(p.Base, p.Working, p.Annotations);
        _previous = null;
    }
}

/// <summary>A command that replaces the visible pixels; tells <see cref="EditorDocument.Changed"/> what to report.</summary>
internal interface IImageEditCommand
{
    /// <summary>True when the command changes the image dimensions.</summary>
    bool ChangesSize { get; }
}
