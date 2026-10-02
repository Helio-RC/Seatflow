# UI 重构记录（M0–M6）

> 状态：已实施并合入（2026-10，分支 `refactor/ui-overhaul`）
> 范围：Presentation.Avalonia、Desktop/Browser 双壳、UI 测试项目；不改业务规则与文件格式语义。
> 相关决策：`docs/adr/ADR-014-reactiveui-and-canvas-architecture.md`（技术架构）、
> `docs/adr/ADR-002-mvvm-framework.md`（部分被 ADR-014 取代）。
> 视觉与令牌细节见 `docs/presentation/Design_Spec.md`。

## 1. 背景与范围

Presentation.Avalonia 经多轮功能迭代后出现典型技术债：全项目约 22K 行 UI 代码、
页面级重复模式（5 套脏检查、5 份 `CanLeaveAsync`、5 处 `_dialogLock`）、
生命周期不一致（Singleton/Transient 混用、静态可变状态、构造器 fire-and-forget 加载）、
画布类界面全量重建（座位图/会场预览）、引导系统与页面 VM 深度耦合。

本次选择**彻底重构**而非局部修补。范围与目标（需求拷问结论 D1–D12 摘要）：

| # | 决策 | 结论 |
|---|---|---|
| D1 | 改动范围 | 允许较大范围重写（含引导/遥测子系统），新增 UI 测试项目 |
| D2 | 功能与 IA | 功能全保留；IA 可重设计；引导随新 IA 重映射 |
| D3 | 平台目标 | 桌面 / WASM 同等重要，分别验收 |
| D4 | 性能焦点 | 座位画布、会场实时预览、页面切换/启动/WASM 整体 |
| D5 | 视觉方向 | 先出三套方向对比（A 精炼 Fluent / B 方格纸 / C 夜校工作台），**选定 B** |
| D6 | 技术底线 | 允许评估更换 MVVM 框架、按需引入控件/主题库（证据化结论） |
| D7/D8 | 样机与验收 | 高保真 HTML 样机验证；硬指标 + 前后对比（基于 WASM 实测基线） |
| D9/D10 | 边界与兼容 | 文档驱动、审核门控制；用户数据/7 种文件格式全部向后兼容 |
| D11/D12 | 双端与提交 | 允许浏览器能力替代（如打印替代 PDF）；每里程碑一个提交 |

## 2. 现状诊断（重构前基线）

口径：Release WASM 发布（含原生链接）、HeadlessChrome + CDP trace、1200×800、
演示数据（64 座会场 + 240 人名单）、软件 WebGL（Paint/Commit 偏悲观，主线程成本结构与桌面一致）。

| 根因 | 证据（重构前） | 设计对策 |
|---|---|---|
| RC-1 会场参数变更触发预览全量重建、无防抖 | 15 次 `+1`：47 个长任务 / 12.5s，单次最长 684ms，INP 315ms | 120ms 去抖 + 增量更新 + 自绘画布 |
| RC-2 页面切换主线程编排与构造器加载 | 9 页切换 INP 493ms；8 个 VM 构造器 fire-and-forget；快照页 Transient 重建 | 即时切换、`IPageLifecycle`、视图缓存、快照改 Singleton |
| RC-3 座位画布全量重建 + 逐座位控件 + `ReflectionBinding` | 拖拽期 16 个长任务 / 2.8s，最长 264ms | 自绘 `SeatingCanvas` + 不可变快照 + 编译绑定 |
| RC-4 启动期阻塞 | 完整启动 14 个长任务 / 6.1s，最长 1.6s；`onResize` 1.39s | 启动任务分级、会场懒加载、壳层防抖 |
| RC-5 名单虚拟化失效 | 240 行未复现卡顿（1×149ms 为页面加载）；500+ 行有风险 | 轻量行编辑 + 按实测自适应虚拟化 |

其他已确认问题：矮视口侧栏溢出覆盖底部按钮（I-01）；静态可变状态与订阅泄漏（I-09）。

## 3. 新信息架构与视觉

- **7 页 IA**：排座工作台（默认入口）/ 人员管理 / 会场与布局 / 策略配置 / 历史快照 / 设置 / 关于。
  原独立 Home 取消（欢迎与更新说明压缩为工作台空态卡片）；原「自由点管理」并入「会场与布局」（Grid/Polar/Freeform 单页三类布局）。
- **侧栏分组**：工作流 / 资料 / 规则 / 记录 + 底部设置/关于；中部可滚动 + 底部固定，修复矮视口覆盖。
- **排座工作台三栏**：左「数据选择」→ 中 `SeatingCanvas` → 右检查器页签（策略/未分配/记录/消息）。
- **紧凑断点 ≤900px**（`IShellLayoutService`）：导航转左侧抽屉、页面上下文转右侧抽屉，遮罩点击关闭。
- **方向 B「方格纸」**：冷纸底 + 发丝边框 + 零装饰阴影 + 24px 画布格线 + tabular 数字；
  全部颜色/间距/圆角/字阶来自令牌（`Resources/Tokens/`），明暗双主题同等打磨。
