using System.Text.Json;
using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Diary image lifecycle, following the "upload on commit" model the user chose:
/// a picked/pasted image is stored LOCALLY (IndexedDB, encrypted at rest when a vault
/// is unlocked) and only pushed to GitHub when the entry is committed. Until then the
/// preview renders it straight from the local bytes, so writing works offline.
/// </summary>
/// <remarks>
/// Three responsibilities:
/// <list type="bullet">
/// <item><see cref="AttachAsync"/> — stash bytes locally, hand back the Markdown ref.</item>
/// <item><see cref="ResolveToDataUrlAsync"/> — turn a ref into a <c>data:</c> URL for
/// the preview: pending-local first, else an authenticated fetch (works for private
/// repos, where a bare &lt;img&gt; URL cannot).</item>
/// <item><see cref="UploadPendingForAsync"/> — at commit time, push every pending
/// image the entry references, BEFORE the .md is saved.</item>
/// </list>
/// Path/reference math lives in <see cref="ImagePaths"/> and is unit-tested there.
/// </remarks>
public sealed class ImageService
{
    private readonly IJSRuntime _js;
    private readonly VaultService _vault;
    private readonly GitHubApiClient _api;

    // data: URL cache keyed by absolute repo path. The service is scoped — one
    // instance per app in WASM — so this survives preview re-renders and spares a
    // GitHub round-trip on every keystroke for already-resolved committed images.
    private readonly Dictionary<string, string> _dataUrlCache = new();

    // Commit-date cache keyed by repo path, so reopening the gallery doesn't re-hit
    // the commits API for images whose upload time we already know.
    private readonly Dictionary<string, DateTimeOffset?> _commitDateCache = new();

    public ImageService(IJSRuntime js, VaultService vault, GitHubApiClient api)
    {
        _js = js;
        _vault = vault;
        _api = api;
    }

    /// <summary>
    /// Store a picked/pasted image locally (pending commit) and return the Markdown
    /// image snippet to insert at the caret, e.g. <c>![photo](assets/15-ab12cd34.png)</c>.
    /// </summary>
    public async Task<string> AttachAsync(DiaryEntry entry, string mime, string base64, string? originalName)
    {
        var ext = ImagePaths.ExtensionForMime(mime);
        if (ext == "bin")
        {
            // Unknown/empty MIME (common for clipboard blobs) — fall back to the
            // original filename's extension so PNGs don't land as ".bin". Sanitized:
            // the filename is arbitrary and this ext flows into the stored path.
            var fromName = ImagePaths.SafeExtension(ExtensionOf(originalName));
            if (fromName != "bin") ext = fromName;
        }
        // Never trust the browser-supplied MIME verbatim — it ends up in a data: URL
        // that the preview injects as innerHTML. Whitelist it (see ImagePaths.SafeMime).
        var effectiveMime = ImagePaths.SafeMime(mime, ext);

        // The image lives in an assets/ folder next to the entry's own .md — which is
        // Diary/YYYY/MM/assets for a diary day or Docs/assets for a document. The DD
        // prefix keeps a stable, human-scannable order within a diary month's assets.
        var fileStem = $"{entry.Date.Day:D2}-{Guid.NewGuid():N}"[..11];
        var repoPath = ImagePaths.BuildImagePath(entry.Path, fileStem, ext);
        var reference = ImagePaths.BuildReference(fileStem, ext);

        var envelope = JsonSerializer.Serialize(new StoredImage(effectiveMime, base64));
        var payload = _vault.IsUnlocked ? await _vault.EncryptStringAsync(envelope) : envelope;
        await _js.InvokeVoidAsync("gitdiaryImageStore.put", repoPath, payload);

        // Prime the cache so the preview paints immediately with no fetch.
        _dataUrlCache[repoPath] = $"data:{effectiveMime};base64,{base64}";

        return $"![{MakeAlt(originalName)}]({reference})";
    }

    /// <summary>
    /// Resolve a Markdown image reference to a <c>data:</c> URL for the preview, or
    /// null to leave it untouched (external URL, or the image can't be found).
    /// </summary>
    public async Task<string?> ResolveToDataUrlAsync(string entryPath, string reference)
    {
        var abs = ImagePaths.ResolveReference(entryPath, reference);
        if (abs is null) return null;
        return await ResolveAbsoluteAsync(abs);
    }

    /// <summary>
    /// Resolve an absolute repo image path (e.g. from the gallery's tree listing) to a
    /// <c>data:</c> URL, or null if it can't be loaded.
    /// </summary>
    public Task<string?> GetDataUrlForPathAsync(string absolutePath) =>
        ResolveAbsoluteAsync(absolutePath);

