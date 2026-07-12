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
    /// Push the current entry to GitHub. On success, the local draft is discarded.
    /// Invoked explicitly by the Commit button.
    /// </summary>
    public async Task CommitCurrentEntryAsync()
    {
        if (CurrentEntry == null) return;

        // Snapshot latest buffer into the entry and into the local draft first,
        // so nothing is lost if the network call fails.
        CurrentEntry.Content = CurrentContent;
        await _indexedDb.SaveDraftAsync(new Draft
        {
            Path = CurrentEntry.Path,
            Content = CurrentContent,
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
            await _indexedDb.RemoveDraftAsync(CurrentEntry.Path);
        }
        else
        {
            // Keep the draft around so the user can retry later.
            var failedState = result.Error?.Contains("sha", StringComparison.OrdinalIgnoreCase) == true
                ? SyncState.Conflict
                : SyncState.Failed;

            CurrentEntry.SyncState = failedState;
            SyncState = failedState;

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
