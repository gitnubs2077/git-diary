using GitDiary.Client.Stores;
using Microsoft.JSInterop;

namespace GitDiary.Client.Services;

/// <summary>
/// Bridges the browser's <c>online</c> event to <see cref="SyncService.SyncPendingDraftsAsync"/>.
/// Also runs one flush at startup so drafts pending from a previous session get
/// pushed as soon as the app boots (and reachability is available).
/// </summary>
/// <remarks>
/// Registration is idempotent — safe to call <see cref="EnsureStartedAsync"/> from
/// multiple entry points (initial load, first-time setup, settings save). A single
/// <see cref="DotNetObjectReference{T}"/> is retained for the lifetime of the app;
/// the JS side guards against double-subscribing.
/// </remarks>
public sealed class OnlineSyncCoordinator : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly SyncService _sync;
    private readonly DiaryStore _store;

    private DotNetObjectReference<OnlineSyncCoordinator>? _selfRef;
    private bool _started;
    // 0 = idle, 1 = flushing. Interlocked so concurrent `online` bursts collapse.
    private int _syncing;

    public OnlineSyncCoordinator(IJSRuntime js, SyncService sync, DiaryStore store)
    {
        _js = js;
        _sync = sync;
        _store = store;
    }

    public async Task EnsureStartedAsync()
    {
        if (_started) return;
        _started = true;

        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            await _js.InvokeVoidAsync("gitdiaryOnline.register", _selfRef);
        }
        catch
        {
            // JS glue unavailable (e.g. prerender) — the initial pass below still runs.
        }

        // Initial pass — try to flush drafts that were left Pending/Failed last session.
        // Fire-and-forget: this runs concurrently with the rest of Home's boot so it
        // never blocks the first paint.
        _ = RunSyncAsync();
    }

    /// <summary>
    /// Invoked from JS when <c>window.online</c> fires.
    /// </summary>
    [JSInvokable]
    public Task OnBrowserOnline() => RunSyncAsync();

    private async Task RunSyncAsync()
    {
        // At most one active flush — if a pass is already running, coalesce.
        if (Interlocked.Exchange(ref _syncing, 1) == 1) return;
        try
        {
            await _sync.SyncPendingDraftsAsync();
            // Pull fresh SHAs / entry list after commits so the UI reflects the new state.
            await _store.RefreshEntriesAsync();
        }
        catch (Exception ex)
        {
            // Best-effort background sync — per-entry failures surface via SyncStateChanged.
            // Log the outer wrapper failure at Error so unexplained silence has a trail.
            Console.Error.WriteLine($"[GitDiary] OnlineSyncCoordinator.RunSyncAsync: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("gitdiaryOnline.unregister");
        }
        catch
        {
            // JS may already be torn down.
        }
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
