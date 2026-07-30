using System.Globalization;
using GitDiary.Client.Models;

namespace GitDiary.Client.Infrastructure;

public static class PathHelper
{
    // Public so image paths (see ImagePaths) anchor to the same root as entries
    // instead of hardcoding the literal a second time.
    public const string BaseDirectory = "Diary";

    public static string GetPath(DateOnly date)
    {
        return $"{BaseDirectory}/{date.Year:D4}/{date.Month:D2}/{date.Day:D2}.md";
    }

    public static string GetDirectoryPath(int year, int month)
    {
        return $"{BaseDirectory}/{year:D4}/{month:D2}";
    }

    public static string GetYearPath(int year)
    {
        return $"{BaseDirectory}/{year:D4}";
    }

    public static DateOnly? ParsePath(string path)
    {
        // Expected format: Diary/YYYY/MM/DD.md
        var parts = path.Split('/');
        if (parts.Length != 4)
            return null;

        var fileName = parts[3];
        if (!fileName.EndsWith(".md"))
            return null;

        var dayStr = fileName[..^3];

        // Compose canonical YYYY-MM-DD and delegate to DateOnly's own strict
        // parser. This rejects impossible combinations (e.g. 2025/02/30, month 13,
        // year 0) *without throwing* — the previous `new DateOnly(y, m, d)` threw
        // ArgumentOutOfRangeException for a single malformed path in the Git tree
        // and took down the entire entries load.
        var canonical = $"{parts[1]}-{parts[2]}-{dayStr}";
        return DateOnly.TryParseExact(
            canonical,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    public static bool IsDiaryFile(string path)
    {
        return path.StartsWith(BaseDirectory + "/") && path.EndsWith(".md");
    }

    /// <summary>
    /// Returns "today" as observed by the user's device — i.e. the local wall-clock
    /// date at call time. This is the canonical anchor for the diary path
    /// (<see cref="GetPath"/>) and the sidebar's "Today" highlight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GitDiary intentionally does <b>not</b> normalize to UTC or to a stored
    /// preference. Diaries are personal and location-bound: 22:00 in Tokyo and
    /// 22:00 in Berlin should each land in their own day, and a user crossing
    /// midnight while typing will simply keep writing in "yesterday's" file
    /// until they explicitly pick a new date.
    /// </para>
    /// <para>
    /// Consequence: users who cross time zones may see the same wall-clock
    /// moment map to different filenames on different devices. This is a
    /// documented, deliberate trade-off — see <c>docs/tech-design.md</c>.
    /// </para>
    /// </remarks>
    public static DateOnly GetToday() =>
        DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime);

    public static string GetTitle(DateOnly date) => date.ToString("yyyy-MM-dd");

    /// <summary>
    /// Builds the browser URL for the repository root on github.com. Returns
    /// <c>null</c> when the config is missing owner/repo — the caller decides
    /// whether to hide the affordance or show a disabled state.
    /// </summary>
    public static string? GetGitHubRepoUrl(RepositoryConfig? config)
    {
        if (config is null) return null;
        if (string.IsNullOrEmpty(config.Owner) || string.IsNullOrEmpty(config.Repo))
            return null;

        // Owner/repo pass the SetupWizard regex (letters/digits/./_/-), so no
        // percent-encoding is required for realistic inputs. Branch may contain
        // slashes (e.g. `release/1.0`) which GitHub accepts verbatim in the URL.
        return $"https://github.com/{config.Owner}/{config.Repo}";
    }

    /// <summary>
    /// Builds the browser URL for a specific diary file on github.com. Uses
    /// <c>/blob/{branch}/{path}</c> so the user lands on the rendered file
    /// view. Returns <c>null</c> when required config is missing.
    /// </summary>
    public static string? GetGitHubFileUrl(RepositoryConfig? config, DateOnly date)
        => GetGitHubFileUrlForPath(config, GetPath(date));

    /// <summary>
    /// Builds the browser URL for an arbitrary repo file path on github.com
    /// (diary or document). Returns <c>null</c> when required config is missing.
    /// </summary>
    public static string? GetGitHubFileUrlForPath(RepositoryConfig? config, string path)
    {
        var repoUrl = GetGitHubRepoUrl(config);
        if (repoUrl is null || string.IsNullOrEmpty(path)) return null;
        var branch = string.IsNullOrEmpty(config!.Branch) ? "main" : config.Branch;
        return $"{repoUrl}/blob/{branch}/{path}";
    }
}
