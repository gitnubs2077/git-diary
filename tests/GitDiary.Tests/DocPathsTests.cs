using GitDiary.Client.Infrastructure;
using Xunit;

namespace GitDiary.Tests;

/// <summary>
/// Document path/title logic. The invariants that matter: a title round-trips through
/// the filename, the creation-timestamp prefix gives newest-first ordering by a plain
/// reverse string sort, renames keep that prefix, and titles are made filename-safe
/// without losing Unicode.
/// </summary>
public class DocPathsTests
{
    private static readonly DateTimeOffset T = new(2026, 7, 25, 14, 30, 5, TimeSpan.FromHours(8));

    [Fact]
    public void BuildPath_EncodesTimestampPrefixAndTitle()
    {
        Assert.Equal("Docs/20260725-143005-Project Plan.md", DocPaths.BuildPath(T, "Project Plan"));
    }

    [Fact]
    public void BuildThenParse_RoundTripsTitle()
    {
        var path = DocPaths.BuildPath(T, "会议纪要");
        Assert.True(DocPaths.IsDocPath(path));
        Assert.Equal("会议纪要", DocPaths.ParseTitle(path));
    }

    [Fact]
    public void ParseCreatedAt_ReadsBackTheTimestamp()
    {
        var path = DocPaths.BuildPath(T, "x");
        var parsed = DocPaths.ParseCreatedAt(path);
        Assert.NotNull(parsed);
        Assert.Equal(T.DateTime, parsed!.Value.DateTime); // same local wall-clock
    }

    [Fact]
    public void ReverseStringSort_IsNewestFirst()
    {
        var older = DocPaths.BuildPath(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), "a");
        var newer = DocPaths.BuildPath(new DateTimeOffset(2026, 7, 25, 9, 0, 0, TimeSpan.Zero), "b");
        var sorted = new[] { older, newer }.OrderByDescending(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(newer, sorted[0]);
        Assert.Equal(older, sorted[1]);
    }

    [Fact]
    public void Rename_KeepsTimestampPrefix_SwapsTitle()
    {
        var path = DocPaths.BuildPath(T, "old title");
        var renamed = DocPaths.RenamePath(path, "new title");
        Assert.Equal("Docs/20260725-143005-new title.md", renamed);
        Assert.Equal("new title", DocPaths.ParseTitle(renamed!));
        Assert.Equal(DocPaths.ParseCreatedAt(path), DocPaths.ParseCreatedAt(renamed!)); // sort stable
    }

    [Theory]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "abcdefghij")] // all illegal chars stripped
    [InlineData("  trimmed  ", "trimmed")]
    [InlineData("mult\n\t  space", "mult space")] // whitespace collapsed to one space
    [InlineData("", "untitled")]
    [InlineData("   ", "untitled")]
    [InlineData("///", "untitled")]
    public void SanitizeTitle_MakesFilenameSafe(string input, string expected)
    {
        Assert.Equal(expected, DocPaths.SanitizeTitle(input));
    }

    [Fact]
    public void SanitizeTitle_KeepsUnicode()
    {
        Assert.Equal("项目计划 📋", DocPaths.SanitizeTitle("项目计划 📋"));
    }

    [Fact]
    public void SanitizeTitle_CapsLength()
    {
        var result = DocPaths.SanitizeTitle(new string('x', 200));
        Assert.True(result.Length <= 80);
    }

    [Theory]
    [InlineData("Docs/20260725-143005-title.md", true)]
    [InlineData("Docs/20260725-143005-会议.md", true)]
    [InlineData("Docs/assets/25-ab12cd34.png", false)]  // an image, not a doc
    [InlineData("Docs/notes.md", false)]                // no timestamp prefix
    [InlineData("Docs/20260725-143005-a/b.md", false)]  // nested past the doc file
    [InlineData("Diary/2026/07/25.md", false)]          // a diary entry
    [InlineData("", false)]
    public void IsDocPath_MatchesOnlyDocumentFiles(string path, bool expected)
    {
        Assert.Equal(expected, DocPaths.IsDocPath(path));
    }
}
