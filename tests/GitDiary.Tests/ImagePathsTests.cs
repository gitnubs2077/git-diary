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

    [Theory]
    [InlineData("Diary/2026/07/assets/15-ab12cd34.png", true)]
    [InlineData("Diary/2026/07/assets/15-ab12cd34.JPG", true)]   // case-insensitive
    [InlineData("Diary/2026/12/assets/01-x.webp", true)]
    [InlineData("Diary/2026/07/15.md", false)]                   // an entry, not an image
    [InlineData("Diary/2026/07/assets/notes.txt", false)]        // non-image in assets
    [InlineData("Diary/2026/07/assets/sub/deep.png", false)]     // nested past assets/
    [InlineData("other/2026/07/assets/15-x.png", false)]         // outside the diary root
    [InlineData("Diary/assets/x.png", false)]                    // not the YYYY/MM layout
    [InlineData("", false)]
    public void IsAssetImagePath_MatchesOnlyDayAssetImages(string path, bool expected)
    {
        Assert.Equal(expected, ImagePaths.IsAssetImagePath(path));
    }

    [Fact]
    public void IsAssetImagePath_MatchesWhatBuildImagePathProduces()
    {
        // The gallery filter and the writer must agree, or uploaded images vanish
        // from the gallery.
        var produced = ImagePaths.BuildImagePath(Day, "ab12cd34", "png");
        Assert.True(ImagePaths.IsAssetImagePath(produced));
    }

    // --- Security: MIME / extension sanitizing ---------------------------------
    // These strings ultimately land in a data: URL that the preview injects via
    // innerHTML. A stray quote in the MIME or extension could break out of the
    // src="…" attribute, so both are whitelisted at the trust boundary.

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("image/webp", "image/webp")]
    [InlineData("IMAGE/PNG", "IMAGE/PNG")] // case-insensitive match, still safe
    public void SafeMime_KeepsKnownImageTypes(string mime, string expected)
    {
        Assert.Equal(expected, ImagePaths.SafeMime(mime, "png"));
    }

    [Theory]
    [InlineData("image/png\";onerror=\"alert(1)")] // attribute-breakout payload
    [InlineData("image/png; charset=x")]
    [InlineData("text/html")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a mime")]
    public void SafeMime_RejectsAnythingElse_FallingBackToExtension(string? mime)
    {
        // Falls back to the extension's MIME — never echoes the untrusted input.
        var result = ImagePaths.SafeMime(mime, "png");
        Assert.Equal("image/png", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("onerror", result);
    }

    [Fact]
    public void SafeMime_UnknownMimeAndUnknownExtension_IsInertOctetStream()
    {
        Assert.Equal("application/octet-stream", ImagePaths.SafeMime("evil\"payload", "zzz"));
    }

    [Theory]
    [InlineData("png", "png")]
    [InlineData("jpeg", "jpeg")]
    [InlineData(".PNG", "png")]   // leading dot stripped, lowercased
    [InlineData("webp", "webp")]
    public void SafeExtension_KeepsShortAlphanumeric(string ext, string expected)
    {
        Assert.Equal(expected, ImagePaths.SafeExtension(ext));
    }

    [Theory]
    [InlineData("pn\"g")]          // embedded quote
    [InlineData("p/g")]            // slash
    [InlineData("png ")]           // trailing space
    [InlineData("toolong")]        // > 5 chars
    [InlineData("a.b")]            // dot
    [InlineData("")]
    [InlineData(null)]
    public void SafeExtension_CollapsesEverythingElseToBin(string? ext)
    {
        Assert.Equal("bin", ImagePaths.SafeExtension(ext));
    }
}
