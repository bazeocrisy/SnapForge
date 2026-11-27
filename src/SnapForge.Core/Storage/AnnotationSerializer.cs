using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Storage;

/// <summary>
/// Explicit, documented JSON schema for annotations (see docs/CAPTURE-FORMAT.md). Hand-written so the stored format
/// does not change when C# types are refactored. Unknown annotation types are rejected rather than silently dropped.
/// </summary>
public static class AnnotationSerializer
{
    public static JsonArray ToJson(IEnumerable<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        var array = new JsonArray();
        foreach (Annotation a in annotations)
        {
            var o = new JsonObject
            {
                ["id"] = a.Id.ToString("D"),
                ["color"] = a.Color.ToString(),
                ["strokeWidth"] = a.StrokeWidth,
            };
            switch (a)
            {
                case ArrowAnnotation x:
                    o["type"] = "arrow";
                    o["start"] = Point(x.Start);
                    o["end"] = Point(x.End);
                    break;
                case LineAnnotation x:
                    o["type"] = "line";
                    o["start"] = Point(x.Start);
                    o["end"] = Point(x.End);
                    break;
                case RectangleAnnotation x:
                    o["type"] = "rectangle";
                    o["rect"] = Rect(x.Rect);
                    break;
                case EllipseAnnotation x:
                    o["type"] = "ellipse";
                    o["rect"] = Rect(x.Rect);
                    break;
                case HighlightAnnotation x:
                    o["type"] = "highlight";
                    o["rect"] = Rect(x.Rect);
                    o["opacity"] = x.Opacity;
                    break;
                case TextAnnotation x:
                    o["type"] = "text";
                    o["position"] = Point(x.Position);
                    o["text"] = x.Text;
                    o["fontSize"] = x.FontSize;
                    o["measuredSize"] = Point(x.MeasuredSize);
                    break;
                case StepMarkerAnnotation x:
                    o["type"] = "step";
                    o["center"] = Point(x.Center);
                    o["number"] = x.Number;
                    o["radius"] = x.Radius;
                    break;
                case FreehandAnnotation x:
                    o["type"] = "freehand";
                    o["points"] = new JsonArray([.. x.Points.SelectMany(p => new JsonNode?[] { p.X, p.Y })]);
                    break;
                case CalloutAnnotation x:
                    o["type"] = "callout";
                    o["box"] = Rect(x.Box);
                    o["tail"] = Point(x.Tail);
                    o["text"] = x.Text;
                    o["fontSize"] = x.FontSize;
                    break;
                case StampAnnotation x:
                    o["type"] = "stamp";
                    o["center"] = Point(x.Center);
                    o["kind"] = x.Kind.ToString().ToLowerInvariant();
                    o["size"] = x.Size;
                    break;
                case RedactionMaskAnnotation x:
                    o["type"] = "redaction";
                    o["region"] = new JsonArray(x.Region.X, x.Region.Y, x.Region.Width, x.Region.Height);
                    break;
                default:
                    throw new NotSupportedException($"Cannot serialize {a.GetType().Name}.");
            }

            array.Add(o);
        }

        return array;
    }

