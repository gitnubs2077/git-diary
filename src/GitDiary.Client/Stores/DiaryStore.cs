using GitDiary.Client.Infrastructure;
using GitDiary.Client.Models;
using GitDiary.Client.Services;

namespace GitDiary.Client.Stores;

public sealed class DiaryStore : StoreBase
{
    private readonly DiaryRepository _diaryRepo;
    private readonly IndexedDbRepository _indexedDb;
    private readonly SearchService _searchService;
    private readonly ImageService _images;

    private DiaryEntry? _currentEntry;
    private List<DiaryEntryInfo> _entries = new();
    private bool _isLoading;
    private string _currentContent = "";
    private bool _isDirty;
    private SyncState _syncState = SyncState.Synced;

    public DiaryEntry? CurrentEntry
    {
        get => _currentEntry;
        private set
        {
            _currentEntry = value;
            NotifyStateChanged();
        }
    }

    public List<DiaryEntryInfo> Entries
    {
        get => _entries;
        private set
        {
            _entries = value;
            NotifyStateChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            NotifyStateChanged();
        }
    }

    public string CurrentContent
    {
        get => _currentContent;
        set
        {
            if (_currentContent != value)
            {
                _currentContent = value;
                _isDirty = true;
                NotifyStateChanged();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            _isDirty = value;
            NotifyStateChanged();
        }
    }

    /// <summary>
    /// True when there is a local draft (in localStorage) that has not yet been committed to GitHub.
    /// </summary>
    public bool HasUncommittedDraft =>
        _syncState is SyncState.Pending or SyncState.Failed or SyncState.Conflict;

    public SyncState SyncState
    {
        get => _syncState;
        private set
        {
            _syncState = value;
            NotifyStateChanged();
        }
    }

    // Last failure from loading the entry list or an entry. Before this existed, both
    // RefreshEntriesAsync and LoadEntryAsync silently swallowed Result.Failure: an
    // invalid or expired token produced an empty sidebar, an empty editor, and no hint
    // that authentication was the problem. Home renders a banner off this state.
    private string? _loadError;
    private bool _loadErrorIsAuth;

    public string? LoadError
    {
        get => _loadError;
        private set
        {
            _loadError = value;
            NotifyStateChanged();
        }
    }

    /// <summary>True when the last load failure was an auth problem (HTTP 401/403).</summary>
    public bool LoadErrorIsAuth
    {
        get => _loadErrorIsAuth;
        private set
        {
            _loadErrorIsAuth = value;
            NotifyStateChanged();
        }
    }

    /// <summary>Dismiss the load-error banner without retrying.</summary>
    public void ClearLoadError()
    {
        if (_loadError is null && !_loadErrorIsAuth) return;
        _loadError = null;
        _loadErrorIsAuth = false;
        NotifyStateChanged();
    }

    private void SetLoadError(string? error, int? statusCode)
    {
        _loadErrorIsAuth = statusCode is 401 or 403;
        // Assign through the property last so the single notification it fires reflects
        // both fields already updated.
        LoadError = error;
    }

    public DiaryStore(DiaryRepository diaryRepo, IndexedDbRepository indexedDb, SearchService searchService, ImageService images)
    {
        _diaryRepo = diaryRepo;
        _indexedDb = indexedDb;
        _searchService = searchService;
        _images = images;
    }

    public async Task LoadEntryAsync(DateOnly date)
    {
        IsLoading = true;

        var result = await _diaryRepo.LoadAsync(date);
        if (result.IsSuccess)
        {
            var entry = result.Value!;

            // Check if there's a draft in localStorage that hasn't been committed yet
            var draft = await _indexedDb.LoadDraftAsync(entry.Path);
            if (draft != null && !string.IsNullOrEmpty(draft.Content))
            {
                entry.Content = draft.Content;
                entry.SyncState = draft.State;
            }

            CurrentEntry = entry;
            CurrentContent = entry.Content;
            SyncState = entry.SyncState;
            IsDirty = false;
            SetLoadError(null, null);
        }
        else
        {
            // Leave any previously loaded entry untouched, but surface why this load
            // failed instead of swallowing it. NOT_FOUND never reaches here — the repo
            // maps a missing file to a blank success entry.
            SetLoadError(result.Error, result.StatusCode);
        }

        IsLoading = false;
    }

    /// <summary>
    /// Persist the current buffer to localStorage as a draft. Does NOT talk to GitHub.
    /// Invoked by the autosave debounce and the Save button.
    /// </summary>
    public async Task SaveDraftAsync()
    {
        if (CurrentEntry == null) return;
        if (!_isDirty) return;

        CurrentEntry.Content = CurrentContent;

        await _indexedDb.SaveDraftAsync(new Draft
        {
            Path = CurrentEntry.Path,
            Content = CurrentContent,
            Sha = CurrentEntry.Sha,
            State = SyncState.Pending,
            UpdatedAt = DateTimeOffset.Now
        });

        CurrentEntry.SyncState = SyncState.Pending;
        SyncState = SyncState.Pending;
        IsDirty = false;
    }

    /// <summary>
    /// Push the current entry to GitHub. On success, the local draft is discarded
    /// <em>only</em> when the buffer is still identical to what we committed.
    /// If the user kept typing while the network round-trip was in flight
    /// (200–1000 ms is normal for GitHub), those keystrokes must not be lost —
    /// we keep them as a fresh Pending draft against the new SHA.
    /// Invoked explicitly by the Commit button.
    /// </summary>
    public async Task CommitCurrentEntryAsync()
    {
        if (CurrentEntry == null) return;

        // Snapshot the exact bytes we are about to send. Anything the user types
        // after this line is a NEW edit that must survive the commit.
        var committedContent = CurrentContent;
        var wasNewFile = string.IsNullOrEmpty(CurrentEntry.Sha);

        // Snapshot latest buffer into the entry and into the local draft first,
        // so nothing is lost if the network call fails.
        CurrentEntry.Content = committedContent;
        await _indexedDb.SaveDraftAsync(new Draft
        {
            Path = CurrentEntry.Path,
            Content = committedContent,
            Sha = CurrentEntry.Sha,
            State = SyncState.Saving,
            UpdatedAt = DateTimeOffset.Now
        });
        IsDirty = false;
        SyncState = SyncState.Saving;

        // Push any pending (locally-stored) images this entry references BEFORE the
        // .md is saved — a committed entry must never point at an image GitHub does
        // not have yet. If an upload fails, abort the commit and leave the draft
        // intact so the user can retry.
        var imageResult = await _images.UploadPendingForAsync(CurrentEntry);
        if (imageResult.IsFailure)
        {
            var imageFailState = imageResult.IsConflict ? SyncState.Conflict : SyncState.Failed;
            CurrentEntry.SyncState = imageFailState;
            SyncState = imageFailState;
            await _indexedDb.SaveDraftAsync(new Draft
            {
                Path = CurrentEntry.Path,
                Content = CurrentContent,
                Sha = CurrentEntry.Sha,
                State = SyncState.Pending,
                UpdatedAt = DateTimeOffset.Now
            });
            return;
        }

        var result = await _diaryRepo.SaveAsync(CurrentEntry);
        if (result.IsSuccess)
        {
            SyncState = SyncState.Synced;
            CurrentEntry.SyncState = SyncState.Synced;
            // SHA is already updated on CurrentEntry by DiaryRepository.SaveAsync.

            // Did the user keep typing while we were waiting on GitHub? If so,
            // *do not* delete the draft — re-save it as Pending against the fresh
            // SHA so the autosave/commit path picks up right where we left off.
            if (CurrentContent != committedContent)
            {
                CurrentEntry.Content = CurrentContent;
                CurrentEntry.SyncState = SyncState.Pending;
                SyncState = SyncState.Pending;
                IsDirty = true;

                await _indexedDb.SaveDraftAsync(new Draft
                {
                    Path = CurrentEntry.Path,
                    Content = CurrentContent,
                    Sha = CurrentEntry.Sha,
                    State = SyncState.Pending,
                    UpdatedAt = DateTimeOffset.Now
                });
            }
            else
            {
                await _indexedDb.RemoveDraftAsync(CurrentEntry.Path);
            }

            // A brand-new file needs to show up in the sidebar tree. Otherwise the
            // just-committed day is invisible until page reload or the next `online`
            // event. Existing files already appear (the tree entry was there); skip
            // the extra GitHub GET in that case.
            if (wasNewFile)
            {
                await RefreshEntriesAsync();
            }
        }
        else
        {
            // Classify by HTTP status: 409/422 → SHA/precondition conflict, everything else → generic failure.
            // Substring-matching English error text was fragile and broke under localization / API-message drift.
            var failedState = result.IsConflict ? SyncState.Conflict : SyncState.Failed;

            CurrentEntry.SyncState = failedState;
            SyncState = failedState;

            // Preserve whatever the user has NOW (possibly newer than committedContent),
            // not the stale committed snapshot.
            await _indexedDb.SaveDraftAsync(new Draft
            {
                Path = CurrentEntry.Path,
                Content = CurrentContent,
                Sha = CurrentEntry.Sha,
                State = SyncState.Pending,
                UpdatedAt = DateTimeOffset.Now
            });
        }
    }

    public async Task<Result<bool>> DeleteCurrentEntryAsync()
    {
        if (CurrentEntry == null) return Result<bool>.Success(true);

        var path = CurrentEntry.Path;
        var result = await _diaryRepo.DeleteAsync(CurrentEntry);
        if (result.IsFailure)
        {
            // Surface the failure to the caller (the editor) so the user sees
            // *why* the entry didn't disappear. Do NOT clear the local buffer —
            // otherwise the UI silently drops content that still lives on GitHub.
            CurrentEntry.SyncState = SyncState.Failed;
            SyncState = SyncState.Failed;
            return result;
        }

        await _indexedDb.RemoveDraftAsync(path);
        CurrentEntry = null;
        CurrentContent = "";
        IsDirty = false;
        SyncState = SyncState.Synced;

        // Drop the deleted date from the sidebar tree so it disappears immediately
        // instead of reappearing until the next tree fetch.
        await RefreshEntriesAsync();
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Drops all loaded diary state back to first-run defaults. Called on disconnect
    /// so the previous account's entry list and open entry do not survive in memory
    /// and flash on screen behind the setup wizard.
    /// <para>
    /// Assigns through the properties rather than the backing fields so each raises
    /// StateChanged and every subscribed component re-renders.
    /// </para>
    /// </summary>
    public void Reset()
    {
        CurrentEntry = null;
        Entries = new List<DiaryEntryInfo>();
        CurrentContent = "";
        IsDirty = false;
        IsLoading = false;
        SyncState = SyncState.Synced;
        SetLoadError(null, null);
    }

    public async Task RefreshEntriesAsync()
    {
        var result = await _diaryRepo.GetAllAsync();
        if (result.IsSuccess)
        {
            Entries = result.Value!;
            SetLoadError(null, null);
        }
        else
        {
            SetLoadError(result.Error, result.StatusCode);
        }
    }

    public async Task BuildSearchIndexAsync()
    {
        // Fingerprint the entry set — path + SHA per row. If nothing on the
        // GitHub side changed since the last index build, we can reuse the
        // existing SearchService index instead of re-downloading N files.
        // This is what turns "Search on a 300-entry repo" from a 30-second
        // wait into an instant hit on repeat queries.
        var fingerprint = ComputeEntryFingerprint(_entries);
        if (fingerprint == _searchIndexFingerprint) return;

        var entries = new List<DiaryEntry>();
        foreach (var info in _entries)
        {
            var result = await _diaryRepo.LoadAsync(info.Date);
            if (result.IsSuccess && result.Value != null)
            {
                entries.Add(result.Value);
            }
        }
        _searchService.BuildIndex(entries);
        _searchIndexFingerprint = fingerprint;
    }

    private string? _searchIndexFingerprint;

    private static string ComputeEntryFingerprint(List<DiaryEntryInfo> entries)
    {
        // Order-stable hash — Path uniquely identifies a diary file and Sha
        // changes on every write, so the pair is sufficient to detect any
        // relevant modification without touching content bytes.
        var sb = new System.Text.StringBuilder();
        foreach (var info in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            sb.Append(info.Path).Append(':').Append(info.Sha).Append('\n');
        }
        return sb.ToString();
    }
}
