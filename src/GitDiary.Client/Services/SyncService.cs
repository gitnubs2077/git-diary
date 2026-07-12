using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;

namespace GitDiary.Client.Services;

public sealed class SyncService
{
    private readonly GitHubApiClient _gitHubApi;
    private readonly IndexedDbRepository _indexedDb;

    public event Action<SyncState>? SyncStateChanged;

    public SyncService(GitHubApiClient gitHubApi, IndexedDbRepository indexedDb)
    {
        _gitHubApi = gitHubApi;
        _indexedDb = indexedDb;
    }

    public async Task<Result<bool>> SyncDiaryEntry(DiaryEntry entry)
    {
        if (entry.SyncState == SyncState.Synced)
            return Result<bool>.Success(true);

        entry.SyncState = SyncState.Saving;
        SyncStateChanged?.Invoke(SyncState.Saving);

        try
        {
            string? newSha = null;

            if (string.IsNullOrEmpty(entry.Sha))
            {
                // New file
                var result = await _gitHubApi.CreateFileAsync(entry.Path, entry.Content);
                if (result.IsFailure)
                {
                    entry.SyncState = SyncState.Failed;
                    SyncStateChanged?.Invoke(SyncState.Failed);
                    return Result<bool>.Failure(result.Error ?? "Unknown error", result.StatusCode);
                }
                newSha = result.Value;
            }
            else
            {
                // Existing file
                var result = await _gitHubApi.PutFileAsync(entry.Path, entry.Content, entry.Sha);
                if (result.IsFailure)
                {
                    // Classify by HTTP status: 409/422 → SHA/precondition conflict.
                    if (result.IsConflict)
                    {
                        entry.SyncState = SyncState.Conflict;
                        SyncStateChanged?.Invoke(SyncState.Conflict);
                        return Result<bool>.Failure("Conflict detected", result.StatusCode);
                    }

                    entry.SyncState = SyncState.Failed;
                    SyncStateChanged?.Invoke(SyncState.Failed);
                    return Result<bool>.Failure(result.Error ?? "Unknown error", result.StatusCode);
                }
                newSha = result.Value;
            }

            // Success
            entry.Sha = newSha ?? entry.Sha;
            entry.SyncState = SyncState.Synced;
            SyncStateChanged?.Invoke(SyncState.Synced);

            // Update draft in IndexedDB
            await _indexedDb.SaveDraftAsync(new Draft
            {
                Path = entry.Path,
                Content = entry.Content,
                Sha = entry.Sha,
                State = SyncState.Synced,
                UpdatedAt = DateTimeOffset.Now
            });

            return Result<bool>.Success(true);
        }
        catch
        {
            entry.SyncState = SyncState.Failed;
            SyncStateChanged?.Invoke(SyncState.Failed);
            return Result<bool>.Failure("Sync failed");
        }
    }

    public async Task SyncPendingDraftsAsync()
    {
        var pending = await _indexedDb.ListPendingAsync();
        foreach (var draft in pending)
        {
            var entry = new DiaryEntry
            {
                Path = draft.Path,
                Content = draft.Content,
                Sha = draft.Sha,
                SyncState = SyncState.Pending
            };

            var date = PathHelper.ParsePath(draft.Path);
            if (date.HasValue)
                entry.Date = date.Value;

            await SyncDiaryEntry(entry);
        }
    }
}
