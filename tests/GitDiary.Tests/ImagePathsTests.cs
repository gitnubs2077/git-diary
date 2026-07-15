using GitDiary.Client.Infrastructure;
using Xunit;

namespace GitDiary.Tests;

/// <summary>
/// Image path/reference logic. The round-trip that matters: an image attached on a
/// given day gets a repo path AND a relative reference, and that reference — once
/// embedded in the day's Markdown — must resolve back to the very same repo path so
/// the preview can find it. These tests pin that invariant plus the external-URL
/// escape hatch (http/data references are left alone).
/// </summary>
public class ImagePathsTests
{
    private static readonly DateOnly Day = new(2026, 7, 15);

    [Fact]
    public void BuildImagePath_PlacesImageUnderDayAssetsFolder()
    {
        Assert.Equal("Diary/2026/07/assets/15-abc123.png",
            ImagePaths.BuildImagePath(Day, "abc123", "png"));
    }

    [Fact]
    public void BuildReference_IsRelativeToTheEntryFile()
    {
        Assert.Equal("assets/15-abc123.png", ImagePaths.BuildReference(Day, "abc123", "png"));
    }

    [Fact]
    public void AttachThenResolve_RoundTripsToTheSameRepoPath()
    {
        var repoPath = ImagePaths.BuildImagePath(Day, "abc123", "jpg");
        var reference = ImagePaths.BuildReference(Day, "abc123", "jpg");

        Assert.Equal(repoPath, ImagePaths.ResolveReference(Day, reference));
    }

    [Fact]
    public void Resolve_NormalizesDotDotSegments()
    {
        // A relative "../.." reference climbs out of the entry dir back to Diary root.
        Assert.Equal("Diary/other/x.png",
            ImagePaths.ResolveReference(Day, "../../other/x.png"));
    }

    [Fact]
    public void Resolve_RootRelativeIsRepoRootRelative()
    {
        Assert.Equal("Diary/2026/07/assets/x.png",
            ImagePaths.ResolveReference(Day, "/Diary/2026/07/assets/x.png"));
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("http://example.com/a.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//cdn.example.com/a.png")]
    public void Resolve_ReturnsNullForExternalUrls(string reference)
    {
        // External / inline references are not ours to fetch — the preview leaves them be.
        Assert.Null(ImagePaths.ResolveReference(Day, reference));
    }

    [Fact]
    public void ExtractImageReferences_FindsAllImagesButNotPlainLinks()
    {
        var md = "text ![one](assets/a.png) more [nota link](http://x) ![two](assets/b.jpg \"title\")";
        var refs = ImagePaths.ExtractImageReferences(md).ToList();

        Assert.Equal(new[] { "assets/a.png", "assets/b.jpg" }, refs);
    }

    [Fact]
    public void ExtractImageReferences_EmptyOrNull_ReturnsNothing()
    {
        Assert.Empty(ImagePaths.ExtractImageReferences(null));
        Assert.Empty(ImagePaths.ExtractImageReferences(""));
    }

    [Theory]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/gif", "gif")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/heic", "bin")]
    public void ExtensionForMime_MapsKnownTypes(string mime, string ext)
    {
        Assert.Equal(ext, ImagePaths.ExtensionForMime(mime));
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    [InlineData("xyz", "application/octet-stream")]
    public void MimeForExtension_MapsKnownExtensions(string ext, string mime)
    {
        Assert.Equal(mime, ImagePaths.MimeForExtension(ext));
    }
}
