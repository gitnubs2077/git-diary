# Security Policy

## Threat model

GitDiary is a browser-only Blazor WebAssembly application. It has **no backend
and no server-side database**. All persistent data lives in one of three places:

1. Your GitHub repository (source of truth — Markdown files under `Diary/YYYY/MM/DD.md`).
2. Your browser's `localStorage`:
   - `gitdiary_owner`, `gitdiary_repo`, `gitdiary_branch`, `gitdiary_token`
   - `gitdiary_language`, `gitdiary_theme`, `gitdiary_sidebar_width`, `gitdiary_sidebar_expand`
3. Your browser's IndexedDB (offline draft cache — persisted per-page-origin).

The **GitHub Personal Access Token** is the sensitive value. It is transmitted
**only** to `https://api.github.com` and is otherwise never sent anywhere.

## What we defend against

- **Markdown-preview XSS.** The renderer uses a hardened Markdig pipeline that
  disables inline HTML and rewrites `javascript:` / `vbscript:` / `data:` URLs,
  backed by a `Content-Security-Policy` header that limits `connect-src` to
  `'self'` and `https://api.github.com`. A compromised entry cannot exfiltrate
  the PAT to a foreign host.
- **Log exfiltration.** GitHub error bodies routed through `Result<T>.Error`
  are replaced with short `HTTP {status} {reason}` strings before surfacing
  to the UI. Full bodies go to `console.error` with `Bearer …` and `gh?_…`
  tokens redacted.
- **Format-error confusion.** The Setup Wizard validates owner, repo, and
  token formats client-side before firing any request.
- **Silent write-permission gaps.** The Setup Wizard performs a real write
  probe (creating and deleting a namespaced `Diary/.gitdiary-test-{guid}.md`
  file) so `Contents: Read` PATs surface as errors instead of "everything
  looked fine then died on your first save".

## What we do NOT defend against

- A compromised browser extension with access to page contents.
- A compromised device where an attacker can read `localStorage` directly.
- GitHub itself. GitDiary trusts `api.github.com` and the returned SHAs.
- Non-Markdown content (attachments, images) — these aren't supported yet.

## Reporting a vulnerability

Please open a private security advisory at
<https://github.com/cholf5/GitDiary/security/advisories/new>, or if that
isn't available, email the repository owner listed on the GitHub profile.

Please include:
- A clear description of the vulnerability
- Reproduction steps or a proof-of-concept
- Which browser / OS combination you tested on
- Whether the issue is disclosed elsewhere

We aim to acknowledge reports within **7 days** and to publish a fix or a
mitigation within **30 days** for high-severity issues.

## Supported versions

Only the latest `main` and the most recent tagged release receive security
fixes. Older releases will not be patched.
