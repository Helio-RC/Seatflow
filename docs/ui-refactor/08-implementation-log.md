# 08 · 实施日志（Implementation Log）

> 用途：记录 M0–M6 实施进度、验证证据、决策与偏差。上下文压缩后以本文件为续跑基准。
> 工作树：`.worktrees/ui-refactor` · 分支：`refactor/ui-overhaul` · 开始：2026-10-01

## 0. 本轮执行约定（用户实时确认，覆盖 07 交接稿）

| 项 | 决定 | 来源 |
|---|---|---|
| 范围 | **全量实施 M0–M6**（07 交接稿「只做 M0–M1」不适用） | 用户本轮问答 |
| 节奏 | **连续执行到 DoD**，仅阻塞/需决策时暂停 | 用户本轮问答 |
| 提交 | **每个里程碑一个提交**（07 交接稿「不提交」不适用） | 用户本轮问答 |
| 门 6 | 视为已批准 | 用户本轮问答 |
| 硬约束 | Avalonia 全家族 12.1.3；ReactiveUI.Avalonia 12.1.5；普通 UserControl；全量编译绑定；清静态状态；数据兼容 | 用户本轮指示 + 05/06 |

## 1. 环境取证（2026-10-01）

- .NET SDK 10.0.401；wasm-tools 已安装（10.0.112/10.0.100）。
- 无头（无 DISPLAY）；CDP `localhost:3000` 在线（HeadlessChrome 153）。
- HTML 样机服务 8080 在线；WASM 站点需发布（8090）。
- NuGet 可访问；ReactiveUI.Avalonia 12.1.5 / Avalonia 12.1.3 / Avalonia.Headless.XUnit 12.1.3 可还原。
- 基线：`dotnet build` 通过；`dotnet test` 381 通过（待本轮复核）。

## 2. 里程碑进度

### M0 · 基座 —— ✅ 实施完成，验收中
- [x] 步 0：docs 提交（b7e292c）+ 基线 tag `ui-refactor-baseline` + 基线 WASM 烟测（截图 `assets/after/M0-baseline-*.png`，控制台零错误）
- [x] Avalonia 12.1.3（Presentation/Desktop/Browser 全家族）+ ReactiveUI.Avalonia 12.1.5；双壳 `.UseReactiveUI(_ => { })`
- [x] 方向 B 令牌：`Resources/Tokens/{Colors,Spacing,Typography}.axaml` + `Tokens/MotionTokens.cs`；`Styles/Components.axaml`（sf-* 类）；App.axaml 挂接并覆盖 Fluent SystemControl*/SystemAccentColor*
- [x] 横切服务：`IPageLifecycle` / `IDialogGate`(+DialogGate) / `IBusyScope`(+BusyScope) / `DirtyTracker` / `NullDialogService`
- [x] `Controls/SeatingCanvas.cs` + `SeatVisual.cs`（渲染骨架 + 快照驱动重绘；命中/拖拽/缩放留给 M1）
- [x] 静态状态清除：ViewModelBase.Dialog→构造注入（15 个 VM + 3 个叶级 VM 改造）、KeyboardShortcutHandler→DI 单例、FileDropHandler→DI 单例、OnboardingService 4 静态字段→实例、WatchdogService._dialog→构造注入；删除 `MainWindowViewModel`（未用）与 `AnimateCardBounceAsync` 死代码
- [x] 构建双 TFM 0 警告 0 错误；`dotnet test` 381/381
- [x] WASM 实机验证（待补：明暗截图）
- [x] 提交（4ea146f）

**M0 说明**
- ViewModelBase 仍基于 CTK `ObservableObject`（源生成属性 trim 安全）；ReactiveUI 已接入并用于组合/命令/节流；新页面优先 `ReactiveObject`/`[Reactive]`（M1 起）。
- `ZoomOnScroll` 为过渡期服务定位读取快捷键配置（M5 并入画布后退役）。
- App 壳层 3 个启动握手静态属性（PendingSeatSetsFilePath/AutoImportSeatSetsPath/IsFirstRunAfterInstall）属桌面 Velopack 启动链，无头环境无法回归，计划在 M3/M5 壳重写时随文件关联流程一并改造。