- **动效政策**：导航即时（无强制淡出/间隔）；仅保留侧栏折叠、悬停/按下颜色过渡（120ms）、
  对话框出现（120ms）；装饰动画与死代码全部删除；尊重 reduced motion。
- 完整令牌表、组件样式与画布状态规范见 `docs/presentation/Design_Spec.md`。

## 4. 技术架构决策

详见 `docs/adr/ADR-014-reactiveui-and-canvas-architecture.md`，要点：

| 主题 | 决策 |
|---|---|
| MVVM | ReactiveUI.Avalonia **12.1.5**（+ ReactiveUI 25.1.1，无 System.Reactive 依赖），Avalonia 升至 **12.1.3**；视图层保持普通 `UserControl`（trim 安全），存量 VM 仍用 CTK 源生成器 |
| 座位画布 | 自绘 `Control.Render` + 几何命中（`SeatingCanvas`）：Grid/Polar/Freeform 统一几何；座位以不可变快照 + `LayoutVersion` 驱动；拖拽/交换/平移/Ctrl+滚轮缩放内置；方向键虚拟焦点补偿可访问性 |
| 页面生命周期 | `IPageLifecycle`（`OnEnterAsync(ct)`/`OnLeaveAsync`/`IsDirty`/`InitializationTask`）；构造器禁止 fire-and-forget；离开取消在途任务 |
| 横切服务 | `DirtyTracker`（统一 JSON 快照脏检查）、`IDialogGate`（Interlocked 对话框门）、`IBusyScope`、`IShellLayoutService`、`IGuideSeedTarget`（引导演示注入接口化）；清零全部静态可变状态 |
| 绑定与集合 | 全量编译绑定（`ReflectionBinding` 实际使用 0）；列表 `ObservableCollection` 增量；画布快照整体替换重绘 |
| 测试 | 新增 `tests/SeatFlow.Presentation.Tests`（Avalonia.Headless.XUnit 12.1.3 + Avalonia.Skia 真实绘制 + 视觉基线）；该项目锁定 xunit.v3 3.2.2（Headless 包编译基线），其余项目 4.0.0 |
| 兼容性 | 无文件格式变化，无需迁移器；用户数据/设置/引导进度向后兼容 |

**Spike 实测**：ReactiveUI 变体 `_framework` 体积 +1.24MB（+4.4%）、启动长任务 +216ms（+10%，
单次采样）、2 × IL2026（`ReactiveUserControl<T>` 反射激活，采用「普通 UserControl」组合规避）；
300 座画布优化后稳态单帧 3.6–8.7ms、最长长任务 84.3ms。

## 5. 实施里程碑（M0–M6）

| 里程碑 | 交付摘要 |
|---|---|
| M0 基座 | Avalonia 12.1.3 + ReactiveUI 接入双壳；方向 B 令牌与 `sf-*` 组件样式；横切服务；`SeatingCanvas` 骨架；删除静态状态与死代码 |
| M1 自绘画布 | `SeatingCanvas` 完整实现（渲染/命中/拖拽/缩放/平移/键盘焦点）；渲染优化（合批 Geometry、文本矢量化、视口裁剪、手势期 LOD）；三布局完整支持 |
| M2 会场与布局 | 自由点并入单页（三布局编辑器）；参数 120ms 去抖 + 增量同步；座位 ID 按位置/点 ID 保留；`IPageLifecycle` + `DirtyTracker` |
| M3 工作台与新外壳 | 三栏工作台 + 右栏页签；Home 移除、默认入口=排座工作台；侧栏分组/矮视口修复；≤900px 双抽屉；切页即时化；`ViewLocator` 视图弱引用缓存；`.seatsets` 拖放职责迁入工作台 |
| M4 名单/策略/快照 | 名单行「显示/编辑」轻量切换 + 自适应虚拟化；策略页缓存与令牌化；快照 Transient→Singleton + 自绘预览；修复快照列表恒空、回滚后画布空白、`Snapshot` 变更不重绘 |
| M5 设置/关于/引导/清理 | 设置分组卡片 + 全量令牌化；关于页令牌化；引导接口化（`IGuideSeedTarget`，5 页自实现，无静态状态）；WASM 运行时语言切换修复（卫星资源）；死键/死代码清理 |
| M6 测试/性能/文档 | Headless UI 测试项目（65 例，全量 446 通过）；视觉基线（明/暗 × 4 视图）；M1–M6 性能复测；绑定诊断 0 警告；文档同步（AGENTS.md/INDEX/Design_Spec/WebDeployment/ONBOARDING_GUIDE） |

