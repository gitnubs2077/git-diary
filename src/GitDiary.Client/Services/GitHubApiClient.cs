using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;

namespace GitDiary.Client.Services;

public sealed class GitHubApiClient
{
    private readonly HttpClient _httpClient;
    private RepositoryConfig? _config;
    private string? _cachedToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Defense-in-depth: strip anything that looks like a Bearer/token secret
    // from strings we're about to log. GitHub's response bodies do not
    // normally echo the Authorization header, but errors from proxies or
    // future SDK bugs could — and console.error is world-readable via the
    // browser devtools.
    private static readonly Regex BearerPattern = new(
        @"Bearer\s+[A-Za-z0-9_\-\.]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // `github_pat_` must be listed here and originally wasn't: fine-grained PATs are
    // the format this app is built around (SetupWizard validates them as the primary
    // case and links users straight at the fine-grained token page), yet the previous
    // pattern — `gh[pousr]_` — cannot match them, because the third character of
    // `github_pat_` is `i` and `i` is not in that character class. The one token
    // format essentially every user actually pastes in was the one this redactor
    // silently skipped.
    //
    // Legacy 40-hex tokens are deliberately NOT matched. They are extinct on
    // github.com, and `[a-f0-9]{40}` also matches every commit/blob/tree SHA — which
    // GitHub's response bodies are full of. Adding it would redact all of them out of
    // the error logs (the only thing this regex is ever applied to) in exchange for
    // covering a format nobody can still issue.
    private static readonly Regex TokenPattern = new(
        @"github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}",
        RegexOptions.Compiled);

    public GitHubApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetConfig(RepositoryConfig config)
    {
        _config = config;
        _cachedToken = config.Token;
    }

    public void SetToken(string token)
    {
        _cachedToken = token;
    }

    // Drop the in-memory credential. Callers disconnecting an account must ALSO
    // clear the persisted copy in localStorage (see Home.ClearStoredConfigAsync) —
    // this only sheds the process-lifetime cache, and on its own would be undone by
    // the next page load.
    public void ClearConfig()
    {
        _config = null;
        _cachedToken = null;
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (_cachedToken is null)
            throw new InvalidOperationException("Token not configured");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitDiary", "1.0"));
    }

    private string GetApiUrl(string path)
    {
        if (_config is null)
            throw new InvalidOperationException("Repository config not set");
        return $"https://api.github.com/repos/{_config.Owner}/{_config.Repo}/{path}";
    }

    private static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var redacted = BearerPattern.Replace(text, "Bearer <redacted>");
        redacted = TokenPattern.Replace(redacted, "<redacted-token>");
        return redacted;
    }

    /// <summary>
    /// Writes full GitHub error diagnostics to console.error (redacted) and
    /// returns the short, stable message that should be placed in
    /// <see cref="Result{T}.Error"/> for UI/log surfaces. Keeps the noisy JSON
    /// body out of the app's user-facing state.
    /// </summary>
    private static string LogAndFormatHttpError(
        HttpMethod method, string url, System.Net.HttpStatusCode status, string? body)
    {
        var reason = status.ToString();
        Console.Error.WriteLine(
            $"[GitDiary] GitHub {method.Method} {url} -> {(int)status} {reason}: {Redact(body)}");
        return $"HTTP {(int)status} {reason}";
    }

    private static string LogAndFormatException(string operation, Exception ex)
    {
        Console.Error.WriteLine(
            $"[GitDiary] GitHub {operation} threw {ex.GetType().Name}: {Redact(ex.Message)}");
        return $"{operation} failed";
    }

