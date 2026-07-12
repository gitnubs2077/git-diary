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
        if (int.TryParse(parts[1], out var year) &&
            int.TryParse(parts[2], out var month) &&
            int.TryParse(dayStr, out var day))
        {
            return new DateOnly(year, month, day);
        }

        return null;
    }

    public static bool IsDiaryFile(string path)
    {
        return path.StartsWith(BaseDirectory + "/") && path.EndsWith(".md");
    }

    public static DateOnly GetToday() => DateOnly.FromDateTime(DateTime.Now);

    public static string GetTitle(DateOnly date) => date.ToString("yyyy-MM-dd");
}
