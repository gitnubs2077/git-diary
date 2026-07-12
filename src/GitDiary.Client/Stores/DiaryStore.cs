using GitDiary.Client.Models;
using GitDiary.Client.Services;

namespace GitDiary.Client.Stores;

public sealed class DiaryStore : StoreBase
{
    private readonly DiaryRepository _diaryRepo;
    private readonly IndexedDbRepository _indexedDb;
    private readonly SearchService _searchService;

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

    public DiaryStore(DiaryRepository diaryRepo, IndexedDbRepository indexedDb, SearchService searchService)
    {
        _diaryRepo = diaryRepo;
        _indexedDb = indexedDb;
        _searchService = searchService;
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

    public async Task DeleteCurrentEntryAsync()
    {
        if (CurrentEntry == null) return;
        await _diaryRepo.DeleteAsync(CurrentEntry);
        await _indexedDb.RemoveDraftAsync(CurrentEntry.Path);
        CurrentEntry = null;
        CurrentContent = "";
        IsDirty = false;
        SyncState = SyncState.Synced;
    }

    public async Task RefreshEntriesAsync()
    {
        var result = await _diaryRepo.GetAllAsync();
        if (result.IsSuccess)
        {
            Entries = result.Value!;
        }
    }

    public async Task BuildSearchIndexAsync()
    {
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
    }
}