### M2 · 会场与布局页（自由点并入）—— ✅ 完成（已验收）
- [x] 「自由点管理」并入单页：Grid / Polar / Freeform 三布局完整编辑（参数 + 坐标表增删改 + CSV/JSON 导入导出 + 拖放导入），独立页/导航/DI/page_navigation/引导 pageGuides 全部移除（`grep FreeformManagement src` 零残留）
- [x] 预览改用 `SeatingCanvas`（单控件自绘）；`BuildPreviewSnapshot()` 输出 `SeatVisual`/`BoardOverlay` 快照，支持讲台/门/禁用座位
- [x] 参数变更 **120ms 去抖**（ReactiveUI `Throttle` + `PreviewRevision` 单一入口），替代原 ~40 个 `OnXxxChanged` 直接重建
- [x] 生命周期：实现 `IPageLifecycle`（构造器不再 fire-and-forget；View Loaded/Unloaded 桥接）；脏检查统一 `DirtyTracker`；`CanLeaveAsync` 三选保留
- [x] 座位 ID 保留：Grid/Polar 按位置复用、Freeform 按点 ID 复用（顺带修复旧自由点页保存丢 ID 的问题）
- [x] 兼容性：OnboardingService 依赖公开成员逐条保留（仅 `PreviewSeats/PreviewOverlays.Clear()` → `PreviewSnapshot = null` 两行同步）
- [x] 验收证据：`assets/after/M2-venues*.png`（三布局编辑器、预览、脏状态）；trace `M2-venue-*.json.gz`

**M2 性能复测（同 02 §C 口径：15 次 +1，WASM 软件渲染）**
| 场景 | 基线（旧页） | 修复前（新页首测） | 修复后 |
|---|---|---|---|
| 行数 +1 ×15：INP | 315ms | 165ms | **79ms** ✅（≤100ms） |
| 行数 +1 ×15：长任务 | 47 个 / 最长 684ms | 48 个 / 最长 564ms | **18 个 / 最长 149ms** |
| 水平间距 +1 ×15：INP | — | — | **33ms**，长任务最长 80ms |
- 关键修复：过道选项由「整表替换 ObservableCollection」（每次步进重建全部 CheckBox，WASM 下数百 ms）改为**去抖 + 增量同步**（仅增删差异项）；典型单次变更 ~65–80ms、INP 79ms 达标；极端增长（8→23 行）下仍有 128/149ms 两次尖峰，严格 ≤50ms 未完全达成（无头软件渲染口径，桌面推断显著更优，M6 复测）。

### M1 · 自绘画布与几何内核 —— ✅ 完成（已验收）
- [x] `SeatingCanvas` 完整实现：渲染（座位/覆盖物/状态色/选中/悬停/失效）、指针命中、内部拖拽（座位↔座位 / 座位→垃圾桶）、Ctrl+滚轮缩放（锚点保持）、空白拖拽平移、键盘方向键虚拟焦点 + Enter 激活
- [x] 渲染性能优化（关键）：按状态合批 `GeometryGroup`（单次 DrawGeometry）、文本 FormattedText 缓存 + **文本矢量化几何**（渐进构建，缩放/平移零重建）、视口裁剪、手势期间 LOD（停绘文本/描边，200ms 后补全）、网格几何缓存
- [x] 接入排座页：替换两层 ItemsControl+Canvas 与 CanvasZoomPan/ZoomOnScroll；code-behind 重写为事件桥接（SeatClicked/SeatActivated/SeatDropped + 外部拖放命中）；移除 Popup 拖拽卡片（消除泄漏）
- [x] 三布局完整支持：Grid（行列+过道）/ Polar（环+角度）/ Freeform（坐标散点），统一由 Core `SeatGeometryHelper` 换算
- [x] seed 工具扩展：修正演示会场 `VerticalSpacing=1.0` 缺陷；新增 Polar(24)/Freeform(30)/大教室(300)/超大教室(800) 五个会场；`cdp.mjs` 增加自动建页与 `size` 命令（R-01 缓解）
- [x] 验收证据：`assets/after/M1-canvas-{grid,polar,freeform,300}.png`、`M1-swap-source/done.png`、`M1-trash-drop.png`、`M1-zoom-in.png`；trace `assets/after/traces/M1-spike-300*.json.gz`

