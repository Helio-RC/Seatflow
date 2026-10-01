# 04 · HTML 高保真样机（React/Vite）

> 阶段 4 交付物 · 2026-10-01 · 状态：待审核（审核门 4）
> 依据：阶段 3 决策 —— 视觉方向 B「方格纸」、新 IA 提案通过。
> 位置：`html-sample/`；截图：`assets/html-sample/`；设计稿：`direction-mocks/`（阶段 3）。

## 1. 运行方式（无头环境已验证）

```bash
cd docs/ui-refactor/html-sample
pnpm install          # 首次
./node_modules/.bin/vite --host 0.0.0.0 --port 8080   # 当前展示端口：8080
# 浏览器访问 http://localhost:8080/
```

> 环境注意：pnpm 11 默认拦截 esbuild 安装脚本；已通过 `package.json > pnpm.onlyBuiltDependencies`
> 声明。若 `pnpm dev` 因 deps 检查失败，可直接用 `./node_modules/.bin/vite` 启动（当前环境即如此运行）。
> 深链接参数：`?page=<workbench|members|venues|strategies|snapshots|settings|about>&theme=<light|dark>&lang=<zh|en>`。

## 2. 覆盖范围（对应验收要求）

| 维度 | 实现情况 |
|---|---|
| 全部页面 | ✅ 7 个新 IA 页面全部实现（排座工作台 / 人员名单 / 会场与布局 / 策略配置 / 历史快照 / 设置 / 关于） |
| 导航与 IA | ✅ 侧栏分组 + 底部常驻；默认入口=排座工作台；「自由点」以 Freeform 布局类型内嵌于「会场与布局」 |
| 对话框 | ✅ 通用模态（未保存修改确认 / 回滚确认）；导出菜单弹出 |
| 座位交互 | ✅ 点击两座交换；拖拽学生到座位；拖拽座位交换；固定座位标记；选中态；图例 |
| 会场参数联动 | ✅ 输入触发 **120ms 防抖**重算预览（含「重算中…」状态），演示 RC-1 的设计对策 |
| 明暗主题 | ✅ B 方向浅色为主 + 暗色令牌变体（设置页切换，深链接 `theme=dark` 可用） |
| 中英切换 | ✅ 导航/标题/主要命令/字段标签（设置页切换） |
| 数据规模 | ✅ 240 学生名单表格可编辑（含搜索过滤） |
| 快照 | ✅ 列表 / 预览 / 回滚确认 / 批量删除模式（勾选） |
| 策略 | ✅ 列表（独立/依赖层级标识）、启停、优先级步进、冲突提示、参数与配置块区 |
| **移动/窄屏模式** | ✅ ≤900px 自动切换：导航（左上按钮）自左侧抽屉；右上按钮呼出的抽屉（数据/面板/数据集/会场/策略/快照）**统一从右侧弹出** + 遮罩，点击遮罩关闭 |
| **大教室画布** | ✅ 300 座大教室：画布区自动滚动；`Ctrl/⌘+滚轮` 缩放（40%–200%）；拖拽空白处平移；缩放条（−/＋/100%/适应窗口）；自然尺寸不受 transform 影响 |
| **三种布局预览** | ✅ 会场页预览支持 Grid / **Polar（环+中心，参数联动）** / **Freeform（坐标散点）**；真实实现中 Polar/Freeform 的全部参数与交互为必修项（样机为简化演示） |
| 表单可访问性 | ✅ 输入控件补充 `name`/`aria-label`，控制台无警告 |

## 3. 截图索引（桌面 1440×900，移动 420×860）