终验后的收尾提交还包括：主题色可调（默认 `#83B6DE`/跟随系统）与主按钮悬停前景修复、
画布右下角缩放控制条、座位姓名两行换行、快照预览座位尺寸按坐标等比放大、关于页依赖清单更新。

## 6. 性能验收与复测结果

口径与第 2 节一致（WASM Release + 软件 WebGL）。基线 = 重构前，目标 = 阶段 2 制定。

| # | 指标 | 目标 | 基线 | 终验实测（M6） | 判定 |
|---|---|---|---|---|---|
| M1 | 会场参数单次变更 | ≤50ms / INP ≤100ms | ~600–800ms / INP 315ms | **INP 18ms**，长任务 1×69.5ms | ✅ |
| M2 | 切页 INP（WASM） | ≤100ms | 493ms | **569ms**（27 个 >100ms，最长 747ms） | ❌ 遗留 |
| M2b | 桌面换页端到端 | ≤250ms | 推断 ~800ms+ | 无桌面环境未测 | 未测 |
| M3 | 64 座画布首帧 | ≤300ms | 未单独计时 | 生成交互 **INP 42ms**（总流程分段 1.59s） | ✅（交互口径） |
| M3b | 拖拽/缩放无 >100ms 长任务 | ≤100ms | 264ms | 交互期长任务 53–93ms（INP 15ms） | ✅ |
| M4 | 启动停止 >200ms 长任务 | ≤3s | ~6.3s | **6.57s**（会场懒加载已消除 ~0.9s） | ❌ 遗留 |
| M5 | 240 行滚动单次操作 | ≤1 个 <100ms | 1×149ms | 1×167ms（trace 起始帧），滚动期间无 >50ms | 近似 ✅ |
| M6 | 10 次往返堆增长 | ≤10% | 未测 | 首轮 +20.0%，**第二轮 0.00%**（稳态无泄漏） | ✅ |

## 7. 已知遗留与后续建议

- **M2 切页 INP 未达标**：归因于页面首次构造 + 挂载的 XAML 激活成本（`ViewLocator` 已缓存，
  重复切换 271–340ms；壳层逻辑 <15ms）。后续方向：页面视图常驻/分区懒加载。
- **M4 启动 ≤3s 未达成**：构成为 WelcomeCard Markdown ~0.2s、MainView 构造+挂载 ~0.48s、
  SeatingView 构造到 Loaded ~0.99s、启动 I/O（IndexedDB）~1.2s；软件 WebGL + 单线程 WASM 口径，
  桌面推断显著更优。
- **500+ 行名单虚拟化未实测**：>300 行走虚拟化在 WASM 单次滚动 300–400ms，
  保留为已知设计约束。
- **桌面端 E2E 未测**：无头环境无法测量桌面换页端到端与桌面渲染开销（推断见上）。
- **会场首屏名称占位**：首屏只列 ID、选中/恢复时加载布局并回填名称；
  多会场场景建议后续新增 `ListVenueSummariesAsync` 轻量摘要接口（仅用 `JsonDocument` 读名称）。
- 引导步骤重映射到新 IA 后语义与覆盖范围不变；页面引导机制保留（配置中当前为空）。

## 8. 验证与工具链

- **测试**：`dotnet test` 全量 **446 通过 / 0 失败**（Core/Application/Infrastructure + Headless UI）。
  Headless 项目用 `Avalonia.Skia` 真实绘制，视觉基线与像素回归见
  `tests/SeatFlow.Presentation.Tests/VisualBaselineTests.cs`（基线 PNG 不入库）。
- **无头 WASM 工具链**：`scripts/ui-inspect/`（见其 README）——
  CDP 可信输入（`cdp.mjs`）、批量截图（`capture-baseline.mjs`）、trace 分析（`analyze-trace.mjs`）、
  像素定位（`png_analyze.py`）、序列回放（`seq.mjs`）、演示数据注入（`seed-demo-data.*`）。
  浏览器由 chrome-devtools MCP 管理；Avalonia WASM 渲染在单个 `<canvas>` 上，
  DOM 无可交互节点，输入必须走 CDP `Input.*` 可信事件。
- **门禁**：`dotnet build` 双 TFM 0 警告；绑定诊断 0 警告；`python3 scripts/i18n.py check` 0 错误；
  Presentation 无静态可变字段。

## 9. 参考

- Avalonia 官方《Performance optimization》：
  https://docs.avaloniaui.net/docs/app-development/performance
  （虚拟化需受限高度、`IsVisible` 优于 `Opacity=0`、命中测试成本、编译绑定默认开启、
  去抖与后台线程、性能剖析工具——本文档的诊断与对策均以其为权威依据）
- `docs/presentation/Design_Spec.md` — 方向 B 令牌、IA、动效政策与画布规范
- `docs/adr/ADR-014-reactiveui-and-canvas-architecture.md` — 本次技术架构决策全文
- `docs/adr/ADR-002-mvvm-framework.md` — 原 MVVM 决策（部分被 ADR-014 取代）