**Spike #2 结论（300 座，WASM + 软件渲染，同 02 口径）**
| 指标 | 优化前（本轮首测） | 优化后 | 目标 |
|---|---|---|---|
| 稳态单帧渲染 | ~230ms | **3.6–8.7ms** | <16ms ✅ |
| 最长长任务 | 392ms | **84.3ms** | ≤100ms ✅ |
| 长任务总时长 | 5552ms | **3104ms** | 前后对比 ✅ |
| INP | 350ms | **129ms** | ≤100ms（贴近，软件渲染口径 ⚠️） |

**M1 偏差与说明**
- 800 座实机采样因浏览器频繁重置未完成；工程依据：低缩放下标签阈值自动跳过（44px×fitZoom<18px），工作量为 800 矩形合批（≤300 座含 240 文本的实测上限），M6 性能复测时补采。
- 无头软件 WebGL 使 Paint/Commit/INP 偏悲观（02 §1 口径），桌面端推断显著优于此。

### M0 验收补充
- [x] WASM 实机：9 页导航零控制台错误；明/暗主题切换（`prefers-color-scheme` 模拟）截图 `assets/after/M0-light-*.png`、`M0-dark-home.png`

### M3 · 排座工作台 + 新外壳 IA —— ✅ 完成（验收中）

**范围交付**
- [x] 新外壳 IA：侧栏分组（工作流/资料/规则/记录 + 底部设置/关于）；中部导航改为 `ScrollViewer` + 固定底栏（修复 I-01 矮视口覆盖）
- [x] Home 移除：默认入口=排座工作台（`PageKey` 删除 Home；`NavigationService`/`App.axaml.cs`/双壳 DI/`page_navigation.json` 同步；`HomeViewModel/HomeView` 删除）；欢迎语/快捷链接/RELEASE 压缩为工作台空态欢迎卡（新增 `WelcomeCardViewModel`）
- [x] ≤900px 紧凑断点：导航左抽屉 + 上下文右抽屉（数据/面板）+ 遮罩点击关闭 + 切页自动收起；断点状态经新增 `IShellLayoutService`（桌面/WASM 同源）
- [x] 切页即时化：删除 200ms 淡出 + 100ms 间隔；内容同步切换
- [x] 工作台三栏：左「数据选择」（会场/名单 + 显式刷新）、中 `SeatingCanvas`、右检查器 `TabControl`（策略/未分配/记录/消息，短标签 + 计数）
- [x] 命令栏：撤销/重做、保存快照、导出菜单（学生/教师 × Excel/CSV/PDF/PNG；Web 隐藏 PDF/PNG，并修复此前学生视角未隐藏 PDF/PNG 的问题）、生成/创建空白
- [x] 生命周期：`SeatingArrangementViewModel` 实现 `IPageLifecycle`（View Loaded/Unloaded 桥接；构造器不再 fire-and-forget；保留 `InitializationTask` 供引导）；离开取消在途生成/加载
- [x] 缓存：`ViewLocator` 按 VM 实例弱引用缓存 View（Build 命中 0ms）；工作台会场/名单列表进入不重复加载 + 显式刷新（`InvalidateData` 由引导清理、工作台拖放、设置页/系统文件关联导入 `App.RefreshAfterImportAsync` 触发）
- [x] `IFileDropHandler`：`.seatsets` 拖放导入职责由 Home 迁至工作台
- [x] 引导最小重映射：`OnboardingService` 起点/中转页/完成兜底从 Home 改为排座工作台；首启实机验证不崩
- [x] i18n：新增导航分组/紧凑外壳/页签短标签等 14 键；`Seating_MemberData` 数值调整为「名单」
- [x] 编译绑定：排座页/Home 无 `ReflectionBinding`（剩余 `SnapshotHistoryView` 两条属 M4）

**M3 性能复测（WASM + 软件渲染，1200×800，5–8 次切页采样；口径同 02）**
| 项 | 结果 |
|---|---|
| 切页 INP（首访页面） | 569ms（会场页首访，含 View 构造 ~200ms）⚠️ |
| 切页 INP（重复切换、视图已缓存） | 230–350ms ⚠️（基线 493ms，约 -30%~-50%） |
| 切页最长长任务 | 343ms ⚠️（目标：无 >100ms） |
| `ViewLocator.Build` 耗时 | 首建 187–200ms；缓存命中 0ms |
| 归因（临时打点） | `setVm`（ContentPresenter 重新挂载整棵页面树：样式/绑定激活 + 首次 XAML 构造）为唯一大头；壳层自身逻辑 <15ms（setPage 2–13ms） |
| 结论 | 壳层即时化目标达成（无强制延迟）；INP/长任务未达 ≤100ms，属页面内容挂载成本（名单/策略/快照页尚未重构同样拖累），转入 M4–M6（页面视图常驻/分区懒加载、Snapshot Transient 取消等） |

