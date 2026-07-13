# Blazor WASM vs React + TS：一次因 Bug 引发的技术选型反思

> 日期：2026-07-14
> 触发：GitDiary 加"双栏（Split）视图"时踩了三个连环坑，反思是不是选错了栈
> 参与：cholf5 & ZCode
> 结论口径：这是一次带具体证据的、多轮 pushback 的技术复盘，不是营销文案

---

## 缘起

给 GitDiary（Blazor WebAssembly 单页应用，个人日记 SPA，GitHub 当后端）加"Edit / Preview / Split"三态视图时，连续踩了三个 bug：

1. **mermaid guard 遗漏 `CurrentEntry is null`** → JS 崩溃
2. **多次切换日记后右栏预览停止刷新** → snapshot 的更新时序依赖 `IsDirty` 通知顺序，被 sidebar 的重复加载路径打乱
3. **`TypeError: Cannot read properties of null (reading 'removeChild')`** → Blazor render 崩溃

修完之后，我提出了这个问题：

> **我**：这次做双栏吃了不少苦头，都是 Blazor 造成的。用 React + TS 肯定没这问题。是不是我选 Blazor 选错了？要不要用 React 重写？

下面是从这个问题一路发散出的讨论。

---

## 一、这些 bug 真的是 Blazor 的锅吗？

### ZCode 的分析

抛开表象看本质，那三个 bug 分别是：

1. **多次切换预览不刷新**：`snapshot` 的更新绑在 `IsDirty` 时序上——这是**应用层状态管理的设计 bug**，与框架无关。React 里用 `useEffect(() => setSnapshot(content), [isDirty])` 一样会踩同样的坑。
2. **mermaid guard 漏空引用检查**：普通的 null-check 漏洞，任何框架都会犯。
3. **`removeChild is null` 崩溃**：**VDOM 通病**，不是 Blazor 独有的。

第 3 条是唯一"跟框架相关"的，但它在 React 里长得几乎一模一样：

```jsx
// React 版本，同样会崩
function Preview({ markdown }) {
  const ref = useRef();
  return <div
    ref={ref}
    dangerouslySetInnerHTML={{ __html: renderMarkdown(markdown) }}
  />;
}

// 你在 useEffect 里跑 mermaid，它调 pre.replaceWith(host)
// 下次 markdown 变了 → React reconcile 尝试更新
//   dangerouslySetInnerHTML 内部的 diff
// → 走到 mermaid 抽走的节点
// → "Failed to execute 'removeChild' on 'Node':
//    The node to be removed is not a child of this node"
```

React、Vue、Solid、Svelte——**所有 VDOM/reactive 框架都有这条铁律**：

> **你不能同时让框架和第三方 JS 拥有同一块 DOM。**

这就是 `dangerouslySetInnerHTML` 名字里 "dangerously" 三个字的由来。React 官方文档明确说过：跟 D3、jQuery、mermaid 这类会 mutate DOM 的库集成时，必须用 uncontrolled ref + 手动管理——就是我们最后做的 `preview-interop.js` 那个方案。

### 阶段结论

**这次踩的坑 90% 是设计问题（应用层），10% 是 VDOM 通病（跨框架）**。没有一个是"Blazor 特有、React 里不存在"的问题。

因此结论：**不重写**。

---

## 二、那 Blazor WASM 究竟合不合适？

### 我的直觉

> **我**：我现在也发现 Blazor 冷启动真的蛮痛的，电脑上都得转 2~5 秒，尤其国内网络差。我是为了用 C# 才用 Blazor 的——我喜欢 C# 那种强类型稳定感，TS 那种半吊子强类型总是没 C# 踏实。但我觉得，Blazor 只有在能写出几万行 C# 时才值得用，业务量足够大，C# 的开发体验才划算。像 GitDiary 这样的小项目，真不值得。

### ZCode 的分析

**Blazor WASM 的成本结构是"固定成本 + 边际收益"**：

- **固定成本**：
  - ~2MB WASM runtime，冷启动 2~5s
  - 生态断裂（mermaid、富文本、图表这类库都得自己封或 interop）
  - 社区小，Stack Overflow 命中率低
  - 工具链、DevTools/HMR 体验不如 JS 生态

