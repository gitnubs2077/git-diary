using System.Text.Json;
using GitDiary.Client.Models;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Draft store backed by browser localStorage. Despite the historical class name,
/// this repository no longer uses IndexedDB — all drafts are persisted under a
/// single JSON blob at <see cref="StorageKey"/>.
/// </summary>
public sealed class IndexedDbRepository
{
    private const string StorageKey = "gitdiary_drafts";

    private readonly IJSRuntime _jsRuntime;
    private readonly Dictionary<string, Draft> _drafts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public IndexedDbRepository(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SaveDraftAsync(Draft draft)
    {
        await EnsureLoadedAsync();
        _drafts[draft.Path] = new Draft
        {
            Path = draft.Path,
            Content = draft.Content,
            Sha = draft.Sha,
            State = draft.State,
            UpdatedAt = draft.UpdatedAt
        };
        await PersistAsync();
    }

    public async Task<Draft?> LoadDraftAsync(string path)
    {
        await EnsureLoadedAsync();
        return _drafts.TryGetValue(path, out var draft) ? draft : null;
    }

    public async Task RemoveDraftAsync(string path)
    {
        await EnsureLoadedAsync();
        if (_drafts.Remove(path))
        {
            await PersistAsync();
        }
    }

    public async Task<List<Draft>> ListPendingAsync()
    {
        await EnsureLoadedAsync();
        return _drafts.Values
            .Where(d => d.State == SyncState.Pending || d.State == SyncState.Failed)
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
    }

    public async Task<List<Draft>> GetAllDraftsAsync()
    {
        await EnsureLoadedAsync();
        return _drafts.Values.OrderByDescending(d => d.UpdatedAt).ToList();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;

            string? raw = null;
            try
            {
                raw = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            }
            catch
            {
                // localStorage unavailable (e.g. prerender): fall back to in-memory only.
            }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, Draft>>(raw);
                    if (loaded is not null)
                    {
                        foreach (var kv in loaded)
                        {
                            _drafts[kv.Key] = kv.Value;
                        }
                    }
                }
                catch
                {
                    // Corrupted payload — discard and start fresh.
                }
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_drafts);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch
        {
            // localStorage unavailable; changes remain in-memory for this session.
        }
    }
}
