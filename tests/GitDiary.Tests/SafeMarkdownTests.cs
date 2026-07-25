using GitDiary.Client.Infrastructure;
using Markdig;
using Xunit;

namespace GitDiary.Tests;

/// <summary>
/// Regression suite for the Markdown trust boundary.
/// </summary>
/// <remarks>
/// Diary content is attacker-influenceable in practice — entries sync from a GitHub
/// repo, and a repo can be written to by anything holding the token, by a collaborator,
/// or by a pasted-in file from elsewhere. The rendered HTML is handed straight to
/// <c>innerHTML</c>, and the GitHub PAT sits in plaintext localStorage on the same
/// origin. So a single successful injection here is a full compromise of the user's
/// diary repository.
///
/// These tests exist because that property is invisible in a diff. Adding one
/// innocuous-looking line to the pipeline — <c>UseAdvancedExtensions()</c> — silently
/// re-enables attribute injection and turns the app into a credential-theft vector,
/// with nothing in code review to signal it. <see cref="AdvancedExtensions_WouldBeXss"/>
/// documents that exact trap.
/// </remarks>
public class SafeMarkdownTests
{
    // Anything that would let script run, or would smuggle a scheme past the URL
    // allow-list. Each string is a payload that MUST NOT survive rendering.
    public static TheoryData<string, string> XssPayloads() => new()
    {
        // --- Raw HTML injection (blocked by DisableHtml) ---
        { "raw script tag", "<script>alert(1)</script>" },
        { "img onerror", "<img src=x onerror=alert(1)>" },
        { "svg onload", "<svg onload=alert(1)>" },
        { "iframe javascript:", "<iframe src=\"javascript:alert(1)\"></iframe>" },
        { "body onload", "<body onload=alert(1)>" },
        { "details ontoggle", "<details open ontoggle=alert(1)>" },
        { "html comment breakout", "<!--><script>alert(1)</script>-->" },

        // --- javascript: URLs in links (blocked by the scheme allow-list) ---
        { "javascript link", "[x](javascript:alert(1))" },
        { "javascript mixed case", "[x](JaVaScRiPt:alert(1))" },
        { "javascript with tab", "[x](java\tscript:alert(1))" },
        { "javascript entity-encoded", "[x](&#106;avascript:alert(1))" },
        { "javascript entity tab", "[x](java&#9;script:alert(1))" },
        { "javascript newline", "[x](java\nscript:alert(1))" },
        { "javascript leading space", "[x]( javascript:alert(1))" },
        { "vbscript", "[x](vbscript:msgbox(1))" },
        { "data html", "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)" },

        // --- javascript: URLs in images ---
        { "image javascript", "![x](javascript:alert(1))" },
        { "image data html", "![x](data:text/html,<script>alert(1)</script>)" },

        // --- Reference-style definitions ---
        { "reference link", "[x][ref]\n\n[ref]: javascript:alert(1)" },

        // --- Attribute breakout via link title ---
        { "title quote breakout", "[x](/a \"\\\" onmouseover=alert(1) \\\"\")" },

        // --- Autolinks ---
        { "autolink javascript", "<javascript:alert(1)>" },
    };

    [Theory]
    [MemberData(nameof(XssPayloads))]
    public void Render_NeutralizesXssPayload(string scenario, string markdown)
    {
        var html = SafeMarkdown.Render(markdown);

        Assert.False(
            ContainsExecutableVector(html),
            $"XSS payload survived rendering ({scenario}).\nInput:  {markdown}\nOutput: {html}");
    }

