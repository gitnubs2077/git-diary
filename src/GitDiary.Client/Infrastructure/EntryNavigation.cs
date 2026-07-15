namespace GitDiary.Client.Infrastructure;

/// <summary>
/// Keyboard day-navigation (Arrow Up/Down) jumps between days that actually HAVE an
/// entry, skipping empty calendar days. This is the pure target-picking logic, split
/// out from Home so it can be unit-tested without a browser or a live repo.
/// </summary>
public static class EntryNavigation
{
    /// <summary>
    /// Returns the nearest existing entry date relative to <paramref name="current"/>
    /// in the given direction, or null if there is none.
    /// </summary>
    /// <param name="existing">Dates that have an entry (any order, may include duplicates).</param>
    /// <param name="current">The date currently open.</param>
    /// <param name="direction">+1 = newer (the smallest existing date strictly after
    /// current); -1 = older (the largest existing date strictly before current).</param>
    public static DateOnly? FindAdjacent(IEnumerable<DateOnly> existing, DateOnly current, int direction)
    {
        DateOnly? best = null;
        foreach (var date in existing)
        {
            if (direction < 0)
            {
                // Older: want the largest date that is still strictly before `current`.
                if (date < current && (best is null || date > best.Value))
                    best = date;
            }
            else
            {
                // Newer: want the smallest date that is still strictly after `current`.
                if (date > current && (best is null || date < best.Value))
                    best = date;
            }
        }
        return best;
    }
}