    public static List<Annotation> FromJson(JsonArray array)
    {
        ArgumentNullException.ThrowIfNull(array);
        var result = new List<Annotation>(array.Count);
        foreach (JsonNode? node in array)
        {
            JsonObject o = node as JsonObject ?? throw new InvalidDataException("Annotation entry is not an object.");
            string type = Str(o, "type");
            Annotation a = type switch
            {
                "arrow" => new ArrowAnnotation(Point(o, "start"), Point(o, "end")),
                "line" => new LineAnnotation(Point(o, "start"), Point(o, "end")),
                "rectangle" => new RectangleAnnotation(Rect(o, "rect")),
                "ellipse" => new EllipseAnnotation(Rect(o, "rect")),
                "highlight" => new HighlightAnnotation(Rect(o, "rect")) { Opacity = Num(o, "opacity") },
                "text" => new TextAnnotation(Point(o, "position"), Str(o, "text"), Num(o, "fontSize")) { MeasuredSize = Point(o, "measuredSize") },
                "step" => new StepMarkerAnnotation(Point(o, "center"), (int)Num(o, "number"), Num(o, "radius")),
                "redaction" => new RedactionMaskAnnotation(Region(o)),
                "freehand" => new FreehandAnnotation(FreehandPoints(o)),
                "callout" => new CalloutAnnotation(Rect(o, "box"), Point(o, "tail"), Str(o, "text"), Num(o, "fontSize")),
                "stamp" => new StampAnnotation(Point(o, "center"), Enum.TryParse(Str(o, "kind"), ignoreCase: true, out StampKind k) && Enum.IsDefined(k)
                    ? k : throw new InvalidDataException("Unknown stamp kind."), Num(o, "size")),
                _ => throw new InvalidDataException($"Unknown annotation type '{type}'."),
            };
            a = a with
            {
                Id = Guid.Parse(Str(o, "id"), CultureInfo.InvariantCulture),
                Color = ArgbColor.Parse(Str(o, "color")),
                StrokeWidth = Num(o, "strokeWidth"),
            };
            if (a is RedactionMaskAnnotation m)
            {
                a = m with { Color = m.Color with { A = 255 } };
            }

            result.Add(a);
        }

        return result;
    }

    private static JsonArray Point(PointD p) => new(p.X, p.Y);

    private static JsonArray Rect(RectD r) => new(r.X, r.Y, r.Width, r.Height);

    private static string Str(JsonObject o, string name) =>
        o[name]?.GetValue<string>() ?? throw new InvalidDataException($"Missing '{name}'.");

    private static double Num(JsonObject o, string name)
    {
        double v = o[name]?.GetValue<double>() ?? throw new InvalidDataException($"Missing '{name}'.");
        return double.IsFinite(v) ? v : throw new InvalidDataException($"'{name}' is not finite.");
    }

    private static double[] Numbers(JsonObject o, string name, int count)
    {
        JsonArray arr = o[name] as JsonArray ?? throw new InvalidDataException($"Missing '{name}'.");
        if (arr.Count != count)
        {
            throw new InvalidDataException($"'{name}' must have {count} numbers.");
        }

        double[] values = arr.Select(n => n?.GetValue<double>() ?? throw new InvalidDataException($"Null in '{name}'.")).ToArray();
        return values.All(double.IsFinite) ? values : throw new InvalidDataException($"'{name}' is not finite.");
    }

    private static PointD Point(JsonObject o, string name)
    {
        double[] v = Numbers(o, name, 2);
        return new PointD(v[0], v[1]);
    }

    private static RectD Rect(JsonObject o, string name)
    {
        double[] v = Numbers(o, name, 4);
        return new RectD(v[0], v[1], v[2], v[3]);
    }

    private static List<PointD> FreehandPoints(JsonObject o)
    {
        JsonArray arr = o["points"] as JsonArray ?? throw new InvalidDataException("Missing 'points'.");
        if (arr.Count < 2 || arr.Count % 2 != 0 || arr.Count > FreehandAnnotation.MaxPoints * 2)
        {
            throw new InvalidDataException("'points' must hold 1 to 4000 x,y pairs.");
        }

        var points = new List<PointD>(arr.Count / 2);
        for (int i = 0; i < arr.Count; i += 2)
        {
            double x = arr[i]?.GetValue<double>() ?? throw new InvalidDataException("Null in 'points'.");
            double y = arr[i + 1]?.GetValue<double>() ?? throw new InvalidDataException("Null in 'points'.");
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new InvalidDataException("'points' is not finite.");
            }

            points.Add(new PointD(x, y));
        }

        return points;
    }

    private static PixelRect Region(JsonObject o)
    {
        double[] v = Numbers(o, "region", 4);
        return new PixelRect((int)v[0], (int)v[1], (int)v[2], (int)v[3]);
    }

    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
