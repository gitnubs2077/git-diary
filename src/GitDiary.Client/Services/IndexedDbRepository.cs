using System.Text.Json;
using GitDiary.Client.Models;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Draft store backed by browser localStorage. Despite the historical class name,
/// this repository no longer uses IndexedDB — all drafts are persisted under a
/// single JSON blob at <see cref="StorageKey"/>.
/// </summary>
public sealed class IndexedDbRepository : IDisposable
{
    private const string StorageKey = "gitdiary_drafts";

    private readonly IJSRuntime _jsRuntime;
    private readonly Dictionary<string, Draft> _drafts = new();
    // Every localStorage round-trip is async, so without a gate two concurrent
    // SaveDraftAsync calls could interleave dictionary writes and racing
    // JsonSerializer.Serialize passes could observe a torn snapshot
    // ("Collection was modified"). The gate guards both the in-memory
    // dictionary and the localStorage round-trip end-to-end.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private bool _disposed;

    public IndexedDbRepository(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SaveDraftAsync(Draft draft)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            _drafts[draft.Path] = new Draft
            {
                Path = draft.Path,
                Content = draft.Content,
                Sha = draft.Sha,
                State = draft.State,
                UpdatedAt = draft.UpdatedAt
            };
            await PersistLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Draft?> LoadDraftAsync(string path)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            return _drafts.TryGetValue(path, out var draft) ? draft : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveDraftAsync(string path)
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            if (_drafts.Remove(path))
            {
                await PersistLockedAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<Draft>> ListPendingAsync()
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            return _drafts.Values
                .Where(d => d.State == SyncState.Pending || d.State == SyncState.Failed)
                .OrderByDescending(d => d.UpdatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<Draft>> GetAllDraftsAsync()
    {
        await EnsureLoadedAsync();
        await _gate.WaitAsync();
        try
        {
            return _drafts.Values.OrderByDescending(d => d.UpdatedAt).ToList();
        }
        finally
        {
            _gate.Release();
        }
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

    /// <summary>
    /// Serializes <see cref="_drafts"/> and writes it to localStorage. Caller
    /// MUST hold <see cref="_gate"/>; otherwise a concurrent mutation to the
    /// dictionary while Serialize is enumerating throws
    /// InvalidOperationException.
    /// </summary>
    private async Task PersistLockedAsync()
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

    /// <summary>
    /// Erases every draft, in memory and in localStorage. Used when disconnecting an
    /// account: drafts hold the diary text itself, so leaving them behind would make
    /// "disconnect" a lie on a shared machine — the next person to open the app would
    /// see the previous user's writing even with the token gone.
    /// <para>
    /// This DISCARDS unsynced work by design. The caller is responsible for warning
    /// the user first (see setup.disconnectConfirm).
    /// </para>
    /// </summary>
    public async Task ClearAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _drafts.Clear();
            // Mark as loaded so a later read doesn't repopulate the cache from the
            // very localStorage blob we are about to delete.
            _loaded = true;
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            }
            catch
            {
                // localStorage unavailable; the in-memory drafts are cleared regardless.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
