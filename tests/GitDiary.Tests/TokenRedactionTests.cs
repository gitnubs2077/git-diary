using System.Reflection;
using System.Text.RegularExpressions;
using GitDiary.Client.Services;
using Xunit;

namespace GitDiary.Tests;

/// <summary>
/// GitHubApiClient redacts anything token-shaped out of strings before they reach
/// <c>console.error</c>, which is world-readable from devtools. The regression this
/// pins: the original pattern was <c>gh[pousr]_[A-Za-z0-9]{20,}</c>, which cannot match
/// a fine-grained <c>github_pat_…</c> token — the third character is <c>i</c>, and <c>i</c>
/// is not in <c>[pousr]</c>. The format the app is actually built around was the one
/// format the redactor skipped.
/// </summary>
public class TokenRedactionTests
{
    // Redact is a private static helper; there is no seam to call it through, and
    // introducing one purely for tests would widen the public surface of the class that
    // holds the credential. Reflection is the lesser evil here.
    private static string Redact(string input)
    {
        var method = typeof(GitHubApiClient).GetMethod(
            "Redact", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, new object?[] { input }));
    }

    [Theory]
    [InlineData("github_pat_11ABCDEFG0aBcDeFgHiJkL_mNoPqRsTuVwXyZ0123456789abcdefghij")] // fine-grained (the regression)
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]                             // classic
    [InlineData("gho_abcdefghijklmnopqrstuvwxyz0123456789")]                             // oauth
    [InlineData("ghs_abcdefghijklmnopqrstuvwxyz0123456789")]                             // server-to-server
    public void Redact_RemovesEveryTokenFormat(string token)
    {
        var redacted = Redact($"{{\"message\":\"Bad credentials\",\"token\":\"{token}\"}}");

        Assert.DoesNotContain(token, redacted);
    }

    [Fact]
    public void Redact_RemovesBearerHeader()
    {
        const string token = "github_pat_11ABCDEFG0aBcDeFgHiJkL_mNoPqRsTuVwXyZ0123456789abcdefghij";
        var redacted = Redact($"Authorization: Bearer {token}");

        Assert.DoesNotContain(token, redacted);
    }

    /// <summary>
    /// Commit/blob/tree SHAs are 40 hex characters and appear all over GitHub's response
    /// bodies. They are not secrets, and redacting them would gut the error logs this
    /// regex is applied to — so the pattern must NOT treat them as tokens.
    /// </summary>
    [Fact]
    public void Redact_LeavesGitShasIntact()
    {
        const string sha = "e6a643e7b12d52d1b77d5afb39e70971a72ecf9f";
        var redacted = Redact($"{{\"sha\":\"{sha}\"}}");

        Assert.Contains(sha, redacted);
    }

    /// <summary>
    /// The wizard's validator and the log redactor must agree on what a token looks
    /// like. If the validator accepts a format the redactor cannot match, that format is
    /// exactly the one that leaks — which is how the original bug arose.
    /// </summary>
    [Fact]
    public void RedactorCoversEveryFormatTheWizardAccepts()
    {
        var redactorPattern = GetPrivateStaticRegex("TokenPattern");

        foreach (var sample in new[]
                 {
                     "github_pat_11ABCDEFG0aBcDeFgHiJkL_mNoPqRsTuVwXyZ0123456789abcdefghij",
                     "ghp_abcdefghijklmnopqrstuvwxyz0123456789",
                 })
        {
            Assert.True(
                redactorPattern.IsMatch(sample),
                $"GitHubApiClient.TokenPattern does not match '{sample[..12]}…', a format SetupWizard accepts. " +
                "That token would be logged verbatim on an API error.");
        }
    }

    private static Regex GetPrivateStaticRegex(string fieldName)
    {
        var field = typeof(GitHubApiClient).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsType<Regex>(field!.GetValue(null));
    }
}
