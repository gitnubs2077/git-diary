namespace GitDiary.Client.Models;

public sealed class Draft
{
    public string Path { get; set; } = "";

    public string Content { get; set; } = "";

    public string Sha { get; set; } = "";

    public SyncState State { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
