# Changelog

All notable changes to GitDiary are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
