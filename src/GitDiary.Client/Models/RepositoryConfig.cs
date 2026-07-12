namespace GitDiary.Client.Models;

public sealed class RepositoryConfig
{
    public string Owner { get; set; } = "";

    public string Repo { get; set; } = "";

    public string Branch { get; set; } = "main";

    public string Token { get; set; } = "";
}
