# GitDiary Technical Design v1.0

## 1. Overview

GitDiary 是一个完全静态的 Web Application。

整个系统由 Browser + GitHub API 构成。

没有 Backend。

没有 Database。

没有任何自建 Server。

所有数据：

* 用户自己的 GitHub Repository
* Browser IndexedDB

GitHub 是唯一事实来源（Source of Truth）。

IndexedDB 仅作为本地缓存与离线编辑缓存。

---

# 2. Architecture

```text
                  +----------------------+
                  |     GitHub Pages     |
                  |   (Static Hosting)   |
                  +----------+-----------+
                             |
                             v
                 +------------------------+
                 |     Blazor WASM App    |
                 +-----------+------------+
                             |
        +--------------------+--------------------+
        |                                         |
        v                                         v
+----------------------+              +-----------------------+
|      IndexedDB       |              |   GitHub REST API     |
| Offline Cache        |              | Repository Storage    |
+----------------------+              +-----------------------+
```

---

# 3. Project Structure

```text
GitDiary/

src/
    GitDiary.Client/

Components/
Pages/
Services/
Models/
Stores/
Repositories/
Infrastructure/

wwwroot/

README.md
```

---

# 4. Layer Design

采用简单分层。

```text
UI

↓

Store

↓

Repository

↓

GitHub API

↓

Git Repository
```

禁止：

UI 直接调用 GitHub API。

---

# 5. Domain Model

## DiaryEntry

```csharp
public sealed class DiaryEntry
{
    public DateOnly Date { get; set; }

    public string Path { get; set; } = "";

    public string Content { get; set; } = "";

    public string Sha { get; set; } = "";

    public DateTimeOffset LastModified { get; set; }

    public SyncState SyncState { get; set; }
}
```

---

## SyncState

```csharp
public enum SyncState
{
    Synced,
    Saving,
    Pending,
    Conflict,
    Failed
}
```

---

## RepositoryConfig

```csharp
public sealed class RepositoryConfig
{
    public string Owner { get; set; } = "";

    public string Repo { get; set; } = "";

    public string Branch { get; set; } = "main";

    public string Token { get; set; } = "";
}
```

---

# 6. Repository Layout

固定目录：

```text
Diary/

2026/
    07/
        12.md
```

Path Rule：

```text
Diary/YYYY/MM/DD.md
```

例如：

```text
Diary/2026/07/12.md
```

以后所有版本保持兼容。

---

# 7. Markdown Format

默认：

```markdown
# 2026-07-12

今天完成了……

```

以后允许增加 YAML Front Matter。

例如：

```markdown
---
title: 2026-07-12
tags:
  - work
favorite: true
---

# Today

...
```

MVP 不解析。

仅保留兼容性。

---

# 8. Services

## GitHubApiClient

负责：

所有 REST API。

接口：

```csharp
Task<GetFileResult> GetFileAsync()

Task PutFileAsync()

Task DeleteFileAsync()

Task<List<TreeNode>> GetTreeAsync()
```

---

## DiaryRepository

职责：

封装业务。

接口：

```csharp
Task<DiaryEntry> LoadAsync(DateOnly date)

Task SaveAsync(DiaryEntry)

Task DeleteAsync(DiaryEntry)

Task<List<DiaryEntryInfo>> GetAllAsync()
```

---

## IndexedDbRepository

职责：

离线缓存。

接口：

```csharp
SaveDraft()

LoadDraft()

RemoveDraft()

ListPending()
```

---

## SearchService

职责：

全文搜索。

接口：

```csharp
Search(string keyword)
```

---

## SyncService

职责：

后台同步。

负责：

* 自动上传
* Retry
* Conflict Detection

---

# 9. GitHub REST API

仅使用：

Contents API

Git Tree API

无需 GraphQL。

---

## Load File

```http
GET

/repos/{owner}/{repo}/contents/{path}
```

---

## Save

```http
PUT

/repos/{owner}/{repo}/contents/{path}
```

Body：

```json
{
  "message": "Update diary 2026-07-12",
  "content": "...Base64...",
  "sha": "...",
  "branch": "main"
}
```

---

## Delete

```http
DELETE

/repos/{owner}/{repo}/contents/{path}
```

---

## List Files

```http
GET

/repos/{owner}/{repo}/git/trees/main?recursive=1
```