    /// <summary>
    /// Guards the one-line mistake that would undo every other test in this file.
    /// </summary>
    /// <remarks>
    /// This asserts on Markdig's behavior, not on GitDiary's, and that is the point:
    /// it proves the danger is real rather than theoretical, so nobody "cleans up" the
    /// deliberately-minimal pipeline in SafeMarkdown by reaching for the convenient
    /// <c>UseAdvancedExtensions()</c>. If this test ever fails, Markdig changed and
    /// the warning comment in SafeMarkdown.cs should be revisited — it does not mean
    /// GitDiary is broken.
    /// </remarks>
    [Fact]
    public void AdvancedExtensions_WouldBeXss()
    {
        var unsafePipeline = new Markdig.MarkdownPipelineBuilder()
            .DisableHtml()             // note: still set, and still not enough
            .UseAdvancedExtensions()
            .Build();

        var html = Markdig.Markdown.ToHtml("# heading {onclick=\"alert(1)\"}", unsafePipeline);

        Assert.Contains("onclick", html);
    }

    [Theory]
    // Legitimate content must keep working — a sanitizer that eats the good cases
    // gets turned off by whoever is next in this file.
    [InlineData("[x](https://example.com)", "https://example.com")]
    [InlineData("[x](http://example.com)", "http://example.com")]
    [InlineData("[x](mailto:a@b.com)", "mailto:a@b.com")]
    [InlineData("[x](/relative/path)", "/relative/path")]
    [InlineData("[x](#anchor)", "#anchor")]
    [InlineData("[x](./sibling.md)", "./sibling.md")]
    public void Render_PreservesSafeUrls(string markdown, string expectedUrl)
    {
        var html = SafeMarkdown.Render(markdown);
        Assert.Contains($"href=\"{expectedUrl}\"", html);
    }

    [Fact]
    public void Render_PreservesOrdinaryMarkdown()
    {
        var html = SafeMarkdown.Render("# Title\n\nSome **bold** text.");

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>bold</strong>", html);
    }

    [Fact]
    public void Render_Strikethrough_EmittedByToolbarSyntax()
    {
        // The formatting toolbar's "S" button wraps text in ~~…~~. The EmphasisExtras
        // (Strikethrough) extension must render it, otherwise the button produces
        // literal tildes in the preview.
        var html = SafeMarkdown.Render("hello ~~gone~~ world");

        Assert.Contains("<del>gone</del>", html);
    }

    [Fact]
    public void Render_Underline_EmittedByToolbarSyntax()
    {
        // The toolbar's "U" button wraps text in ++…++. The EmphasisExtras (Inserted)
        // extension must render it as <ins> (underlined) — Markdown has no native
        // underline and DisableHtml() escapes raw <u>.
        var html = SafeMarkdown.Render("hello ++under++ world");

        Assert.Contains("<ins>under</ins>", html);
    }

