using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// GET file content. Returns "sha|content" on success.
    /// </summary>
    public async Task<Result<string>> GetFileContentAsync(string path)
    {
        try
        {
            var url = GetApiUrl($"contents/{path}");
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return Result<string>.Failure("NOT_FOUND");
                var errorBody = await response.Content.ReadAsStringAsync();
                return Result<string>.Failure($"GitHub API error: {response.StatusCode} - {errorBody}");
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var encodedContent = root.GetProperty("content").GetString() ?? "";
            var sha = root.GetProperty("sha").GetString() ?? "";

            var base64 = encodedContent.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);

            return Result<string>.Success(sha + "|" + text);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to get file: {ex.Message}");
        }
    }

    /// <summary>
    /// PUT file contents (update existing). Returns the new SHA on success.
    /// </summary>
    public async Task<Result<string>> PutFileAsync(string path, string content, string sha)
    {
        try
        {
            var url = GetApiUrl($"contents/{path}");
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
                return Result<string>.Failure($"GitHub API error: {response.StatusCode} - {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var newSha = doc.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? "";

            return Result<string>.Success(newSha);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to save file: {ex.Message}");
        }
    }

    /// <summary>
    /// DELETE file.
    /// </summary>
    public async Task<Result<bool>> DeleteFileAsync(string path, string sha)
    {
        try
        {
            var url = GetApiUrl($"contents/{path}");
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
                return Result<bool>.Failure($"GitHub API error: {response.StatusCode} - {errorBody}");
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Failed to delete file: {ex.Message}");
        }
    }

    /// <summary>
    /// GET repository git tree (recursive).
    /// </summary>
    public async Task<Result<List<TreeNode>>> GetTreeAsync()
    {
        try
        {
            var url = GetApiUrl($"git/trees/{_config?.Branch ?? "main"}?recursive=1");
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

                return Result<List<TreeNode>>.Failure($"GitHub API error: {response.StatusCode} - {errorBody}");
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
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
            return Result<List<TreeNode>>.Failure($"Failed to get tree: {ex.Message}");
        }
    }

    /// <summary>
    /// PUT file contents (create new). Returns the new SHA on success.
    /// </summary>
    public async Task<Result<string>> CreateFileAsync(string path, string content)
    {
        try
        {
            var url = GetApiUrl($"contents/{path}");
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
                return Result<string>.Failure($"GitHub API error: {response.StatusCode} - {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var newSha = doc.RootElement.GetProperty("content").GetProperty("sha").GetString() ?? "";

            return Result<string>.Success(newSha);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to create file: {ex.Message}");
        }
    }

    /// <summary>
    /// Test write access by creating and deleting a temp file.
    /// </summary>
    public async Task<Result<bool>> TestWriteAccessAsync()
    {
        var testPath = ".gitdiary-test";
        try
        {
            // Try to clean up any leftover test file first
            var existing = await GetFileContentAsync(testPath);
            if (existing.IsSuccess)
            {
                var data = existing.Value!;
                var sep = data.IndexOf('|');
                if (sep >= 0)
                    await DeleteFileAsync(testPath, data[..sep]);
            }

            // Create test file — if this succeeds, write works
            var result = await CreateFileAsync(testPath, "ok");
            if (result.IsFailure)
                return Result<bool>.Failure(result.Error!);

            // Clean up using the SHA from the create response
            if (!string.IsNullOrEmpty(result.Value))
            {
                await DeleteFileAsync(testPath, result.Value);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Write test failed: {ex.Message}");
        }
    }
}
