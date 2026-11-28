using SnapForge.Core.Editing;

namespace SnapForge.App.Services;

/// <summary>One capture open in the Editor (library-backed, or "without history" when not saved to the Library).</summary>
internal sealed class EditorSession
{
    public EditorSession(EditorDocument document, DateTime capturedAt, string title, Guid? captureId)
    {
        Document = document;
        CapturedAt = capturedAt;
        Title = title;
        CaptureId = captureId;
        Key = captureId ?? Guid.NewGuid();
    }

    /// <summary>Stable identity for the tray (equals <see cref="CaptureId"/> once in the library).</summary>
    public Guid Key { get; set; }

    public Guid? CaptureId { get; set; }

    public EditorDocument Document { get; }

    public DateTime CapturedAt { get; }

    public string Title { get; set; }

    public bool InLibrary => CaptureId is not null;

    /// <summary>Pending creation of the library entry (new captures are written in the background).</summary>
    public Task? Creation { get; set; }

    /// <summary>Incremented whenever the base pixels change (crop, sanitize) so the next save rewrites them.</summary>
    public int BaseVersion { get; set; }

    public int SavedBaseVersion { get; set; }

    public string? LastSaveError { get; set; }

    public bool IsSaving { get; set; }
}