启动时读取一次。

---

# 10. Authentication

MVP：

Fine-Grained PAT。

保存：

Browser Local Storage。

后续：

支持：

GitHub Device Flow。

Authentication Provider：

```csharp
interface ICredentialProvider
{
    Task<string> GetTokenAsync();
}
```

方便以后扩展。

---

# 11. Local Storage

保存：

```text
Owner

Repository

Branch

Theme

Token
```

---

# 12. IndexedDB

Database：

```text
GitDiary
```

Stores：

```text
Drafts

Index

Settings
```

---

## Draft

```csharp
public class Draft
{
    public string Path { get; set; }

    public string Content { get; set; }

    public string Sha { get; set; }

    public SyncState State { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
```

---

# 13. Sync Flow

Online：

```text
User Input

↓

Debounce

↓

Save IndexedDB

↓

GitHub

↓

Success

↓

Update SHA
```

---

Offline：

```text
User Input

↓

IndexedDB

↓

Pending
```

---

Recovery：

```text
Network Online

↓

Sync Queue

↓

GitHub

↓

Success

↓

Synced
```

---

# 14. Conflict Detection

PUT 时：

SHA 不一致：

进入：

```text
Conflict
```

UI：

显示：

```text
Remote changed.

Reload

Overwrite
```

MVP：

不自动 Merge。

---

# 15. Search

启动：

读取：

Git Tree。

随后：

逐篇下载 Markdown。

建立：

```text
Dictionary<Path, DiaryEntry>
```

搜索：

内存完成。

MVP 不使用全文索引。

几千篇日记足够。

---

# 16. State Management

使用：

Blazor 原生。

Store：

```text
AppStore

DiaryStore

SettingsStore
```

事件：

```text
StateChanged
```

无需 Flux。

无需 Redux。

---

# 17. Components

```text
MainLayout

Sidebar

DiaryList

DiaryEditor

MarkdownPreview

SearchBox

StatusBar

SettingsDialog

SetupWizard
```

每个组件：

单一职责。

---

# 18. Auto Save

Debounce：

2 秒。

或者：

Ctrl+S。

关闭页面：

```text
beforeunload

↓

Save Draft
```

---

# 19. Error Handling

统一：

Result<T>

例如：

```csharp
Result<DiaryEntry>

Result<List<DiaryEntry>>
```

避免：

大量 Exception。

---

# 20. Logging

Development：

Console。

Production：

仅记录：

Error。

不上传任何日志。

---

# 21. Security

Token：

绝不上传。

仅：

Browser Local Storage。

所有请求：

HTTPS。

---

# 22. Performance

启动：

< 2 秒。

单篇日记：

< 100ms。

搜索：

< 50ms。

保存：

< 500ms。

---

# 23. Coding Style

* Nullable Enabled
* Implicit Usings Enabled
* Treat Warnings As Errors
* EditorConfig
* StyleCop（可选）

命名：

PascalCase。

接口：

I 开头。

异步：

全部 Async。

---

# 24. Dependencies

尽量保持极简。

推荐：

* Markdig（Markdown 渲染）
* Blazored.LocalStorage（或自行封装 JS Interop）
* 一个成熟的 IndexedDB 封装（或自行封装）

避免重量级 UI 框架，优先使用原生 HTML + CSS，仅在确有必要时引入少量组件库。

---

# 25. Testing

优先保证核心逻辑可测试。

单元测试覆盖：

* Path 生成
* Markdown 解析（未来）
* 搜索
* Sync 流程
* 冲突检测

GitHub API 通过接口 Mock，不依赖真实网络。

---

# 26. Future Extension Points

预留接口：

* IStorageProvider（GitHub / GitLab / Gitea / Forgejo）
* ICredentialProvider（PAT / Device Flow）
* IMarkdownProcessor（未来支持 Front Matter）
* IAttachmentProvider（图片、附件）

保持 UI 与存储层解耦，使未来支持其他 Git 托管平台时无需修改业务逻辑。

---

# 27. Design Principles

1. Git is the database.
2. Markdown is the file format.
3. Browser is the runtime.
4. Offline-first.
5. Local-first.
6. No backend.
7. User owns all data.
8. Simplicity over features.
9. Minimize dependencies.
10. Long-term maintainability over short-term convenience.
