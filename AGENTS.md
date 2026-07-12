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
```

Dependencies: `Markdig` (Markdown render), `Blazored.LocalStorage`.

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
- **No real IndexedDB persistence**: `IndexedDbRepository` stores drafts in-memory only. The JS interop layer for localStorage-backed persistence is stubbed but not wired. For full offline support, implement the JS side.
