namespace SnapForge.Core.Annotations;

public enum AnnotationTool
{
    Select,
    Arrow,
    Line,
    Rectangle,
    Highlight,
    Ellipse,
    Text,
    StepMarker,
    Redact,
    Crop,

    /// <summary>Blurs a region of the image (not redaction: shapes and large text may stay recognisable).</summary>
    Blur,

    /// <summary>Pixelates a region of the image (not redaction).</summary>
    Pixelate,
}
