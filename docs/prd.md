# GitDiary PRD

## 1. 项目概述

### 项目名称

GitDiary（暂定）

### 项目定位

一个基于 GitHub 仓库存储的极简个人日记系统。

用户通过浏览器访问网页即可编写、浏览、搜索和管理日记，所有数据以 Markdown 文件形式存储在用户自己的 GitHub Private Repository 中。

系统本身不提供任何后端服务，不存储用户数据，不依赖数据库。

### 核心理念

* Git 是存储层，而不是备份层
* Markdown 是唯一数据格式
* 用户完全拥有自己的数据
* 无 Vendor Lock-in
* 无服务器成本
* 可长期维护
* 支持离线编辑

---

# 2. 产品目标

## 目标用户

主要面向：

* 程序员
* 独立开发者
* Git 重度用户
* Markdown 用户
* 希望掌控自己数据的人

## 产品目标

让用户能够：

* 快速记录日记
* 方便浏览历史日记
* 全文搜索历史内容
* 离线编辑
* 自动同步到 GitHub
* 保留完整 Git 历史

---

# 3. MVP 范围

首个版本必须实现：

## 写日记

支持：

* 创建今日日记
* 编辑日记
* 自动保存
* 手动保存

文件格式：

```text
Diary/
  2026/
    07/
      12.md
```

示例：

```markdown
# 2026-07-12

今天完成了 GitDiary 的设计。

感觉 Git 作为存储层很适合个人工具。
```

---

## 浏览日记

支持：

* 年份列表
* 月份列表
* 日期列表
* 打开指定日记

---

## 删除日记

支持：

* 删除指定日记
* 删除前二次确认

---

## 搜索

支持：

* 标题搜索
* 内容搜索

范围：

* 当前仓库全部日记

---

## Markdown

支持：

* Markdown 原文编辑
* Markdown 预览模式

无需支持：

* 所见即所得编辑器

---

## GitHub 同步

支持：

* 读取文件
* 创建文件
* 更新文件
* 删除文件

---

## 离线编辑

支持：

* 无网络情况下继续编辑
* 自动保存到本地

恢复网络后：

* 自动同步

---

# 4. 非目标

首个版本不实现：

* 多人协作
* 评论
* 分享
* 标签系统
* 图片上传
* 附件上传
* 云数据库
* GitLab 支持
* OneDrive 支持
* 手机 App
* Electron 桌面版

---

# 5. 技术架构

## 技术栈

Frontend:

* Blazor WebAssembly (.NET 10)

Storage:

* GitHub Repository

Hosting:

* GitHub Pages

Data Format:

* Markdown

Authentication:

* GitHub Fine-Grained PAT

Local Cache:

* IndexedDB

---

## 架构图

```text
+---------------------+
|     Browser         |
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
| IndexedDB      |  | GitHub API     |
+----------------+  +----------------+
```

---

# 6. Repository 要求

用户需要准备一个 GitHub Repository。

推荐：

```text
my-diary
```

可选：

* Public
* Private

推荐：

* Private

---

# 7. 首次配置流程

首次访问：

显示 Setup Wizard。

配置：

## GitHub Repository

```text
Owner:
theogogh

Repository:
my-diary

Branch:
main
```

## GitHub Token

输入：

```text
Fine-Grained Personal Access Token
```

要求权限：

```text
Repository:
Only selected repositories

Permission:
Contents

Read and Write
```

保存到：

```text
Browser Local Storage
```

配置完成后进入主页。

---

# 8. 页面设计

## 主界面

布局：

```text
+--------------------------------------------------+
| GitDiary                                         |
+----------------+---------------------------------+
|                |                                 |
| 日期列表       | Markdown Editor                 |
|                |                                 |
| 2026-07-12     |                                 |
| 2026-07-11     |                                 |
| 2026-07-10     |                                 |
|                |                                 |
+----------------+---------------------------------+
| Save | Preview | Search                          |
+--------------------------------------------------+
```

---

## 左侧区域

显示：

* 年份
* 月份
* 日期

默认：

按时间倒序。

---

## 右侧区域

显示：

当前日记内容。

支持：

* 编辑
* 保存
* 预览

---

# 9. 自动保存

触发条件：

停止输入 2 秒。

执行：

```text
Save Draft
```

保存位置：

```text
IndexedDB
```

同时：

尝试同步 GitHub。

---

# 10. 同步机制

## 在线

保存：

```text
User Edit
    ↓
IndexedDB
    ↓
GitHub API
```

---

## 离线

保存：

```text
User Edit
    ↓
IndexedDB
```

状态：

```text
Pending Sync
```

---

## 网络恢复

自动执行：

```text
Pending Drafts
      ↓
Sync
      ↓
GitHub
```

---

# 11. 冲突策略

MVP 采用简单策略。

假设：

* 同一篇日记被多个浏览器修改

同步时：

发现 SHA 不一致。

提示：

```text
Conflict Detected

Choose:

1. Overwrite Remote
2. Reload Remote
```

不实现自动 Merge。

---

# 12. 搜索实现

启动时：

读取：

```text
Git Tree
```

获取全部 Markdown 文件。

建立本地索引。

支持：

* 标题搜索
* 内容搜索

搜索实时响应。

---

# 13. 状态提示

编辑器顶部显示：

```text
Saved
Saving...
Offline
Syncing...
Sync Failed
Pending Sync
```

---

# 14. 数据目录规范

固定结构：

```text
Diary/
  2026/
    07/
      12.md
```

路径格式：

```text
Diary/YYYY/MM/DD.md
```

示例：

```text
Diary/2026/07/12.md
```

---

# 15. 开源要求

License:

MIT

Repository:

GitDiary

要求：

* 支持自部署
* 支持 Fork
* 无第三方付费依赖

---

# 16. 后续版本规划

V1.1

* 标签(Tag)
* 收藏(Favorite)

V1.2

* 图片附件

V1.3

* GitHub OAuth Device Flow

V2.0

* GitLab 支持
* Gitea 支持
* Forgejo 支持

---

# 17. 成功标准

用户完成首次配置后：

1. 打开网页
2. 自动进入今天的日记
3. 开始输入
4. 自动保存
5. 自动同步 GitHub

整个过程无需安装软件，无需数据库，无需服务器。

用户的数据始终以 Markdown 文件形式保存在自己的 Git 仓库中。
