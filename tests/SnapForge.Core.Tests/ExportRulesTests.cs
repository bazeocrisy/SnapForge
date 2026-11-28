using SnapForge.Core.Export;

namespace SnapForge.Core.Tests;

public class ExportRulesTests
{
    [Theory]
    [InlineData("a.png", ImageFileFormat.Png)]
    [InlineData("a.PNG", ImageFileFormat.Png)]
    [InlineData("a.jpg", ImageFileFormat.Jpeg)]
    [InlineData("a.JPEG", ImageFileFormat.Jpeg)]
    [InlineData("a.jpe", ImageFileFormat.Jpeg)]
    [InlineData("a.bmp", ImageFileFormat.Bmp)]
    [InlineData("a.TIFF", ImageFileFormat.Tiff)]
    [InlineData("a.tif", ImageFileFormat.Tiff)]
    public void Format_from_extension(string path, ImageFileFormat expected) =>
        Assert.Equal(expected, ExportRules.FormatFromPath(path));

    [Theory]
    [InlineData("a.gif")]
    [InlineData("a")]
    [InlineData("a.png.txt")]
    public void Unsupported_extension_is_null(string path) => Assert.Null(ExportRules.FormatFromPath(path));

    [Fact]
    public void Explicit_extension_wins_over_filter()
    {
        Assert.Equal(("x.jpg", ImageFileFormat.Jpeg), ExportRules.ResolveSaveTarget("x.jpg", 1));
        Assert.Equal(("x.png", ImageFileFormat.Png), ExportRules.ResolveSaveTarget("x.png", 2));
    }

    [Fact]
    public void Missing_extension_uses_filter()
    {
        Assert.Equal(("x.jpg", ImageFileFormat.Jpeg), ExportRules.ResolveSaveTarget("x", 2));
        Assert.Equal(("x.png", ImageFileFormat.Png), ExportRules.ResolveSaveTarget("x", 1));
        Assert.Equal(("x.bmp", ImageFileFormat.Bmp), ExportRules.ResolveSaveTarget("x", 3));
        Assert.Equal(("x.tif", ImageFileFormat.Tiff), ExportRules.ResolveSaveTarget("x", 4));
        Assert.Equal(("x.v2.png", ImageFileFormat.Png), ExportRules.ResolveSaveTarget("x.v2", 1));
    }

    [Fact]
    public void Default_file_name_is_sortable_and_safe()
    {
        string name = ExportRules.DefaultFileName(new DateTime(2026, 11, 5, 9, 4, 3), ImageFileFormat.Png);
        Assert.Equal("SnapForge 2026-11-05 090403.png", name);
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToArray()));
    }
}
