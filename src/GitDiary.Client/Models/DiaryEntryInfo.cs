namespace GitDiary.Client.Models;

/// <summary>Lightweight listing row for the sidebar — a diary day or a document.</summary>
public sealed class DiaryEntryInfo
{
    public EntryKind Kind { get; set; } = EntryKind.Diary;

    public DateOnly Date { get; set; }

    public string Path { get; set; } = "";

    public string Sha { get; set; } = "";

    public bool Exists { get; set; }

    /// <summary>Document title (empty for diary rows, which display their Date).</summary>
    public string Title { get; set; } = "";

    /// <summary>Creation timestamp — documents sort by this, newest first.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
