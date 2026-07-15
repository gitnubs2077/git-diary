using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GitDiary.Client.Infrastructure;

/// <summary>
/// The single trust boundary between diary content and the DOM.
/// </summary>
/// <remarks>
/// <para>
/// The rendered HTML from <see cref="Render"/> is injected into the page via
/// <c>innerHTML</c> (see <c>wwwroot/js/preview-interop.js</c>), which bypasses
/// Blazor's automatic escaping entirely. The GitHub PAT lives in plaintext
/// <c>localStorage</c> on the same origin, so any script that executes here can read
/// it and has full read/write access to the user's diary repository. Everything this
/// class does exists to make that impossible.
/// </para>
/// <para>
/// <b>Do not add extensions to this pipeline without understanding what they enable.</b>
/// In particular, <c>UseAdvancedExtensions()</c> turns on Markdig's
/// <c>GenericAttributes</c> extension, and that alone is a complete compromise:
/// <c># heading {onclick="alert(1)"}</c> renders as
/// <c>&lt;h1 onclick="alert(1)"&gt;</c> — arbitrary attribute injection, no raw HTML
/// required, <see cref="MarkdownPipelineBuilder.DisableHtml"/> notwithstanding.
/// The minimality of this pipeline is a security property, not a stylistic one.
/// </para>
/// <para>
/// Regression tests live in <c>tests/GitDiary.Tests/SafeMarkdownTests.cs</c>. They are
/// the executable version of this comment — if you change this file, they should be
/// the thing that tells you whether you broke it.
/// </para>
/// </remarks>
public static class SafeMarkdown
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    /// <summary>
    /// Schemes a link or image may use. This is an ALLOW-list, and it must stay one.
    /// </summary>
    /// <remarks>
    /// A deny-list (<c>url.StartsWith("javascript:")</c>) is the classic way to get
    /// this wrong, because browsers strip embedded control characters before parsing a
    /// URL scheme: <c>java&amp;#9;script:alert(1)</c> defeats a deny-list but not this,
    /// since the tab stays inside the scheme and <c>"java\tscript"</c> is simply not a
    /// member of this set. Anything unrecognized is rejected by default.
    /// </remarks>
    private static readonly HashSet<string> AllowedUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "mailto",
        "tel"
    };

    /// <summary>Renders diary Markdown to HTML that is safe to inject as innerHTML.</summary>
    public static string Render(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return Markdown.ToHtml(content, Pipeline);
    }

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            // Escapes every raw HTML block and inline tag, and disables HTML parsing
            // inside autolinks. This is what stops <img src=x onerror=alert(1)>.
            .DisableHtml()
            .UseAutoLinks()
            // Strikethrough (~~text~~) only — the toolbar exposes it. These two
            // extensions render into a FIXED, attribute-free tag set (<del>, <table>,
            // <th>, <td>, …). Unlike UseAdvancedExtensions()/GenericAttributes, they
            // grant no way to inject attributes or raw HTML, so they do not widen the
            // trust boundary this class defends. The escape guarantee from
            // DisableHtml() still holds; SafeMarkdownTests pins both facts.
            .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
            .UsePipeTables()
            .UseSoftlineBreakAsHardlineBreak();

        // DocumentProcessed runs after Markdig has decoded HTML entities and
        // backslash escapes, so SanitizeLinks sees the same final string the browser
        // will — not the raw source text. Sanitizing earlier would be bypassable by
        // entity-encoding the payload.
        builder.DocumentProcessed += SanitizeLinks;
        return builder.Build();
    }

    private static void SanitizeLinks(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url))
            {
                link.Url = string.Empty;
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafeUrl(link.Url))
            {
                link.Url = string.Empty;
            }
        }
    }

    private static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        var trimmed = url.TrimStart();
        if (trimmed.Length == 0)
            return true;

        // Fragment, root-relative, query-relative, and dot-relative paths are all safe.
        // None of these can introduce a scheme: a URL scheme must begin with an ASCII
        // letter, so no string starting with one of these characters can ever parse as
        // one (WHATWG URL, §scheme state).
        if (trimmed[0] is '#' or '/' or '?' or '.')
            return true;

        var colonIdx = trimmed.IndexOf(':');
        if (colonIdx <= 0)
            return true; // no scheme → treat as relative

        var scheme = trimmed[..colonIdx];
        return AllowedUrlSchemes.Contains(scheme);
    }
}
