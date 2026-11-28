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

    /// <summary>Freehand pen.</summary>
    Freehand,

    /// <summary>Speech-bubble callout: drag from the point of interest to where the bubble goes.</summary>
    Callout,

    /// <summary>Click to place a stamp (check, cross, warning, info, question, star).</summary>
    Stamp,
}