- **边际收益**（每写一行 C# 业务）：
  - `record`、`sealed`、pattern matching 都是正收益
  - `Result<T>`、`Task<T>`、cancellation token 语义强
  - 运行时不变式（真 nullable ref types）
  - reflection、source generators、attributes

**所以有个回本点**。5000 行业务时固定成本摊不平；50000 行时，C# 类型系统省下的时间可能远超那 2MB。

这跟"上不上 K8s"、"要不要写单元测试"是同一类**门槛问题**，不是好坏问题。

**用户的"几万行才值得"这个直觉方向对。**

---

## 三、TS 真的是"半吊子强类型"吗？

### ZCode 的辩证

**部分同意**：

- TS 类型**运行时不存在**，全靠 tsc 静态保护，`any` 一泄漏就是黑洞
- 没有真正的 nominal typing（`type UserId = string` 底层就是 string）
- 没有值类型 record，没有 sealed 语义
- 泛型 variance 更弱
- Nullable 不是一等公民（`undefined` vs `null` vs missing 混淆）

**但 TS 也有 C# 相形见绌的地方**：

- discriminated union 的自动窄化（`if (x.kind === 'ok')` 后 TS 会自动知道 `x` 是 Ok 分支）
- template literal types（`type Route = \`/api/${string}\``）
- conditional / mapped types，做类型级编程
- API 契约建模的表达力上，TS 有时比 C# 更细腻

**运行时保证**上，C# 明显赢。要"这个字段一定非 null"、"这个状态机不会漏 case"这类硬约束，C# 踏实太多。

**编译期表达能力**上，TS 未必输。

---

## 四、那 Blazor 适合做 DOM-flavor 游戏吗？

### 我的推测

> **我**：我反倒觉得 Blazor WASM 做重 UI 的游戏挺合适——游戏代码量动不动几万行 C#，正好摊平固定成本。游戏也用不到复杂的 React 生态，Blazor 贫瘠的生态够用。而且我说的是 DOM-flavor 游戏（象棋、卡牌这种），不是 canvas 类。

### ZCode 的分析

**这个想法一半对，但需要拆细**。

用户抓住了正确的一面（大代码量摊固定成本），但只在**渲染模型对得上的前提下**才成立。

Blazor 是给**表单 UI** 造的：retained-mode + 组件 + diff。你写组件、`StateHasChanged`、Blazor diff 渲染树、更新 DOM。这套跟游戏的核心工作方式**相反**——游戏是 immediate-mode + game loop，每帧从头画。

按游戏类型拆：

**Blazor WASM 合适的（DOM-flavor）**：
- 象棋 / 卡牌 / 消除 / 数独类：DOM 就够画，低帧率，组件模型天然对应"格子"、"卡牌"
- Roguelike（ASCII / tile-based DOM）：天造地设
- 视觉小说 / 文字冒险 / 互动小说：菜单 + 立绘 + 文本，Blazor 强项
- 4X / 模拟经营（大量表格、面板主导）：本质是"复杂 CRUD + 数值模拟"，Blazor 挺合适

**Blazor WASM 不合适的（canvas-flavor）**：
- 走 canvas / WebGL / WebGPU 的动作类：平台跳跃、FPS、Bullet Hell
- 高帧率 / 低延迟输入敏感：格斗、音游
- 大量粒子 / 物理模拟：JS interop 频次爆炸

真要用 C# 做浏览器游戏，其实**有更对口的工具**：

- **Unity WebGL**：C# 全套 + 为游戏专造（渲染、物理、动画、音频、输入），但 runtime 20MB+，冷启动更痛
- **Godot + C#**：能导出 HTML5，比 Unity 轻，C# 在 web 端还不算最成熟
- **MonoGame** / **Stride**：更小众

行业里几乎没人拿 Blazor 做正经浏览器游戏——**这个"没人做"本身就是信号**。不是没人试过，而是它设计上不是为这个造的。

### 阶段结论

**代码量大 ≠ Blazor 合适，还得看工作负载类型跟框架架构假设是否对得上**。

