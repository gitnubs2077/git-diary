<div align="center">

<img src="./docs/logo.svg" alt="GitDiary logo" width="128" height="128" />

# GitDiary

**A minimalist personal diary that lives inside your own GitHub repository.**

*No backend. No database. No server costs. Just you, your browser, and Git.*

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor WASM](https://img.shields.io/badge/Blazor-WebAssembly-5C2D91?logo=blazor&logoColor=white)](https://learn.microsoft.com/aspnet/core/blazor/)
[![Storage](https://img.shields.io/badge/Storage-GitHub%20API-181717?logo=github&logoColor=white)](https://docs.github.com/rest)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](#-license)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-ff69b4.svg)](#-contributing)

[English](./README.md) · [简体中文](./README.zh-CN.md)

</div>

---

## 🌟 Why GitDiary?

> *Git is your storage layer, not your backup layer.*

- 🔐 **You own your data** — every entry is a plain Markdown file in **your** repo.
- 🪶 **Zero infrastructure** — a single Blazor WASM bundle, hostable on GitHub Pages.
- 🧘 **Distraction-free** — one page, one editor, one keystroke to save.
- 🕰 **Full Git history** — every diary edit is a commit. Time-travel included, for free.
- 🚫 **No vendor lock-in** — clone the repo, `cat` your `.md` files, walk away anytime.

## ✨ Features

| | |
|---|---|
| ✍️ **Write** | Daily entries, live Markdown editing |
| 💾 **Autosave** | 2-second debounce → IndexedDB → GitHub |
| 📅 **Browse** | Navigate history by year / month / day |
| 🔍 **Search** | Instant full-text search across every entry |
| 👀 **Preview** | Toggle between raw Markdown and rendered view |
| 📶 **Offline** | Keep writing without a network; sync resumes automatically |
| ⚔️ **Conflicts** | Detected on SHA mismatch — pick *Overwrite* or *Reload* |
| 🎨 **Dark theme** | Handcrafted CSS, no Bootstrap, no heavy UI kit |

## 🏗 Architecture

```text
+---------------------+
|      Browser        |
+----------+----------+
           |
           v
+---------------------+
|  Blazor WASM App    |
+----------+----------+
           |
           +----------------+
           |                |
           v                v
+----------------+  +----------------+
| IndexedDB      |  | GitHub REST    |
| (drafts/cache) |  | (source truth) |
+----------------+  +----------------+
```

**Tech stack:** Blazor WebAssembly (.NET 10) · Markdig · Blazored.LocalStorage · GitHub REST API · IndexedDB

**Data layout in your repo:**

```text
Diary/
  2026/
    07/
      12.md   ← one Markdown file per day
```

Each file is just Markdown — no frontmatter, no proprietary schema:

```markdown
# 2026-07-12

Today I set up GitDiary. It was surprisingly easy!
```

## 🚀 How to Use

This section walks you through going from zero to writing your first entry.

### 1. Create a GitHub repository for your diary

1. Sign in to GitHub and go to **[Create a new repository](https://github.com/new)**.
2. Fill in the form:
   - **Repository name**: anything you like — `my-diary` is a good default.
   - **Visibility**: **Private** is strongly recommended (this repo will hold your personal writing).
   - **Initialize this repository with a README**: ✅ tick it, so the default branch exists.
3. Click **Create repository**.
4. Note three values you'll need shortly:
   - `Owner`   — your GitHub username or organization name
   - `Repo`    — the repository name you just chose
   - `Branch`  — usually `main`

> 💡 You can use an existing repository too. GitDiary only writes inside a top-level `Diary/` folder, so it won't collide with the rest of your files.

### 2. Generate a Fine-Grained Personal Access Token (PAT)

GitDiary talks to GitHub directly from your browser and needs a token with **write** access to that one repository.

1. Open **[Settings → Developer settings → Personal access tokens → Fine-grained tokens](https://github.com/settings/tokens?type=beta)**.
2. Click **Generate new token**.
3. Configure it:
   - **Token name**: `GitDiary`
   - **Expiration**: whatever you're comfortable with (90 days, 1 year, custom…)
   - **Repository access**: **Only select repositories** → pick the diary repo from step 1.
   - **Repository permissions**:
     - **Contents** → **Read and write** ✅ *(required)*
     - Everything else can stay `No access`.
4. Click **Generate token** and **copy the value immediately** — GitHub will never show it again.

> ⚠️ Treat this token like a password. It's stored only in your browser's `localStorage`; GitDiary never sends it anywhere except to `api.github.com`.

### 3. Connect GitDiary to your repository

1. Open GitDiary in your browser (see [Run locally](#4-run-locally-optional) below, or use your hosted deployment).
2. The **Setup Wizard** appears on first launch. Fill in:
   - **Owner** → your GitHub username
   - **Repository** → the repo you created
   - **Branch** → `main` (or whatever your default branch is)
   - **Personal Access Token** → paste the token from step 2
3. Click **Test & Save**. GitDiary will:
   - ✅ read your repo tree (verifies token + repo)
   - ✅ perform a tiny write test (verifies the `Contents: Read and write` permission)
4. Wizard closes → the editor opens on **today's** entry. Start typing!

> 🔁 Need to change repo or rotate your token later? Click the ⚙️ button in the sidebar to reopen the wizard.

### 4. Run locally (optional)

If you want to build and run GitDiary yourself:

```bash
# Prerequisites: .NET 10 SDK
cd src/GitDiary.Client
dotnet build            # 0 warnings enforced
dotnet run              # http://localhost:5016
# or, for hot reload:
dotnet watch run
```

Publish a static build (deployable to GitHub Pages / Netlify / Cloudflare Pages / any static host):

```bash
dotnet publish -c Release -o publish
# static output lives in: publish/wwwroot/
```

### 5. Daily workflow

- **Write** — open GitDiary, today's entry is already selected. Type freely.
- **Save** — happens automatically 2 seconds after your last keystroke, or press `Ctrl + S`.
- **Preview** — toggle to see the rendered Markdown.
- **Browse** — pick any past date from the left sidebar.
- **Search** — hit the search box to full-text search across every entry.
- **Offline** — lose your connection? Keep writing. The status bar shows *Pending*, and GitDiary re-syncs when you're back online.

That's it. Your diary lives in Git; you can `git clone` it, grep it, back it up, or read it in plain text forever.

## 📦 Project Structure

```text
src/GitDiary.Client/
├── Components/       # Blazor UI components
│   ├── DiaryEditor.razor
│   ├── SearchBox.razor
│   ├── SetupWizard.razor
│   ├── Sidebar.razor
│   └── StatusBar.razor
├── Infrastructure/   # Result<T>, PathHelper
├── Layout/           # MainLayout
├── Models/           # DiaryEntry, SyncState, RepositoryConfig, ...
├── Pages/            # Home.razor
├── Services/         # GitHubApiClient, DiaryRepository, SyncService, SearchService, ...
├── Stores/           # DiaryStore, SettingsStore
└── wwwroot/css/      # Custom dark-theme CSS
```

Architecture is enforced by convention:

```text
UI (Components/Pages) → Store → Repository → GitHub API
```

The UI never talks to `GitHubApiClient` directly — always through `DiaryStore`.

## 🔮 Roadmap

- **v1.1** — Tags & Favorites
- **v1.2** — Image attachments
- **v1.3** — GitHub OAuth Device Flow (no more manual PATs)
- **v2.0** — GitLab / Gitea / Forgejo support

## 🤝 Contributing

Issues and PRs are welcome. Please read `docs/prd.md` and `docs/tech-design.md` before proposing architectural changes.

## 📝 License

[MIT](./LICENSE) © GitDiary contributors