    private async Task<string?> ResolveAbsoluteAsync(string abs)
    {
        if (_dataUrlCache.TryGetValue(abs, out var cached)) return cached;

        string? dataUrl = null;

        var pending = await LoadPendingAsync(abs);
        if (pending is not null)
        {
            // Re-sanitize on read: the stored MIME originated from the browser and this
            // string is about to enter the preview's innerHTML via a data: URL.
            var mime = ImagePaths.SafeMime(pending.Mime, ExtensionOf(abs));
            dataUrl = $"data:{mime};base64,{pending.Base64}";
        }
        else
        {
            var fetched = await _api.GetRawBase64Async(abs);
            if (fetched.IsSuccess)
            {
                var mime = ImagePaths.MimeForExtension(ExtensionOf(abs));
                dataUrl = $"data:{mime};base64,{fetched.Value}";
            }
        }

        if (dataUrl is not null) _dataUrlCache[abs] = dataUrl;
        return dataUrl;
    }

    /// <summary>
    /// List every committed image under the diary's assets folders, newest-path first.
    /// Pending (not-yet-committed) images aren't in the tree and are intentionally
    /// excluded — the gallery shows what actually lives in the repo.
    /// </summary>
    public async Task<Result<List<GalleryImage>>> ListImagesAsync()
    {
        var tree = await _api.GetTreeAsync();
        if (tree.IsFailure)
            return Result<List<GalleryImage>>.Failure(tree.Error!, tree.StatusCode);

        var images = tree.Value!
            .Where(n => n.Type == "blob" && ImagePaths.IsAssetImagePath(n.Path))
            .Select(n => new GalleryImage { Path = n.Path, Sha = n.Sha, Size = n.Size })
            .OrderByDescending(g => g.Path, StringComparer.Ordinal)
            .ToList();

        return Result<List<GalleryImage>>.Success(images);
    }

    /// <summary>
    /// The upload (commit) time of a committed image, cached per path. Returns null
    /// when unknown or the lookup fails — the gallery simply omits the time then.
    /// </summary>
    public async Task<DateTimeOffset?> GetCommitDateAsync(string absolutePath)
    {
        if (_commitDateCache.TryGetValue(absolutePath, out var cached)) return cached;

        var res = await _api.GetLastCommitDateAsync(absolutePath);
        if (!res.IsSuccess) return null; // transient — don't cache, allow a later retry

        _commitDateCache[absolutePath] = res.Value;
        return res.Value;
    }

    /// <summary>
    /// Delete a committed image from the repo. Also evicts it from the data-URL cache
    /// and drops any lingering local copy so it doesn't reappear.
    /// </summary>
    public async Task<Result<bool>> DeleteImageAsync(string absolutePath, string sha)
    {
        var res = await _api.DeleteFileAsync(absolutePath, sha);
        if (res.IsFailure) return res;

        _dataUrlCache.Remove(absolutePath);
        _commitDateCache.Remove(absolutePath);
        try { await _js.InvokeVoidAsync("gitdiaryImageStore.remove", absolutePath); }
        catch { /* IndexedDB unavailable — nothing local to drop */ }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Upload every pending image the entry references. Returns failure if any upload
    /// fails, so the caller can abort the commit rather than save an entry that points
    /// at images GitHub doesn't have yet.
    /// </summary>
    public async Task<Result<bool>> UploadPendingForAsync(DiaryEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in ImagePaths.ExtractImageReferences(entry.Content))
        {
            var abs = ImagePaths.ResolveReference(entry.Path, reference);
            if (abs is null || !seen.Add(abs)) continue;

            var pending = await LoadPendingAsync(abs);
            if (pending is null) continue; // external, or already uploaded

            var res = await _api.CreateBinaryFileAsync(abs, pending.Base64, $"Add diary image {abs}");
            if (res.IsFailure)
                return Result<bool>.Failure(res.Error!, res.StatusCode);

            // Committed — drop the local copy. The data-URL cache keeps rendering it.
            await _js.InvokeVoidAsync("gitdiaryImageStore.remove", abs);
        }
        return Result<bool>.Success(true);
    }

    private async Task<StoredImage?> LoadPendingAsync(string repoPath)
    {
        string? raw;
        try
        {
            raw = await _js.InvokeAsync<string?>("gitdiaryImageStore.get", repoPath);
        }
        catch
        {
            return null; // IndexedDB unavailable — treat as no pending image
        }
        if (string.IsNullOrEmpty(raw)) return null;

        // Mirror the draft store: only attempt decryption when a vault is unlocked.
        if (_vault.IsUnlocked)
            raw = await _vault.DecryptStringAsync(raw) ?? raw;

        try { return JsonSerializer.Deserialize<StoredImage>(raw); }
        catch { return null; }
    }

    private static string ExtensionOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        if (dot <= slash || dot == path.Length - 1) return "";
        return path[(dot + 1)..].ToLowerInvariant();
    }

    // Alt text from the original filename (sans extension), scrubbed of characters
    // that would break the ![]() syntax. Falls back to a generic word.
    private static string MakeAlt(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "image";
        var baseName = name;
        var dot = baseName.LastIndexOf('.');
        if (dot > 0) baseName = baseName[..dot];
        var cleaned = new string(baseName
            .Where(c => c is not ('[' or ']' or '(' or ')' or '\n' or '\r'))
            .ToArray()).Trim();
        return cleaned.Length == 0 ? "image" : cleaned;
    }

    private sealed record StoredImage(string Mime, string Base64);
}
