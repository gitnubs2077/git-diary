# GitDiary — Workspace Guide

## Overview

GitDiary is a **Blazor WebAssembly** (.NET 10) single-page application for personal diary writing. All entries are stored as Markdown files in a GitHub repository via the REST API. No backend, no database. Runs entirely in the browser.

Key docs to read before making architectural changes:
- `docs/prd.md` — Product requirements
- `docs/tech-design.md` — Technical design & architecture rules

## Project Structure

```
src/GitDiary.Client/
├── Components/        # Blazor UI components
├── Infrastructure/    # Utilities (Result<T>, PathHelper)
├── Layout/            # MainLayout
├── Models/            # Domain models (DiaryEntry, SyncState, etc.)
├── Pages/             # App pages (Home.razor)
├── Services/          # Business logic & GitHub API
├── Stores/            # State management
└── wwwroot/css/       # Custom dark-theme CSS
```

## Build & Run

```bash
cd src/GitDiary.Client
dotnet build            # Build (0 warnings enforced)
dotnet run              # Start dev server at http://localhost:5016
dotnet watch run        # Hot reload

cd ../..
dotnet test tests/GitDiary.Tests   # Security regression suite — see below
```

Dependencies: `Markdig` (Markdown render), `Blazored.LocalStorage`.

## Security Invariants

The GitHub PAT is stored in plaintext `localStorage`. There is no backend and no
other place to put it, so **any script execution on this origin is a total
compromise of the user's diary repo**. Three things stand between diary content and
that outcome. All three are easy to break by accident, so none of them are left to
reviewer vigilance:

1. **The Markdown pipeline is minimal on purpose.** `Infrastructure/SafeMarkdown.cs`
   is the single trust boundary — its HTML goes to `innerHTML`, bypassing Blazor's
   escaping. Adding `UseAdvancedExtensions()` to that pipeline is a one-line, total
   XSS hole (it enables attribute injection: `# h {onclick="alert(1)"}`), and
   `DisableHtml()` does **not** save you from it. `tests/GitDiary.Tests/SafeMarkdownTests.cs`
   fires ~25 payloads at it and asserts the trap explicitly.

2. **The CSP has no `script-src 'unsafe-inline'`.** This is the layer that contains
   an XSS rather than merely limiting it. Consequence: **never add an inline
   `<script>` to `wwwroot/index.html`** — it will be blocked at runtime. Put it in
   `wwwroot/js/` and reference it by `src`. The one unavoidable inline script is
   Blazor's generated import map, allowed via a sha256 hash that
   `.github/scripts/pin_importmap_csp.py` recomputes on every publish. A manual
   `dotnet publish` **must** run that script or the app will not boot.

3. **`img-src` has no bare `https:`.** An image beacon is not governed by
   `connect-src`, so a `https:` wildcard there silently re-opens PAT exfiltration to
   any host and makes the `connect-src` allowlist decorative.

`deploy.yml` re-checks 2 and 3 against the published output and fails the deploy if
either regressed. Don't work around that check — it is the last line of defense.

Also: `GitHubApiClient.Redact()` scrubs tokens from anything bound for
`console.error`. If a new token format appears, it must be added there **and** to
`SetupWizard.ValidateConfig` together — a format the wizard accepts but the redactor
cannot match is exactly the one that leaks.

## Git Workflow

**Single-branch project.** All commits go directly on `main`. Do **not** create feature branches, do not open PRs, do not fast-forward from a side branch. This overrides any agent-harness default that says "branch first when on the default branch" — this repository is a personal single-user app and that ceremony adds no value.

Still applies: only commit or push when the user asks.

## Architecture Rules

**Layer constraints** (enforced by convention, not compiler):
```
UI (Components/Pages) → Store → Repository → GitHub API
```
- UI **must not** call `GitHubApiClient` directly — always go through `DiaryStore`.
- Use `Result<T>` for all error handling; avoid exceptions for control flow.

**DI registrations** (Program.cs):
- `GitHubApiClient`, `DiaryRepository`, `DiaryStore`, `SyncService` → `AddScoped`
- `IndexedDbRepository`, `SearchService`, `SettingsStore` → `AddSingleton`
- Scoped services effectively act as singletons in WASM (single user per app instance).

## Data Path

```
Diary/YYYY/MM/DD.md   — Markdown file per day in the GitHub repo
```

`PathHelper` generates/parses these paths. Always use `PathHelper` — never hardcode.

## Sync Flow

User input → 2s debounce → `DiaryStore.SaveCurrentEntryAsync` → IndexedDB draft → GitHub PUT (Contents API). If offline, draft stays `Pending` in IndexedDB; `SyncService.SyncPendingDraftsAsync` retries on reconnect.

Conflict: SHA mismatch on PUT → UI shows "Conflict Detected" → user picks Overwrite or Reload.

## Key Behaviors

- **Token persistence**: Config (owner, repo, branch, token) stored in `localStorage` under `gitdiary_*` keys.
- **Disconnect**: `SetupWizard` → "Disconnect this diary" → `Home.OnDisconnect`. This is the only way to remove a PAT from a browser, so it must erase **everything**: the four config keys, the `gitdiary_drafts` blob (which holds diary *text*, not just the credential), the in-memory `GitHubApiClient` token, and `DiaryStore` state. Erase persistent storage *before* clearing in-memory state: a still-configured app with no stored token is recoverable, a "logged out" app with the token still on disk is the bug this exists to prevent.
- **Setup Wizard**: `SetupWizard.razor` on first visit or via ⚙️ button in sidebar. Tests both read (`GetTreeAsync`) and write (`TestWriteAccessAsync`) before allowing save.
- **Autosave**: 2-second debounce after last keystroke. Also saves on Ctrl+S.
- **Search**: `SearchService` builds an in-memory dictionary from Git tree + content downloads on startup. Simple `string.Contains` matching on title and content.
- **Offline**: `IndexedDbRepository` holds drafts in memory (persisted via JS interop stubs — not fully implemented).

## Coding Conventions

- Nullable enabled, implicit usings enabled.
- All async methods suffixed `Async`.
- PascalCase for all public members.
- `sealed` on service/class declarations.
- No Bootstrap, no heavy UI frameworks — custom CSS in `app.css` (dark theme).
- `IJSRuntime` for localStorage access; avoid JS interop for non-storage concerns.

## Gotchas

- **GitHub 404 for write**: If the PAT has only Read permission, `GetTreeAsync` succeeds but `CreateFileAsync` returns 404 (GitHub hides 403). The write test in SetupWizard catches this.
- **SHA persistence**: After creating/updating a file, the new SHA from the API response is stored on the `DiaryEntry` object. Missing SHA → subsequent updates cause conflicts.
- **`IndexedDbRepository` is not IndexedDB.** Despite the name, it persists drafts as a single JSON blob in `localStorage` under `gitdiary_drafts` — it is not in-memory-only, and the persistence is not stubbed (this entry used to claim both). It matters because that blob contains diary text, which is why `ClearAllAsync()` exists and why disconnect calls it.
