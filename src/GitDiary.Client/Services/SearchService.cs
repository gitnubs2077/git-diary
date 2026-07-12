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

        // `Contains(..., StringComparison.OrdinalIgnoreCase)` does its own culture-
        // insensitive case folding, so pre-lowercasing the keyword and the entire
        // content is dead work — the second copy of a large diary body is a real
        // GC pressure on repos with hundreds of entries.
        var results = new List<DiaryEntry>();

        foreach (var entry in _index.Values)
        {
            var title = PathHelper.GetTitle(entry.Date);

            if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                entry.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
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
