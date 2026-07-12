namespace GitDiary.Client.Models;

public sealed class DiaryEntryInfo
{
    public DateOnly Date { get; set; }

    public string Path { get; set; } = "";

    public string Sha { get; set; } = "";

    public bool Exists { get; set; }
}
