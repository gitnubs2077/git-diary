# Contributing to GitDiary

Thanks for your interest! GitDiary is a small project with a clear scope, so
before opening a large PR please skim the docs and the sections below.

## Ground rules

1. **Read `docs/prd.md` and `docs/tech-design.md`** before proposing architectural
   changes. The tech design lists the layer rules that the codebase enforces by
   convention:

   ```text
   UI (Components/Pages) → Store → Repository → GitHub API
   ```

   The UI **must not** call `GitHubApiClient` directly — always go through
   `DiaryStore`.

2. **Zero warnings.** `TreatWarningsAsErrors` is on. Any new code must build
   clean with `dotnet build --nologo -clp:NoSummary`.

3. **`Result<T>` for control flow.** New code should return `Result<T>` for
   fallible operations and avoid exceptions for expected failure modes.

4. **Preserve token safety.** The GitHub PAT lives only in `localStorage`
   under `gitdiary_token` and is transmitted only to `https://api.github.com`.
   Any change that logs, transmits, or otherwise handles the token must
   redact it (`Bearer …`, `gh?_…` patterns) — see `GitHubApiClient.Redact()`.

## Local dev

Prerequisites: **.NET 10 SDK**.

```bash
cd src/GitDiary.Client
dotnet build            # 0 warnings enforced
dotnet run              # http://localhost:5016
# or, for hot reload:
dotnet watch run
```

Publish a static build:

```bash
dotnet publish -c Release -o publish
# → publish/wwwroot/
```

## Coding conventions

- Nullable enabled, implicit usings enabled.
- All async methods suffixed `Async`.
- PascalCase for all public members.
- `sealed` on service/class declarations.
- No Bootstrap, no heavy UI kits — custom CSS in `wwwroot/css/app.css`.
- `IJSRuntime` for `localStorage` access; avoid JS interop for non-storage concerns.

## Pull requests

- Keep PRs small and single-purpose.
- Explain the *why* in the description, not just the *what*.
- Include a screenshot or short video for UI changes.
- If you're adding a new i18n key, update **all five** locale files
  (`en`, `zh-CN`, `zh-TW`, `ja`, `ko`) — even if you can only provide a
  reasonable fallback for languages you don't speak. A best-effort translation
  is preferable to an English string leaking through.

## Security issues

Please **do not** open a public issue for a suspected vulnerability. See
[`SECURITY.md`](./SECURITY.md) for the disclosure process.

## Filing bugs

Include:

- Browser + version.
- Steps to reproduce.
- Console errors if any (`console.error` output — GitDiary logs redacted
  diagnostics there).
- Whether it reproduces with a fresh `localStorage`.
