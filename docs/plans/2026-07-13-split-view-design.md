# 双栏（Split）编辑/预览模式 — 设计文档

**日期**：2026-07-13
**状态**：设计定稿，待实施
**范围**：`src/GitDiary.Client/Components/DiaryEditor.razor` 及关联 CSS / i18n

## 背景

现状：`DiaryEditor` 是二态视图 —— `Edit`（textarea）与 `Preview`（Markdown 渲染）通过 `bool ShowPreview` 切换，同时只显示其一。

用户诉求：桌面写作时希望同屏同时看到编辑与预览，减少切换成本。

设计原则：KISS。**不做**滚动同步、不做拖拽分隔条、不做实时预览刷新。

## 决策摘要

| 项 | 决定 |
|---|---|
| 视图模型 | 三态 enum `ViewMode { Edit, Preview, Split }` 替换 `bool ShowPreview` |
| 切换 UI | 段控件（segmented control），顺序：**Edit → Preview → Split** |
| 键盘快捷键 | 不加 |
| 预览刷新节奏 | 跟随现有 2s autosave 防抖（方案 A） |
| 布局 | 复用现有 `.editor-content` 的 flex 容器；Split 时两个子节点各 `flex: 1` |
| 分割线 | `border-left: 1px solid var(--border)` on `.editor-preview` |
| 响应式断点 | `@media (max-width: 1000px)` 时 Split 降级为 Edit（隐藏 Preview 列，**不改** `_viewMode`） |
| 偏好持久化 | 组件内私有 + `IJSRuntime`；storage key `gitdiary_view_mode` |
| 首次加载闪烁 | 接受（默认 Edit → 异步读回 Split 时闪 1 帧） |
| CSS 类命名 | `.btn-preview-toggle` → `.view-mode-btn` |

## §1 · 状态模型与切换 UI

```csharp
private enum ViewMode { Edit, Preview, Split }
private ViewMode _viewMode = ViewMode.Edit;
```

段控件 razor：

```razor
<div class="view-mode-toggle" role="group" aria-label="@L["editor.viewMode"]">
    <button class="view-mode-btn @(_viewMode == ViewMode.Edit ? "active" : "")"
            @onclick="() => SetViewModeAsync(ViewMode.Edit)"
            aria-pressed="@(_viewMode == ViewMode.Edit)">
        ✏️ @L["editor.edit"]
    </button>
    <button class="view-mode-btn @(_viewMode == ViewMode.Preview ? "active" : "")"
            @onclick="() => SetViewModeAsync(ViewMode.Preview)"
            aria-pressed="@(_viewMode == ViewMode.Preview)">
        👁️ @L["editor.preview"]
    </button>
    <button class="view-mode-btn @(_viewMode == ViewMode.Split ? "active" : "")"
            @onclick="() => SetViewModeAsync(ViewMode.Split)"
            aria-pressed="@(_viewMode == ViewMode.Split)">
        ⬒ @L["editor.split"]
    </button>
</div>
```

## §2 · 双栏布局与响应式降级

Razor 分支：

```razor
<div class="editor-content @(_viewMode == ViewMode.Split ? "split" : "")">
    @if (_viewMode != ViewMode.Preview)
    {
        <textarea class="editor-textarea" ...></textarea>
    }
    @if (_viewMode != ViewMode.Edit)
    {
        <div class="editor-preview markdown-body" @ref="_previewRef">
            @MarkdownContent
        </div>
    }
</div>
```

CSS 新增：

```css
.editor-content.split .editor-preview {
    border-left: 1px solid var(--border);
}

@media (max-width: 1000px) {
    .editor-content.split .editor-preview {
        display: none;
    }
}
```

`.editor-textarea` 和 `.editor-preview` 现有的 `flex: 1` 天然完成 50/50 均分。

## §3 · 偏好持久化

**Storage 契约**
- Key: `gitdiary_view_mode`
- Value: `"edit"` | `"preview"` | `"split"`
- 默认（未设置 / 非法值）：`"edit"`

**读取**（新增 `OnInitializedAsync`）：

```csharp
protected override async Task OnInitializedAsync()
{
    try
    {
        var code = await JS.InvokeAsync<string?>("localStorage.getItem", "gitdiary_view_mode");
        _viewMode = ParseViewMode(code);
    }
    catch { /* localStorage 不可用，保留默认 */ }
}

private static ViewMode ParseViewMode(string? code) => code switch
{
    "preview" => ViewMode.Preview,
    "split"   => ViewMode.Split,
    _         => ViewMode.Edit,
};
```

现有 sync `OnInitialized`（订阅事件）保持不动。

**写入**：

```csharp
private async Task SetViewModeAsync(ViewMode mode)
{
    if (_viewMode == mode) return;

    // 离开 Edit 前 flush 草稿（承接原 TogglePreview 语义）
    if (_viewMode == ViewMode.Edit && DiaryStore.IsDirty)
    {
        CancelDebounce();
        await DiaryStore.SaveDraftAsync();
    }

    _viewMode = mode;

    // 进入含预览的模式时，立即刷 snapshot（避免空白）
    if (mode != ViewMode.Edit)
    {
        _previewSnapshot = DiaryStore.CurrentContent ?? string.Empty;
    }

    try
    {
        await JS.InvokeVoidAsync("localStorage.setItem", "gitdiary_view_mode",
            mode.ToString().ToLowerInvariant());
    }
    catch { /* 忽略，内存状态已生效 */ }
}
```

