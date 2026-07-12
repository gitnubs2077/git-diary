namespace GitDiary.Client.Models;

public sealed class TreeNode
{
    public string Path { get; set; } = "";

    public string Mode { get; set; } = "";

    public string Type { get; set; } = "";

    public string Sha { get; set; } = "";

    public int Size { get; set; }
}
