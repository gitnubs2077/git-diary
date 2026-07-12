namespace GitDiary.Client.Models;

/// <summary>
/// Payload returned by GitHub's <c>GET contents/{path}</c> endpoint: the file's
/// blob SHA plus its UTF-8 decoded content. Split into its own type so callers
/// don't have to unpack a fragile "sha|content" delimiter — a stray '|' in a
/// hex SHA (impossible today but not compiler-enforced) or in future non-hex
/// identifiers would silently mangle the content otherwise.
/// </summary>
public sealed record FileContent(string Sha, string Content);