## §4 · 预览重渲染与 Mermaid 触发

**快照机制**：预览读 `_previewSnapshot`，不直接读 `DiaryStore.CurrentContent`。

```csharp
private string _previewSnapshot = string.Empty;
private DateOnly? _lastSnapshotEntry;
private MarkupString MarkdownContent => RenderMarkdown(_previewSnapshot);
```

**刷新时机**：
1. `SetViewModeAsync` 进入非 Edit 模式时（立即刷）
2. `HandleStoreChanged` 检测到 `CurrentEntry.Date` 变化时（切条目立即刷）
3. `AutoSaveDraftAsync` 防抖到期时（写作过程中每 2s 刷）
4. `SaveAsync`（Ctrl+S）触发时（立即刷）

```csharp
private async void HandleStoreChanged()
{
    try
    {
        var currentDate = DiaryStore.CurrentEntry?.Date;
        if (currentDate != _lastSnapshotEntry)
        {
            _lastSnapshotEntry = currentDate;
            _previewSnapshot = DiaryStore.CurrentContent ?? string.Empty;
        }
        await InvokeAsync(StateHasChanged);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[GitDiary] DiaryEditor.HandleStoreChanged: {ex.GetType().Name}: {ex.Message}");
    }
}

private async Task AutoSaveDraftAsync()
{
    if (DiaryStore.IsDirty && DiaryStore.CurrentEntry is not null)
    {
        await DiaryStore.SaveDraftAsync();
    }
    _previewSnapshot = DiaryStore.CurrentContent ?? string.Empty;
    StateHasChanged();
}
```

**Mermaid 触发**：`RenderMermaidIfNeededAsync` 只需两处小改：

```csharp
if (_viewMode == ViewMode.Edit)   // 原 !ShowPreview
{
    _lastMermaidContent = null;
    _lastMermaidTheme = null;
    return;
}

var content = _previewSnapshot;   // 原 DiaryStore.CurrentContent ?? string.Empty
```

## §5 · i18n keys

新增 2 个 key，5 个语言文件各改一次（共 10 处）：

| Key | zh-CN | en | zh-TW | ja | ko |
|---|---|---|---|---|---|
| `editor.split` | 双栏 | Split | 分割 | 分割 | 분할 |
| `editor.viewMode` | 视图模式 | View mode | 檢視模式 | 表示モード | 보기 모드 |

> 韩语翻译需要实施时二次核对。

## 边界情况清单

| # | 场景 | 处理 |
|---|---|---|
| 1 | 首访无 localStorage key | 默认 Edit |
| 2 | localStorage 值非法 | Fallback Edit，不写回 |
| 3 | 离开 Edit 时有未提交草稿 | flush 到 IndexedDB（承接旧 TogglePreview 逻辑） |
| 4 | Split 下切换日记条目 | `_lastSnapshotEntry` 追踪，snapshot 立即刷 |
| 5 | Split + 窄屏（<1000px） | CSS 层降级为 Edit，`_viewMode` 保持 Split |
| 6 | 窗口宽度动态变化 | CSS `@media` 自动响应，无 JS 参与 |
| 7 | Preview / Split 下 mermaid 渲染 | `_viewMode != Edit` 覆盖旧 `ShowPreview` 判断，`_previewSnapshot` 保证节流 |
| 8 | Ctrl+S | 现有 `HandleInput` 不变；`SaveAsync` 内追加 snapshot 更新 |
| 9 | 空条目（`CurrentEntry is null`） | 段控件在 `if (CurrentEntry is not null)` 之内，天然不渲染 |

## 待删除 / 迁移

- 删除字段 `bool ShowPreview`
- 删除方法 `TogglePreview`（逻辑并入 `SetViewModeAsync`）
- CSS 类 `.btn-preview-toggle` 重命名为 `.view-mode-btn`（同时保留 `:hover` 高亮的 accent 色行为）

## 非目标（YAGNI）

- 滚动同步
- 拖拽分隔条 / 可调栏宽
- 独立预览防抖（例如 300ms）
- 键盘快捷键
- 双栏在窄屏下的备选布局（如上下栈）
- 从 index.html 抢跑读 localStorage 避免首帧闪烁

## 实施顺序（建议）

1. i18n（5 个 JSON 文件加 2 key）
2. Razor 结构（段控件 + 双分支渲染）
3. CSS（新增 `.view-mode-toggle` / `.view-mode-btn` / `.editor-content.split` / `@media`）
4. C# 状态与逻辑（enum、`SetViewModeAsync`、snapshot、mermaid 引用替换、localStorage 读写）
5. 验证：`dotnet build` 0 warning、手测三种模式切换 + 窄屏降级 + mermaid 渲染 + 主题切换 + Ctrl+S

## 风险

- **Mermaid 快照与 mermaid 追踪的 tuple 一致性**：改成读 `_previewSnapshot` 后要确认 `_lastMermaidContent` 也用 snapshot 值比较，否则永远不匹配。
- **首帧闪烁的可感知度**：从 Split 用户角度会看到"1 帧 Edit → Split"。若实测明显，可考虑加 `_prefsLoaded` 标记延迟首渲染，但目前接受不加。
