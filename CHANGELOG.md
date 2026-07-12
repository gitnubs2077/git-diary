# Changelog

All notable changes to GitDiary are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Sidebar tree now refreshes automatically after a **new day's first commit** —
  no more page-reload to see the new date appear.
- Sidebar tree now refreshes after **delete** so the removed entry disappears
  immediately.
- **Delete failures are no longer silent.** `DeleteCurrentEntryAsync` returns
  a `Result<bool>` and the editor keeps the modal open on failure, flips the
  status bar to Failed, and logs the API error. Previously a 5xx would clear
  the UI while the entry stayed on GitHub.
- Rapid double-click on **Commit**, **Delete-confirm**, and **Save Config**
  can no longer fire two GitHub round-trips (in-flight guards short-circuit
  the second re-entry before Blazor re-renders `disabled`).
- Delete-modal initial focus now lands on the **destructive button** itself,
  not on the wrapper `<div>` — keyboard users can confirm with Enter directly.
- OS **`prefers-color-scheme` listener** is now unregistered on `ThemeService`
  dispose. Previously it kept a closure over a disposed `DotNetObjectReference`
  and threw on the next OS-level theme flip.
- `SyncService` and `OnlineSyncCoordinator` catches now log at
  `console.error` instead of swallowing silently.
- Boot IIFE in `index.html` gracefully degrades when `window.matchMedia`
  is missing (very old WebViews); the theme + shortcut + net-status bridges
  no longer all disappear because of a single unsupported API.

### Performance

- **Search index is cached** by `path:sha` fingerprint. Re-opening Search
  after a Sidebar/Editor round-trip no longer re-downloads every diary file;
  the index is rebuilt only when the tree actually changed.
- `SearchService.Search` no longer allocates two lowercase copies of every
  diary body per query — `Contains(..., OrdinalIgnoreCase)` already handles
  case folding.

## [1.0.0] — 2026-07-12

Initial public release.

### Added

- **Diary editor** with 2-second debounced autosave to IndexedDB and
  explicit-commit push to GitHub via the Contents API.
- **Setup Wizard** with client-side format validation for `owner` / `repo` /
  `token`, plus a real write probe (namespaced under
  `Diary/.gitdiary-test-{guid}.md`) that catches Read-only PATs before the
  first entry is written.
- **Sidebar** with year/month/day navigation, today's-entry highlight,
  and expand-state persistence in `localStorage`.
- **Search** built from an in-memory tree + content index.
- **Preview** toggle with a hardened Markdig pipeline (inline HTML disabled;
  `javascript:` / `vbscript:` / `data:` URLs rewritten). Backed by a
  `Content-Security-Policy` that restricts `connect-src` to `'self'` and
  `https://api.github.com`.
- **Offline banner** in the status bar. Drafts stay `Pending` in IndexedDB
  and are flushed on `window.online`.
- **PWA support** with a service worker that pre-caches the shell using
  `Promise.allSettled` (a single asset 404 no longer aborts install) and a
  manifest with separate `any` + `maskable` icon entries.
- **Theme switcher** — System / Light / Dark, applied before first paint.
- **Sidebar resizer** — drag to resize, double-click to reset, persisted.
- **i18n** — English, 简体中文, 繁體中文, 日本語, 한국어. `<html lang>` stays
  in sync with the active language.
- **Keyboard** — global `Ctrl/Cmd+S` for local draft save, works from any
  focused element.
- **beforeunload guard** — the browser confirms navigation while there are
  unsaved changes.
- **Word count** in the status bar.
- **Delete-entry modal** with proper `role="dialog"`, `aria-modal`, initial
  focus, and Escape-to-dismiss.

### Security

- All GitHub error bodies routed through `Result<T>.Error` are replaced with
  short `HTTP {status} {reason}` strings before surfacing to the UI. Full
  bodies go to `console.error` with `Bearer …` and `gh?_…` tokens redacted.
- Setup Wizard validates PAT / owner / repo formats before firing requests.
- Sanitizing Markdig pipeline strips inline HTML and unsafe URL schemes.

### Build

- `TreatWarningsAsErrors` on — the release gate rejects any new warning.

[Unreleased]: https://github.com/cholf5/GitDiary/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/cholf5/GitDiary/releases/tag/v1.0.0