- DOM-flavor 游戏（回合制、文字、UI-heavy 策略）：Blazor 可以，甚至比 Unity 更爽
- canvas-flavor 游戏：换 Unity WebGL / Godot，别跟工具较劲

---

## 五、那 React + TS 是"终极解决方案"吗？

### 我的极端化

> **我**：这么看下来，Blazor 几乎没什么用啊，尤其跟 React 相比。以后不管是 GitDiary 这种小项目还是 DOM-flavor 游戏，都用 React + TS，这应该是终极解决方案。

### ZCode 的强硬 pushback

**慢——你跳到了另一个极端**。"终极解决方案"这四个字在工程决策里是危险信号，见着基本都得多想两秒。

React + TS 是浏览器应用的好**默认选项**，但不是万能。

#### Blazor 真正的杀手锏（React + TS 永远做不到的）

1. **后端已经是 ASP.NET Core**
   最大的一条。Blazor 可以用**项目引用**跨前后端共享同一个 C# 类——不是 DTO 复制，不是 OpenAPI codegen，**同一个 `.cs` 文件被两边都编译**。改一个字段前后端一起动。React + TS 永远做不到，只能写两遍或走 codegen 管道。中大型项目里这个优势非常凶。

2. **深度依赖 .NET 库**
   已有 5 万行 C# 业务，或需要 NuGet 包（复杂解析、加密、PDF、Excel、领域计算）没有 JS 等价物。重写代价 = 灾难。Blazor 让你直接用。

3. **运行时不变式关键**
   金融、审计、合规、医疗、生命科学——那种"这个字段绝对不能为 null / 这个 discriminator 绝对不能漏 case"的场景。C# 是**运行时保证**，TS 全是 compile-time，`any` 泄漏就裸奔。

4. **企业内部工具 / Blazor Server**
   不对外、团队都是 .NET、冷启动无所谓——Blazor Server 秒开、后端渲染、SignalR 实时、共享服务端状态。这类场景 React + TS 反而更折腾。

5. **值类型 / 零分配 / 高性能业务**
   `record struct`、`readonly struct`、`Span<T>`——真值语义、零堆分配。TS 全部 heap，GC 抖动。大量数值计算时体感差异明显。

#### React + TS 自己的坑（用户可能低估的）

- **JS 生态是跑步机**：webpack → vite → turbopack、Redux → MobX → Zustand → Jotai → Valtio、CRA 死了、Next 每半年一大改。你的 5% 时间常年在追工具链
- **TS 类型运行时不存在**：真要在边界验证数据，还得再上 zod / valibot / io-ts
- **React 本身也有心智负担**：stale closure、effect 依赖数组、re-render 触发条件、Server Components——不是傻瓜级简单
- **peer dependency 地狱**：`npm install` 常态 20 行 warning，`--legacy-peer-deps` 是标配
- **JSX 不是 TS**：JSX 里的类型错误经常不如纯 TS 清晰
- **性能优化是专门学问**：`useMemo` / `useCallback` / React.memo 何时用、React DevTools profiler 怎么看——认真调优是门手艺

---

## 综合结论

### 什么时候用 React + TS

- **个人 SPA 小项目 / 无 .NET 后端**（比如 GitDiary 这类）
- **DOM-flavor 小游戏 / 个人娱乐项目**
- **纯前端团队，不涉及 .NET 后端**
- **强依赖 npm 生态**（富文本 Tiptap、图表 Recharts、拖拽 dnd-kit 等）
- **重视首屏加载和冷启动**（面向 C 端用户，尤其国内网络）

### 什么时候用 Blazor WASM

- **有 ASP.NET Core 后端 + 项目引用共享模型是巨大生产力**
- **已有大量 C# 业务代码或 NuGet 依赖**
- **企业内部工具、Line-of-Business 应用、冷启动不敏感**
- **业务对运行时不变式要求高（金融、审计、合规）**
- **代码量预期 > ~50000 行 C# 业务逻辑**
- **团队 C# 深、JS 浅**

### 什么时候用其他方案