    [Fact]
    public void Render_PipeTable_EmittedByToolbarSyntax()
    {
        // The "table" button inserts a pipe-table skeleton; PipeTables must render it.
        var html = SafeMarkdown.Render("| A | B |\n| --- | --- |\n| 1 | 2 |");

        Assert.Contains("<table>", html);
        Assert.Contains("<th>A</th>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public void Render_TaskList_EmitsDisabledCheckboxes()
    {
        // GFM task lists render as disabled checkboxes (ported from upstream f92a306).
        var html = SafeMarkdown.Render("- [ ] todo\n- [x] done");

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("disabled", html);
        Assert.Contains("checked", html); // the [x] item
    }

    [Fact]
    public void Render_EnabledExtensions_StillGrantNoAttributeInjection()
    {
        // Enabling EmphasisExtras + PipeTables must NOT reopen the GenericAttributes
        // hole that UseAdvancedExtensions() would. The {onclick=…} attribute block is
        // the canonical probe (see AdvancedExtensions_WouldBeXss for the unsafe
        // baseline). Here it must render INERT: the braces survive as escaped body
        // text (proving no attribute was parsed) and no live onclick="…" attribute —
        // unescaped quotes — lands on the tag.
        var html = SafeMarkdown.Render("# heading {onclick=\"alert(1)\"}");

        Assert.Contains("{onclick", html);                 // stayed literal text
        Assert.DoesNotContain("onclick=\"alert(1)\"", html); // never a real attribute
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Render_EmptyInputReturnsEmpty(string? content)
    {
        Assert.Equal(string.Empty, SafeMarkdown.Render(content));
    }

    /// <summary>
    /// Proves <see cref="ContainsExecutableVector"/> actually discriminates.
    /// </summary>
    /// <remarks>
    /// Every XSS assertion above is of the form "the detector did not fire". A detector
    /// that never fires — because a refactor broke it, or because it was quietly
    /// loosened to make a failing test go away — would make all of them pass while
    /// testing nothing at all. So: feed it HTML that genuinely executes and require it
    /// to fire, and inert-but-suspicious-looking HTML and require it to stay silent.
    /// </remarks>
    [Theory]
    [InlineData(true, "<script>alert(1)</script>")]
    [InlineData(true, "<a href=\"/a\" onmouseover=\"alert(1)\">x</a>")]
    [InlineData(true, "<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData(true, "<img src=x onerror=alert(1)>")]
    // Inert: the tag opener is escaped, so this is text, not markup.
    [InlineData(false, "&lt;img src=x onerror=alert(1)&gt;")]
    // Inert: the handler is trapped inside a quoted attribute value.
    [InlineData(false, "<a href=\"/a\" title=\"&quot; onmouseover=alert(1) &quot;\">x</a>")]
    // Inert: ordinary safe output.
    [InlineData(false, "<p><a href=\"https://example.com\">x</a></p>")]
    public void Detector_DistinguishesExecutableFromInert(bool expected, string html)
    {
        Assert.Equal(expected, ContainsExecutableVector(html));
    }

    /// <summary>
    /// Looks for anything the browser could actually execute, rather than for the
    /// literal payload text. Escaped output such as
    /// <c>&amp;lt;img src=x onerror=alert(1)&amp;gt;</c> still *contains* the substring
    /// "onerror", but is inert — asserting on the raw substring would fail on correct
    /// output and, worse, could be "fixed" by weakening the assertion.
    /// </summary>
    private static bool ContainsExecutableVector(string html)
    {
        // An unescaped tag opener is the prerequisite for every HTML-injection vector
        // here; DisableHtml escapes them to &lt;.
        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<iframe", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<svg", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<img src=x", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<details", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // An event handler that is live rather than inert. Two conditions must BOTH
        // hold for it to fire:
        //
        //   1. it sits inside a real tag (an unescaped '<' precedes it with no '>' in
        //      between), and
        //   2. it is not trapped inside a quoted attribute value.
        //
        // Condition 2 is not pedantry. `[x](/a "\" onmouseover=alert(1) \"")` renders as
        //     <a href="/a" title="&quot; onmouseover=alert(1) &quot;">
        // The breakout quote is escaped to &quot;, so the handler is just characters in
        // the title text — completely inert — yet it is undeniably "inside a tag". A
        // check that stops at condition 1 reports that correct output as a vulnerability.
        // Since attribute values are '"'-delimited and Markdig escapes any literal '"'
        // within them, an odd number of quotes between the tag opener and the handler
        // means we are inside a value.
        foreach (var handler in new[] { "onerror=", "onload=", "onclick=", "onmouseover=", "ontoggle=" })
        {
            var idx = html.IndexOf(handler, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var open = html.LastIndexOf('<', idx);
                var close = html.LastIndexOf('>', idx);
                if (open > close)
                {
                    var quotesBefore = html[open..idx].Count(c => c == '"');
                    if (quotesBefore % 2 == 0)
                        return true;
                }
                idx = html.IndexOf(handler, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        // A dangerous scheme surviving inside an href/src attribute.
        foreach (var attr in new[] { "href=\"", "src=\"" })
        {
            var idx = html.IndexOf(attr, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var start = idx + attr.Length;
                var end = html.IndexOf('"', start);
                if (end > start)
                {
                    var url = html[start..end];
                    // Strip characters browsers ignore when parsing a scheme, so
                    // "java\tscript:" and "java&#9;script:" collapse to "javascript:".
                    var collapsed = new string(url.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
                    if (collapsed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                        collapsed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
                        collapsed.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                idx = html.IndexOf(attr, idx + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
