namespace SnapForge.Core.Imaging;

/// <summary>A lossless whole-image transform (a permutation of the pixels).</summary>
public enum ImageTransform
{
    /// <summary>Rotate a quarter turn clockwise (width and height swap).</summary>
    Rotate90,

    /// <summary>Rotate a half turn.</summary>
    Rotate180,

    /// <summary>Rotate three quarter turns clockwise, i.e. a quarter turn counter-clockwise (width and height swap).</summary>
    Rotate270,

    /// <summary>Mirror left to right.</summary>
    FlipHorizontal,

    /// <summary>Mirror top to bottom.</summary>
    FlipVertical,
}
