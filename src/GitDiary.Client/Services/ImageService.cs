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
    public async Task<string> AttachAsync(DateOnly date, string mime, string base64, string? originalName)
    {
        var ext = ImagePaths.ExtensionForMime(mime);
        if (ext == "bin")
        {
            // Unknown/empty MIME (common for clipboard blobs) — fall back to the
            // original filename's extension so PNGs don't land as ".bin".
            var fromName = ExtensionOf(originalName);
            if (fromName.Length is > 0 and <= 5) ext = fromName;
        }
        var effectiveMime = string.IsNullOrEmpty(mime) ? ImagePaths.MimeForExtension(ext) : mime;

        var id = Guid.NewGuid().ToString("N")[..8];
        var repoPath = ImagePaths.BuildImagePath(date, id, ext);
        var reference = ImagePaths.BuildReference(date, id, ext);

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
    public async Task<string?> ResolveToDataUrlAsync(DateOnly entryDate, string reference)
    {
        var abs = ImagePaths.ResolveReference(entryDate, reference);
        if (abs is null) return null;
        if (_dataUrlCache.TryGetValue(abs, out var cached)) return cached;

        string? dataUrl = null;

        var pending = await LoadPendingAsync(abs);
        if (pending is not null)
        {
            dataUrl = $"data:{pending.Mime};base64,{pending.Base64}";
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
    /// Upload every pending image the entry references. Returns failure if any upload
    /// fails, so the caller can abort the commit rather than save an entry that points
    /// at images GitHub doesn't have yet.
    /// </summary>
    public async Task<Result<bool>> UploadPendingForAsync(DiaryEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in ImagePaths.ExtractImageReferences(entry.Content))
        {
            var abs = ImagePaths.ResolveReference(entry.Date, reference);
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
