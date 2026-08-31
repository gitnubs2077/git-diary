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
    private List<DiaryEntryInfo> _docs = new();
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

    /// <summary>Document listing (the "文档" section), newest created first.</summary>
    public List<DiaryEntryInfo> Docs
    {
        get => _docs;
        private set
        {
            _docs = value;
            NotifyStateChanged();
        }
    }

    /// <summary>One-shot hint for the editor's view mode on the next entry load
    /// ("edit" or "preview"): the sidebar sets it so "+今日" opens in Edit and picking a
    /// date opens in Preview. The editor applies and clears it. Null keeps the current
    /// (persisted) mode — e.g. on boot.</summary>
    public string? PendingViewMode { get; set; }

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

        // A PDF is binary and read-only — commit its bytes directly, no text/draft dance.
        if (CurrentEntry.IsPdf) { await CommitCurrentPdfAsync(); return; }

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
                await RefreshListForKindAsync(CurrentEntry.Kind);
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

    // Commit an uploaded PDF: push its pending bytes as a new binary file, then clear the
    // local draft/copy. PDFs are never edited, so there's no content snapshot or retry
    // buffer to manage — success flips it to Synced, failure leaves the pending copy.
    private async Task CommitCurrentPdfAsync()
    {
        var entry = CurrentEntry!;
        var wasNew = string.IsNullOrEmpty(entry.Sha);
        SyncState = SyncState.Saving;
        entry.SyncState = SyncState.Saving;
        NotifyStateChanged();

        var res = await _images.CommitPendingFileAsync(entry.Path, $"Add document {entry.Path}");
        if (res.IsSuccess)
        {
            entry.Sha = res.Value!;
            entry.SyncState = SyncState.Synced;
            SyncState = SyncState.Synced;
            IsDirty = false;
            await _indexedDb.RemoveDraftAsync(entry.Path);
            if (wasNew) await RefreshDocsAsync();
        }
        else
        {
            var failed = res.IsConflict ? SyncState.Conflict : SyncState.Failed;
            entry.SyncState = failed;
            SyncState = failed;
        }
        NotifyStateChanged();
    }

    public async Task<Result<bool>> DeleteCurrentEntryAsync()
    {
        if (CurrentEntry == null) return Result<bool>.Success(true);

        var path = CurrentEntry.Path;
        var kind = CurrentEntry.Kind;
        var isPdf = CurrentEntry.IsPdf;
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
        if (isPdf) await _images.RemovePendingFileAsync(path);
        CurrentEntry = null;
        CurrentContent = "";
        IsDirty = false;
        SyncState = SyncState.Synced;

        // Drop the deleted row from the sidebar so it disappears immediately instead of
        // reappearing until the next tree fetch (diary tree or doc list, per kind).
        await RefreshListForKindAsync(kind);
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
        Docs = new List<DiaryEntryInfo>();
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

    private Task RefreshListForKindAsync(EntryKind kind) =>
        kind == EntryKind.Doc ? RefreshDocsAsync() : RefreshEntriesAsync();

    /// <summary>
    /// Refresh the document list: committed docs from the tree, plus any documents that
    /// exist only as a local draft (created but not yet committed) so they don't vanish
    /// from the sidebar — a new doc has no other way to be reached.
    /// </summary>
    public async Task RefreshDocsAsync()
    {
        var result = await _diaryRepo.GetAllDocsAsync();

        // On a failed tree fetch (offline / rejected token) don't blank the list:
        // keep the committed docs we already had and still merge local drafts below,
        // so a just-created uncommitted doc never vanishes just because GitHub is
        // unreachable. Mirrors RefreshEntriesAsync, which leaves Entries intact on error.
        List<DiaryEntryInfo> docs;
        if (result.IsFailure)
        {
            SetLoadError(result.Error, result.StatusCode);
            docs = _docs.Where(d => !string.IsNullOrEmpty(d.Sha)).ToList();
        }
        else
        {
            SetLoadError(null, null);
            docs = result.Value!;
        }

        var committed = docs.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
        try
        {
            foreach (var draft in await _indexedDb.GetAllDraftsAsync())
            {
                if (DocPaths.IsDocPath(draft.Path) && committed.Add(draft.Path))
                {
                    var createdAt = DocPaths.ParseCreatedAt(draft.Path) ?? DateTimeOffset.MinValue;
                    docs.Add(new DiaryEntryInfo
                    {
                        Kind = EntryKind.Doc, Path = draft.Path, Sha = "", Exists = false,
                        Title = DocPaths.ParseTitle(draft.Path), CreatedAt = createdAt,
                        Date = DateOnly.FromDateTime(createdAt.LocalDateTime),
                    });
                }
            }
        }
        catch { /* drafts unavailable — show committed only */ }

        Docs = docs.OrderByDescending(d => d.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>Load a document into the editor (mirrors LoadEntryAsync for diary).</summary>
    public async Task LoadDocAsync(string path)
    {
        IsLoading = true;

        // A PDF has no text to fetch — the viewer loads the bytes itself. Build the entry
        // from the path + the list row's SHA (present once committed, empty for a draft).
        if (DocPaths.IsPdfPath(path))
        {
            var info = _docs.FirstOrDefault(d => d.Path == path);
            var sha = info?.Sha ?? "";
            var createdAt = DocPaths.ParseCreatedAt(path) ?? DateTimeOffset.Now;
            CurrentEntry = new DiaryEntry
            {
                Kind = EntryKind.Doc, Path = path, Title = DocPaths.ParseTitle(path),
                CreatedAt = createdAt, Date = DateOnly.FromDateTime(createdAt.LocalDateTime),
                Content = "", Sha = sha,
                SyncState = string.IsNullOrEmpty(sha) ? SyncState.Pending : SyncState.Synced,
            };
            CurrentContent = "";
            IsDirty = false;
            SyncState = CurrentEntry.SyncState;
            SetLoadError(null, null);
            IsLoading = false;
            return;
        }

        var result = await _diaryRepo.LoadDocAsync(path);
        if (result.IsSuccess)
        {
            var entry = result.Value!;
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
            SetLoadError(result.Error, result.StatusCode);
        }
        IsLoading = false;
    }

    /// <summary>Bytes (base64) of a PDF document for the viewer: the local pending copy
    /// while uncommitted, otherwise the committed file fetched from GitHub. Null if it
    /// can't be found.</summary>
    public async Task<string?> LoadPdfBase64Async(DiaryEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Sha))
            return await _images.LoadPendingFileBase64Async(entry.Path);
        var res = await _images.GetCommittedFileBase64Async(entry.Path);
        return res.IsSuccess ? res.Value : null;
    }

    /// <summary>
    /// Start a NEW document with the given title: build its path/content, open it in the
    /// editor as an uncommitted draft, and surface it at the top of the doc list. It is
    /// written to GitHub on the next commit. When <paramref name="content"/> is supplied
    /// (e.g. an uploaded .md file) it becomes the body verbatim; otherwise the doc opens
    /// with a "# {title}" heading.
    /// </summary>
    public async Task CreateDocAsync(string title, string? content = null)
    {
        var createdAt = DateTimeOffset.Now;
        var path = DocPaths.BuildPath(createdAt, title);
        var displayTitle = DocPaths.ParseTitle(path);
        var body = string.IsNullOrEmpty(content) ? $"# {displayTitle}\n\n" : content;

        var entry = new DiaryEntry
        {
            Kind = EntryKind.Doc, Path = path, Title = displayTitle, CreatedAt = createdAt,
            Date = DateOnly.FromDateTime(createdAt.LocalDateTime),
            Content = body, Sha = "", SyncState = SyncState.Pending,
        };

        CurrentEntry = entry;
        CurrentContent = entry.Content;
        SyncState = SyncState.Pending;
        IsDirty = true;

        await _indexedDb.SaveDraftAsync(new Draft
        {
            Path = path, Content = entry.Content, Sha = "",
            State = SyncState.Pending, UpdatedAt = createdAt
        });

        await RefreshDocsAsync();
    }

    /// <summary>
    /// Create a read-only PDF document from uploaded bytes. The binary is stored locally
    /// (pending commit) in the same encrypted store as images; a blank draft marks it as
    /// pending so it lists and survives reload. Opened in the viewer, uploaded on commit.
    /// </summary>
    public async Task CreatePdfDocAsync(string title, string base64)
    {
        var createdAt = DateTimeOffset.Now;
        var path = DocPaths.BuildPath(createdAt, title, "pdf");
        var displayTitle = DocPaths.ParseTitle(path);

        await _images.StorePendingFileAsync(path, "application/pdf", base64);
        await _indexedDb.SaveDraftAsync(new Draft
        {
            Path = path, Content = "", Sha = "", State = SyncState.Pending, UpdatedAt = createdAt
        });

        CurrentEntry = new DiaryEntry
        {
            Kind = EntryKind.Doc, Path = path, Title = displayTitle, CreatedAt = createdAt,
            Date = DateOnly.FromDateTime(createdAt.LocalDateTime),
            Content = "", Sha = "", SyncState = SyncState.Pending,
        };
        CurrentContent = "";
        SyncState = SyncState.Pending;
        IsDirty = true;

        await RefreshDocsAsync();
    }

    /// <summary>
    /// Rename the current document (its title → its filename). Not-yet-committed docs are
    /// renamed in place; committed docs are moved on GitHub (create the new file, delete
    /// the old). Images resolve via a shared Docs/assets folder, so they survive the move.
    /// No-op for diary or an unchanged title.
    /// </summary>
    public async Task<Result<bool>> RenameCurrentDocAsync(string newTitle)
    {
        if (CurrentEntry is null || CurrentEntry.Kind != EntryKind.Doc)
            return Result<bool>.Success(true);

        var oldPath = CurrentEntry.Path;
        var newPath = DocPaths.RenamePath(oldPath, newTitle);
        if (newPath is null || newPath == oldPath)
            return Result<bool>.Success(true);

        var content = CurrentContent;
        CurrentEntry.Content = content;

        if (string.IsNullOrEmpty(CurrentEntry.Sha))
        {
            await _indexedDb.RemoveDraftAsync(oldPath);
            CurrentEntry.Path = newPath;
            CurrentEntry.Title = DocPaths.ParseTitle(newPath);
            await _indexedDb.SaveDraftAsync(new Draft
            {
                Path = newPath, Content = content, Sha = "",
                State = CurrentEntry.SyncState, UpdatedAt = DateTimeOffset.Now
            });
            NotifyStateChanged();
            await RefreshDocsAsync();
            return Result<bool>.Success(true);
        }

        var created = new DiaryEntry { Path = newPath, Content = content, Sha = "" };
        var createResult = await _diaryRepo.SaveAsync(created);
        if (createResult.IsFailure)
            return createResult;

        await _diaryRepo.DeleteAsync(new DiaryEntry { Path = oldPath, Sha = CurrentEntry.Sha });
        await _indexedDb.RemoveDraftAsync(oldPath);

        CurrentEntry.Path = newPath;
        CurrentEntry.Sha = created.Sha;
        CurrentEntry.Title = DocPaths.ParseTitle(newPath);
        NotifyStateChanged();
        await RefreshDocsAsync();
        return Result<bool>.Success(true);
    }

    public async Task BuildSearchIndexAsync()
    {
        // Fingerprint the entry set — path + SHA per row. If nothing on the
        // GitHub side changed since the last index build, we can reuse the
        // existing SearchService index instead of re-downloading N files.
        // This is what turns "Search on a 300-entry repo" from a 30-second
        // wait into an instant hit on repeat queries.
        var fingerprint = ComputeEntryFingerprint(_entries) + "||" + ComputeEntryFingerprint(_docs);
        if (fingerprint == _searchIndexFingerprint) return;

        var entries = new List<DiaryEntry>();
        foreach (var info in _entries)
        {
            var result = await _diaryRepo.LoadAsync(info.Date);
            if (result.IsSuccess && result.Value != null)
                entries.Add(result.Value);
        }
        // Unified search: documents are indexed alongside diary entries.
        foreach (var info in _docs)
        {
            // PDFs are binary — index the title only (no text body to fetch/scan).
            if (info.IsPdf)
            {
                entries.Add(new DiaryEntry
                {
                    Kind = EntryKind.Doc, Path = info.Path, Title = info.Title,
                    CreatedAt = info.CreatedAt, Content = ""
                });
                continue;
            }

            var result = await _diaryRepo.LoadDocAsync(info.Path);
            if (result.IsSuccess && result.Value != null)
                entries.Add(result.Value);
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
