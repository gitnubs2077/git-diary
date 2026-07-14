using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;

namespace GitDiary.Client.Services;

public sealed class DiaryRepository
{
    private readonly GitHubApiClient _gitHubApi;

    public DiaryRepository(GitHubApiClient gitHubApi)
    {
        _gitHubApi = gitHubApi;
    }

    public async Task<Result<DiaryEntry>> LoadAsync(DateOnly date)
    {
        var path = PathHelper.GetPath(date);
        var result = await _gitHubApi.GetFileContentAsync(path);

        if (result.IsFailure && result.Error == "NOT_FOUND")
        {
            return Result<DiaryEntry>.Success(new DiaryEntry
            {
                Date = date,
                Path = path,
                Content = $"# {PathHelper.GetTitle(date)}\n\n",
                Sha = "",
                SyncState = SyncState.Synced
            });
        }

        if (result.IsFailure)
            return Result<DiaryEntry>.Failure(result.Error!, result.StatusCode);

        var payload = result.Value!;

        return Result<DiaryEntry>.Success(new DiaryEntry
        {
            Date = date,
            Path = path,
            Content = payload.Content,
            Sha = payload.Sha,
            SyncState = SyncState.Synced
        });
    }

    public async Task<Result<bool>> SaveAsync(DiaryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Sha))
        {
            var result = await _gitHubApi.CreateFileAsync(entry.Path, entry.Content);
            if (result.IsSuccess)
            {
                entry.Sha = result.Value!;
                entry.SyncState = SyncState.Synced;
                return Result<bool>.Success(true);
            }
            return Result<bool>.Failure(result.Error ?? "Unknown error", result.StatusCode);
        }

        var putResult = await _gitHubApi.PutFileAsync(entry.Path, entry.Content, entry.Sha);
        if (putResult.IsSuccess)
        {
            entry.Sha = putResult.Value!;
            entry.SyncState = SyncState.Synced;
            return Result<bool>.Success(true);
        }

        return Result<bool>.Failure(putResult.Error ?? "Unknown error", putResult.StatusCode);
    }

    public async Task<Result<bool>> DeleteAsync(DiaryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Sha))
            return Result<bool>.Success(true);

        var result = await _gitHubApi.DeleteFileAsync(entry.Path, entry.Sha);
        if (result.IsSuccess)
            entry.SyncState = SyncState.Synced;

        return result;
    }

    public async Task<Result<List<DiaryEntryInfo>>> GetAllAsync()
    {
        var treeResult = await _gitHubApi.GetTreeAsync();
        if (treeResult.IsFailure)
            // Preserve StatusCode so callers can tell an auth failure (401/403) from a
            // transient/network one — the UI banner wording depends on it.
            return Result<List<DiaryEntryInfo>>.Failure(treeResult.Error!, treeResult.StatusCode);

        var infos = new List<DiaryEntryInfo>();
        foreach (var node in treeResult.Value!)
        {
            if (PathHelper.IsDiaryFile(node.Path))
            {
                var date = PathHelper.ParsePath(node.Path);
                if (date.HasValue)
                {
                    infos.Add(new DiaryEntryInfo
                    {
                        Date = date.Value,
                        Path = node.Path,
                        Sha = node.Sha,
                        Exists = true
                    });
                }
            }
        }

        return Result<List<DiaryEntryInfo>>.Success(
            infos.OrderByDescending(i => i.Date).ToList());
    }
}
