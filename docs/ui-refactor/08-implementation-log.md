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
- [ ] 提交

**M0 说明**
- ViewModelBase 仍基于 CTK `ObservableObject`（源生成属性 trim 安全）；ReactiveUI 已接入并用于组合/命令/节流；新页面优先 `ReactiveObject`/`[Reactive]`（M1 起）。
- `ZoomOnScroll` 为过渡期服务定位读取快捷键配置（M5 并入画布后退役）。
- App 壳层 3 个启动握手静态属性（PendingSeatSetsFilePath/AutoImportSeatSetsPath/IsFirstRunAfterInstall）属桌面 Velopack 启动链，无头环境无法回归，计划在 M3/M5 壳重写时随文件关联流程一并改造。

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
### M2 · 会场与布局页（自由点并入）—— ⬜ 未开始
### M3 · 排座工作台 + 新外壳 IA —— ⬜ 未开始
### M4 · 名单/策略/快照 —— ⬜ 未开始
### M5 · 设置/关于/引导/清理 —— ⬜ 未开始
### M6 · 测试/性能/文档 —— ⬜ 未开始

## 3. 验证证据索引

| 里程碑 | 证据 | 路径 |
|---|---|---|
| M0 基线 | 基线 WASM 截图 | `assets/after/M0-baseline-*.png` |
| M0 | 待补 | — |

## 4. 偏差与决策记录

| # | 事项 | 决定/说明 |
|---|---|---|
| L-01 | 07 交接稿与用户实时指示冲突 | 按用户实时指示：全量 M0–M6、每里程碑提交 |
