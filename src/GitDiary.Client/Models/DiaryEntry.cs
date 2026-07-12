namespace GitDiary.Client.Models;

public sealed class DiaryEntry
{
    public DateOnly Date { get; set; }

    public string Path { get; set; } = "";

    public string Content { get; set; } = "";

    public string Sha { get; set; } = "";

    public DateTimeOffset LastModified { get; set; }

    public SyncState SyncState { get; set; }
}
