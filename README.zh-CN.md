<div align="center">

# 📔 GitDiary

**一个把日记直接存进你自己 GitHub 仓库的极简写作工具。**

*没有后端。没有数据库。没有服务器成本。只有你、浏览器和 Git。*

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor WASM](https://img.shields.io/badge/Blazor-WebAssembly-5C2D91?logo=blazor&logoColor=white)](https://learn.microsoft.com/aspnet/core/blazor/)
[![Storage](https://img.shields.io/badge/Storage-GitHub%20API-181717?logo=github&logoColor=white)](https://docs.github.com/rest)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](#-开源协议)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-ff69b4.svg)](#-参与贡献)

[English](./README.md) · [简体中文](./README.zh-CN.md)

</div>

---

## 🌟 为什么选 GitDiary？

> *Git 是存储层，而不是备份层。*

- 🔐 **数据完全属于你** — 每一篇日记都是 **你自己仓库** 里的一个 Markdown 文件。
- 🪶 **零基础设施** — 一个 Blazor WASM 静态包，可直接部署到 GitHub Pages。
- 🧘 **心无旁骛** — 一个页面、一个编辑器、一次按键即保存。
- 🕰 **完整 Git 历史** — 每次编辑就是一次 commit，天然自带时光机。
- 🚫 **无供应商锁定** — 随时可以 `git clone` 拿走、`cat` 出全部内容、彻底离开。

## ✨ 功能一览

| | |
|---|---|
| ✍️ **写作** | 每日一篇，实时 Markdown 编辑 |
| 💾 **自动保存** | 2 秒防抖 → IndexedDB → GitHub |
| 📅 **浏览** | 按 年 / 月 / 日 快速翻阅 |
| 🔍 **搜索** | 全文即时搜索 |
| 👀 **预览** | Markdown 源码与渲染视图一键切换 |
| 📶 **离线** | 断网也能继续写，网络恢复后自动同步 |
| ⚔️ **冲突处理** | SHA 不一致时提示 *覆盖远端* 或 *重新加载* |
| 🎨 **深色主题** | 手写 CSS，不引入 Bootstrap 等重型 UI 框架 |

## 🏗 架构

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
| (草稿/缓存)    |  | (数据源)       |
+----------------+  +----------------+
```

**技术栈：** Blazor WebAssembly (.NET 10) · Markdig · Blazored.LocalStorage · GitHub REST API · IndexedDB

**仓库中的数据结构：**

```text
Diary/
  2026/
    07/
      12.md   ← 每天一个 Markdown 文件
```

每个文件就是一段普通 Markdown，没有 frontmatter，也没有私有格式：

```markdown
# 2026-07-12

今天搭好了 GitDiary，比想象中容易。
```

## 🚀 如何使用

从零开始到写下第一篇日记的完整步骤。

### 1. 创建一个用于存放日记的 GitHub 仓库

1. 登录 GitHub，打开 **[新建仓库](https://github.com/new)**。
2. 填写表单：
   - **Repository name**（仓库名）：随意，`my-diary` 是个不错的默认值。
   - **Visibility**（可见性）：强烈建议 **Private**，毕竟是私人日记。
   - **Initialize this repository with a README**：✅ 勾选，这样默认分支立即存在。
3. 点击 **Create repository**。
4. 记下稍后要用到的三个信息：
   - `Owner`  — 你的 GitHub 用户名或组织名
   - `Repo`   — 刚才起的仓库名
   - `Branch` — 通常是 `main`

> 💡 你也可以复用已有仓库。GitDiary 只往顶层 `Diary/` 目录里写文件，不会污染仓库其他内容。

### 2. 生成 Fine-Grained Personal Access Token（PAT）

GitDiary 是在浏览器里直接调用 GitHub API 的，因此需要一个对目标仓库拥有**写权限**的 token。

1. 打开 **[Settings → Developer settings → Personal access tokens → Fine-grained tokens](https://github.com/settings/tokens?type=beta)**。
2. 点击 **Generate new token**。
3. 配置：
   - **Token name**（名称）：`GitDiary`
   - **Expiration**（有效期）：按需选择（90 天、1 年、自定义……）
   - **Repository access**：选 **Only select repositories**，然后勾选第 1 步创建的仓库。
   - **Repository permissions**：
     - **Contents** → **Read and write** ✅ *（必需）*
     - 其他保持 `No access` 即可。
4. 点击 **Generate token**，**立刻复制** token 字符串 —— GitHub 只显示一次。

> ⚠️ Token 与密码等价。它只保存在你浏览器的 `localStorage` 里，GitDiary 除了 `api.github.com` 之外不会发送到任何地方。

### 3. 把 GitDiary 连接到你的仓库

1. 在浏览器里打开 GitDiary（本地运行方式见下方 [本地运行](#4-本地运行可选)，也可以用你部署好的地址）。
2. 首次访问时会弹出 **Setup Wizard**，依次填写：
   - **Owner** → GitHub 用户名
   - **Repository** → 上一步创建的仓库名
   - **Branch** → `main`（或你的默认分支）
   - **Personal Access Token** → 粘贴第 2 步的 token
3. 点击 **Test & Save**，GitDiary 会：
   - ✅ 读取仓库 tree（验证 token + 仓库是否可访问）
   - ✅ 进行一次极小的写入测试（验证 `Contents: Read and write` 权限是否到位）
4. 向导关闭 → 直接进入 **今天** 的日记，开始写吧！

> 🔁 想更换仓库或轮换 token？点击左侧栏的 ⚙️ 按钮即可重新打开向导。

### 4. 本地运行（可选）

如果你希望自己构建并运行 GitDiary：

```bash
# 前置：.NET 10 SDK
cd src/GitDiary.Client
dotnet build            # 强制 0 warning
dotnet run              # http://localhost:5016
# 或者启用热重载：
dotnet watch run
```

发布静态站点（可部署到 GitHub Pages / Netlify / Cloudflare Pages / 任意静态托管）：

```bash
dotnet publish -c Release -o publish
# 静态产物位于：publish/wwwroot/
```

### 5. 日常使用

- **写作** — 打开 GitDiary，今天的日记已自动选中，直接开始输入。
- **保存** — 停止输入 2 秒后自动保存，也可以 `Ctrl + S` 手动触发。
- **预览** — 在 Markdown 源码与渲染视图之间切换。
- **浏览** — 在左侧栏点击任意历史日期。
- **搜索** — 使用搜索框对全部日记进行全文搜索。
- **离线** — 断网也能继续写，状态栏会显示 *Pending*，网络恢复后自动同步。

就这么简单。你的日记就住在 Git 里，可以 `git clone`、`grep`、备份、或以纯文本永远读下去。

## 📦 项目结构

```text
src/GitDiary.Client/
├── Components/       # Blazor UI 组件
│   ├── DiaryEditor.razor
│   ├── SearchBox.razor
│   ├── SetupWizard.razor
│   ├── Sidebar.razor
│   └── StatusBar.razor
├── Infrastructure/   # Result<T>、PathHelper
├── Layout/           # MainLayout
├── Models/           # DiaryEntry、SyncState、RepositoryConfig 等
├── Pages/            # Home.razor
├── Services/         # GitHubApiClient、DiaryRepository、SyncService、SearchService 等
├── Stores/           # DiaryStore、SettingsStore
└── wwwroot/css/      # 手写深色主题 CSS
```

分层约束（约定式，不由编译器强制）：

```text
UI (Components/Pages) → Store → Repository → GitHub API
```

UI 永远不直接调用 `GitHubApiClient`，一律通过 `DiaryStore`。

## 🔮 后续规划

- **v1.1** — 标签 & 收藏
- **v1.2** — 图片附件
- **v1.3** — GitHub OAuth Device Flow（不再手动填 PAT）
- **v2.0** — 支持 GitLab / Gitea / Forgejo

## 🤝 参与贡献

欢迎提 Issue 和 PR。涉及架构改动前请先阅读 `docs/prd.md` 与 `docs/tech-design.md`。

## 📝 开源协议

[MIT](./LICENSE) © GitDiary contributors
