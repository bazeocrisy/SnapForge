using System.Globalization;

namespace SnapForge.Core.Export;

public enum ImageFileFormat
{
    Png,
    Jpeg,
}

public static class ExportRules
{
    /// <summary>JPEG quality (0–100). High enough that UI text stays crisp.</summary>
    public const int JpegQuality = 92;

    /// <summary>Filter string for Win32 save dialogs; index 1 = PNG, 2 = JPEG.</summary>
    public const string SaveDialogFilter = "PNG image (*.png)|*.png|JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg";

    public static string Extension(ImageFileFormat format) => format switch
    {
        ImageFileFormat.Png => ".png",
        ImageFileFormat.Jpeg => ".jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Format implied by a path's extension; null if unsupported.</summary>
    public static ImageFileFormat? FormatFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToUpperInvariant() switch
        {
            ".PNG" => ImageFileFormat.Png,
            ".JPG" or ".JPEG" or ".JPE" => ImageFileFormat.Jpeg,
            _ => null,
        };
    }

    /// <summary>
    /// Final format and path for a save-dialog result: an explicit, supported extension wins;
    /// otherwise the chosen filter decides and its extension is appended.
    /// </summary>
    public static (string Path, ImageFileFormat Format) ResolveSaveTarget(string chosenPath, int filterIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chosenPath);
        ImageFileFormat? fromExtension = FormatFromPath(chosenPath);
        if (fromExtension is { } format)
        {
            return (chosenPath, format);
        }

        ImageFileFormat fromFilter = filterIndex == 2 ? ImageFileFormat.Jpeg : ImageFileFormat.Png;
        return (chosenPath + Extension(fromFilter), fromFilter);
    }

    /// <summary>Default file name such as "SnapForge 2026-11-25 134905.png" (sortable, no characters Windows forbids).</summary>
    public static string DefaultFileName(DateTime timestamp, ImageFileFormat format) =>
        string.Create(CultureInfo.InvariantCulture, $"{ProductInfo.FilePrefix} {timestamp:yyyy-MM-dd HHmmss}{Extension(format)}");
}
