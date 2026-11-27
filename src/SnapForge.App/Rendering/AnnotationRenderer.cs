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
            case HighlightAnnotation h:
                dc.DrawRectangle(Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(h.Color with { A = (byte)Math.Round(255 * Math.Clamp(h.Opacity, 0.05, 1)) }))), null, R(h.Rect));
                break;
            case RedactionMaskAnnotation m:
                // Integer-aligned, unstroked, opaque: covers whole pixels exactly with no anti-aliased edge.
                dc.DrawRectangle(Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(m.Color with { A = 255 }))), null,
                    new Rect(m.Region.X, m.Region.Y, m.Region.Width, m.Region.Height));
                break;
            case FreehandAnnotation f:
                DrawFreehand(dc, f);
                break;
            case CalloutAnnotation c:
                DrawCallout(dc, c);
                break;
            case StampAnnotation st:
                DrawStamp(dc, st);
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

    private static void DrawFreehand(DrawingContext dc, FreehandAnnotation f)
    {
        if (f.Points.Count == 1)
        {
            dc.DrawEllipse(Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(f.Color))), null, P(f.Points[0]), f.StrokeWidth / 2, f.StrokeWidth / 2);
            return;
        }

        var g = new StreamGeometry();
        using (StreamGeometryContext ctx = g.Open())
        {
            ctx.BeginFigure(P(f.Points[0]), isFilled: false, isClosed: false);
            ctx.PolyLineTo([.. f.Points.Skip(1).Select(P)], isStroked: true, isSmoothJoin: true);
        }

        g.Freeze();
        dc.DrawGeometry(null, StrokePen(f.Color, f.StrokeWidth), g);
    }

    private static void DrawCallout(DrawingContext dc, CalloutAnnotation c)
    {
        Rect box = R(c.Box);
        double radius = Math.Min(10, Math.Min(box.Width, box.Height) / 4);
        var bubble = new RectangleGeometry(box, radius, radius);
        // Tail: a triangle from two points on the box edge facing the tail to the tail point.
        Point centre = P(c.Box.Center), tail = P(c.Tail);
        Vector dir = tail - centre;
        Geometry shape = bubble;
        if (!box.Contains(tail) && dir.Length > 1)
        {
            dir.Normalize();
            var normal = new Vector(-dir.Y, dir.X);
            double baseHalf = Math.Min(14, Math.Min(box.Width, box.Height) / 4);
            var tri = new StreamGeometry();
            using (StreamGeometryContext ctx = tri.Open())
            {
                ctx.BeginFigure(centre + (normal * baseHalf), isFilled: true, isClosed: true);
                ctx.LineTo(tail, isStroked: true, isSmoothJoin: false);
                ctx.LineTo(centre - (normal * baseHalf), isStroked: true, isSmoothJoin: false);
            }

            tri.Freeze();
            shape = new CombinedGeometry(GeometryCombineMode.Union, bubble, tri);
        }

        shape.Freeze();
        dc.DrawGeometry(Brushes.White, StrokePen(c.Color, c.StrokeWidth, PenLineJoin.Round), shape);
        if (!string.IsNullOrEmpty(c.Text))
        {
            FormattedText ft = Format(c.Text, c.FontSize, Frozen(new SolidColorBrush(Color.FromRgb(20, 20, 20))));
            ft.MaxTextWidth = Math.Max(10, box.Width - 16);
            ft.MaxTextHeight = Math.Max(10, box.Height - 8);
            ft.TextAlignment = TextAlignment.Center;
            dc.DrawText(ft, new Point(box.X + 8, box.Y + ((box.Height - ft.Height) / 2)));
        }
    }

    /// <summary>Original vector stamps (no third-party artwork).</summary>
    private static void DrawStamp(DrawingContext dc, StampAnnotation st)
    {
        double s = st.Size, x = st.Center.X - (s / 2), y = st.Center.Y - (s / 2);
        Brush fill = Frozen(new SolidColorBrush(BitmapConversion.ToMediaColor(st.Color)));
        Brush ink = Frozen(new SolidColorBrush(ContrastFor(st.Color)));
        var inkPen = new Pen(ink, Math.Max(2, s / 9)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        inkPen.Freeze();
        Point c = P(st.Center);
        switch (st.Kind)
        {
            case StampKind.Warning:
                var tri = new StreamGeometry();
                using (StreamGeometryContext ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(c.X, y + (s * 0.06)), isFilled: true, isClosed: true);
                    ctx.LineTo(new Point(x + (s * 0.97), y + (s * 0.92)), true, true);
                    ctx.LineTo(new Point(x + (s * 0.03), y + (s * 0.92)), true, true);
                }

                tri.Freeze();
                dc.DrawGeometry(fill, null, tri);
                dc.DrawLine(inkPen, new Point(c.X, y + (s * 0.35)), new Point(c.X, y + (s * 0.62)));
                dc.DrawEllipse(ink, null, new Point(c.X, y + (s * 0.77)), s / 18, s / 18);
                return;
            case StampKind.Star:
                var star = new StreamGeometry();
                using (StreamGeometryContext ctx = star.Open())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        double r = (i % 2 == 0 ? 0.5 : 0.21) * s, a = (-Math.PI / 2) + (i * Math.PI / 5);
                        var p = new Point(c.X + (r * Math.Cos(a)), c.Y + (r * Math.Sin(a)));
                        if (i == 0)
                        {
                            ctx.BeginFigure(p, isFilled: true, isClosed: true);
                        }
                        else
                        {
                            ctx.LineTo(p, true, true);
                        }
                    }
                }

                star.Freeze();
                dc.DrawGeometry(fill, null, star);
                return;
        }

        dc.DrawEllipse(fill, null, c, s / 2, s / 2);
        switch (st.Kind)
        {
            case StampKind.Check:
                var check = new StreamGeometry();
                using (StreamGeometryContext ctx = check.Open())
                {
                    ctx.BeginFigure(new Point(x + (s * 0.27), y + (s * 0.52)), false, false);
                    ctx.LineTo(new Point(x + (s * 0.44), y + (s * 0.69)), true, true);
                    ctx.LineTo(new Point(x + (s * 0.74), y + (s * 0.34)), true, true);
                }

                check.Freeze();
                dc.DrawGeometry(null, inkPen, check);
                break;
            case StampKind.Cross:
                dc.DrawLine(inkPen, new Point(x + (s * 0.32), y + (s * 0.32)), new Point(x + (s * 0.68), y + (s * 0.68)));
                dc.DrawLine(inkPen, new Point(x + (s * 0.68), y + (s * 0.32)), new Point(x + (s * 0.32), y + (s * 0.68)));
                break;
            default:
                string glyph = st.Kind == StampKind.Info ? "i" : "?";
                var ft = new FormattedText(glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, StepTypeface, s * 0.62, ink, PixelsPerDip);
                dc.DrawText(ft, new Point(c.X - (ft.Width / 2), c.Y - (ft.Height / 2)));
                break;
        }
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
