using System.Text.RegularExpressions;

namespace GitDiary.Client.Infrastructure;

/// <summary>
/// Pure path/reference logic for diary images. Split out from ImageService so the
/// tricky bits — where an image file lives in the repo, how it is referenced from a
/// day's Markdown, and how that reference resolves back to an absolute repo path for
/// the preview — can be unit-tested without a browser, IndexedDB, or a live repo.
/// </summary>
/// <remarks>
/// Layout: a day's entry is <c>Diary/YYYY/MM/DD.md</c> (see <see cref="PathHelper"/>).
/// Its images live in a sibling <c>assets/</c> folder — <c>Diary/YYYY/MM/assets/FILE</c>
/// — and are referenced RELATIVELY as <c>assets/FILE</c>. The relative form is what
/// makes the image render both in this app's preview AND on github.com when browsing
/// the raw <c>.md</c>, because GitHub resolves relative image paths against the file's
/// own directory.
/// </remarks>
public static class ImagePaths
{
    // Matches Markdown image references: ![alt](url "optional title").
    // Captures the URL only (group 1), stopping at whitespace or ')'.
    private static readonly Regex ImageRefPattern = new(
        @"!\[[^\]]*\]\(\s*([^)\s]+)",
        RegexOptions.Compiled);

    // A committed image blob under a day's assets folder, e.g.
    // Diary/2026/07/assets/15-ab12cd34.png. This is what the gallery lists out of the
    // git tree, so it must match the exact layout BuildImagePath produces.
    private static readonly Regex AssetImagePattern = new(
        $@"^{Regex.Escape(PathHelper.BaseDirectory)}/\d{{4}}/\d{{2}}/assets/[^/]+\.(png|jpe?g|gif|webp|svg|bmp|avif)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True when <paramref name="path"/> is an image under a day's assets folder.</summary>
    public static bool IsAssetImagePath(string? path) =>
        !string.IsNullOrEmpty(path) && AssetImagePattern.IsMatch(path);

    /// <summary>Directory that holds a day's images, e.g. <c>Diary/2026/07/assets</c>.</summary>
    public static string AssetsDirectory(DateOnly date) =>
        $"{PathHelper.BaseDirectory}/{date.Year:D4}/{date.Month:D2}/assets";

    /// <summary>Absolute repo path for a new image on <paramref name="date"/>.</summary>
    public static string BuildImagePath(DateOnly date, string id, string extension) =>
        $"{AssetsDirectory(date)}/{date.Day:D2}-{id}.{Normalize(extension)}";

    /// <summary>The RELATIVE reference to embed in the day's Markdown.</summary>
    public static string BuildReference(DateOnly date, string id, string extension) =>
        $"assets/{date.Day:D2}-{id}.{Normalize(extension)}";

    /// <summary>The directory a day's entry file lives in, e.g. <c>Diary/2026/07</c>.</summary>
    private static string EntryDirectory(DateOnly date) =>
        $"{PathHelper.BaseDirectory}/{date.Year:D4}/{date.Month:D2}";

    /// <summary>
    /// Resolves a Markdown image reference to the absolute repo path it points at, or
    /// null when the reference is something we neither store nor fetch — an absolute
    /// URL (http/https/etc.), a data: URI, or a protocol-relative <c>//host</c> path.
    /// Those are left untouched in the preview.
    /// </summary>
    public static string? ResolveReference(DateOnly entryDate, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var reftrimmed = reference.Trim();

        // Anything with a scheme, a data: URI, or a protocol-relative //host is external.
        if (reftrimmed.StartsWith("//") ||
            reftrimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            HasUriScheme(reftrimmed))
        {
            return null;
        }

        // A root-relative "/a/b" is taken as repo-root-relative; strip the leading slash.
        // Otherwise resolve against the entry's own directory.
        string combined = reftrimmed.StartsWith('/')
            ? reftrimmed.TrimStart('/')
            : $"{EntryDirectory(entryDate)}/{reftrimmed}";

        return NormalizePath(combined);
    }

    /// <summary>All image reference URLs appearing in <paramref name="markdown"/>, in order.</summary>
    public static IEnumerable<string> ExtractImageReferences(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            yield break;

        foreach (Match m in ImageRefPattern.Matches(markdown))
        {
            var url = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(url))
                yield return url;
        }
    }

    /// <summary>File extension (no dot) for a MIME type; "bin" for anything unknown.</summary>
    public static string ExtensionForMime(string? mime) => (mime?.ToLowerInvariant()) switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/jpg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        "image/bmp" => "bmp",
        "image/avif" => "avif",
        _ => "bin",
    };

    /// <summary>MIME type for a file extension; "application/octet-stream" if unknown.</summary>
    public static string MimeForExtension(string? extension) => (Normalize(extension)) switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "bmp" => "image/bmp",
        "avif" => "image/avif",
        _ => "application/octet-stream",
    };

    // The image MIME types this app recognizes — the same set MimeForExtension emits.
    // Used to sanitize MIME strings that come from the browser (File.type) before they
    // are trusted; a browser normally emits one of these, but the app must not assume so.
    private static readonly HashSet<string> AllowedImageMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp",
        "image/svg+xml", "image/bmp", "image/avif",
    };

    private static readonly Regex SafeExtensionPattern = new("^[a-z0-9]{1,5}$", RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="mime"/> only when it is a known, safe image MIME type;
    /// otherwise falls back to the MIME implied by <paramref name="extensionFallback"/>.
    /// This sanitizes MIME strings that originate from the browser (a picked/pasted
    /// file's <c>type</c>) before they are embedded in a <c>data:</c> URL that the
    /// preview injects via innerHTML — a stray quote in the MIME would otherwise break
    /// out of the <c>src="…"</c> attribute. The trust boundary is enforced at this sink,
    /// not just at storage time, so a value persisted before this guard existed is still
    /// neutralized on read.
    /// </summary>
    public static string SafeMime(string? mime, string extensionFallback)
    {
        var m = mime?.Trim();
        if (!string.IsNullOrEmpty(m) && AllowedImageMimes.Contains(m))
            return m;
        return MimeForExtension(extensionFallback);
    }

    /// <summary>
    /// Keeps a file extension only if it is short and purely alphanumeric; anything else
    /// (embedded quotes, slashes, dots, over-long) collapses to <c>bin</c>. Guards the
    /// repo path and Markdown reference an image is stored under, which is derived from
    /// an arbitrary picked filename when the MIME is unrecognized.
    /// </summary>
    public static string SafeExtension(string? extension)
    {
        var e = Normalize(extension);
        return SafeExtensionPattern.IsMatch(e) ? e : "bin";
    }

    private static string Normalize(string? extension) =>
        (extension ?? "").TrimStart('.').ToLowerInvariant();

    // A URI scheme per RFC 3986: ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":".
    // We only need to distinguish "has a scheme" (external) from "relative path".
    private static bool HasUriScheme(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0) return false;
        // A Windows-free repo path never contains ':', but a scheme like "http:" does.
        for (int i = 0; i < colon; i++)
        {
            char c = value[i];
            bool ok = char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.';
            if (!ok) return false;
            if (i == 0 && !char.IsAsciiLetter(c)) return false;
        }
        return true;
    }

    // Collapse "." and ".." segments in a slash-separated path. A ".." that would
    // climb above the root is dropped (clamped), matching how browsers resolve.
    private static string NormalizePath(string path)
    {
        var stack = new List<string>();
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }
        return string.Join('/', stack);
    }
}
