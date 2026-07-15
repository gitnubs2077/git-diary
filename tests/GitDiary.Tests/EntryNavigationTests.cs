using GitDiary.Client.Infrastructure;
using Xunit;

namespace GitDiary.Tests;

/// <summary>
/// Arrow-key day navigation must jump to the nearest day that HAS an entry, skipping
/// empty calendar days. These cases pin that behavior — the reason the logic was
/// extracted from Home in the first place, since it can't be exercised in the browser
/// without a live repo full of entries.
/// </summary>
public class EntryNavigationTests
{
    private static DateOnly D(int day) => new(2026, 7, day);

    private static readonly DateOnly[] Existing = { D(2), D(5), D(10), D(20) };

    [Fact]
    public void Older_SkipsEmptyDays_ToPreviousExistingEntry()
    {
        // From the 10th, going older lands on the 5th — NOT the 9th (empty).
        Assert.Equal(D(5), EntryNavigation.FindAdjacent(Existing, D(10), -1));
    }

    [Fact]
    public void Newer_SkipsEmptyDays_ToNextExistingEntry()
    {
        // From the 10th, going newer lands on the 20th — NOT the 11th (empty).
        Assert.Equal(D(20), EntryNavigation.FindAdjacent(Existing, D(10), +1));
    }

    [Fact]
    public void Older_FromDayWithNoEntry_UsesNearestExistingBefore()
    {
        // Current day (the 15th) isn't itself an entry; older → the 10th.
        Assert.Equal(D(10), EntryNavigation.FindAdjacent(Existing, D(15), -1));
    }

    [Fact]
    public void Newer_FromDayWithNoEntry_UsesNearestExistingAfter()
    {
        Assert.Equal(D(20), EntryNavigation.FindAdjacent(Existing, D(15), +1));
    }

    [Fact]
    public void Older_AtOldestEntry_ReturnsNull()
    {
        Assert.Null(EntryNavigation.FindAdjacent(Existing, D(2), -1));
    }

    [Fact]
    public void Newer_AtNewestEntry_ReturnsNull()
    {
        // Nothing after the 20th — including "today" being later means no future jump.
        Assert.Null(EntryNavigation.FindAdjacent(Existing, D(20), +1));
    }

    [Fact]
    public void EmptyEntryList_ReturnsNull()
    {
        Assert.Null(EntryNavigation.FindAdjacent(System.Array.Empty<DateOnly>(), D(10), -1));
        Assert.Null(EntryNavigation.FindAdjacent(System.Array.Empty<DateOnly>(), D(10), +1));
    }

    [Fact]
    public void CurrentDateExactlyOnAnEntry_IsExcluded_StrictlyAdjacent()
    {
        // Standing on the 5th, older must move OFF it to the 2nd, not stay.
        Assert.Equal(D(2), EntryNavigation.FindAdjacent(Existing, D(5), -1));
        Assert.Equal(D(10), EntryNavigation.FindAdjacent(Existing, D(5), +1));
    }

    [Fact]
    public void UnorderedInput_HandledCorrectly()
    {
        // Order of the source list must not matter.
        var shuffled = new[] { D(20), D(2), D(10), D(5) };
        Assert.Equal(D(5), EntryNavigation.FindAdjacent(shuffled, D(10), -1));
        Assert.Equal(D(20), EntryNavigation.FindAdjacent(shuffled, D(10), +1));
    }
}