    /// <summary>
    /// GET file content. Returns the file's SHA and UTF-8 decoded content.
    /// </summary>
    public async Task<Result<FileContent>> GetFileContentAsync(string path)
    {
        var url = GetApiUrl($"contents/{path}");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return Result<FileContent>.Failure("NOT_FOUND", (int)response.StatusCode);
                var errorBody = await response.Content.ReadAsStringAsync();
                var msg = LogAndFormatHttpError(HttpMethod.Get, url, response.StatusCode, errorBody);
                return Result<FileContent>.Failure(msg, (int)response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var encodedContent = root.GetProperty("content").GetString() ?? "";
            var sha = root.GetProperty("sha").GetString() ?? "";

            var base64 = encodedContent.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);

            return Result<FileContent>.Success(new FileContent(sha, text));
        }
        catch (Exception ex)
        {
            return Result<FileContent>.Failure(LogAndFormatException("Get file", ex));
        }
    }

    /// <summary>
    /// PUT file contents (update existing). Returns the new SHA on success.
    /// </summary>
    public async Task<Result<string>> PutFileAsync(string path, string content, string sha)
    {
        var url = GetApiUrl($"contents/{path}");
        try
        {
            var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

            var body = new
            {
                message = $"Update diary {path}",
                content = base64Content,
                sha,
                branch = _config?.Branch ?? "main"
            };

            var json = JsonSerializer.Serialize(body, JsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var msg = LogAndFormatHttpError(HttpMethod.Put, url, response.StatusCode, errorBody);
                return Result<string>.Failure(msg, (int)response.StatusCode);
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var newSha = doc.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? "";

            return Result<string>.Success(newSha);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(LogAndFormatException("Save file", ex));
        }
    }

    /// <summary>
    /// DELETE file.
    /// </summary>
    public async Task<Result<bool>> DeleteFileAsync(string path, string sha)
    {
        var url = GetApiUrl($"contents/{path}");
        try
        {
            var body = new
            {
                message = $"Delete diary {path}",
                sha,
                branch = _config?.Branch ?? "main"
            };

            var json = JsonSerializer.Serialize(body, JsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var msg = LogAndFormatHttpError(HttpMethod.Delete, url, response.StatusCode, errorBody);
                return Result<bool>.Failure(msg, (int)response.StatusCode);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(LogAndFormatException("Delete file", ex));
        }
    }

    /// <summary>
    /// GET repository git tree (recursive).
    /// </summary>
    public async Task<Result<List<TreeNode>>> GetTreeAsync()
    {
        // Called from DiaryStore.RefreshEntriesAsync, which can race ahead of
        // config being applied during a first-run render (see SetupWizard /
        // Home.OnInitializedAsync ordering). Surface a clean failure instead
        // of throwing so the sidebar just renders empty until config lands.
        if (_config is null)
            return Result<List<TreeNode>>.Failure("Repository config not set");

        var url = GetApiUrl($"git/trees/{_config.Branch ?? "main"}?recursive=1");
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();

                // Empty repository (no commits yet) returns 409 with "Git Repository is empty".
                // That's a valid state for us — treat it as an empty tree so setup/listing succeeds.
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict &&
                    errorBody.Contains("empty", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<List<TreeNode>>.Success(new List<TreeNode>());
                }

                var msg = LogAndFormatHttpError(HttpMethod.Get, url, response.StatusCode, errorBody);
                return Result<List<TreeNode>>.Failure(msg, (int)response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // GitHub caps the recursive tree response at ~100k entries or ~7 MB
            // and sets "truncated": true when it drops entries. For a personal
            // diary that limit would take centuries to reach, but if we ever
            // hit it we'd silently miss files — surface a loud warning so it
            // shows up in bug reports instead of manifesting as "some old
            // entries just vanished from the sidebar".
            if (root.TryGetProperty("truncated", out var truncated) &&
                truncated.ValueKind == JsonValueKind.True)
            {
                Console.Error.WriteLine(
                    "[GitDiary] GitHub tree response was truncated — the entry list is incomplete. " +
                    "This means the repository has grown past GitHub's recursive-tree cap (~100k files / ~7 MB). " +
                    "Consider splitting the diary into multiple repositories.");
            }

            var tree = root.GetProperty("tree");

            var nodes = new List<TreeNode>();
            foreach (var item in tree.EnumerateArray())
            {
                nodes.Add(new TreeNode
                {
                    Path = item.GetProperty("path").GetString() ?? "",
                    Mode = item.GetProperty("mode").GetString() ?? "",
                    Type = item.GetProperty("type").GetString() ?? "",
                    Sha = item.GetProperty("sha").GetString() ?? "",
                    Size = item.TryGetProperty("size", out var size) ? size.GetInt32() : 0
                });
            }

            return Result<List<TreeNode>>.Success(nodes);
        }
        catch (Exception ex)
        {
            return Result<List<TreeNode>>.Failure(LogAndFormatException("Get tree", ex));
        }
    }

    /// <summary>
    /// PUT file contents (create new). Returns the new SHA on success.
    /// </summary>
    public async Task<Result<string>> CreateFileAsync(string path, string content)
    {
        var url = GetApiUrl($"contents/{path}");
        try
        {
            var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

            var body = new
            {
                message = $"Create diary {path}",
                content = base64Content,
                branch = _config?.Branch ?? "main"
            };

            var json = JsonSerializer.Serialize(body, JsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var msg = LogAndFormatHttpError(HttpMethod.Put, url, response.StatusCode, errorBody);
                return Result<string>.Failure(msg, (int)response.StatusCode);
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var newSha = doc.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? "";

            return Result<string>.Success(newSha);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(LogAndFormatException("Create file", ex));
        }
    }

    /// <summary>
    /// Test write access by creating and deleting a temp file. The probe lives
    /// inside <c>Diary/</c> (not the repo root) and is name-randomized to avoid
    /// (a) polluting the user's top-level tree with a stray <c>.gitdiary-test</c>
    /// artefact if a network hiccup interrupts cleanup, and (b) colliding with
    /// a parallel setup attempt from a second tab.
    /// </summary>
    public async Task<Result<bool>> TestWriteAccessAsync()
    {
        var testPath = $"Diary/.gitdiary-test-{Guid.NewGuid():N}.md";
        try
        {
            // Create test file — if this succeeds, write works.
            var result = await CreateFileAsync(testPath, "GitDiary write-access probe. Safe to delete.");
            if (result.IsFailure)
                return Result<bool>.Failure(result.Error!, result.StatusCode);

            // Best-effort clean up using the SHA from the create response. If
            // the delete fails (transient network) the file is still harmless:
            // it's a random-suffixed marker file that the user can remove later.
            if (!string.IsNullOrEmpty(result.Value))
            {
                await DeleteFileAsync(testPath, result.Value);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(LogAndFormatException("Write test", ex));
        }
    }
}