- **canvas / 高帧率游戏** → Unity WebGL / Godot
- **服务端渲染 + 富交互** → Blazor Server / Next.js
- **极致强类型 + 小 bundle** → F# + Fable / Elm
- **性能极致 + 手动内存管理** → Rust + WASM / C++ + Emscripten

### 三条元原则

1. **没有"终极解决方案"**。每个技术栈都是一组固定的取舍，选对了某个 workload，另一个 workload 上它就是负担。
2. **"某某语言/框架是终极方案"和"某某必然被淘汰"是同一个思维陷阱**——都属于把复杂决策一维化。
3. **判断技术选型看两条**：
   - workload 的核心工作方式是否匹配框架的架构假设（retained vs immediate、SPA vs SSR、CRUD vs 数值密集…）
   - 项目预期规模能不能摊平框架的固定成本

---

## 附：GitDiary Split 视图这次踩的三个具体坑

留作技术笔记，未来做类似特性时对号入座：

### 1. mermaid guard 遗漏 `CurrentEntry is null`

```csharp
// 修复前：只检查了 viewMode
if (_viewMode == ViewMode.Edit) return;

// 修复后：ElementReference 可能是 default，先确认 mount
if (_viewMode == ViewMode.Edit || DiaryStore.CurrentEntry is null) return;
```

**教训**：`@ref` 的 `ElementReference` 在对应 DOM 元素未 mount 时是 default 值，传给 JS interop 会崩。任何用 `@ref` 的地方都要考虑"元素可能不在 DOM 里"这个情况。

### 2. Snapshot 更新绑在 `IsDirty` 时序上（应用层设计 bug）

**症状**：多次切换日记后，右栏预览不再刷新。

**根因**：原本 snapshot 只在 `HandleStoreChanged` 里 `if (!IsDirty)` 时更新。但 `Sidebar.SelectEntry` 会 `await LoadEntryAsync` 然后触发 `OnEntrySelected`，`Home.OnEntrySelected` **又调一次** `LoadEntryAsync`——每次点侧栏加载两次。加上 `CurrentContent` setter 里的 `if (_currentContent != value)` 相等就 no-op 的守卫，第二次加载常常静默无通知，导致原本假设的"最后一次通知一定是 `IsDirty=false 且新内容`"不成立。

**修法**：换判据。真正的信号是"用户此刻在不在敲键"——用 `_debounceCts != null` 精确表达：

```csharp
private string PreviewSource =>
    _debounceCts is not null
        ? _previewSnapshot                              // 打字中：pin 快照
        : (DiaryStore.CurrentContent ?? string.Empty);  // 空闲：走 live
```

**教训**：状态刷新的触发条件应该建立在**语义清晰的信号**上，不要绑在多个 setter 的通知时序上。多个通知的顺序永远比你想象的脆弱。

### 3. Blazor render 崩溃 `Cannot read properties of null (reading 'removeChild')`

**根因**：mermaid-interop 里 `fresh[i].pre.replaceWith(host)` 把 `<pre><code>` 从 DOM 里抽走。Blazor 的 render 树还留着这些节点的引用。切日记时 `MarkupString` 值变了，Blazor 内部帧尝试 `parent.removeChild(oldNode)`，但 `oldNode.parent` 已经为 null → 崩。

**修法**：预览容器 innerHTML 交给 JS 管，Blazor 只拥有空的外壳 `<div>`：

```html
<!-- Razor：Blazor 只管外壳 -->
<div class="editor-preview" @ref="_previewRef"></div>
```

```csharp
// C# 侧：显式 push HTML
await JS.InvokeVoidAsync("gitdiaryPreview.setHtml", _previewRef, html);
// 再跑 mermaid，它在 JS 管辖的 DOM 里怎么改都不影响 Blazor
await JS.InvokeVoidAsync("gitdiaryMermaid.render", _previewRef, theme);
```

**教训（VDOM 铁律）**：**不能让框架和第三方 JS 拥有同一块 DOM**。要么全给框架管，要么开一块"框架不 diff"的区域给 JS 独占。React 的 `dangerouslySetInnerHTML` + ref 模式、Vue 的 `v-html`、Blazor 的 `MarkupString`——都有这个陷阱。

---

*完*
