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

    /// <summary>Load a document by its repo path. A path we know about (from a draft)
    /// but that isn't on GitHub yet maps to a blank success entry, same as diary.</summary>
    public async Task<Result<DiaryEntry>> LoadDocAsync(string path)
    {
        var title = DocPaths.ParseTitle(path);
        var createdAt = DocPaths.ParseCreatedAt(path) ?? DateTimeOffset.Now;
        var day = DateOnly.FromDateTime(createdAt.LocalDateTime);

        var result = await _gitHubApi.GetFileContentAsync(path);
        if (result.IsFailure && result.Error == "NOT_FOUND")
        {
            return Result<DiaryEntry>.Success(new DiaryEntry
            {
                Kind = EntryKind.Doc, Path = path, Title = title, CreatedAt = createdAt, Date = day,
                Content = $"# {title}\n\n", Sha = "", SyncState = SyncState.Synced
            });
        }
        if (result.IsFailure)
            return Result<DiaryEntry>.Failure(result.Error!, result.StatusCode);

        var payload = result.Value!;
        return Result<DiaryEntry>.Success(new DiaryEntry
        {
            Kind = EntryKind.Doc, Path = path, Title = title, CreatedAt = createdAt, Date = day,
            Content = payload.Content, Sha = payload.Sha, SyncState = SyncState.Synced
        });
    }

    /// <summary>List every committed document, newest first (the created-timestamp
    /// filename prefix means a reverse path sort is chronological).</summary>
    public async Task<Result<List<DiaryEntryInfo>>> GetAllDocsAsync()
    {
        var treeResult = await _gitHubApi.GetTreeAsync();
        if (treeResult.IsFailure)
            return Result<List<DiaryEntryInfo>>.Failure(treeResult.Error!, treeResult.StatusCode);

        var infos = new List<DiaryEntryInfo>();
        foreach (var node in treeResult.Value!)
        {
            if (node.Type == "blob" && DocPaths.IsDocPath(node.Path))
            {
                var createdAt = DocPaths.ParseCreatedAt(node.Path) ?? DateTimeOffset.MinValue;
                infos.Add(new DiaryEntryInfo
                {
                    Kind = EntryKind.Doc,
                    Path = node.Path,
                    Sha = node.Sha,
                    Exists = true,
                    Title = DocPaths.ParseTitle(node.Path),
                    CreatedAt = createdAt,
                    Date = DateOnly.FromDateTime(createdAt.LocalDateTime),
                });
            }
        }
        return Result<List<DiaryEntryInfo>>.Success(
            infos.OrderByDescending(i => i.Path, StringComparer.Ordinal).ToList());
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
