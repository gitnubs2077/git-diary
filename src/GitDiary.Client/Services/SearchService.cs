using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;

namespace GitDiary.Client.Services;

public sealed class SearchService
{
    private readonly Dictionary<string, DiaryEntry> _index = new();

    public void BuildIndex(List<DiaryEntry> entries)
    {
        _index.Clear();
        foreach (var entry in entries)
        {
            _index[entry.Path] = entry;
        }
    }

    public List<DiaryEntry> Search(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<DiaryEntry>();

        var keywordLower = keyword.ToLowerInvariant();
        var results = new List<DiaryEntry>();

        foreach (var entry in _index.Values)
        {
            var title = PathHelper.GetTitle(entry.Date);
            var content = entry.Content.ToLowerInvariant();

            if (title.Contains(keywordLower, StringComparison.OrdinalIgnoreCase) ||
                content.Contains(keywordLower, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(entry);
            }
        }

        return results.OrderByDescending(e => e.Date).ToList();
    }

    public void Clear()
    {
        _index.Clear();
    }
}
