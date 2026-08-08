namespace GitDiary.Client.Models;

/// <summary>
/// A single editable entry — either a dated diary page or a free-form document.
/// <see cref="Path"/> is the real identity for both; <see cref="Date"/> is derived
/// metadata (the diary's day, or a document's creation day).
/// </summary>
public sealed class DiaryEntry
{
    public EntryKind Kind { get; set; } = EntryKind.Diary;

    public DateOnly Date { get; set; }

    public string Path { get; set; } = "";

    public string Content { get; set; } = "";

    public string Sha { get; set; } = "";

    public DateTimeOffset LastModified { get; set; }

    public SyncState SyncState { get; set; }

    /// <summary>Display title. Diary: the date string; document: its title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Creation timestamp — the sort key for documents (newest first).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True for an uploaded PDF document: binary, read-only (viewed, not edited).</summary>
    public bool IsPdf => Infrastructure.DocPaths.IsPdfPath(Path);
}