**验收证据**
- 截图：`assets/after/M3-workbench-empty.png`、`M3-workbench-generated.png`、`M3-compact-workbench.png`、`M3-compact-picker-drawer.png`、`M3-compact-nav-drawer.png`、`M3-compact-inspector-drawer.png`
- trace：`assets/after/traces/M3-switch-trace.json.gz`
- 实机交互（CDP）：选会场/名单→生成；座位交换；保存快照；撤销/重做；导出菜单（Web 隐藏 PDF/PNG）；右栏页签；紧凑模式三抽屉 + 遮罩；首启引导（1/9 出场、关闭确认、完成跳关于）不崩

**M3 评审修复（OpenCode 独立评审后）**
- [x] Blocker：设置页/系统关联导入 `.seatsets` 未失效工作台列表缓存 → `App.RefreshAfterImportAsync` 统一 `InvalidateData()`；若导入时已停留在工作台（`NavigateTo` 跳过重进）则显式 `RefreshDataAsync()`，覆盖全部导入入口
- [x] `InitializationTask` 改用「离开换新、进入置位」的完成信号（TCS），引导注入不再可能读到旧任务；`OnboardingService` 等待加 10s 兜底超时
- [x] 生成/创建空白在页面离开取消时不再弹「生成失败」（新增 `ViewModelBase.SafeCancelableAsync`：用户取消静默）
- [x] 紧凑抽屉强制展开文字（不受桌面折叠偏好影响）；`IsDirty` 补变更通知

**M3 偏差与说明**
- 「切页 INP ≤100ms / 无 >100ms 长任务」未达成：经打点定位为页面树重新挂载成本（非动画/壳层），已在测试构建中确认（Build 缓存命中仍 230–280ms）；M4 页面缓存策略与 M6 性能复测继续收敛。
- 图标按钮在紧凑模式隐藏文字（避免 780px 溢出）；命令栏副标题在紧凑模式隐藏。
- `.seatsets` 在浏览器端仍走文件关联入口（OS 拖放不适用），与 Home 时代行为一致。
- `Classes.sf-active` 动态类绑定保留：WASM 实机截图可见导航激活高亮生效（`sf-compact` 一类的失败实例已全部改为 VM 属性绑定，无回退）。
- 评审遗留 Minors 记录待 M5/M6：Home_* 死键清理、`SnapshotHistoryView` 两条 ReflectionBinding（M4）、紧凑抽屉宽度 `min()` 视口自适应。

### M4 · 名单/策略/快照 —— ⬜ 未开始
### M5 · 设置/关于/引导/清理 —— ⬜ 未开始
### M6 · 测试/性能/文档 —— ⬜ 未开始

## 3. 验证证据索引

| 里程碑 | 证据 | 路径 |
|---|---|---|
| M0 基线 | 基线 WASM 截图 | `assets/after/M0-baseline-*.png` |
| M0 | 明/暗主题 + 九页导航 | `assets/after/M0-light-*.png`、`M0-dark-home.png` |
| M1 | 三布局画布/交换/垃圾桶/缩放 + 300 座 trace | `assets/after/M1-*.png`、`assets/after/traces/M1-spike-300*.json.gz` |
| M2 | 三布局编辑器/预览/脏状态 + 会场页 trace | `assets/after/M2-venues*.png`、`assets/after/traces/M2-venue-*.json.gz` |
| M3 | 三栏工作台/紧凑抽屉 + 切页 trace | `assets/after/M3-*.png`、`assets/after/traces/M3-switch-trace.json.gz` |

## 4. 偏差与决策记录

| # | 事项 | 决定/说明 |
|---|---|---|
| L-01 | 07 交接稿与用户实时指示冲突 | 按用户实时指示：全量 M0–M6、每里程碑提交 |
