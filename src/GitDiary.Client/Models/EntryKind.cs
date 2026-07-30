namespace GitDiary.Client.Models;

/// <summary>
/// Which top-level collection an entry belongs to. Diary entries are keyed by date
/// (one per day, path <c>Diary/YYYY/MM/DD.md</c>); documents are free-form,
/// title-keyed, sorted by creation time (path <c>Docs/{timestamp}-{title}.md</c>).
/// </summary>
public enum EntryKind
{
    Diary,
    Doc
}
