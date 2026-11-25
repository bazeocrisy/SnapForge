using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Rendering;

/// <summary>
/// Draws annotations in image-pixel space. Used both on screen and for export, so what the user sees is
/// what gets saved.
/// </summary>
internal static class AnnotationRenderer
{
    /// <summary>Text is laid out at a fixed 1.0 pixels-per-DIP so measurement and output never depend on the monitor.</summary>
    private const double PixelsPerDip = 1.0;

    private static readonly Typeface TextTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface StepTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static void Draw(DrawingContext dc, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(dc);
        switch (annotation)
        {
            case ArrowAnnotation a:
                DrawArrow(dc, a);
                break;
            case LineAnnotation l:
                dc.DrawLine(StrokePen(l.Color, l.StrokeWidth), P(l.Start), P(l.End));
                break;
            case RectangleAnnotation r:
                dc.DrawRectangle(null, StrokePen(r.Color, r.StrokeWidth, PenLineJoin.Miter), R(r.Rect));
                break;
            case EllipseAnnotation e:
                dc.DrawEllipse(null, StrokePen(e.Color, e.StrokeWidth), P(e.Rect.Center), e.Rect.Width / 2, e.Rect.Height / 2);
                break;
            case TextAnnotation t:
                DrawText(dc, t);
                break;
            case StepMarkerAnnotation s:
                DrawStep(dc, s);
                break;
            case RedactionMaskAnnotation m:
                // Integer-aligned, unstroked, opaque: covers whole pixels exactly with no anti-aliased edge.
                dc.DrawRectangle(Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(m.Color with { A = 255 }))), null,
                    new Rect(m.Region.X, m.Region.Y, m.Region.Width, m.Region.Height));
                break;
            default:
                throw new NotSupportedException($"No renderer for {annotation?.GetType().Name}.");
        }
    }

    /// <summary>Size of the text block as it will be drawn (used to store hit-test bounds).</summary>
    public static PointD MeasureText(string text, double fontSize)
    {
        FormattedText ft = Format(text, fontSize, Brushes.Black);
        return new PointD(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }

    /// <summary>A contrasting color for outlines and labels drawn over <paramref name="c"/>.</summary>
    public static Color ContrastFor(ArgbColor c)
    {
        double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return luminance > 0.6 ? Color.FromRgb(20, 20, 20) : Colors.White;
    }

    private static void DrawArrow(DrawingContext dc, ArrowAnnotation a)
    {
        ArrowGeometry g = a.Geometry;
        Brush fill = Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(a.Color)));
        dc.DrawLine(StrokePen(a.Color, a.StrokeWidth), P(g.ShaftStart), P(g.ShaftEnd));
        if (g.HeadLength <= 0)
        {
            return;
        }

        var head = new StreamGeometry();
        using (StreamGeometryContext ctx = head.Open())
        {
            ctx.BeginFigure(P(g.Tip), isFilled: true, isClosed: true);
            ctx.LineTo(P(g.Left), isStroked: true, isSmoothJoin: true);
            ctx.LineTo(P(g.Right), isStroked: true, isSmoothJoin: true);
        }

        head.Freeze();
        // A thin round-join outline in the same color softens the head's corners.
        dc.DrawGeometry(fill, StrokePen(a.Color, Math.Max(1, a.StrokeWidth / 3)), head);
    }

    private static void DrawText(DrawingContext dc, TextAnnotation t)
    {
        if (string.IsNullOrEmpty(t.Text))
        {
            return;
        }

        Brush fill = Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(t.Color)));
        FormattedText ft = Format(t.Text, t.FontSize, fill);
        Geometry glyphs = ft.BuildGeometry(P(t.Position));
        glyphs.Freeze();

        // A halo in the contrasting color keeps text legible on any background.
        var halo = new Pen(Frozen(new SolidColorBrush(ContrastFor(t.Color))), Math.Max(2, t.FontSize / 7))
        {
            LineJoin = PenLineJoin.Round,
        };
        halo.Freeze();
        dc.DrawGeometry(null, halo, glyphs);
        dc.DrawGeometry(fill, null, glyphs);
    }

    private static void DrawStep(DrawingContext dc, StepMarkerAnnotation s)
    {
        Brush fill = Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(s.Color)));
        Color contrast = ContrastFor(s.Color);
        var border = new Pen(Frozen(new SolidColorBrush(contrast)), Math.Max(2, s.Radius / 8));
        border.Freeze();
        dc.DrawEllipse(fill, border, P(s.Center), s.Radius, s.Radius);

        string label = s.Number.ToString(CultureInfo.InvariantCulture);
        double fontSize = s.Radius * (label.Length > 2 ? 0.8 : label.Length > 1 ? 1.0 : 1.2);
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, StepTypeface, fontSize, Frozen(new SolidColorBrush(contrast)), PixelsPerDip)
        {
            TextAlignment = TextAlignment.Left,
        };
        dc.DrawText(ft, new Point(s.Center.X - ft.Width / 2, s.Center.Y - ft.Height / 2));
    }

    private static FormattedText Format(string text, double fontSize, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextTypeface, fontSize, brush, PixelsPerDip);

    private static Pen StrokePen(ArgbColor color, double width, PenLineJoin join = PenLineJoin.Round)
    {
        var pen = new Pen(Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(color))), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = join,
        };
        pen.Freeze();
        return pen;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Point P(PointD p) => new(p.X, p.Y);

    private static Rect R(RectD r) => new(r.X, r.Y, r.Width, r.Height);
}