| 文件 | 内容 |
|---|---|
| `01-workbench.png` | 排座工作台（浅色，方向 B；默认入口） |
| `02-members.png` | 人员名单（240 行、可编辑、搜索） |
| `03-venues.png` | 会场与布局（Grid 参数 + 防抖预览 + Freeform 内嵌） |
| `04-strategies.png` | 策略配置（优先级/启停/冲突提示） |
| `05-snapshots.png` | 历史快照（列表 + 预览 + 回滚/批量删除） |
| `06-settings.png` | 设置（主题/语言/快捷键/存储） |
| `07-about.png` | 关于（版本与依赖） |
| `08-workbench-dark.png` | 工作台暗色变体 |
| `09-members-en.png` | 名单页英文界面 |
| `10-workbench-large-fit.png` | 300 座大教室 · 适应窗口 |
| `11-workbench-large-zoom.png` | 300 座大教室 · 118% 缩放（可滚动） |
| `12-mobile-workbench.png` | 移动模式 · 工作台（顶栏左导航 / 右数据·面板） |
| `13-mobile-drawer-data.png` | 移动模式 · 「数据」抽屉（会场/名单） |
| `14-mobile-drawer-panel.png` | 移动模式 · 「面板」抽屉（策略/未分配/记录/消息） |
| `15-mobile-nav-drawer.png` | 移动模式 · 「导航」抽屉（分组导航） |
| `16-mobile-members-list.png` | 移动模式 · 名单页「数据集」抽屉（右侧） |
| `17-venues-polar.png` | 会场与布局 · 极坐标预览（4 环 × 12 座 + 中心） |
| `18-venues-freeform.png` | 会场与布局 · 自由布局散点预览 + 坐标表 |

## 4. 移动/窄屏行为（≤900px 触发）

- 布局：单列；**导航抽屉自左侧滑入（左上按钮）**；**数据选择、检查器、各页列表等由右上按钮呼出的抽屉统一自右侧滑入**（`position: fixed` + `translateX`），
  配半透明遮罩；点击遮罩或切换页面自动关闭。已用 `getBoundingClientRect` 断言右侧锚定（`right == innerWidth`）。
- 顶栏 `CompactBar`：左上「☰ 导航」，居中页面标题，右上按页呈现上下文按钮——
  工作台「数据 / 面板」、名单「数据集」、会场「会场」、策略「策略」、快照「快照」。
- 已实测：抽屉开合、页面切换自动收起、桌面 ↔ 移动断点切换（截图 12–16）。

## 5. 大教室画布导航（可滚动 + 可拖动 + 缩放）

- 画布容器 `overflow: auto`（滚动条）；`Ctrl/⌘+滚轮` 缩放 40%–200%；`−/＋/100%/适应窗口` 按钮；
  空白处按住拖拽平移（`pointer capture`，座位交互优先，不误触）。
- 实现要点：`board` 用 `transform: scale()`（`transform-origin: top left`）缩放，外层 `canvas-stage`
  以**自然尺寸 × zoom** 撑开滚动区域；自然尺寸用 `offsetWidth`（不受 transform 影响）测量。
- 浏览器内断言实测：68% → 118% 缩放生效；`scrollWidth 1155 > clientWidth 698` 可滚动；
  拖拽 120px → `scrollLeft` 精确 +120。

## 6. 与原型的差异与已知简化

- 座位生成/回滚为**前端模拟**（无真实策略引擎）；导出为提示性交互。
- 「虚拟化」以固定行高 + 滚动容器演示（240 行足够表达目标；真实实现用 `VirtualizingStackPanel`）。
- 引导（onboarding）未在原型中实现——流程与控件目标已由阶段 3 §2.1 映射，真实实现阶段纳入。
- i18n 覆盖主界面骨架，非全量文案（真实实现走 `.resx`）。

## 7. 设计验证记录（过程证据）

- 截图审查发现并修复了一个样式缺陷：`tokens.css` 首行误用 `//` 注释（非法 CSS），导致浏览器丢弃 `:root` 令牌、仅暗色规则生效——已改为 `/* */` 并重新截图验证。**说明像素级截图审查有效**。
- 名单模拟数据最初仅 20 个唯一姓名（大面积重复），已改为三段式组合生成，提升评审真实性。
- 原型为真实 DOM：chrome-devtools MCP 的 a11y 快照可直接列出交互元素（uid 点击可用），便于后续自动化审查。

## 8. 与真实 Avalonia 实现的映射要点（阶段 5/6 输入）

- `src/tokens.css` → `Resources/Tokens/*.axaml`（`ThemeDictionaries` Light/Dark）；
- `app.css` 中的组件类 → `Styles/Components.axaml` 中的 `ControlTheme`/样式类；
- 页签检查器 → `TabControl`；防抖预览 → `IDisposable` 计时器或响应式 `Throttle`（阶段 5 裁决）；
- 拖拽交互 → Avalonia 12 `DataTransfer`（现有 `SeatingArrangementView` 已有实现可复用改造）。
