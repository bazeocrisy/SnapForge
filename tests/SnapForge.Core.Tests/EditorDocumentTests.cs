using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.Core.Tests;

public class EditorDocumentTests
{
    private static EditorDocument NewDoc() => new(PixelBufferTests.Gradient(50, 40));

    private static LineAnnotation Line(double x) => new(new PointD(x, 0), new PointD(x, 30));

    [Fact]
    public void New_document_is_clean_with_empty_history()
    {
        EditorDocument d = NewDoc();
        Assert.False(d.IsDirty);
        Assert.False(d.History.CanUndo);
        Assert.False(d.History.CanRedo);
        Assert.False(d.Undo());
        Assert.False(d.Redo());
    }

    [Fact]
    public void Add_undo_redo_round_trip()
    {
        EditorDocument d = NewDoc();
        LineAnnotation a = Line(5);
        d.Execute(new AddAnnotationCommand(a));
        Assert.Single(d.Annotations);
        Assert.True(d.IsDirty);
        Assert.Equal("Add line", d.History.UndoDescription);

        Assert.True(d.Undo());
        Assert.Empty(d.Annotations);
        Assert.False(d.IsDirty);
        Assert.Equal("Add line", d.History.RedoDescription);

        Assert.True(d.Redo());
        Assert.Same(a, Assert.Single(d.Annotations));
    }

    [Fact]
    public void New_command_clears_redo()
    {
        EditorDocument d = NewDoc();
        d.Execute(new AddAnnotationCommand(Line(1)));
        d.Undo();
        d.Execute(new AddAnnotationCommand(Line(2)));
        Assert.False(d.History.CanRedo);
    }

    [Fact]
    public void Remove_restores_original_z_order_on_undo()
    {
        EditorDocument d = NewDoc();
        LineAnnotation a = Line(1), b = Line(2), c = Line(3);
        d.Execute(new AddAnnotationCommand(a));
        d.Execute(new AddAnnotationCommand(b));
        d.Execute(new AddAnnotationCommand(c));
        d.Execute(new RemoveAnnotationCommand(b));
        Assert.Equal([a, c], d.Annotations);
        d.Undo();
        Assert.Equal([a, b, c], d.Annotations);
    }

    [Fact]
    public void Replace_moves_and_undo_restores()
    {
        EditorDocument d = NewDoc();
        LineAnnotation a = Line(1);
        d.Execute(new AddAnnotationCommand(a));
        Annotation moved = a.Translate(10, 0);
        d.Execute(new ReplaceAnnotationCommand(a, moved, "Move line"));
        Assert.Equal(moved, d.FindById(a.Id));
        d.Undo();
        Assert.Equal(a, d.FindById(a.Id));
    }

    [Fact]
    public void Replace_requires_same_id()
    {
        Assert.Throws<ArgumentException>(() => new ReplaceAnnotationCommand(Line(1), Line(2), "x"));
    }

    [Fact]
    public void Redaction_permanently_changes_pixels_and_undo_restores_them_in_session()
    {
        EditorDocument d = NewDoc();
        byte[] original = (byte[])d.Image.Pixels.Clone();
        bool imageChanged = false;
        d.Changed += (_, e) => imageChanged = e.ImageChanged;

        d.Execute(new RedactCommand(new PixelRect(10, 10, 20, 5), ArgbColor.Black));
        Assert.True(imageChanged);
        for (int y = 10; y < 15; y++)
        {
            for (int x = 10; x < 30; x++)
            {
                Assert.Equal(ArgbColor.Black, d.Image.GetPixel(x, y));
            }
        }

        Assert.Equal(d.Image.GetPixel(9, 9), new PixelBuffer(50, 40, original).GetPixel(9, 9));

        d.Undo();
        Assert.Equal(original, d.Image.Pixels);
        d.Redo();
        Assert.Equal(ArgbColor.Black, d.Image.GetPixel(10, 10));
    }

    [Fact]
    public void Overlapping_redactions_undo_in_order()
    {
        EditorDocument d = NewDoc();
        byte[] original = (byte[])d.Image.Pixels.Clone();
        d.Execute(new RedactCommand(new PixelRect(0, 0, 20, 20), ArgbColor.Black));
        d.Execute(new RedactCommand(new PixelRect(10, 10, 20, 20), ArgbColor.White));
        Assert.Equal(ArgbColor.White, d.Image.GetPixel(15, 15));
        d.Undo();
        Assert.Equal(ArgbColor.Black, d.Image.GetPixel(15, 15));
        d.Undo();
        Assert.Equal(original, d.Image.Pixels);
    }

    [Fact]
    public void Redaction_fill_is_forced_opaque()
    {
        var cmd = new RedactCommand(new PixelRect(0, 0, 2, 2), new ArgbColor(0, 9, 9, 9));
        Assert.Equal(255, cmd.Fill.A);
    }

    [Fact]
    public void Redaction_outside_image_is_rejected()
    {
        EditorDocument d = NewDoc();
        Assert.Throws<InvalidOperationException>(() => d.Execute(new RedactCommand(new PixelRect(100, 100, 5, 5), ArgbColor.Black)));
        Assert.False(d.History.CanUndo);
        Assert.Throws<ArgumentException>(() => new RedactCommand(PixelRect.Empty, ArgbColor.Black));
    }

    [Fact]
    public void Dirty_tracking_follows_saved_state()
    {
        EditorDocument d = NewDoc();
        d.Execute(new AddAnnotationCommand(Line(1)));
        d.MarkSaved();
        Assert.False(d.IsDirty);
        d.Execute(new AddAnnotationCommand(Line(2)));
        Assert.True(d.IsDirty);
        d.Undo();
        Assert.False(d.IsDirty);
        d.Undo();
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void Undo_then_different_edit_is_dirty_even_with_same_count()
    {
        EditorDocument d = NewDoc();
        d.Execute(new AddAnnotationCommand(Line(1)));
        d.MarkSaved();
        d.Undo();
        d.Execute(new AddAnnotationCommand(Line(2)));
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void History_capacity_drops_oldest_steps()
    {
        var d = new EditorDocument(new PixelBuffer(10, 10), historyCapacity: 3);
        for (int i = 0; i < 5; i++)
        {
            d.Execute(new AddAnnotationCommand(Line(i)));
        }

        Assert.Equal(3, d.History.UndoCount);
        while (d.Undo())
        {
        }

        Assert.Equal(2, d.Annotations.Count);
    }

    [Fact]
    public void Hit_test_returns_topmost()
    {
        EditorDocument d = NewDoc();
        LineAnnotation bottom = Line(10), top = Line(10);
        d.Execute(new AddAnnotationCommand(bottom));
        d.Execute(new AddAnnotationCommand(top));
        Assert.Same(top, d.HitTest(new PointD(10, 15), 2));
        Assert.Null(d.HitTest(new PointD(40, 15), 2));
    }
}
