using System.Globalization;

namespace GitDiary.Client.Infrastructure;

public static class PathHelper
{
    private const string BaseDirectory = "Diary";

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
}
