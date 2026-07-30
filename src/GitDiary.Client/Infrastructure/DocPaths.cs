using System.Globalization;
using System.Text.RegularExpressions;

namespace GitDiary.Client.Infrastructure;

/// <summary>
/// Pure path/title logic for documents — the free-form counterpart to dated diary
/// entries (see <see cref="PathHelper"/>). Split out so it can be unit-tested without
/// a browser or a live repo.
/// </summary>
/// <remarks>
/// Layout: a document lives at <c>Docs/{yyyyMMdd-HHmmss}-{title}.md</c>. The
/// creation-timestamp prefix makes a reverse filename sort equal newest-first order
/// (no extra API calls) and keeps names unique; the title rides in the filename so the
/// sidebar can show it straight from the git tree without fetching each file's body,
/// and it stays readable when browsing the repo on github.com. Its own images live in
/// a sibling <c>Docs/assets/</c> folder, referenced relatively as <c>assets/…</c> —
/// the same convention diary entries use, so <see cref="ImagePaths"/> handles both.
/// </remarks>
public static class DocPaths
{
    public const string BaseDirectory = "Docs";
    private const int MaxTitleLength = 80;
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    // Docs/20260725-143005-<title>.md — a SINGLE segment after Docs/, so this never
    // matches Docs/assets/<image> (which has an extra '/' and no timestamp prefix).
    private static readonly Regex DocPattern = new(
        $@"^{Regex.Escape(BaseDirectory)}/(\d{{8}}-\d{{6}})-([^/]*)\.md$",
        RegexOptions.Compiled);

    // Characters that are illegal in a path segment across git / URLs / filesystems,
    // plus control characters. Unicode letters (Chinese, emoji, …) are intentionally
    // kept — GitHub stores Unicode filenames fine and the API path is URL-encoded.
    private static readonly Regex IllegalChars =
        new("[/\\\\:*?\"<>|\\x00-\\x1f]", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Repo path for a new document created at <paramref name="createdAt"/>.</summary>
    public static string BuildPath(DateTimeOffset createdAt, string? title) =>
        $"{BaseDirectory}/{createdAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{SanitizeTitle(title)}.md";

    /// <summary>
    /// Filename-safe form of a title: illegal characters removed, whitespace collapsed,
    /// trimmed, length-capped; Unicode letters preserved. Empty input → "untitled".
    /// </summary>
    public static string SanitizeTitle(string? title)
    {
        var t = IllegalChars.Replace(title ?? "", "");
        t = Whitespace.Replace(t, " ").Trim().Trim('.').Trim();
        if (t.Length > MaxTitleLength) t = t[..MaxTitleLength].Trim();
        return t.Length == 0 ? "untitled" : t;
    }

    /// <summary>True for a document file (excludes Docs/assets images and subfolders).</summary>
    public static bool IsDocPath(string? path) =>
        !string.IsNullOrEmpty(path) && DocPattern.IsMatch(path);

    /// <summary>The creation timestamp encoded in the filename, or null if not a doc path.</summary>
    public static DateTimeOffset? ParseCreatedAt(string path)
    {
        var m = DocPattern.Match(path);
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups[1].Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    /// <summary>The document title stored in the filename, or "" if not a doc path.</summary>
    public static string ParseTitle(string path)
    {
        var m = DocPattern.Match(path);
        return m.Success ? m.Groups[2].Value : "";
    }

    /// <summary>
    /// The path for a renamed document: keep the original created-timestamp prefix
    /// (so the sort position doesn't jump), swap in the new sanitized title. Returns
    /// null if <paramref name="oldPath"/> isn't a document path.
    /// </summary>
    public static string? RenamePath(string oldPath, string newTitle)
    {
        var m = DocPattern.Match(oldPath);
        return m.Success
            ? $"{BaseDirectory}/{m.Groups[1].Value}-{SanitizeTitle(newTitle)}.md"
            : null;
    }
}
